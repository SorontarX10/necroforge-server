using UnityEngine;
using System.Collections.Generic;
using UnityEngine.SceneManagement;

public class SpawnManager : MonoBehaviour
{
    private const string TestSceneName = "TestScene";

    [Header("Spawn Area")]
    public Vector3 center = Vector3.zero;
    public Vector3 size = new Vector3(50f, 0f, 50f);

    [Header("Prefabs")]
    public GameObject enemyPrefab;
    public GameObject zombiePrefab;

    [Header("Counts")]
    public int enemyCount = 5;
    public int zombieCount = 10;

    [Header("Combat Distances")]
    public float playerAttackRange = 2.5f;
    public float unitAttackRange = 2.0f;
    public float spawnOffset = 1.0f;

    [Header("Rules")]
    public int maxAttemptsPerSpawn = 40;

    [Header("References")]
    public Transform player;

    [SerializeField]
    private LayerMask groundLayer;

    List<Vector3> usedPositions = new List<Vector3>();

    void Start()
    {
        Scene activeScene = SceneManager.GetActiveScene();
        if (activeScene.IsValid() && string.Equals(activeScene.name, TestSceneName, System.StringComparison.Ordinal))
        {
            enabled = false;
            return;
        }

        if (!player)
        {
            Debug.LogError("SpawnManager: Player reference missing!");
            return;
        }

        SpawnGroup(enemyPrefab, enemyCount);
        SpawnGroup(zombiePrefab, zombieCount);
    }

    // =========================
    // SPAWN GROUP
    // =========================
    void SpawnGroup(GameObject prefab, int count)
    {
        for (int i = 0; i < count; i++)
        {
            if (TryGetValidPosition(out Vector3 pos))
            {
                // dopasuj wysokość Y względem ziemi
                pos = AdjustYToGround(pos);

                Instantiate(prefab, pos, Quaternion.identity);
                usedPositions.Add(pos);
            }
            else
            {
                Debug.LogWarning($"SpawnManager: failed to place {prefab.name}");
            }
        }
    }

    // =========================
    // POSITION VALIDATION
    // =========================
    bool TryGetValidPosition(out Vector3 position)
    {
        for (int i = 0; i < maxAttemptsPerSpawn; i++)
        {
            Vector3 candidate = GetRandomPointInArea();

            if (!IsFarEnoughFromPlayer(candidate))
                continue;

            if (!IsFarEnoughFromUnits(candidate))
                continue;

            position = candidate + new Vector3(0,2,0);
            return true;
        }

        position = Vector3.zero;
        return false;
    }

    bool IsFarEnoughFromPlayer(Vector3 pos)
    {
        float minDist = playerAttackRange + spawnOffset;
        return Vector3.Distance(pos, player.position) >= minDist;
    }

    bool IsFarEnoughFromUnits(Vector3 pos)
    {
        float minDist = unitAttackRange + spawnOffset;

        foreach (var used in usedPositions)
        {
            if (Vector3.Distance(pos, used) < minDist)
                return false;
        }

        return true;
    }

    // =========================
    // RANDOM POINT
    // =========================
    Vector3 GetRandomPointInArea()
    {
        Vector3 half = size * 0.5f;

        return new Vector3(
            Random.Range(center.x - half.x, center.x + half.x),
            center.y,
            Random.Range(center.z - half.z, center.z + half.z)
        );
    }

    // =========================
    // DEBUG
    // =========================
    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.green;
        Gizmos.DrawWireCube(center, size);

        if (player)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(player.position, playerAttackRange + spawnOffset);
        }
    }

    private Vector3 AdjustYToGround(Vector3 positionXZ)
    {
        // wysokość, z której zaczynamy strzelać ray
        float raycastHeight = 10f;
        
        // start 10 jednostek nad bazową wysokością
        Vector3 rayStart = new Vector3(positionXZ.x, positionXZ.y + raycastHeight, positionXZ.z);

        if (Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit, raycastHeight * 2f, groundLayer))
        {
            // hit.point.y jest dokładną wysokością ziemi
            positionXZ.y = hit.point.y;
        }
        else
        {
            // jeśli nie trafiono w teren — opcjonalnie logujemy
            Debug.LogWarning("SpawnManager: raycast nie trafił w ziemię!");
            // zostawiamy oryginalne Y lub możesz override do 0
            positionXZ.y = 0f;
        }

        return positionXZ;
    }
}
