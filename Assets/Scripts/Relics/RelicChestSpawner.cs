using System.Collections;
using UnityEngine;

public class RelicChestSpawner : MonoBehaviour
{
    [Header("Refs")]
    public ChunkedProceduralLevelGenerator world;
    public GameObject chestPrefab;

    [Header("Spawn Settings")]
    public int chestsPerWave = 15;
    public float phaseADelay = 5f;
    [Min(0.01f)] public float spawnedChestScaleMultiplier = 0.5f;

    private bool phaseASpawned;
    private bool phaseBSpawned;
    private bool phaseCSpawned;

    private void Start()
    {
        StartCoroutine(WaitForWorldAndSpawnPhaseA());

        if (GameTimerController.Instance != null)
            GameTimerController.Instance.OnPhaseChanged += OnPhaseChanged;
        else
            Debug.LogError("[RelicChestSpawner] GameTimerController missing!");
    }

    private void OnDestroy()
    {
        if (GameTimerController.Instance != null)
            GameTimerController.Instance.OnPhaseChanged -= OnPhaseChanged;
    }

    // Phase A spawn (default flow).
    private IEnumerator WaitForWorldAndSpawnPhaseA()
    {
        while (!ChunkedProceduralLevelGenerator.WorldReady)
            yield return null;

        if (phaseASpawned)
            yield break;

        if (phaseADelay > 0f)
            yield return new WaitForSeconds(phaseADelay);

        if (phaseASpawned)
            yield break;

        if (SpawnWave("PhaseA"))
            phaseASpawned = true;
    }

    // Used by map bootstrap to make sure chests exist before map bake.
    public void EnsureInitialWaveSpawnedNow()
    {
        if (phaseASpawned)
            return;

        if (SpawnWave("PhaseA_PreMapBake"))
            phaseASpawned = true;
    }

    private void OnPhaseChanged(GameTimerController.TimerPhase phase)
    {
        switch (phase)
        {
            case GameTimerController.TimerPhase.PhaseB:
                if (!phaseBSpawned)
                {
                    if (SpawnWave("PhaseB"))
                        phaseBSpawned = true;
                }
                break;

            case GameTimerController.TimerPhase.PhaseC:
                if (!phaseCSpawned)
                {
                    if (SpawnWave("PhaseC"))
                        phaseCSpawned = true;
                }
                break;
        }
    }

    private bool SpawnWave(string reason)
    {
        if (world == null)
            world = FindFirstObjectByType<ChunkedProceduralLevelGenerator>();

        if (world == null || chestPrefab == null)
        {
            Debug.LogError("[RelicChestSpawner] Missing refs!");
            return false;
        }

        Debug.Log($"[RelicChestSpawner] Spawning chests: {reason}");

        for (int i = 0; i < chestsPerWave; i++)
        {
            Vector3 pos = world.GetRandomValidWorldPosition();
            GameObject chest = Instantiate(chestPrefab, pos, Quaternion.identity);
            if (chest != null && !Mathf.Approximately(spawnedChestScaleMultiplier, 1f))
                chest.transform.localScale *= spawnedChestScaleMultiplier;
        }

        return true;
    }
}
