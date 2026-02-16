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
            int missing = TargetCount - sim.Count;
            if (missing <= 0)
                return;

            int spawnBudget = Mathf.Min(missing, Mathf.Max(1, maxSimSpawnsPerTick));
            int beforeCount = sim.Count;
            for (int i = 0; i < spawnBudget; i++)
                SpawnSimEnemy(playerPosition);

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

        private void SpawnSimEnemy(Vector3 playerPosition)
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
            float minR = Mathf.Max(minSpawnDistanceFromPlayer, fogSafeMin);

            if (EnemyActivationController.Instance != null)
            {
                float activationLimit = Mathf.Max(2f, EnemyActivationController.Instance.activeDistance - activationSafetyMargin);
                float preferredMin = Mathf.Max(2f, activationLimit * Mathf.Clamp(minSpawnToActivationRatio, 0.35f, 1f));
                minR = Mathf.Min(minR, preferredMin);
            }

            minR = Mathf.Max(1f, minR);
            float maxR = Mathf.Max(minR + 0.5f, spawnRadius);
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
