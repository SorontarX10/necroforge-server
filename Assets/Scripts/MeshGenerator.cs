using UnityEngine;

public static class MeshGenerator
{
    /// <summary>
    /// verts = ilość vertexów w osi (chunkResolution + 1)
    /// step  = chunkSize / (verts - 1)
    /// </summary>
    public static Mesh GenerateTerrainMesh(
        HeightProvider heightProvider,
        int verts,
        float step
    )
    {
        int vertCount = verts * verts;
        Vector3[] vertices = new Vector3[vertCount];
        Vector2[] uvs = new Vector2[vertCount];

        int quadCount = (verts - 1) * (verts - 1);
        int[] triangles = new int[quadCount * 6];

        // =========================
        // VERTICES
        // =========================
        int v = 0;
        for (int z = 0; z < verts; z++)
        {
            for (int x = 0; x < verts; x++)
            {
                float y = heightProvider.GetWorldHeight(x, z);

                vertices[v] = new Vector3(
                    x * step,
                    y,
                    z * step
                );

                uvs[v] = new Vector2(
                    x / (float)(verts - 1),
                    z / (float)(verts - 1)
                );

                v++;
            }
        }

        // =========================
        // TRIANGLES
        // =========================
        int t = 0;
        int vi = 0;

        for (int z = 0; z < verts - 1; z++)
        {
            for (int x = 0; x < verts - 1; x++)
            {
                triangles[t++] = vi;
                triangles[t++] = vi + verts;
                triangles[t++] = vi + 1;

                triangles[t++] = vi + 1;
                triangles[t++] = vi + verts;
                triangles[t++] = vi + verts + 1;

                vi++;
            }
            vi++;
        }

        Mesh mesh = new Mesh
        {
            indexFormat = vertCount > 65000
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16
        };

        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.uv = uvs;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        return mesh;
    }
}
