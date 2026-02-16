using System.Collections.Generic;
using UnityEngine;
using GrassSim.AI;
using GrassSim.Core;

[DisallowMultipleComponent]
public class BossEncounterController : MonoBehaviour
{
    [Header("Boss Spawn Times (seconds)")]
    [SerializeField] private List<float> spawnTimes = new() { 300f, 600f };

    [Header("Optional Prefab Override")]
    [SerializeField] private List<GameObject> bossPrefabsOverride = new();

    [Header("Spawn Placement")]
    [SerializeField] private float spawnDistanceMin = 18f;
    [SerializeField] private float spawnDistanceMax = 28f;
    [SerializeField] private float spawnRayHeight = 45f;
    [SerializeField] private float spawnYOffset = 0.6f;
    [SerializeField] private LayerMask groundMask;

    [Header("Boss Buff")]
    [SerializeField] private float bossHealthMultiplier = 10f;
    [SerializeField] private float bossDamageMultiplier = 3.5f;
    [SerializeField] private int bossExpMultiplier = 5;

    [Header("Boss Reward")]
    [SerializeField] private int rewardChoices = 3;
    [SerializeField] private RelicLibrary relicLibrary;
    [SerializeField] private RelicSelectionUI relicSelectionUI;

    private readonly List<GameObject> cachedBossPrefabs = new();
    private GameTimerController timer;
    private BossEnemyController activeBoss;
    private int nextSpawnIndex;
    private int queuedSpawns;
    private float lastElapsedTime;
    private bool missingPrefabsWarningShown;

    public BossEnemyController ActiveBoss => activeBoss != null && activeBoss.IsAlive ? activeBoss : null;

    private void Awake()
    {
        timer = GetComponent<GameTimerController>();
        if (timer == null)
            timer = GameTimerController.Instance;

        NormalizeSpawnTimes();
        ResolveRewardReferences();
        RebuildBossPrefabCache(logIfMissing: false);
    }

    private void OnValidate()
    {
        spawnDistanceMin = Mathf.Max(3f, spawnDistanceMin);
        spawnDistanceMax = Mathf.Max(spawnDistanceMin + 0.5f, spawnDistanceMax);
        spawnRayHeight = Mathf.Max(5f, spawnRayHeight);
        spawnYOffset = Mathf.Max(0f, spawnYOffset);
        rewardChoices = Mathf.Clamp(rewardChoices, 1, 3);
        bossHealthMultiplier = Mathf.Max(1f, bossHealthMultiplier);
        bossDamageMultiplier = Mathf.Max(1f, bossDamageMultiplier);
        bossExpMultiplier = Mathf.Max(1, bossExpMultiplier);
    }

    private void Update()
    {
        if (timer == null || timer.gameEnded)
            return;

        float elapsed = timer.elapsedTime;
        if (elapsed + 0.1f < lastElapsedTime)
            ResetSchedule();

        lastElapsedTime = elapsed;

        while (nextSpawnIndex < spawnTimes.Count && elapsed >= spawnTimes[nextSpawnIndex])
        {
            TrySpawnOrQueueBoss();
            nextSpawnIndex++;
        }

        if (activeBoss == null && queuedSpawns > 0)
        {
            if (SpawnBoss(logIfMissing: true))
                queuedSpawns--;
        }
    }

    public void NotifyBossDeath(BossEnemyController boss)
    {
        if (boss == null)
            return;

        if (activeBoss == boss)
            activeBoss = null;
    }

    private void ResetSchedule()
    {
        nextSpawnIndex = 0;
        queuedSpawns = 0;
        activeBoss = null;
    }

    private void NormalizeSpawnTimes()
    {
        if (spawnTimes == null)
            spawnTimes = new List<float>();

        spawnTimes.RemoveAll(t => t <= 0f);
        spawnTimes.Sort();

        for (int i = spawnTimes.Count - 1; i > 0; i--)
        {
            if (Mathf.Approximately(spawnTimes[i], spawnTimes[i - 1]))
                spawnTimes.RemoveAt(i);
        }
    }

    private void TrySpawnOrQueueBoss()
    {
        if (activeBoss != null && activeBoss.IsAlive)
        {
            queuedSpawns++;
            return;
        }

        if (!SpawnBoss(logIfMissing: true))
            queuedSpawns++;
    }

    private bool SpawnBoss(bool logIfMissing)
    {
        if (!EnsureBossPrefabs(logIfMissing))
            return false;

        Transform player = ResolvePlayerTransform();
        if (player == null)
            return false;

        if (!TryFindSpawnPosition(player, out Vector3 spawnPos))
            return false;

        GameObject prefab = cachedBossPrefabs[Random.Range(0, cachedBossPrefabs.Count)];
        if (prefab == null)
            return false;

        Vector3 forward = player.position - spawnPos;
        forward.y = 0f;
        Quaternion rotation = forward.sqrMagnitude > 0.001f
            ? Quaternion.LookRotation(forward.normalized, Vector3.up)
            : Quaternion.identity;

        GameObject instance = Instantiate(prefab, spawnPos, rotation);
        var boss = instance.GetComponent<BossEnemyController>();
        if (boss == null)
            boss = instance.AddComponent<BossEnemyController>();

        ResolveRewardReferences();

        boss.Initialize(
            owner: this,
            relicLibrary: relicLibrary,
            relicSelectionUI: relicSelectionUI,
            rewardChoices: rewardChoices,
            healthMultiplier: bossHealthMultiplier,
            damageMultiplier: bossDamageMultiplier,
            expMultiplier: bossExpMultiplier,
            groundMask: GetGroundMask(),
            groundRayHeight: spawnRayHeight,
            groundSnapOffset: spawnYOffset
        );

        activeBoss = boss;
        return true;
    }

    private bool EnsureBossPrefabs(bool logIfMissing)
    {
        if (cachedBossPrefabs.Count > 0)
            return true;

        RebuildBossPrefabCache(logIfMissing);
        return cachedBossPrefabs.Count > 0;
    }

    private Transform ResolvePlayerTransform()
    {
        return PlayerLocator.GetTransform();
    }

    private bool TryFindSpawnPosition(Transform player, out Vector3 spawnPos)
    {
        LayerMask mask = GetGroundMask();
        float maxDistance = Mathf.Max(spawnDistanceMin + 0.1f, spawnDistanceMax);

        for (int i = 0; i < 12; i++)
        {
            Vector2 dir2 = Random.insideUnitCircle;
            if (dir2.sqrMagnitude < 0.0001f)
                dir2 = Vector2.up;

            dir2.Normalize();

            float distance = Random.Range(spawnDistanceMin, maxDistance);
            Vector3 candidate = player.position + new Vector3(dir2.x, 0f, dir2.y) * distance;

            if (TrySnapToGround(candidate, mask, out spawnPos))
                return true;
        }

        Vector3 fallback = player.position + player.forward * Mathf.Max(4f, spawnDistanceMin);
        return TrySnapToGround(fallback, mask, out spawnPos);
    }

    private bool TrySnapToGround(Vector3 worldPos, LayerMask mask, out Vector3 snapped)
    {
        Vector3 rayStart = worldPos + Vector3.up * spawnRayHeight;
        float rayDistance = spawnRayHeight * 2f;

        if (Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit, rayDistance, mask))
        {
            snapped = hit.point + Vector3.up * spawnYOffset;
            return true;
        }

        snapped = worldPos;
        return false;
    }

    private LayerMask GetGroundMask()
    {
        if (groundMask.value != 0)
            return groundMask;

        var activation = EnemyActivationController.Instance;
        if (activation != null && activation.groundMask.value != 0)
            return activation.groundMask;

        int groundBits = LayerMask.GetMask("Ground");
        return groundBits != 0 ? groundBits : Physics.AllLayers;
    }

    private void ResolveRewardReferences()
    {
        if (relicSelectionUI == null)
            relicSelectionUI = FindFirstObjectByType<RelicSelectionUI>();

        if (relicLibrary != null)
            return;

        var chestSpawner = FindFirstObjectByType<RelicChestSpawner>();
        if (chestSpawner != null && chestSpawner.chestPrefab != null)
        {
            var chestTrigger = chestSpawner.chestPrefab.GetComponent<ChestRelicTrigger>();
            if (chestTrigger != null && chestTrigger.relicLibrary != null)
                relicLibrary = chestTrigger.relicLibrary;
        }

        if (relicLibrary == null)
        {
            var loadedLibraries = Resources.FindObjectsOfTypeAll<RelicLibrary>();
            if (loadedLibraries != null && loadedLibraries.Length > 0)
                relicLibrary = loadedLibraries[0];
        }
    }

    private void RebuildBossPrefabCache(bool logIfMissing)
    {
        cachedBossPrefabs.Clear();

        if (bossPrefabsOverride != null && bossPrefabsOverride.Count > 0)
        {
            for (int i = 0; i < bossPrefabsOverride.Count; i++)
                AddAllowedPrefab(bossPrefabsOverride[i]);
        }

        if (cachedBossPrefabs.Count == 0)
        {
            var sim = EnemySimulationManager.Instance;
            if (sim != null && sim.enemyTypes != null)
            {
                for (int i = 0; i < sim.enemyTypes.Count; i++)
                {
                    var entry = sim.enemyTypes[i];
                    if (entry == null)
                        continue;

                    AddAllowedPrefab(entry.prefab);
                }
            }
        }

        if (cachedBossPrefabs.Count == 0)
        {
            var activation = EnemyActivationController.Instance;
            if (activation != null && activation.enemyPrefabs != null)
            {
                for (int i = 0; i < activation.enemyPrefabs.Count; i++)
                    AddAllowedPrefab(activation.enemyPrefabs[i]);
            }
        }

        if (cachedBossPrefabs.Count == 0)
        {
            if (logIfMissing && !missingPrefabsWarningShown)
            {
                Debug.LogWarning(
                    "[BossEncounterController] Could not find boss prefabs (expected Zombie, Zombie Quick, Zombie Tank, Dog).",
                    this
                );
                missingPrefabsWarningShown = true;
            }
            return;
        }

        missingPrefabsWarningShown = false;
    }

    private void AddAllowedPrefab(GameObject prefab)
    {
        if (prefab == null || !IsAllowedBossPrefab(prefab))
            return;

        if (!cachedBossPrefabs.Contains(prefab))
            cachedBossPrefabs.Add(prefab);
    }

    private bool IsAllowedBossPrefab(GameObject prefab)
    {
        string n = prefab.name.ToLowerInvariant();

        bool quick = n.Contains("quick");
        bool tank = n.Contains("tank");
        bool dog = n.Contains("dog");
        bool normalZombie = n.Contains("zombie") && !quick && !tank;

        return quick || tank || dog || normalZombie;
    }
}
