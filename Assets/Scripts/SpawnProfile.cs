using UnityEngine;
using System.Collections.Generic;

[System.Serializable]
public class SpawnProfile
{
    public string name;

    [Header("Prefabs")]
    public List<GameObject> prefabs;

    [Header("Spawn Count")]
    public int minCount;
    public int maxCount;

    [Header("Rules")]
    [Range(0f, 1f)] public float spawnChance = 1f;
    public bool randomRotationY = true;

    [Header("Scale")]
    public float minScale = 1f;
    public float maxScale = 1f;

    [Header("Placement")]
    public float minDistance = 3f;

    [Header("Type")]
    public bool isTree = false;
    public int enemyPrefabIndex = 0;
    public bool isEnemy;

    [Header("Density")]
    [Min(0)]
    public int spawnAttempts = 500;
    public int maxPerChunk = 500;
    public float densityPer10m2 = 0f;

    [Header("Streaming")]
    [Tooltip("If true, spawned objects stay active even when the source chunk is hidden. Use for atmospheric fog.")]
    public bool persistWhenChunkHidden = false;

    public enum SpawnType
    {
        Environment,
        Enemy
    }

    public SpawnType spawnType = SpawnType.Environment;
}
