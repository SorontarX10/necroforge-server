using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

[BurstCompile]
public struct HeightmapBiomeJob : IJobParallelFor
{
    public int verts;
    public float worldSize;

    public float heightScale;
    public float biomeScale;

    public float offsetX;
    public float offsetZ;

    public float chunkOffsetX;
    public float chunkOffsetZ;

    public NativeArray<float> heights;
    public NativeArray<float> biomeValues;

    public void Execute(int index)
    {
        int z = index / verts;
        int x = index % verts;

        float step = worldSize / (verts - 1);

        float worldX = chunkOffsetX + x * step;
        float worldZ = chunkOffsetZ + z * step;

        // =========================
        // GLOBAL HEIGHT (NO BIOMES)
        // =========================
        float noise = 0f;
        float amp = 1f;
        float freq = heightScale;
        float totalAmp = 0f;

        for (int i = 0; i < 4; i++)
        {
            float n = Mathf.PerlinNoise(
                (worldX + offsetX) * freq,
                (worldZ + offsetZ) * freq
            );

            noise += n * amp;
            totalAmp += amp;
            amp *= 0.5f;
            freq *= 2f;
        }

        heights[index] = noise / totalAmp;

        // =========================
        // BIOME NOISE (SEPARATE)
        // =========================
        biomeValues[index] = Mathf.PerlinNoise(
            (worldX + offsetX) * biomeScale,
            (worldZ + offsetZ) * biomeScale
        );
    }
}
