using UnityEngine;

public static class Noise
    {
        public static float FBM(
        float x,
        float z,
        int octaves,
        float persistence,
        float offsetX,
        float offsetZ
    )
    {
        float total = 0f;
        float amplitude = 1f;
        float frequency = 1f;
        float maxValue = 0f;

        for (int i = 0; i < octaves; i++)
        {
            float perlin = Mathf.PerlinNoise(
                (x + offsetX) * frequency,
                (z + offsetZ) * frequency
            );

            total += perlin * amplitude;
            maxValue += amplitude;

            amplitude *= persistence;
            frequency *= 2f;
        }

        return total / maxValue;
    }
}
