using UnityEngine;

[System.Serializable]
public class EnemySpawnEntry
{
    public GameObject prefab;

    [Tooltip("Waga losowania (nie procent). 1 = rzadki, 10 = częsty")]
    public float spawnWeight = 1f;
}
