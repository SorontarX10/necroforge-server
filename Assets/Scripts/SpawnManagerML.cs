using System.Collections.Generic;
using UnityEngine;
using GrassSim.Combat;

public class SpawnManagerML : MonoBehaviour
{
    [Header("Prefabs")]
    public GameObject enemyPrefab;
    public GameObject zombiePrefab;
    public GameObject simulatedPlayerPrefab;

    [Header("Counts")]
    public int enemyCount = 5;
    public int zombieCount = 8;
    public int simPlayerCount = 1;

    [Header("Spawn Settings")]
    public float spawnRadius = 20f;
    public float minDistanceFromPlayer = 5f;
    public LayerMask groundMask;

    [Header("Fallback Health (only if agent missing)")]
    public float defaultSpawnHealth = 100f;

    private readonly List<GameObject> enemies = new();
    private readonly List<GameObject> zombies = new();
    private readonly List<GameObject> simPlayers = new();

    private Transform player;

    private void Awake()
    {
        player = GameObject.FindGameObjectWithTag("Player")?.transform;
        SpawnInitial();
    }

    private void Update()
    {
        MaintainCounts();
    }

    // --------------------------------------------------
    // SPAWN FLOW
    // --------------------------------------------------

    private void SpawnInitial()
    {
        SpawnList(enemyPrefab, enemyCount, enemies);
        SpawnList(zombiePrefab, zombieCount, zombies);
        SpawnList(simulatedPlayerPrefab, simPlayerCount, simPlayers);
    }

    private void MaintainCounts()
    {
        SpawnList(enemyPrefab, enemyCount, enemies);
        SpawnList(zombiePrefab, zombieCount, zombies);
        SpawnList(simulatedPlayerPrefab, simPlayerCount, simPlayers);
    }

    private void SpawnList(GameObject prefab, int targetCount, List<GameObject> list)
    {
        if (prefab == null)
            return;

        CleanupDead(list);

        while (list.Count < targetCount)
        {
            if (!TrySpawn(prefab, list))
                break;
        }
    }

    // --------------------------------------------------
    // SPAWN SINGLE
    // --------------------------------------------------

    private bool TrySpawn(GameObject prefab, List<GameObject> list)
    {
        if (!TryGetValidPosition(out Vector3 pos))
            return false;

        GameObject go = Instantiate(prefab, pos, Quaternion.identity);
        list.Add(go);

        InitializeCombatant(go);
        HookDeath(go, list);

        return true;
    }

    // --------------------------------------------------
    // COMBATANT INIT / DEATH
    // --------------------------------------------------

    private void InitializeCombatant(GameObject go)
    {
        Combatant combatant = go.GetComponent<Combatant>();
        if (combatant == null)
            return;

        // 1️⃣ Jeśli to Agent (ML) – on ma własne maxHealth
        var enemyAgent = go.GetComponent<EnemyAgent>();
        if (enemyAgent != null)
        {
            combatant.Initialize(enemyAgent.maxHealth);
            return;
        }

        var simPlayerAgent = go.GetComponent<SimulatedPlayerAgent>();
        if (simPlayerAgent != null)
        {
            combatant.Initialize(simPlayerAgent.maxHealth);
            return;
        }

        // 2️⃣ Fallback – stała wartość
        combatant.Initialize(defaultSpawnHealth);
    }

    private void HookDeath(GameObject go, List<GameObject> list)
    {
        Combatant combatant = go.GetComponent<Combatant>();
        if (combatant == null)
            return;

        // wykorzystujemy SendMessage z Combatant.Die()
        var deathHook = go.AddComponent<SpawnDeathHook>();
        deathHook.Init(go, list);
    }

    private void CleanupDead(List<GameObject> list)
    {
        for (int i = list.Count - 1; i >= 0; i--)
        {
            if (list[i] == null)
                list.RemoveAt(i);
        }
    }

    // --------------------------------------------------
    // POSITION SAMPLING
    // --------------------------------------------------

    private bool TryGetValidPosition(out Vector3 result)
    {
        for (int i = 0; i < 30; i++)
        {
            Vector2 rnd = Random.insideUnitCircle * spawnRadius;
            Vector3 p = transform.position + new Vector3(rnd.x, 0f, rnd.y);

            if (player != null && Vector3.Distance(p, player.position) < minDistanceFromPlayer)
                continue;

            if (Physics.Raycast(
                p + Vector3.up * 50f,
                Vector3.down,
                out RaycastHit hit,
                100f,
                groundMask
            ))
            {
                result = hit.point;
                return true;
            }
        }

        result = Vector3.zero;
        return false;
    }

    // --------------------------------------------------
    // INNER HELPER
    // --------------------------------------------------

    private class SpawnDeathHook : MonoBehaviour
    {
        private GameObject owner;
        private List<GameObject> list;

        public void Init(GameObject owner, List<GameObject> list)
        {
            this.owner = owner;
            this.list = list;
        }

        // wywoływane przez Combatant.SendMessage("OnCombatantDied")
        private void OnCombatantDied()
        {
            if (list != null)
                list.Remove(owner);

            Destroy(owner);
        }
    }
}
