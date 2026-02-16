using UnityEngine;

public class HeightProvider
{
    float[,] heightMap;
    ChunkedProceduralLevelGenerator gen;

    public HeightProvider(
        ChunkedProceduralLevelGenerator gen,
        float[,] heightMap,
        float[,] biomeNoiseMap,
        Vector2Int coord,
        BiomeDefinition biome
    )
    {
        this.gen = gen;
        this.heightMap = heightMap;
    }

    public float GetWorldHeight(int x, int z)
    {
        return heightMap[x, z] * gen.heightMultiplier;
    }
}
