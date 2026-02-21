using System.Collections.Generic;
using GrassSim.Combat;
using GrassSim.Enemies;
using UnityEngine;

namespace GrassSim.AI
{
    public class EnemyActivationController : MonoBehaviour
    {
        public struct ActivationRuntimeSnapshot
        {
            public int frame;
            public int activeCount;
            public int scaledActiveCap;
            public float refreshInterval;
            public int scannedCandidates;
            public int selectedCandidates;
            public int spawnedThisTick;
            public int despawnedThisTick;
            public int cleanupRemoved;
            public int populationSyncTriggered;
            public int recycleSyncTriggered;
            public float updateDurationMs;
        }

        private struct EnemyCandidate
        {
            public EnemySimState state;
            public float sqrDistance;
        }

        public static EnemyActivationController Instance { get; private set; }

        [Header("Activation")]
        public float activeDistance = 48f;
        public int maxActiveEnemies = 34;

        [Header("Update Scheduling")]
        [Min(0.02f)] public float activationRefreshInterval = 0.1f;
        [Min(0.05f)] public float cleanupRefreshInterval = 0.5f;

        [Header("Frame Budgets")]
        [Min(1)] public int maxActivationSpawnsPerTick = 2;
        [Min(1)] public int maxActivationDespawnsPerTick = 5;

        [Header("Enemy Prefabs")]
        public List<GameObject> enemyPrefabs;

        [Header("Pooling")]
        [Min(0)] public int prewarmPerEnemyType = 16;
        [Min(1)] public int prewarmOpsPerFrame = 8;

        [Header("Simulation Sync")]
        [Min(0.05f)] public float populationSyncInterval = 0.2f;
        [Min(0.05f)] public float recycleSyncInterval = 0.16f;

        [Header("Ground Settings")]
        public LayerMask groundMask;
        public float spawnRayHeight = 10f;
        public float spawnYOffset = 0.5f;

        [Header("Spawn Safety")]
        [Min(1f)] public float minSpawnDistanceFromPlayer = 12f;
        [Min(0f)] public float spawnSafetyRandomExtraDistance = 4f;

        [HideInInspector] public Vector3 playerPosition;

        private readonly Dictionary<int, GameObject> active = new();
        private readonly List<EnemyCandidate> nearbyCandidates = new(128);
        private readonly List<EnemyCandidate> selectedCandidates = new(64);
        private readonly List<int> idsToRemove = new(64);
        private readonly List<int> cleanupIds = new(32);

        private float nextActivationRefreshAt;
        private float nextCleanupAt;
        private float nextPopulationSyncAt;
        private float nextRecycleSyncAt;
        private ActivationRuntimeSnapshot lastRuntimeSnapshot;

        public int ActiveCount => active.Count;

        public ActivationRuntimeSnapshot GetRuntimeSnapshot()
        {
            ActivationRuntimeSnapshot snapshot = lastRuntimeSnapshot;
            snapshot.frame = Time.frameCount;
            snapshot.activeCount = active.Count;
            return snapshot;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            EnemyQueryService.ConfigureGlobalBudget(Mathf.Max(140, maxActiveEnemies * 7));
        }

        private void Start()
        {
            if (prewarmPerEnemyType > 0)
                StartCoroutine(PrewarmEnemyPoolsAsync());
        }

        private void Update()
        {
            if (!ChunkedProceduralLevelGenerator.WorldReady)
                return;

            EnemySimulationManager sim = EnemySimulationManager.Instance;
            if (sim == null)
                return;

            int currentMaxEnemies = DifficultyContext.ScaleSpawnCap(maxActiveEnemies);
            float refreshInterval = GetAdaptiveActivationInterval(currentMaxEnemies);
            float now = Time.time;
            if (now < nextActivationRefreshAt)
                return;

            float updateStart = Time.realtimeSinceStartup;
            int scannedCandidates = 0;
            int selectedCount = 0;
            int spawnedThisTick = 0;
            int despawnCount = 0;
            int cleanupRemoved = 0;
            bool populationSyncTriggered = false;
            bool recycleSyncTriggered = false;

            nextActivationRefreshAt = now + refreshInterval;

            if (now >= nextCleanupAt)
            {
                cleanupRemoved = CleanupDestroyed();
                nextCleanupAt = now + Mathf.Max(0.05f, cleanupRefreshInterval);
            }

            if (now >= nextPopulationSyncAt)
            {
                sim.EnsurePopulation(playerPosition);
                nextPopulationSyncAt = now + Mathf.Max(0.05f, populationSyncInterval);
                populationSyncTriggered = true;
            }

            if (now >= nextRecycleSyncAt)
            {
                sim.RecycleFarEnemies(playerPosition);
                nextRecycleSyncAt = now + Mathf.Max(0.05f, recycleSyncInterval);
                recycleSyncTriggered = true;
            }

            float maxD2 = activeDistance * activeDistance;
            nearbyCandidates.Clear();

            foreach (EnemySimState state in sim.GetAll())
            {
                scannedCandidates++;
                Vector3 pos = GetEnemyWorldPosition(state);
                float sqrDistance = SqrDistanceXZ(pos, playerPosition);
                if (sqrDistance <= maxD2)
                {
                    nearbyCandidates.Add(
                        new EnemyCandidate
                        {
                            state = state,
                            sqrDistance = sqrDistance
                        }
                    );
                }
            }

            int allowed = Mathf.Min(currentMaxEnemies, nearbyCandidates.Count);
            selectedCount = SelectClosestCandidates(allowed);

            int spawnBudget = Mathf.Max(1, maxActivationSpawnsPerTick);
            for (int i = 0; i < selectedCount; i++)
            {
                if (spawnedThisTick >= spawnBudget)
                    break;

                EnemySimState state = selectedCandidates[i].state;
                if (active.TryGetValue(state.id, out GameObject existing))
                {
                    if (existing != null)
                        continue;

                    active.Remove(state.id);
                }

                SpawnGO(state);
                spawnedThisTick++;
            }

            idsToRemove.Clear();

            foreach (KeyValuePair<int, GameObject> kv in active)
            {
                GameObject go = kv.Value;
                if (go == null)
                {
                    idsToRemove.Add(kv.Key);
                    continue;
                }

                if (SqrDistanceXZ(go.transform.position, playerPosition) > maxD2)
                    idsToRemove.Add(kv.Key);
            }

            int despawnBudget = Mathf.Max(1, maxActivationDespawnsPerTick);
            despawnCount = Mathf.Min(idsToRemove.Count, despawnBudget);
            for (int i = 0; i < despawnCount; i++)
                DespawnGO(idsToRemove[i]);

            lastRuntimeSnapshot = new ActivationRuntimeSnapshot
            {
                frame = Time.frameCount,
                activeCount = active.Count,
                scaledActiveCap = currentMaxEnemies,
                refreshInterval = refreshInterval,
                scannedCandidates = scannedCandidates,
                selectedCandidates = selectedCount,
                spawnedThisTick = spawnedThisTick,
                despawnedThisTick = despawnCount,
                cleanupRemoved = cleanupRemoved,
                populationSyncTriggered = populationSyncTriggered ? 1 : 0,
                recycleSyncTriggered = recycleSyncTriggered ? 1 : 0,
                updateDurationMs = (Time.realtimeSinceStartup - updateStart) * 1000f
            };
        }

        private void SpawnGO(EnemySimState state)
        {
            if (enemyPrefabs == null || enemyPrefabs.Count == 0)
                return;

            if (state.prefabIndex < 0 || state.prefabIndex >= enemyPrefabs.Count)
                return;

            EnemySimulationManager sim = EnemySimulationManager.Instance;
            if (sim == null || state.prefabIndex >= sim.enemyTypes.Count)
                return;

            EnemySpawnEntry type = sim.enemyTypes[state.prefabIndex];
            if (type == null || type.prefab == null)
                return;

            Vector3 spawnPos = ResolveSpawnPosition(state, sim);

            GameObject go = SimplePool.Get(type.prefab);
            go.transform.SetPositionAndRotation(spawnPos, Quaternion.identity);

            Rigidbody rb = go.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }

            Combatant combatant = go.GetComponent<Combatant>();
            EnemyCombatant enemyCombatant = go.GetComponent<EnemyCombatant>();

            if (combatant != null && enemyCombatant != null && enemyCombatant.stats != null)
            {
                combatant.Initialize(enemyCombatant.stats.maxHealth);
                enemyCombatant.simId = state.id;
            }
            else
            {
                Debug.LogWarning("[EnemyActivation] Enemy spawned WITHOUT Combatant init!", go);
            }

            state.position = spawnPos;
            state.anchor = spawnPos;
            active[state.id] = go;
        }

        private Vector3 ResolveSpawnPosition(EnemySimState state, EnemySimulationManager sim)
        {
            LayerMask mask = groundMask.value != 0 ? groundMask : LayerMask.GetMask("Ground");
            float rayHeight = Mathf.Max(12f, spawnRayHeight);
            float rayDistance = rayHeight * 4f;

            Vector3 spawnPos = state.position + Vector3.up * spawnYOffset;
            if (TrySnapToGround(state.position, mask, rayHeight, rayDistance, out Vector3 snapped))
                spawnPos = snapped;

            float minDistance = GetSpawnSafetyDistance(sim);
            float minDistanceSqr = minDistance * minDistance;
            if (minDistance > 0.01f && SqrDistanceXZ(spawnPos, playerPosition) < minDistanceSqr)
            {
                Vector3 away = spawnPos - playerPosition;
                away.y = 0f;
                if (away.sqrMagnitude < 0.0001f)
                {
                    Vector2 random = Random.insideUnitCircle;
                    if (random.sqrMagnitude < 0.0001f)
                        random = Vector2.right;

                    away = new Vector3(random.x, 0f, random.y);
                }

                float extraDistance = Mathf.Max(0f, spawnSafetyRandomExtraDistance);
                float distance = minDistance + (extraDistance > 0f ? Random.Range(0f, extraDistance) : 0f);
                Vector3 candidate = playerPosition + away.normalized * distance;

                if (TrySnapToGround(candidate, mask, rayHeight, rayDistance, out snapped))
                    spawnPos = snapped;
                else
                    spawnPos = candidate + Vector3.up * spawnYOffset;
            }

            return spawnPos;
        }

        private float GetSpawnSafetyDistance(EnemySimulationManager sim)
        {
            float configuredMin = Mathf.Max(1f, minSpawnDistanceFromPlayer);
            float resolved = configuredMin;
            if (sim != null)
                resolved = Mathf.Max(configuredMin, Mathf.Max(0f, sim.minSpawnDistanceFromPlayer));

            float maxAllowed = Mathf.Max(1f, activeDistance - 1f);
            return Mathf.Min(resolved, maxAllowed);
        }

        private bool TrySnapToGround(Vector3 worldPos, LayerMask mask, float rayHeight, float rayDistance, out Vector3 snapped)
        {
            float baseY = Mathf.Max(worldPos.y, playerPosition.y);
            Vector3 rayStart = new Vector3(worldPos.x, baseY + rayHeight, worldPos.z);
            if (Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit, rayDistance, mask, QueryTriggerInteraction.Ignore))
            {
                snapped = hit.point + Vector3.up * spawnYOffset;
                return true;
            }

            snapped = worldPos + Vector3.up * spawnYOffset;
            return false;
        }

        private void DespawnGO(int id)
        {
            if (!active.TryGetValue(id, out GameObject go))
                return;

            active.Remove(id);

            if (go != null)
                SimplePool.Return(go);
        }

        public void OnEnemyKilled(int simId)
        {
            DespawnGO(simId);

            EnemySimulationManager sim = EnemySimulationManager.Instance;
            if (sim != null)
                sim.RemoveEnemy(simId);

            if (GrassSim.Stats.WorldStats.Instance != null)
                GrassSim.Stats.WorldStats.Instance.AddEnemyKilled();
        }

        private Vector3 GetEnemyWorldPosition(EnemySimState state)
        {
            if (!active.TryGetValue(state.id, out GameObject go) || go == null)
            {
                active.Remove(state.id);
                return state.position;
            }

            return go.transform.position;
        }

        private int CleanupDestroyed()
        {
            cleanupIds.Clear();

            foreach (KeyValuePair<int, GameObject> kv in active)
            {
                if (kv.Value == null)
                    cleanupIds.Add(kv.Key);
            }

            for (int i = 0; i < cleanupIds.Count; i++)
                active.Remove(cleanupIds[i]);

            return cleanupIds.Count;
        }

        private static float SqrDistanceXZ(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return dx * dx + dz * dz;
        }

        private int SelectClosestCandidates(int allowed)
        {
            selectedCandidates.Clear();
            if (allowed <= 0 || nearbyCandidates.Count == 0)
                return 0;

            if (nearbyCandidates.Count <= allowed)
            {
                selectedCandidates.AddRange(nearbyCandidates);
                selectedCandidates.Sort((a, b) => a.sqrDistance.CompareTo(b.sqrDistance));
                return selectedCandidates.Count;
            }

            for (int i = 0; i < nearbyCandidates.Count; i++)
            {
                EnemyCandidate candidate = nearbyCandidates[i];
                if (selectedCandidates.Count < allowed)
                {
                    selectedCandidates.Add(candidate);
                    continue;
                }

                int farthestIndex = 0;
                float farthestDistance = selectedCandidates[0].sqrDistance;
                for (int j = 1; j < selectedCandidates.Count; j++)
                {
                    float candidateDistance = selectedCandidates[j].sqrDistance;
                    if (candidateDistance > farthestDistance)
                    {
                        farthestDistance = candidateDistance;
                        farthestIndex = j;
                    }
                }

                if (candidate.sqrDistance < farthestDistance)
                    selectedCandidates[farthestIndex] = candidate;
            }

            selectedCandidates.Sort((a, b) => a.sqrDistance.CompareTo(b.sqrDistance));
            return selectedCandidates.Count;
        }

        private float GetAdaptiveActivationInterval(int scaledActiveCap)
        {
            float baseInterval = Mathf.Max(0.02f, activationRefreshInterval);
            if (maxActiveEnemies <= 0 || scaledActiveCap <= maxActiveEnemies)
                return baseInterval;

            float overload = (scaledActiveCap - maxActiveEnemies) / (float)maxActiveEnemies;
            float t = Mathf.Clamp01(overload);
            return baseInterval * Mathf.Lerp(1f, 1.35f, t);
        }

        private System.Collections.IEnumerator PrewarmEnemyPoolsAsync()
        {
            EnemySimulationManager sim = EnemySimulationManager.Instance;
            if (sim == null || sim.enemyTypes == null)
                yield break;

            int ops = 0;
            int perType = Mathf.Max(0, prewarmPerEnemyType);
            int opsBudget = Mathf.Max(1, prewarmOpsPerFrame);

            for (int i = 0; i < sim.enemyTypes.Count; i++)
            {
                EnemySpawnEntry type = sim.enemyTypes[i];
                if (type == null || type.prefab == null)
                    continue;

                for (int j = 0; j < perType; j++)
                {
                    GameObject go = SimplePool.Get(type.prefab);
                    if (go != null)
                        SimplePool.Return(go);

                    ops++;
                    if (ops >= opsBudget)
                    {
                        ops = 0;
                        yield return null;
                    }
                }
            }
        }
    }
}
