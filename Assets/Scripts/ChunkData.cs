using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Unity.Jobs;
using Unity.Collections;
using GrassSim.AI;

public class ChunkData
{
    public Vector2Int coord;
    public GameObject root;

    float[,] heightMap;
    float[,] biomeNoiseMap;

    HeightProvider heightProvider;

    int RES;
    float step;
    ChunkedProceduralLevelGenerator gen;

    public BiomeDefinition chunkBiome;
    public Bounds worldBounds;

    List<GameObject> spawnedObjects = new();
    private readonly List<Vector3> usedAll = new();

    public ChunkData(Vector2Int coord, ChunkedProceduralLevelGenerator gen)
    {
        this.coord = coord;
        this.gen = gen;

        RES = gen.chunkResolution;
        step = (float)gen.chunkSize / RES;

        root = new GameObject($"Chunk_{coord.x}_{coord.y}");
        root.transform.position = new Vector3(
            coord.x * gen.chunkSize,
            0,
            coord.y * gen.chunkSize
        );
    }

    public IEnumerator GenerateAsync()
    {
        yield return GenerateMapsAsync();
        GenerateTerrain();

        // ENVIRONMENT (drzewa, kamienie itp.)
        if (chunkBiome != null)
            yield return SpawnProfiles(chunkBiome.environmentProfiles);
        
    }

    IEnumerator GenerateMapsAsync()
    {
        int verts = gen.chunkResolution + 1;
        int size = verts * verts;

        NativeArray<float> heights = default;
        NativeArray<float> biomes = default;

        try
        {
            heights = new NativeArray<float>(size, Allocator.TempJob);
            biomes = new NativeArray<float>(size, Allocator.TempJob);

            var job = new HeightmapBiomeJob
            {
                verts = verts,
                worldSize = gen.chunkSize,

                heightScale = gen.heightNoiseScale,
                biomeScale = gen.biomeNoiseScale,

                offsetX = gen.noiseOffsetX,
                offsetZ = gen.noiseOffsetZ,

                chunkOffsetX = coord.x * gen.chunkSize,
                chunkOffsetZ = coord.y * gen.chunkSize,

                heights = heights,
                biomeValues = biomes
            };

            JobHandle handle = job.Schedule(size, 64);
            handle.Complete();

            heightMap = new float[verts, verts];
            biomeNoiseMap = new float[verts, verts];

            for (int z = 0; z < verts; z++)
            {
                for (int x = 0; x < verts; x++)
                {
                    int i = z * verts + x;
                    heightMap[x, z] = heights[i];
                    biomeNoiseMap[x, z] = biomes[i];
                }
            }

            chunkBiome = PickBiomeByNoise(
                biomeNoiseMap[verts / 2, verts / 2]
            );

            if (chunkBiome == null)
                chunkBiome = gen.biomes[0];

            heightProvider = new HeightProvider(
                gen,
                heightMap,
                biomeNoiseMap,
                coord,
                chunkBiome
            );
        }
        finally
        {
            if (heights.IsCreated) heights.Dispose();
            if (biomes.IsCreated) biomes.Dispose();
        }
        yield break;
    }

    void GenerateTerrain()
    {
        int verts = gen.chunkResolution + 1;
        float step = gen.chunkSize / gen.chunkResolution;

        Mesh mesh = MeshGenerator.GenerateTerrainMesh(
            heightProvider,
            verts,
            step
        );

        GameObject terrain = new GameObject("Terrain");
        terrain.transform.SetParent(root.transform, false);
        terrain.layer = LayerMask.NameToLayer("Ground");

        terrain.AddComponent<MeshFilter>().mesh = mesh;
        terrain.AddComponent<MeshRenderer>().material = chunkBiome.terrainMaterial;
        terrain.AddComponent<MeshCollider>().sharedMesh = mesh;
    }

    IEnumerator SpawnContentAsync()
    {
        if (chunkBiome == null || chunkBiome.environmentProfiles == null)
            yield break;

        // globalne pozycje użyte w chunku (żeby różne profile nie wchodziły w siebie)
        List<Vector3> usedAll = new List<Vector3>(256);

        int yieldEvery = Mathf.Max(1, gen.spawnsPerFrame);
        int ops = 0;

        foreach (var profile in chunkBiome.environmentProfiles)
        {
            if (profile == null || profile.prefabs == null || profile.prefabs.Count == 0)
                continue;

            int targetCount = Random.Range(profile.minCount, profile.maxCount + 1);
            if (targetCount <= 0)
                continue;

            int spawned = 0;
            int attempts = 0;

            // ważne: maxAttempts liczymy od targetCount, nie od maxCount (bo często jest 1)
            int maxAttempts = Mathf.Max(profile.spawnAttempts, targetCount * 30);

            Debug.Log(
                $"[Spawn] Chunk {coord} | Profile '{profile.name}' " +
                $"target={targetCount} attempts={maxAttempts} chance={profile.spawnChance} " +
                $"minDist={profile.minDistance} isTree={profile.isTree} isEnemy={profile.isEnemy}"
            );

            while (spawned < targetCount && attempts < maxAttempts)
            {
                attempts++;

                int x = Random.Range(0, RES);
                int z = Random.Range(0, RES);

                Vector3 rayStart =
                    root.transform.position +
                    new Vector3(
                        x * step,
                        gen.heightMultiplier + 30f,
                        z * step
                    );

                // 1) Raycast tylko w Ground
                if (!Physics.Raycast(
                        rayStart,
                        Vector3.down,
                        out RaycastHit hit,
                        gen.heightMultiplier * 6f,
                        LayerMask.GetMask("Ground")
                    ))
                    continue;

                // 2) Szansa spawnu
                if (Random.value > profile.spawnChance)
                    continue;

                Vector3 spawnPos = hit.point;

                // 3) Minimalny dystans w chunku (między WSZYSTKIMI obiektami)
                float spacing = profile.minDistance;

                // drzewa: zwykle większe modele => dystans zwiększamy, nie zmniejszamy
                if (profile.isTree)
                    spacing *= 1.2f;

                float spacingSqr = spacing * spacing;

                bool tooClose = false;
                for (int i = 0; i < usedAll.Count; i++)
                {
                    Vector3 d = usedAll[i] - spawnPos;
                    d.y = 0f; // dystans w poziomie
                    if (d.sqrMagnitude < spacingSqr)
                    {
                        tooClose = true;
                        break;
                    }
                }

                if (tooClose)
                    continue;

                // 4) Dodatkowy check kolizji (enemy vs enemy / env vs env)
                // (opcjonalne, ale pomaga gdy obiekty mają collidery)
                int mask;
                if (profile.isEnemy)
                    mask = LayerMask.GetMask("Enemy", "Environment"); // enemy nie wchodzą w drzewa
                else if (profile.isTree)
                    mask = LayerMask.GetMask("Environment", "Enemy");
                else
                    mask = LayerMask.GetMask("Environment");

                // promień kolizyjny (możesz dopasować)
                float overlapRadius = spacing * 0.5f;

                if (Physics.CheckSphere(spawnPos + Vector3.up * 0.25f, overlapRadius, mask))
                    continue;

                // 5) Spawn
                GameObject prefab =
                    profile.prefabs[Random.Range(0, profile.prefabs.Count)];

                GameObject go = SimplePool.Get(prefab);
                go.transform.SetParent(root.transform, false);
                go.transform.position = spawnPos;

                // 6) Rotacja
                if (profile.randomRotationY)
                    go.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
                else
                    go.transform.rotation = Quaternion.identity;

                // 7) Skala
                float scale = Random.Range(profile.minScale, profile.maxScale);
                go.transform.localScale = Vector3.one * scale;

                usedAll.Add(spawnPos);

                spawned++;
                ops++;

                if (ops >= yieldEvery)
                {
                    ops = 0;
                    yield return null;
                }
            }

            Debug.Log(
                $"[Spawn] Chunk {coord} | Profile '{profile.name}' " +
                $"spawned={spawned}/{targetCount} attemptsUsed={attempts}/{maxAttempts}"
            );
        }
    }

    BiomeDefinition PickBiomeByNoise(float noise)
    {
        foreach (var b in gen.biomes)
            if (b != null && noise >= b.minBiomeNoise && noise <= b.maxBiomeNoise)
                return b;

        return gen.biomes[0];
    }

    public float GetHeightAt(int x, int z)
    {
        return heightProvider.GetWorldHeight(x, z);
    }

    public void SetRendered(bool visible)
    {
        if (root == null)
            return;

        if (root.activeSelf == visible)
            return;

        root.SetActive(visible);
    }

    Bounds GetWorldBounds(GameObject go)
    {
        Renderer[] rs = go.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0)
            return new Bounds(go.transform.position, Vector3.zero);

        Bounds b = rs[0].bounds;
        for (int i = 1; i < rs.Length; i++)
            b.Encapsulate(rs[i].bounds);

        return b;
    }

    bool CanSpawn(SpawnProfile profile, Vector3 pos)
    {
        LayerMask mask = LayerMask.GetMask("Environment");

        return !Physics.CheckSphere(pos, profile.minDistance, mask);
    }

    IEnumerator SpawnProfiles(List<SpawnProfile> profiles)
    {
        if (profiles == null || profiles.Count == 0)
            yield break;

        int yieldEvery = Mathf.Max(1, gen.spawnsPerFrame);
        int ops = 0;

        foreach (var profile in profiles)
        {
            if (profile == null || profile.prefabs == null || profile.prefabs.Count == 0)
                continue;

            if (profile.isEnemy)
                continue; // ⬅⬅⬅ KLUCZ: ENV ONLY

            int targetCount = Random.Range(profile.minCount, profile.maxCount + 1);
            if (targetCount <= 0)
                continue;

            int spawned = 0;
            int attempts = 0;
            int maxAttempts = Mathf.Max(profile.spawnAttempts, targetCount * 20);

            while (spawned < targetCount && attempts < maxAttempts)
            {
                attempts++;

                int x = Random.Range(0, RES);
                int z = Random.Range(0, RES);

                Vector3 rayStart =
                    root.transform.position +
                    new Vector3(x * step, gen.heightMultiplier + 50f, z * step);

                if (!Physics.Raycast(
                        rayStart,
                        Vector3.down,
                        out RaycastHit hit,
                        gen.heightMultiplier * 10f,
                        LayerMask.GetMask("Ground")
                    ))
                    continue;

                Vector3 spawnPos = hit.point;

                // dystans poziomy
                bool tooClose = false;
                float minDistSqr = profile.minDistance * profile.minDistance;

                foreach (var p in usedAll)
                {
                    Vector3 d = p - spawnPos;
                    d.y = 0f;
                    if (d.sqrMagnitude < minDistSqr)
                    {
                        tooClose = true;
                        break;
                    }
                }

                if (tooClose)
                    continue;

                GameObject prefab =
                    profile.prefabs[Random.Range(0, profile.prefabs.Count)];

                GameObject go = SimplePool.Get(prefab);
                go.transform.SetParent(root.transform, false);
                go.transform.position = spawnPos;

                if (profile.randomRotationY)
                    go.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

                go.transform.localScale = Vector3.one *
                    Random.Range(profile.minScale, profile.maxScale);

                spawnedObjects.Add(go);
                usedAll.Add(spawnPos);
                spawned++;
                ops++;

                if (ops >= yieldEvery)
                {
                    ops = 0;
                    yield return null;
                }
            }
        }
    }

    Bounds GetWorldBoundsPreferColliders(GameObject go)
    {
        var cols = go.GetComponentsInChildren<Collider>();
        if (cols != null && cols.Length > 0)
        {
            Bounds b = cols[0].bounds;
            for (int i = 1; i < cols.Length; i++)
                b.Encapsulate(cols[i].bounds);
            return b;
        }

        var rs = go.GetComponentsInChildren<Renderer>();
        if (rs != null && rs.Length > 0)
        {
            Bounds b = rs[0].bounds;
            for (int i = 1; i < rs.Length; i++)
                b.Encapsulate(rs[i].bounds);
            return b;
        }

        return new Bounds(go.transform.position, Vector3.zero);
    }
    
    void SnapToGroundByCollider(GameObject go, Vector3 groundPoint)
    {
        Physics.SyncTransforms();

        Collider col = go.GetComponentInChildren<Collider>();
        if (col == null)
            return;

        Bounds b = col.bounds;

        float deltaY = groundPoint.y - b.min.y;
        go.transform.position += Vector3.up * deltaY;
    }
}
