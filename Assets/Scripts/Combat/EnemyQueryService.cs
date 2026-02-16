using System.Collections.Generic;
using GrassSim.Combat;
using UnityEngine;

public static class EnemyQueryService
{
    public struct RuntimeSnapshot
    {
        public int frame;
        public int globalQueriesThisFrame;
        public int globalQueryBudget;
        public int ownerStateCount;
        public int combatantCacheCount;
        public int lastCleanupRemoved;
        public int lastCleanupFrame;
        public float cacheCleanupIntervalSeconds;
    }

    private sealed class OwnerState
    {
        public Collider[] buffer = new Collider[DefaultBufferSize];
        public int lastHitCount;
        public int frame = -1;
        public int queriesThisFrame;
        public int maxQueriesPerFrame = DefaultMaxQueriesPerFrame;
        public float nextAllowedAt;
    }

    private const int DefaultBufferSize = 64;
    private const int DefaultMaxQueriesPerFrame = 16;
    private const int DefaultGlobalMaxQueriesPerFrame = 160;
    private const float CacheCleanupInterval = 5f;

    private static readonly Dictionary<int, OwnerState> ownerStates = new(64);
    private static readonly Dictionary<int, Combatant> combatantByColliderId = new(1024);
    private static readonly List<int> cacheKeysToRemove = new(64);
    private static readonly Collider[] emptyHits = System.Array.Empty<Collider>();

    private static float nextCacheCleanupAt;
    private static int globalMaxQueriesPerFrame = DefaultGlobalMaxQueriesPerFrame;
    private static int globalBudgetFrame = -1;
    private static int globalQueriesThisFrame;
    private static int lastCleanupRemoved;
    private static int lastCleanupFrame = -1;

    public static void ConfigureOwnerBudget(Object owner, int maxQueriesPerFrame)
    {
        if (owner == null)
            return;

        OwnerState state = GetOrCreateOwnerState(owner.GetInstanceID());
        state.maxQueriesPerFrame = Mathf.Max(1, maxQueriesPerFrame);
    }

    public static void ConfigureGlobalBudget(int maxQueriesPerFrame)
    {
        globalMaxQueriesPerFrame = Mathf.Max(16, maxQueriesPerFrame);
    }

    public static int GetLastHitCount(Object owner)
    {
        int ownerId = owner != null ? owner.GetInstanceID() : 0;
        if (!ownerStates.TryGetValue(ownerId, out OwnerState state) || state == null)
            return 0;

        return Mathf.Max(0, state.lastHitCount);
    }

    public static RuntimeSnapshot GetRuntimeSnapshot()
    {
        int frame = Time.frameCount;
        return new RuntimeSnapshot
        {
            frame = frame,
            globalQueriesThisFrame = globalBudgetFrame == frame ? globalQueriesThisFrame : 0,
            globalQueryBudget = Mathf.Max(16, globalMaxQueriesPerFrame),
            ownerStateCount = ownerStates.Count,
            combatantCacheCount = combatantByColliderId.Count,
            lastCleanupRemoved = lastCleanupRemoved,
            lastCleanupFrame = lastCleanupFrame,
            cacheCleanupIntervalSeconds = CacheCleanupInterval
        };
    }

    public static Collider[] OverlapSphere(
        Vector3 center,
        float radius,
        int layerMask,
        QueryTriggerInteraction queryTriggerInteraction
    )
    {
        return OverlapSphere(center, radius, layerMask, queryTriggerInteraction, null);
    }

    public static Collider[] OverlapSphere(
        Vector3 center,
        float radius,
        int layerMask,
        QueryTriggerInteraction queryTriggerInteraction,
        Object owner,
        float minInterval = 0f,
        int maxQueriesPerFrame = DefaultMaxQueriesPerFrame
    )
    {
        int ownerId = owner != null ? owner.GetInstanceID() : 0;
        OwnerState state = GetOrCreateOwnerState(ownerId);

        if (maxQueriesPerFrame > 0)
            state.maxQueriesPerFrame = Mathf.Max(1, maxQueriesPerFrame);

        int frame = Time.frameCount;
        if (state.frame != frame)
        {
            state.frame = frame;
            state.queriesThisFrame = 0;
        }

        if (state.queriesThisFrame >= state.maxQueriesPerFrame)
        {
            ClearPreviousHits(state);
            return emptyHits;
        }

        if (minInterval > 0f && Time.time < state.nextAllowedAt)
        {
            ClearPreviousHits(state);
            return emptyHits;
        }

        if (!TryConsumeGlobalQueryBudget())
        {
            ClearPreviousHits(state);
            return emptyHits;
        }

        state.queriesThisFrame++;
        if (minInterval > 0f)
            state.nextAllowedAt = Time.time + minInterval;

        Collider[] buffer = state.buffer;
        int hitCount = Physics.OverlapSphereNonAlloc(
            center,
            radius,
            buffer,
            layerMask,
            queryTriggerInteraction
        );

        while (hitCount == buffer.Length)
        {
            System.Array.Resize(ref buffer, buffer.Length * 2);
            hitCount = Physics.OverlapSphereNonAlloc(
                center,
                radius,
                buffer,
                layerMask,
                queryTriggerInteraction
            );
        }

        state.buffer = buffer;

        if (hitCount < state.lastHitCount)
            System.Array.Clear(state.buffer, hitCount, state.lastHitCount - hitCount);

        state.lastHitCount = hitCount;
        CleanupCombatantCacheIfNeeded();

        return hitCount > 0 ? state.buffer : emptyHits;
    }

    public static Collider[] OverlapCapsule(
        Vector3 point0,
        Vector3 point1,
        float radius,
        int layerMask,
        QueryTriggerInteraction queryTriggerInteraction
    )
    {
        return OverlapCapsule(point0, point1, radius, layerMask, queryTriggerInteraction, null);
    }

    public static Collider[] OverlapCapsule(
        Vector3 point0,
        Vector3 point1,
        float radius,
        int layerMask,
        QueryTriggerInteraction queryTriggerInteraction,
        Object owner,
        float minInterval = 0f,
        int maxQueriesPerFrame = DefaultMaxQueriesPerFrame
    )
    {
        int ownerId = owner != null ? owner.GetInstanceID() : 0;
        OwnerState state = GetOrCreateOwnerState(ownerId);

        if (maxQueriesPerFrame > 0)
            state.maxQueriesPerFrame = Mathf.Max(1, maxQueriesPerFrame);

        int frame = Time.frameCount;
        if (state.frame != frame)
        {
            state.frame = frame;
            state.queriesThisFrame = 0;
        }

        if (state.queriesThisFrame >= state.maxQueriesPerFrame)
        {
            ClearPreviousHits(state);
            return emptyHits;
        }

        if (minInterval > 0f && Time.time < state.nextAllowedAt)
        {
            ClearPreviousHits(state);
            return emptyHits;
        }

        if (!TryConsumeGlobalQueryBudget())
        {
            ClearPreviousHits(state);
            return emptyHits;
        }

        state.queriesThisFrame++;
        if (minInterval > 0f)
            state.nextAllowedAt = Time.time + minInterval;

        Collider[] buffer = state.buffer;
        int hitCount = Physics.OverlapCapsuleNonAlloc(
            point0,
            point1,
            radius,
            buffer,
            layerMask,
            queryTriggerInteraction
        );

        while (hitCount == buffer.Length)
        {
            System.Array.Resize(ref buffer, buffer.Length * 2);
            hitCount = Physics.OverlapCapsuleNonAlloc(
                point0,
                point1,
                radius,
                buffer,
                layerMask,
                queryTriggerInteraction
            );
        }

        state.buffer = buffer;

        if (hitCount < state.lastHitCount)
            System.Array.Clear(state.buffer, hitCount, state.lastHitCount - hitCount);

        state.lastHitCount = hitCount;
        CleanupCombatantCacheIfNeeded();

        return hitCount > 0 ? state.buffer : emptyHits;
    }

    public static Combatant GetCombatant(Collider collider)
    {
        if (collider == null)
            return null;

        CleanupCombatantCacheIfNeeded();

        int colliderId = collider.GetInstanceID();
        if (combatantByColliderId.TryGetValue(colliderId, out Combatant cached))
        {
            if (cached != null)
            {
                Transform cachedTransform = cached.transform;
                Transform colliderTransform = collider.transform;
                if (cachedTransform == colliderTransform || colliderTransform.IsChildOf(cachedTransform))
                    return cached;
            }

            combatantByColliderId.Remove(colliderId);
        }

        Combatant combatant = collider.GetComponentInParent<Combatant>();
        if (combatant != null)
            combatantByColliderId[colliderId] = combatant;

        return combatant;
    }

    private static OwnerState GetOrCreateOwnerState(int ownerId)
    {
        if (ownerStates.TryGetValue(ownerId, out OwnerState state))
            return state;

        state = new OwnerState();
        ownerStates.Add(ownerId, state);
        return state;
    }

    private static void ClearPreviousHits(OwnerState state)
    {
        if (state.lastHitCount <= 0)
            return;

        System.Array.Clear(state.buffer, 0, state.lastHitCount);
        state.lastHitCount = 0;
    }

    private static void CleanupCombatantCacheIfNeeded()
    {
        if (Time.unscaledTime < nextCacheCleanupAt)
            return;

        nextCacheCleanupAt = Time.unscaledTime + CacheCleanupInterval;
        cacheKeysToRemove.Clear();

        foreach (KeyValuePair<int, Combatant> kv in combatantByColliderId)
        {
            if (kv.Value == null)
                cacheKeysToRemove.Add(kv.Key);
        }

        for (int i = 0; i < cacheKeysToRemove.Count; i++)
            combatantByColliderId.Remove(cacheKeysToRemove[i]);

        lastCleanupRemoved = cacheKeysToRemove.Count;
        lastCleanupFrame = Time.frameCount;
    }

    private static bool TryConsumeGlobalQueryBudget()
    {
        int frame = Time.frameCount;
        if (globalBudgetFrame != frame)
        {
            globalBudgetFrame = frame;
            globalQueriesThisFrame = 0;
        }

        if (globalQueriesThisFrame >= Mathf.Max(16, globalMaxQueriesPerFrame))
            return false;

        globalQueriesThisFrame++;
        return true;
    }
}
