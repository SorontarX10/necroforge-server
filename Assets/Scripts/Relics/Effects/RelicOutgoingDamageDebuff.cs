using System.Collections.Generic;
using UnityEngine;

public class RelicOutgoingDamageDebuff : MonoBehaviour
{
    private float reduction;
    private float expiresAt;

    public bool IsActive => Time.time < expiresAt && reduction > 0f;

    public void Apply(float reductionAmount, float duration)
    {
        if (reductionAmount <= 0f || duration <= 0f)
            return;

        reduction = Mathf.Max(reduction, Mathf.Clamp01(reductionAmount));
        expiresAt = Mathf.Max(expiresAt, Time.time + duration);
    }

    public float GetDamageMultiplier()
    {
        if (!IsActive)
            return 1f;

        return Mathf.Clamp(1f - reduction, 0.05f, 1f);
    }
}

public interface IRelicBatchedUpdate
{
    bool IsBatchedUpdateActive { get; }
    float BatchedUpdateInterval { get; }
    void TickFromRelicBatch(float now, float deltaTime);
}

public interface IRelicBatchedFixedUpdate
{
    bool IsBatchedFixedUpdateActive { get; }
    void TickFromRelicBatchFixed(float fixedDeltaTime);
}

public enum RelicTickArchetype : byte
{
    Default = 0,
    PlayerState = 1,
    PlayerAura = 2,
    EnemyDebuff = 3,
    EnemyControl = 4,
    BossDot = 5
}

public interface IRelicBatchedCadence
{
    RelicTickArchetype BatchedTickArchetype { get; }
}

public static class RelicQueryBudgetProfiles
{
    public static int For(RelicTickArchetype archetype)
    {
        return archetype switch
        {
            RelicTickArchetype.PlayerState => 8,
            RelicTickArchetype.PlayerAura => 12,
            RelicTickArchetype.EnemyDebuff => 14,
            RelicTickArchetype.EnemyControl => 18,
            RelicTickArchetype.BossDot => 16,
            _ => 10
        };
    }
}

[DefaultExecutionOrder(-345)]
public sealed class RelicBatchedTickSystem : MonoBehaviour
{
    public struct RuntimeSnapshot
    {
        public int frame;
        public int updateEntryCount;
        public int fixedEntryCount;
        public int updateProcessedThisFrame;
        public int updateBudgetThisFrame;
        public int fixedProcessedThisStep;
        public int fixedBudgetThisStep;
        public float updateDurationMs;
        public float fixedDurationMs;
    }

    [Header("Tick")]
    [SerializeField, Min(0.01f)] private float minUpdateTickInterval = 0.02f;
    [SerializeField, Min(8)] private int minUpdateTicksPerFrame = 64;
    [SerializeField, Min(8)] private int maxUpdateTicksPerFrame = 320;
    [SerializeField, Min(8)] private int minFixedTicksPerStep = 48;
    [SerializeField, Min(8)] private int maxFixedTicksPerStep = 240;

    [Header("Archetype Cadence")]
    [SerializeField, Min(0.25f)] private float playerStateIntervalMultiplier = 0.8f;
    [SerializeField, Min(0.25f)] private float playerAuraIntervalMultiplier = 0.9f;
    [SerializeField, Min(0.25f)] private float enemyDebuffIntervalMultiplier = 1.1f;
    [SerializeField, Min(0.25f)] private float enemyControlIntervalMultiplier = 0.85f;
    [SerializeField, Min(0.25f)] private float bossDotIntervalMultiplier = 0.8f;

    private struct UpdateEntry
    {
        public int id;
        public Object owner;
        public IRelicBatchedUpdate tickable;
        public float nextTickAt;
        public float lastTickAt;
    }

    private struct FixedEntry
    {
        public int id;
        public Object owner;
        public IRelicBatchedFixedUpdate tickable;
    }

    private static RelicBatchedTickSystem instance;
    private static bool shuttingDown;

    private readonly List<UpdateEntry> updateEntries = new(256);
    private readonly Dictionary<int, int> updateIndexById = new(256);

    private readonly List<FixedEntry> fixedEntries = new(128);
    private readonly Dictionary<int, int> fixedIndexById = new(128);
    private int updateCursor;
    private int fixedCursor;
    private RuntimeSnapshot lastRuntimeSnapshot;

    public static bool HasLiveInstance => instance != null && !shuttingDown;

    public static bool TryGetRuntimeSnapshot(out RuntimeSnapshot snapshot)
    {
        if (!HasLiveInstance)
        {
            snapshot = default;
            return false;
        }

        snapshot = instance.GetRuntimeSnapshot();
        return true;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        instance = null;
        shuttingDown = false;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        EnsureInstance();
    }

    public static void Register(object tickable)
    {
        if (tickable == null)
            return;

        RelicBatchedTickSystem sys = EnsureInstance();
        if (sys == null)
            return;

        sys.RegisterInternal(tickable);
    }

    public static void Unregister(object tickable)
    {
        if (!HasLiveInstance || tickable == null)
            return;

        instance.UnregisterInternal(tickable);
    }

    private static RelicBatchedTickSystem EnsureInstance()
    {
        if (shuttingDown)
            return null;

        if (instance != null)
            return instance;

        instance = FindFirstObjectByType<RelicBatchedTickSystem>();
        if (instance != null)
            return instance;

        GameObject go = new("RelicBatchedTickSystem");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<RelicBatchedTickSystem>();
        return instance;
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void OnApplicationQuit()
    {
        shuttingDown = true;
    }

    private void OnDestroy()
    {
        if (instance == this)
            instance = null;
    }

    private RuntimeSnapshot GetRuntimeSnapshot()
    {
        RuntimeSnapshot snapshot = lastRuntimeSnapshot;
        snapshot.frame = Time.frameCount;
        snapshot.updateEntryCount = updateEntries.Count;
        snapshot.fixedEntryCount = fixedEntries.Count;
        return snapshot;
    }

    private void Update()
    {
        float now = Time.time;
        float dt = Time.deltaTime;
        float minInterval = Mathf.Max(0.01f, minUpdateTickInterval);
        int count = updateEntries.Count;
        if (count == 0)
        {
            lastRuntimeSnapshot.frame = Time.frameCount;
            lastRuntimeSnapshot.updateEntryCount = 0;
            lastRuntimeSnapshot.updateProcessedThisFrame = 0;
            lastRuntimeSnapshot.updateBudgetThisFrame = 0;
            lastRuntimeSnapshot.updateDurationMs = 0f;
            lastRuntimeSnapshot.fixedEntryCount = fixedEntries.Count;
            return;
        }

        int budget = ComputeBudget(count, minUpdateTicksPerFrame, maxUpdateTicksPerFrame, 2);
        int processed = 0;
        int safety = 0;
        int safetyLimit = count * 4;
        float updateStart = Time.realtimeSinceStartup;

        while (processed < budget && safety < safetyLimit && updateEntries.Count > 0)
        {
            if (updateCursor >= updateEntries.Count)
                updateCursor = 0;

            int index = updateCursor++;
            safety++;

            UpdateEntry entry = updateEntries[index];
            if (entry.owner == null || entry.tickable == null)
            {
                RemoveUpdateAt(index);
                if (index < updateCursor)
                    updateCursor = Mathf.Max(0, updateCursor - 1);
                continue;
            }

            if (!entry.tickable.IsBatchedUpdateActive)
            {
                if (TryGetUpdateEntryIndex(entry.id, out int inactiveIndex))
                {
                    UpdateEntry inactiveEntry = updateEntries[inactiveIndex];
                    inactiveEntry.lastTickAt = now;
                    updateEntries[inactiveIndex] = inactiveEntry;
                }
                continue;
            }

            if (now < entry.nextTickAt)
                continue;

            float elapsed = Mathf.Clamp(now - entry.lastTickAt, dt, 0.1f);
            entry.tickable.TickFromRelicBatch(now, elapsed);
            processed++;

            // Tick callbacks may unregister/re-register entries; resolve current index before writing back.
            if (!TryGetUpdateEntryIndex(entry.id, out int liveIndex))
                continue;

            UpdateEntry liveEntry = updateEntries[liveIndex];
            if (liveEntry.owner == null || liveEntry.tickable == null)
            {
                RemoveUpdateAt(liveIndex);
                if (liveIndex < updateCursor)
                    updateCursor = Mathf.Max(0, updateCursor - 1);
                continue;
            }

            liveEntry.lastTickAt = now;
            float cadenceMul = ResolveCadenceMultiplier(liveEntry.tickable);
            float interval = Mathf.Max(minInterval, liveEntry.tickable.BatchedUpdateInterval * cadenceMul);
            liveEntry.nextTickAt = now + interval;
            updateEntries[liveIndex] = liveEntry;
        }

        lastRuntimeSnapshot.frame = Time.frameCount;
        lastRuntimeSnapshot.updateEntryCount = updateEntries.Count;
        lastRuntimeSnapshot.fixedEntryCount = fixedEntries.Count;
        lastRuntimeSnapshot.updateProcessedThisFrame = processed;
        lastRuntimeSnapshot.updateBudgetThisFrame = budget;
        lastRuntimeSnapshot.updateDurationMs = (Time.realtimeSinceStartup - updateStart) * 1000f;
    }

    private void FixedUpdate()
    {
        float fixedDt = Time.fixedDeltaTime;
        int count = fixedEntries.Count;
        if (count == 0)
        {
            lastRuntimeSnapshot.frame = Time.frameCount;
            lastRuntimeSnapshot.fixedEntryCount = 0;
            lastRuntimeSnapshot.fixedProcessedThisStep = 0;
            lastRuntimeSnapshot.fixedBudgetThisStep = 0;
            lastRuntimeSnapshot.fixedDurationMs = 0f;
            lastRuntimeSnapshot.updateEntryCount = updateEntries.Count;
            return;
        }

        int budget = ComputeBudget(count, minFixedTicksPerStep, maxFixedTicksPerStep, 2);
        int processed = 0;
        int safety = 0;
        int safetyLimit = count * 4;
        float fixedStart = Time.realtimeSinceStartup;

        while (processed < budget && safety < safetyLimit && fixedEntries.Count > 0)
        {
            if (fixedCursor >= fixedEntries.Count)
                fixedCursor = 0;

            int index = fixedCursor++;
            safety++;

            FixedEntry entry = fixedEntries[index];
            if (entry.owner == null || entry.tickable == null)
            {
                RemoveFixedAt(index);
                if (index < fixedCursor)
                    fixedCursor = Mathf.Max(0, fixedCursor - 1);
                continue;
            }

            if (!entry.tickable.IsBatchedFixedUpdateActive)
                continue;

            entry.tickable.TickFromRelicBatchFixed(fixedDt);
            processed++;
        }

        lastRuntimeSnapshot.frame = Time.frameCount;
        lastRuntimeSnapshot.updateEntryCount = updateEntries.Count;
        lastRuntimeSnapshot.fixedEntryCount = fixedEntries.Count;
        lastRuntimeSnapshot.fixedProcessedThisStep = processed;
        lastRuntimeSnapshot.fixedBudgetThisStep = budget;
        lastRuntimeSnapshot.fixedDurationMs = (Time.realtimeSinceStartup - fixedStart) * 1000f;
    }

    private void RegisterInternal(object tickable)
    {
        Object owner = tickable as Object;
        if (owner == null)
            return;

        int id = owner.GetInstanceID();
        float now = Time.time;

        IRelicBatchedUpdate updateTickable = tickable as IRelicBatchedUpdate;
        if (updateTickable != null)
        {
            if (updateIndexById.TryGetValue(id, out int existingUpdateIndex))
            {
                UpdateEntry existing = updateEntries[existingUpdateIndex];
                existing.owner = owner;
                existing.tickable = updateTickable;
                existing.nextTickAt = Mathf.Min(existing.nextTickAt, now);
                existing.lastTickAt = now;
                updateEntries[existingUpdateIndex] = existing;
            }
            else
            {
                updateIndexById.Add(id, updateEntries.Count);
                updateEntries.Add(new UpdateEntry
                {
                    id = id,
                    owner = owner,
                    tickable = updateTickable,
                    nextTickAt = now,
                    lastTickAt = now
                });
            }
        }

        IRelicBatchedFixedUpdate fixedTickable = tickable as IRelicBatchedFixedUpdate;
        if (fixedTickable != null)
        {
            if (fixedIndexById.TryGetValue(id, out int existingFixedIndex))
            {
                FixedEntry existing = fixedEntries[existingFixedIndex];
                existing.owner = owner;
                existing.tickable = fixedTickable;
                fixedEntries[existingFixedIndex] = existing;
            }
            else
            {
                fixedIndexById.Add(id, fixedEntries.Count);
                fixedEntries.Add(new FixedEntry
                {
                    id = id,
                    owner = owner,
                    tickable = fixedTickable
                });
            }
        }
    }

    private void UnregisterInternal(object tickable)
    {
        Object owner = tickable as Object;
        if (owner == null)
            return;

        int id = owner.GetInstanceID();

        if (updateIndexById.TryGetValue(id, out int updateIndex))
            RemoveUpdateAt(updateIndex);

        if (fixedIndexById.TryGetValue(id, out int fixedIndex))
            RemoveFixedAt(fixedIndex);
    }

    private void RemoveUpdateAt(int index)
    {
        int lastIndex = updateEntries.Count - 1;
        UpdateEntry removed = updateEntries[index];
        UpdateEntry last = updateEntries[lastIndex];

        updateEntries[index] = last;
        updateEntries.RemoveAt(lastIndex);
        updateIndexById.Remove(removed.id);

        if (index < updateEntries.Count)
            updateIndexById[last.id] = index;
    }

    private bool TryGetUpdateEntryIndex(int id, out int index)
    {
        if (updateIndexById.TryGetValue(id, out index)
            && index >= 0
            && index < updateEntries.Count
            && updateEntries[index].id == id)
        {
            return true;
        }

        index = -1;
        return false;
    }

    private void RemoveFixedAt(int index)
    {
        int lastIndex = fixedEntries.Count - 1;
        FixedEntry removed = fixedEntries[index];
        FixedEntry last = fixedEntries[lastIndex];

        fixedEntries[index] = last;
        fixedEntries.RemoveAt(lastIndex);
        fixedIndexById.Remove(removed.id);

        if (index < fixedEntries.Count)
            fixedIndexById[last.id] = index;
    }

    private float ResolveCadenceMultiplier(IRelicBatchedUpdate tickable)
    {
        if (tickable is not IRelicBatchedCadence cadence)
            return 1f;

        return cadence.BatchedTickArchetype switch
        {
            RelicTickArchetype.PlayerState => playerStateIntervalMultiplier,
            RelicTickArchetype.PlayerAura => playerAuraIntervalMultiplier,
            RelicTickArchetype.EnemyDebuff => enemyDebuffIntervalMultiplier,
            RelicTickArchetype.EnemyControl => enemyControlIntervalMultiplier,
            RelicTickArchetype.BossDot => bossDotIntervalMultiplier,
            _ => 1f
        };
    }

    private static int ComputeBudget(int count, int minBudget, int maxBudget, int divider)
    {
        if (count <= 0)
            return 0;

        int dynamicBudget = minBudget + count / Mathf.Max(1, divider);
        return Mathf.Clamp(dynamicBudget, minBudget, maxBudget);
    }
}

[DefaultExecutionOrder(-344)]
public sealed class RelicVfxTickSystem : MonoBehaviour
{
    private struct FollowEntry
    {
        public int id;
        public Transform anchor;
        public Transform visual;
        public Vector3 offset;
    }

    private sealed class PooledVfxHandle : MonoBehaviour
    {
        public int sourcePrefabId;
    }

    private static RelicVfxTickSystem instance;
    private static bool shuttingDown;

    private readonly List<FollowEntry> followEntries = new(128);
    private readonly Dictionary<int, int> followIndexById = new(128);
    private readonly Dictionary<int, Stack<GameObject>> vfxPoolByPrefabId = new(64);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        instance = null;
        shuttingDown = false;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        EnsureInstance();
    }

    public static void Track(Transform anchor, Transform visual, Vector3 offset)
    {
        if (anchor == null || visual == null)
            return;

        RelicVfxTickSystem sys = EnsureInstance();
        if (sys == null)
            return;

        int id = visual.GetInstanceID();
        FollowEntry entry = new()
        {
            id = id,
            anchor = anchor,
            visual = visual,
            offset = offset
        };

        if (sys.followIndexById.TryGetValue(id, out int index))
        {
            sys.followEntries[index] = entry;
            return;
        }

        sys.followIndexById.Add(id, sys.followEntries.Count);
        sys.followEntries.Add(entry);
    }

    public static void Untrack(Transform visual)
    {
        if (instance == null || visual == null)
            return;

        int id = visual.GetInstanceID();
        if (!instance.followIndexById.TryGetValue(id, out int index))
            return;

        instance.RemoveFollowAt(index);
    }

    public static GameObject Rent(GameObject prefab, Vector3 position, Quaternion rotation)
    {
        if (prefab == null)
            return null;

        RelicVfxTickSystem sys = EnsureInstance();
        if (sys == null)
            return null;

        int prefabId = prefab.GetInstanceID();
        GameObject go = null;

        if (sys.vfxPoolByPrefabId.TryGetValue(prefabId, out Stack<GameObject> pool))
        {
            while (pool.Count > 0 && go == null)
                go = pool.Pop();
        }

        if (go == null)
        {
            go = Instantiate(prefab, position, rotation);
            PooledVfxHandle handle = go.GetComponent<PooledVfxHandle>();
            if (handle == null)
                handle = go.AddComponent<PooledVfxHandle>();
            handle.sourcePrefabId = prefabId;
        }
        else
        {
            go.transform.SetPositionAndRotation(position, rotation);
            go.SetActive(true);
        }

        return go;
    }

    public static void Return(GameObject instanceToReturn)
    {
        if (instanceToReturn == null)
            return;

        RelicVfxTickSystem sys = EnsureInstance();
        if (sys == null)
            return;

        PooledVfxHandle handle = instanceToReturn.GetComponent<PooledVfxHandle>();
        if (handle == null)
        {
            Destroy(instanceToReturn);
            return;
        }

        int prefabId = handle.sourcePrefabId;
        if (!sys.vfxPoolByPrefabId.TryGetValue(prefabId, out Stack<GameObject> pool))
        {
            pool = new Stack<GameObject>(8);
            sys.vfxPoolByPrefabId.Add(prefabId, pool);
        }

        instanceToReturn.SetActive(false);
        instanceToReturn.transform.SetParent(sys.transform, false);
        pool.Push(instanceToReturn);
    }

    private static RelicVfxTickSystem EnsureInstance()
    {
        if (shuttingDown)
            return null;

        if (instance != null)
            return instance;

        instance = FindFirstObjectByType<RelicVfxTickSystem>();
        if (instance != null)
            return instance;

        GameObject go = new("RelicVfxTickSystem");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<RelicVfxTickSystem>();
        return instance;
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void OnApplicationQuit()
    {
        shuttingDown = true;
    }

    private void OnDestroy()
    {
        if (instance == this)
            instance = null;
    }

    private void LateUpdate()
    {
        for (int i = followEntries.Count - 1; i >= 0; i--)
        {
            FollowEntry entry = followEntries[i];
            if (entry.anchor == null || entry.visual == null)
            {
                RemoveFollowAt(i);
                continue;
            }

            entry.visual.position = entry.anchor.position + entry.offset;
        }
    }

    private void RemoveFollowAt(int index)
    {
        int lastIndex = followEntries.Count - 1;
        FollowEntry removed = followEntries[index];
        FollowEntry last = followEntries[lastIndex];

        followEntries[index] = last;
        followEntries.RemoveAt(lastIndex);
        followIndexById.Remove(removed.id);

        if (index < followEntries.Count)
            followIndexById[last.id] = index;
    }
}
