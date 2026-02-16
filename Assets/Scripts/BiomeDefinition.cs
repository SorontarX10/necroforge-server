using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "BiomeDefinition", menuName = "World/Biome")]
public class BiomeDefinition : ScriptableObject
{
    [Header("Info")]
    public string biomeName;
    public BiomeType biomeType;

    [Header("Shader Biome Blend")]
    [Range(0f, 1f)] public float biomeCenter = 0.5f;
    [Range(0f, 0.5f)] public float biomeEdgeWidth = 0.12f;
    [Range(0f, 1f)] public float biomeEdgeStrength = 0.35f;


    [Header("Biome Selection")]
    [Range(0f, 1f)] public float minBiomeNoise;
    [Range(0f, 1f)] public float maxBiomeNoise;

    [Header("Height Constraints")]
    [Header("Terrain Height Limits")]
    public float maxBiomeHeight = 5f;

    [Header("Height Shaping")]
    public float biomeHeightMultiplier = 1.0f; // np. forest 1.3, plains 0.8
    public float detailHeightStrength  = 0.1f;   // ile mikro-detalu
    public float heightExponent = 1f;

    [Header("Terrain Height")]
    public float heightMultiplier = 1f;

    [Header("Spawn Rules")]
    public float minSpacing = 2.5f;
    public LayerMask environmentMask;

    [Header("Biome Blending")]
    [Range(0f, 0.5f)]
    public float blendWidth = 0.15f; // procent szerokości chunka

    [Header("Terrain")]
    public Material terrainMaterial;
    public Vector2 textureTile = new Vector2(10, 10);

    [Header("Environment Profiles")]
    public List<SpawnProfile> environmentProfiles;

    [Header("Enemy Profiles")]
    public List<SpawnProfile> enemyProfiles;

    [Header("Ally Profiles")]
    public List<SpawnProfile> allyProfiles;

}

public enum BiomeType
{
    Meadow,
    Forest,
    Snow,
    Desert,
    Swamp
}
