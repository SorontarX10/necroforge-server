using System.Collections.Generic;
using UnityEngine;
using GrassSim.Stats;

namespace GrassSim.AI
{
    public class EnemySimulationManager : MonoBehaviour
    {
        public struct RuntimeSnapshot
        {
            public int frame;
            public int simulatedCount;
            public int targetCount;
            public int lastEnsureFrame;
            public int ensureMissingBefore;
            public int ensureSpawned;
            public int lastRecycleFrame;
            public int recycleProcessed;
            public int recycleMoved;
            public float recycleDurationMs;
        }

        public static EnemySimulationManager Instance { get; private set; }

        [Header("Enemy Types")]
        public List<EnemySpawnEntry> enemyTypes = new();

        [Header("Population")]
        public int baseEnemies = 34;

        [Header("Spawn Area")]
        public float spawnRadius = 45f;
        public float minSpawnDistanceFromPlayer = 28f;
        public float maxSimDistanceFromPlayer = 104f;
        [Min(0f)] public float fogSpawnBuffer = 4f;
        [Min(0f)] public float activationSafetyMargin = 1f;
        [Range(0.35f, 1f)] public float minSpawnToActivationRatio = 0.72f;

        [Header("Performance")]
        [Min(1)] public int maxSimSpawnsPerTick = 4;
        [Min(0.02f)] public float recycleInterval = 0.16f;

        private float nextRecycleAt;
        private float nextEliteSpawnAt;

        [Header("Elite Spawns")]
        public bool enableEliteSpawns = true;
        [Min(10f)] public float eliteSpawnInterval = 60f;
        [Min(0f)] public float eliteSpawnIntervalEarlyVariance = 5f;
        [Min(0f)] public float eliteSpawnIntervalLateVariance = 10f;
        [Min(1)] public int eliteBatchSize = 10;
        [Min(1)] public int eliteSoftCap = 30;
        [Min(1)] public int eliteHardCap = 40;
        [Range(0.05f, 1f)] public float eliteSoftCapMinBatchScale = 0.25f;
        [Range(0f, 1f)] public float eliteSoftCapSpawnChanceFloor = 0.2f;
        [Min(1f)] public float eliteHealthMultiplier = 2.6f;
        [Min(1f)] public float eliteDamageMultiplier = 1.45f;
        [Min(1f)] public float eliteExpMultiplier = 2f;
        [Min(1f)] public float eliteMinimumMaxHealth = 260f;

        private readonly Dictionary<int, EnemySimState> sim = new();
        private int nextId = 1;
        private RuntimeSnapshot lastRuntimeSnapshot;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            nextEliteSpawnAt = Time.time + GetNextEliteSpawnDelay();
        }

        public IEnumerable<EnemySimState> GetAll() => sim.Values;
        public int SimulatedCount => sim.Count;

        public RuntimeSnapshot GetRuntimeSnapshot()
        {
            RuntimeSnapshot snapshot = lastRuntimeSnapshot;
            snapshot.frame = Time.frameCount;
            snapshot.simulatedCount = sim.Count;
            snapshot.targetCount = TargetCount;
            return snapshot;
        }

        /// <summary>
        /// Docelowa liczba enemy w symulacji (skaluje się z difficulty)
        /// </summary>
        public int TargetCount
        {
            get
            {
                int baseCount = baseEnemies;

                if (WorldStats.Instance == null)
                    return baseCount;

                return DifficultyContext.ScaleSpawnCap(baseCount);
            }
        }

        public void EnsurePopulation(Vector3 playerPosition)
        {
            bool spawnedEliteWave = TrySpawnEliteWave(playerPosition);

            int normalCount = CountNonEliteEnemies();
            int missing = TargetCount - normalCount;
            if (missing <= 0)
            {
                if (spawnedEliteWave)
                {
                    lastRuntimeSnapshot.lastEnsureFrame = Time.frameCount;
                    lastRuntimeSnapshot.ensureMissingBefore = missing;
                    lastRuntimeSnapshot.ensureSpawned = 0;
                    lastRuntimeSnapshot.simulatedCount = sim.Count;
                    lastRuntimeSnapshot.targetCount = TargetCount;
                }
                return;
            }

            int spawnBudget = Mathf.Min(missing, Mathf.Max(1, maxSimSpawnsPerTick));
            int beforeCount = sim.Count;
            for (int i = 0; i < spawnBudget; i++)
                SpawnSimEnemy(playerPosition, isElite: false);

            int spawned = Mathf.Max(0, sim.Count - beforeCount);
            lastRuntimeSnapshot.lastEnsureFrame = Time.frameCount;
            lastRuntimeSnapshot.ensureMissingBefore = missing;
            lastRuntimeSnapshot.ensureSpawned = spawned;
            lastRuntimeSnapshot.simulatedCount = sim.Count;
            lastRuntimeSnapshot.targetCount = TargetCount;
        }

        public void RemoveEnemy(int id)
        {
            // USUWAMY z symulacji, ALE NIE ruszamy enemiesSpawned
            sim.Remove(id);
        }

        // ============================
        // 🔥 CORE SPAWN LOGIC
        // ============================

        private void SpawnSimEnemy(Vector3 playerPosition, bool isElite)
        {
            if (enemyTypes == null || enemyTypes.Count == 0)
            {
                Debug.LogError("[EnemySim] No enemyTypes defined!");
                return;
            }

            int prefabIndex = PickEnemyTypeIndex();
            Vector3 pos = FindSpawnPosition(playerPosition);

            var state = new EnemySimState
            {
                id = nextId++,
                prefabIndex = prefabIndex,
                isElite = isElite,
                healthMultiplier = isElite ? Mathf.Max(1f, eliteHealthMultiplier) : 1f,
                damageMultiplier = isElite ? Mathf.Max(1f, eliteDamageMultiplier) : 1f,
                expMultiplier = isElite ? Mathf.Max(1f, eliteExpMultiplier) : 1f,
                eliteMinHealth = isElite ? Mathf.Max(1f, eliteMinimumMaxHealth) : 0f,
                position = pos,
                anchor = pos,
                health = 100f,
                state = EnemyBrainState.Patrol
            };

            sim.Add(state.id, state);

            // 🔥 LICZYMY SPAWN *TYLKO TU*
            if (WorldStats.Instance != null)
                WorldStats.Instance.AddEnemySpawned();
        }

        private int CountNonEliteEnemies()
        {
            int count = 0;
            foreach (EnemySimState state in sim.Values)
            {
                if (state != null && !state.isElite)
                    count++;
            }

            return count;
        }

        private int CountEliteEnemies()
        {
            int count = 0;
            foreach (EnemySimState state in sim.Values)
            {
                if (state != null && state.isElite)
                    count++;
            }

            return count;
        }

        private bool TrySpawnEliteWave(Vector3 playerPosition)
        {
            if (!enableEliteSpawns || enemyTypes == null || enemyTypes.Count == 0)
                return false;

            if (Time.time < nextEliteSpawnAt)
                return false;

            nextEliteSpawnAt = Time.time + GetNextEliteSpawnDelay();

            int eliteCount = CountEliteEnemies();
            int hardCap = Mathf.Max(1, eliteHardCap);
            if (eliteCount >= hardCap)
                return false;

            int count = CalculateEliteWaveSpawnCount(eliteCount, hardCap);
            if (count <= 0)
                return false;

            for (int i = 0; i < count; i++)
                SpawnSimEnemy(playerPosition, isElite: true);

            return true;
        }

        private int CalculateEliteWaveSpawnCount(int currentEliteCount, int hardCap)
        {
            int availableSlots = Mathf.Max(0, hardCap - currentEliteCount);
            if (availableSlots <= 0)
                return 0;

            int baseBatch = Mathf.Max(1, eliteBatchSize);
            int softCap = Mathf.Clamp(eliteSoftCap, 1, hardCap);
            if (currentEliteCount < softCap)
                return Mathf.Min(baseBatch, availableSlots);

            if (hardCap <= softCap)
                return Mathf.Min(1, availableSlots);

            float overSoft = (currentEliteCount - softCap + 1f) / Mathf.Max(1f, hardCap - softCap + 1f);
            float t = Mathf.Clamp01(overSoft);
            float chanceFloor = Mathf.Clamp01(eliteSoftCapSpawnChanceFloor);
            float waveChance = Mathf.Lerp(1f, chanceFloor, t);
            if (Random.value > waveChance)
                return 0;

            float minBatchScale = Mathf.Clamp(eliteSoftCapMinBatchScale, 0.05f, 1f);
            float batchScale = Mathf.Lerp(1f, minBatchScale, t);
            int scaledBatch = Mathf.Max(1, Mathf.RoundToInt(baseBatch * batchScale));
            return Mathf.Min(scaledBatch, availableSlots);
        }

        private float GetNextEliteSpawnDelay()
        {
            float baseInterval = Mathf.Max(10f, eliteSpawnInterval);
            float early = Mathf.Max(0f, eliteSpawnIntervalEarlyVariance);
            float late = Mathf.Max(0f, eliteSpawnIntervalLateVariance);
            float jitter = Random.Range(-early, late);
            return Mathf.Max(10f, baseInterval + jitter);
        }

        // ============================
        // 🎲 WEIGHTED RANDOM
        // ============================

        private int PickEnemyTypeIndex()
        {
            float total = 0f;

            foreach (var e in enemyTypes)
                total += Mathf.Max(0f, e.spawnWeight);

            float roll = Random.Range(0f, total);
            float acc = 0f;

            for (int i = 0; i < enemyTypes.Count; i++)
            {
                acc += Mathf.Max(0f, enemyTypes[i].spawnWeight);
                if (roll <= acc)
                    return i;
            }

            return 0;
        }

        private Vector3 FindSpawnPosition(Vector3 playerPosition)
        {
            float fogSafeMin = Mathf.Max(0f, RenderSettings.fogEndDistance + fogSpawnBuffer);
            float minR = Mathf.Max(1f, minSpawnDistanceFromPlayer);
            float maxR = Mathf.Max(minR + 0.5f, spawnRadius);

            EnemyActivationController activation = EnemyActivationController.Instance;
            if (activation != null)
            {
                float activationLimit = Mathf.Max(2f, activation.activeDistance - activationSafetyMargin);
                maxR = Mathf.Min(maxR, activationLimit);

                float preferredMin = Mathf.Max(2f, activationLimit * Mathf.Clamp(minSpawnToActivationRatio, 0.35f, 1f));
                minR = Mathf.Max(minR, preferredMin);
            }

            minR = Mathf.Max(minR, fogSafeMin);
            if (maxR <= minR + 0.1f)
            {
                // Keep enemy spawns valid even when fog/min-distance settings exceed activation reach.
                minR = Mathf.Max(1f, maxR - 1f);
            }

            maxR = Mathf.Max(minR + 0.5f, maxR);
            float r = Random.Range(minR, maxR);
            float a = Random.Range(0f, Mathf.PI * 2f);

            return playerPosition + new Vector3(
                Mathf.Cos(a) * r,
                0f,
                Mathf.Sin(a) * r
            );
        }

        public void RecycleFarEnemies(Vector3 playerPosition)
        {
            float now = Time.time;
            if (now < nextRecycleAt)
                return;

            nextRecycleAt = now + Mathf.Max(0.02f, recycleInterval);
            float recycleStart = Time.realtimeSinceStartup;

            float maxD2 = maxSimDistanceFromPlayer * maxSimDistanceFromPlayer;
            int processed = 0;
            int moved = 0;

            foreach (var s in sim.Values)
            {
                processed++;
                Vector3 d = s.position - playerPosition;
                d.y = 0f;

                if (d.sqrMagnitude > maxD2)
                {
                    Vector3 newPos = FindSpawnPosition(playerPosition);
                    s.position = newPos;
                    s.anchor = newPos;
                    moved++;
                }
            }

            lastRuntimeSnapshot.lastRecycleFrame = Time.frameCount;
            lastRuntimeSnapshot.recycleProcessed = processed;
            lastRuntimeSnapshot.recycleMoved = moved;
            lastRuntimeSnapshot.recycleDurationMs = (Time.realtimeSinceStartup - recycleStart) * 1000f;
            lastRuntimeSnapshot.simulatedCount = sim.Count;
            lastRuntimeSnapshot.targetCount = TargetCount;
        }
    }
}
