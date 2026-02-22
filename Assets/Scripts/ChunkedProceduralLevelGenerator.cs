using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using GrassSim.AI;
using GrassSim.Combat;
using GrassSim.Core;

public class ChunkedProceduralLevelGenerator : MonoBehaviour
{
    [Header("World")]
    public int worldSizeInChunks = 8;

    [Header("Chunks")]
    public int chunkSize = 32;
    public int chunkResolution = 32;

    [Header("Noise")]
    public float heightNoiseScale = 0.02f;
    public float heightMultiplier = 12f;

    [Header("Streaming")]
    [Min(1)] public int maxActiveChunks = 3; // ← X × X
    public int chunksPerFrame = 1;
    [Min(1)] public int maxChunkVisibilityChangesPerFrame = 3;

    [Header("Biome Noise")]
    public float biomeNoiseScale = 0.005f;

    [Header("Async Tuning")]
    // [Min(1)] public int samplesPerFrame = 512;
    [Min(1)] public int spawnsPerFrame = 8;

    [Header("Streaming Prewarm")]
    public bool prewarmShadersOnLoad = true;
    public bool prewarmChunkTerrainMaterials = true;
    [Min(1)] public int materialPrewarmOpsPerFrame = 2;

    [Header("Content")]
    public List<BiomeDefinition> biomes;
    public GameObject playerPrefab;

    [Header("Culling")]
    public float treeCullDistance = 50f;
    
    [Header("Seed")]
    [SerializeField] bool useRandomSeed = true;
    [SerializeField] int seed = 12345;

    [Header("World Border")]
    public float mountainHeight = 100f;
    public float mountainDepth = 100f;
    public int mountainResolution = 64;
    public Material mountainMaterial;


    public bool addInvisibleWall = true;
    public float invisibleWallHeight = 100f;

    [Header("Enemy Streaming")]
    public int maxEnemiesPerChunk = 30;
    public int maxEnemySpawnsPerFrame = 2;

    [Header("Enemy Prefabs For Simulation (must match EnemyActivationController.enemyPrefabs order)")]
    public List<GameObject> enemyPrefabsForSim = new();

    [HideInInspector] public float noiseOffsetX;
    [HideInInspector] public float noiseOffsetZ;

    public static bool WorldReady { get; private set; }

    Dictionary<Vector2Int, ChunkData> chunks = new();
    HashSet<Vector2Int> activeChunkCoords = new();
    readonly HashSet<Vector2Int> desiredChunkCoords = new();
    readonly List<Vector2Int> chunkCoordsToActivate = new(32);
    readonly List<Vector2Int> chunkCoordsToDeactivate = new(32);

    Transform player;
    Vector2Int lastPlayerChunk = new(int.MinValue, int.MinValue);

    [SerializeField] private GrassSim.AI.EnemyActivationController enemyActivationController;

    void Start()
    {
        WorldReady = false;
        
        if (useRandomSeed)
        {
            seed = System.Environment.TickCount;
        }

        Random.InitState(seed);

        noiseOffsetX = Random.Range(-100000f, 100000f);
        noiseOffsetZ = Random.Range(-100000f, 100000f);
        
        var sim = GrassSim.AI.EnemySimulationManager.Instance;

        StartCoroutine(GenerateWorldAsync());
    }

    IEnumerator GenerateWorldAsync()
    {
        int ops = 0;

        for (int z = 0; z < worldSizeInChunks; z++)
        {
            for (int x = 0; x < worldSizeInChunks; x++)
            {
                var coord = new Vector2Int(x, z);
                var chunk = new ChunkData(coord, this);
                chunks.Add(coord, chunk);

                yield return StartCoroutine(chunk.GenerateAsync());

                ops++;
                if (ops >= chunksPerFrame)
                {
                    ops = 0;
                    yield return null;
                }
            }
        }

        // In Editor this may generate noisy shader mismatch errors for test shaders.
        if (prewarmShadersOnLoad && !Application.isEditor)
            Shader.WarmupAllShaders();

        if (prewarmChunkTerrainMaterials)
            yield return StartCoroutine(PrewarmChunkTerrainMaterialsAsync());

        SpawnPlayer();
        if (player != null)
            lastPlayerChunk = WorldToChunkCoord(player.position);

        UpdateActiveChunks(true);
        GenerateWorldBorder();
        WorldReady = true;
    }

    void Update()
    {
        if (player == null)
            return;

        // ⬅️ KLUCZ
        if (EnemyActivationController.Instance != null)
            EnemyActivationController.Instance.playerPosition = player.position;

        Vector2Int pc = WorldToChunkCoord(player.position);
        if (pc != lastPlayerChunk)
        {
            lastPlayerChunk = pc;
            UpdateActiveChunks(false);
        }

        ProcessChunkVisibilityChanges(Mathf.Max(1, maxChunkVisibilityChangesPerFrame));
    }

    void UpdateActiveChunks(bool instant)
    {
        desiredChunkCoords.Clear();

        int side = Mathf.Max(1, maxActiveChunks);
        if (side % 2 == 0) side += 1;
        int half = side / 2;

        for (int z = -half; z <= half; z++)
        {
            for (int x = -half; x <= half; x++)
            {
                Vector2Int c = new(
                    lastPlayerChunk.x + x,
                    lastPlayerChunk.y + z
                );

                if (c.x < 0 || c.y < 0 ||
                    c.x >= worldSizeInChunks ||
                    c.y >= worldSizeInChunks)
                    continue;

                desiredChunkCoords.Add(c);
            }
        }

        if (instant)
            ProcessChunkVisibilityChanges(int.MaxValue);
    }

    void ProcessChunkVisibilityChanges(int budget)
    {
        if (budget <= 0)
            return;

        chunkCoordsToDeactivate.Clear();
        foreach (var coord in activeChunkCoords)
        {
            if (!desiredChunkCoords.Contains(coord))
                chunkCoordsToDeactivate.Add(coord);
        }

        int changes = 0;
        for (int i = 0; i < chunkCoordsToDeactivate.Count && changes < budget; i++)
        {
            Vector2Int coord = chunkCoordsToDeactivate[i];
            if (chunks.TryGetValue(coord, out var chunk))
                chunk.SetRendered(false);

            activeChunkCoords.Remove(coord);
            changes++;
        }

        if (changes >= budget)
            return;

        chunkCoordsToActivate.Clear();
        foreach (var coord in desiredChunkCoords)
        {
            if (!activeChunkCoords.Contains(coord))
                chunkCoordsToActivate.Add(coord);
        }

        for (int i = 0; i < chunkCoordsToActivate.Count && changes < budget; i++)
        {
            Vector2Int coord = chunkCoordsToActivate[i];
            if (chunks.TryGetValue(coord, out var chunk))
                chunk.SetRendered(true);

            activeChunkCoords.Add(coord);
            changes++;
        }
    }

    void SpawnPlayer()
    {
        int c = worldSizeInChunks / 2;
        var chunk = chunks[new Vector2Int(c, c)];

        LayerMask groundMask = LayerMask.GetMask("Ground");

        Vector3 basePos =
            chunk.root.transform.position +
            new Vector3(chunkSize / 2f, heightMultiplier + 50f, chunkSize / 2f);

        const int maxAttempts = 20;
        const float searchRadius = 6f;

        for (int i = 0; i < maxAttempts; i++)
        {
            Vector3 offset = new Vector3(
                Random.Range(-searchRadius, searchRadius),
                0f,
                Random.Range(-searchRadius, searchRadius)
            );

            Vector3 rayStart = basePos + offset;

            if (Physics.Raycast(
                rayStart,
                Vector3.down,
                out RaycastHit hit,
                heightMultiplier * 5f,
                groundMask
            ))
            {
                // jeśli kolizja z drzewami / environment – spróbuj dalej
                if (Physics.CheckSphere(
                    hit.point,
                    1.2f,
                    LayerMask.GetMask("Environment")
                ))
                {
                    continue;
                }

                // ✅ SPAWN GRACZA
                player = Instantiate(
                    playerPrefab,
                    hit.point + Vector3.up * 0.2f,
                    Quaternion.identity
                ).transform;
                SnapSpawnedPlayerToGround(player, hit.point);

                // init combatant
                var combatant = player.GetComponent<Combatant>();
                var prog = player.GetComponent<PlayerProgressionController>();
                if (prog != null && combatant != null)
                {
                    combatant.Initialize(prog.stats.maxHealth);
                }

                // ✅ HANDOVER KAMER
                Debug.Log("Przed enableCamera");
                EnablePlayerCameraAndDisableSceneCamera(player);
                Debug.Log("Po enableCamera");

                return;
            }
        }

        Debug.LogError("❌ Nie udało się znaleźć poprawnego miejsca spawnu gracza!");
    }

    private static void SnapSpawnedPlayerToGround(Transform playerTransform, Vector3 groundPoint)
    {
        if (playerTransform == null)
            return;

        const float groundClearance = 0.01f;
        float scaleY = Mathf.Abs(playerTransform.lossyScale.y);
        if (scaleY < 0.0001f)
            scaleY = 1f;

        Vector3 pos = playerTransform.position;
        bool snapped = false;

        CharacterController characterController = playerTransform.GetComponent<CharacterController>();
        if (characterController != null)
        {
            bool wasEnabled = characterController.enabled;
            if (wasEnabled)
                characterController.enabled = false;

            float localFeetOffset = characterController.center.y - (characterController.height * 0.5f);
            pos.y = groundPoint.y - localFeetOffset * scaleY + groundClearance;
            playerTransform.position = pos;

            if (wasEnabled)
                characterController.enabled = true;

            snapped = true;
        }
        else
        {
            CapsuleCollider capsule = playerTransform.GetComponent<CapsuleCollider>();
            if (capsule != null)
            {
                float localFeetOffset = capsule.center.y - (capsule.height * 0.5f);
                pos.y = groundPoint.y - localFeetOffset * scaleY + groundClearance;
                playerTransform.position = pos;
                snapped = true;
            }
        }

        if (!snapped)
            playerTransform.position = new Vector3(pos.x, groundPoint.y + groundClearance, pos.z);
    }

    public Vector2Int WorldToChunkCoord(Vector3 pos)
    {
        return new Vector2Int(
            Mathf.FloorToInt(pos.x / chunkSize),
            Mathf.FloorToInt(pos.z / chunkSize)
        );
    }

    public bool TryGetChunkBiome(Vector2Int coord, out BiomeDefinition biome)
    {
        if (chunks.TryGetValue(coord, out var chunk) && chunk != null)
        {
            biome = chunk.chunkBiome;
            return true;
        }

        biome = null;
        return false;
    }

    void GenerateWorldBorder()
    {
        GameObject root = new GameObject("WorldBorder");
        float worldSize = worldSizeInChunks * chunkSize;
        float borderInset = 15f;

        // SOUTH: granica z=0, na zewnątrz -Z (mesh już ma -Z jako outward)
        float wallLength = worldSize + mountainDepth * 2; 
        
        CreateMountainWall(
            position: new Vector3(0f - mountainDepth, 0f, 0f + borderInset),
            rotation: Quaternion.identity,
            length: wallLength,
            root: root.transform
        );

        // NORTH: granica z=worldSize, na zewnątrz +Z => obrót 180° (odwraca -Z na +Z)
        // Uwaga: po obrocie +X stanie się -X, więc startujemy z x=worldSize
        CreateMountainWall(
            position: new Vector3(worldSize + mountainDepth, 0f, worldSize - borderInset),
            rotation: Quaternion.Euler(0f, 180f, 0f),
            length: wallLength,
            root: root.transform
        );

        // WEST: granica x=0, na zewnątrz -X => obrót +90° (mapuje -Z -> -X)
        CreateMountainWall(
            position: new Vector3(0f + borderInset, 0f, worldSize + mountainDepth),
            rotation: Quaternion.Euler(0f, 90f, 0f),
            length: wallLength,
            root: root.transform
        );

        // EAST: granica x=worldSize, na zewnątrz +X => obrót -90° (mapuje -Z -> +X)
        CreateMountainWall(
            position: new Vector3(worldSize - borderInset, 0f, 0f - mountainDepth),
            rotation: Quaternion.Euler(0f, -90f, 0f),
            length: wallLength,
            root: root.transform
        );

        GenerateInvisibleWall(root.transform, borderInset);
    }

    void GenerateInvisibleWall(Transform parent, float borderInset)
    {
        float worldSize = worldSizeInChunks * chunkSize;
        float half = worldSize / 2f;

        float wallDepthInset = -15f;        // <<< KLUCZOWA ZMIENNA
        float wallHeight = invisibleWallHeight;
        float wallY = wallHeight / 2f;

        // SOUTH (do środka +Z)
        CreateWall(
            new Vector3(
                half,
                wallY,
                borderInset + wallDepthInset
            ),
            new Vector3(worldSize, wallHeight, 2f),
            parent
        );

        // NORTH (do środka -Z)
        CreateWall(
            new Vector3(
                half,
                wallY,
                worldSize - borderInset - wallDepthInset
            ),
            new Vector3(worldSize, wallHeight, 2f),
            parent
        );

        // WEST (do środka +X)
        CreateWall(
            new Vector3(
                borderInset + wallDepthInset,
                wallY,
                half
            ),
            new Vector3(2f, wallHeight, worldSize),
            parent
        );

        // EAST (do środka -X)
        CreateWall(
            new Vector3(
                worldSize - borderInset - wallDepthInset,
                wallY,
                half
            ),
            new Vector3(2f, wallHeight, worldSize),
            parent
        );
    }

    Mesh GenerateMountainMesh()
    {
        int res = chunkResolution + 1;
        float step = chunkSize / chunkResolution;

        Vector3[] verts = new Vector3[res * res];
        int[] tris = new int[(res - 1) * (res - 1) * 6];

        int v = 0;
        for (int z = 0; z < res; z++)
        {
            for (int x = 0; x < res; x++)
            {
                float nx = x / (float)(res - 1);
                float nz = z / (float)(res - 1);

                float edge = Mathf.Min(
                    Mathf.Min(nx, 1f - nx),
                    Mathf.Min(nz, 1f - nz)
                );

                float h = Mathf.Lerp(
                    mountainHeight,
                    mountainHeight * 0.3f,
                    edge * 4f
                );

                verts[v++] = new Vector3(
                    x * step,
                    h,
                    z * step
                );
            }
        }

        int t = 0;
        int vi = 0;
        for (int z = 0; z < res - 1; z++)
        {
            for (int x = 0; x < res - 1; x++)
            {
                tris[t++] = vi;
                tris[t++] = vi + res;
                tris[t++] = vi + 1;

                tris[t++] = vi + 1;
                tris[t++] = vi + res;
                tris[t++] = vi + res + 1;

                vi++;
            }
            vi++;
        }

        Mesh m = new Mesh();
        m.vertices = verts;
        m.triangles = tris;
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }

    void GenerateInvisibleWall(Transform parent)
    {
        float worldSize = worldSizeInChunks * chunkSize;
        float half = worldSize / 2f;
        float offset = mountainDepth + 1f; // minimalny zapas

        CreateWall(
            new Vector3(half, invisibleWallHeight / 2f, -offset),
            new Vector3(worldSize, invisibleWallHeight, 2f),
            parent
        );

        CreateWall(
            new Vector3(half, invisibleWallHeight / 2f, worldSize + offset),
            new Vector3(worldSize, invisibleWallHeight, 2f),
            parent
        );

        CreateWall(
            new Vector3(-offset, invisibleWallHeight / 2f, half),
            new Vector3(2f, invisibleWallHeight, worldSize),
            parent
        );

        CreateWall(
            new Vector3(worldSize + offset, invisibleWallHeight / 2f, half),
            new Vector3(2f, invisibleWallHeight, worldSize),
            parent
        );
    }

    void CreateWall(Vector3 pos, Vector3 size, Transform parent)
    {
        GameObject wall = new GameObject("WorldWall");
        wall.transform.SetParent(parent, false);
        wall.transform.position = pos;

        BoxCollider bc = wall.AddComponent<BoxCollider>();
        bc.size = size;
    }

    void CreateMountainWall(Vector3 position, Quaternion rotation, float length, Transform root)
    {
        GameObject go = new GameObject("MountainWall");
        go.transform.SetParent(root, false);
        go.transform.position = position;
        go.transform.rotation = rotation;

        var mf = go.AddComponent<MeshFilter>();
        var mr = go.AddComponent<MeshRenderer>();
        var mc = go.AddComponent<MeshCollider>();

        Mesh mesh = GenerateMountainWallMesh(length);
        mf.mesh = mesh;
        mc.sharedMesh = mesh;

        if (mountainMaterial != null)
            mr.material = mountainMaterial;
    }

    Mesh GenerateMountainWallMesh(float length)
    {
        int res = Mathf.Max(2, mountainResolution);

        Vector3[] verts = new Vector3[res * 2];
        int[] tris = new int[(res - 1) * 6];

        for (int i = 0; i < res; i++)
        {
            float t = i / (float)(res - 1);
            float x = t * length;

            float n = Mathf.PerlinNoise(t * 4f, 123.456f);
            float peak = mountainHeight + n * mountainHeight * 0.6f;

            int vi = i * 2;

            // baza ściany na z=0
            verts[vi] = new Vector3(x, 0f, 0f);

            // szczyt „na zewnątrz” w -Z
            verts[vi + 1] = new Vector3(x, peak, -mountainDepth);
        }

        int ti = 0;
        for (int i = 0; i < res - 1; i++)
        {
            int v = i * 2;

            tris[ti++] = v;
            tris[ti++] = v + 2;
            tris[ti++] = v + 1;

            tris[ti++] = v + 1;
            tris[ti++] = v + 2;
            tris[ti++] = v + 3;
            }

        Mesh m = new Mesh();
        m.vertices = verts;
        m.triangles = tris;
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }

    public bool TryGetHeightAtWorld(float worldX, float worldZ, out float height)
    {
        height = 0f;

        int cx = Mathf.FloorToInt(worldX / chunkSize);
        int cz = Mathf.FloorToInt(worldZ / chunkSize);

        var coord = new Vector2Int(cx, cz);
        if (!chunks.TryGetValue(coord, out var chunk) || chunk == null)
            return false;

        float localX = worldX - cx * chunkSize;
        float localZ = worldZ - cz * chunkSize;

        float step = (float)chunkSize / chunkResolution;

        int ix = Mathf.Clamp(Mathf.RoundToInt(localX / step), 0, chunkResolution);
        int iz = Mathf.Clamp(Mathf.RoundToInt(localZ / step), 0, chunkResolution);

        height = chunk.GetHeightAt(ix, iz); // ta metoda ma zwracać wysokość w world units
        return true;
    }

    Mesh GenerateMountainWallMeshWithBase(float length, System.Func<float, float> sampleBaseHeight)
    {
        int res = Mathf.Max(2, mountainResolution);

        Vector3[] verts = new Vector3[res * 2];
        int[] tris = new int[(res - 1) * 6];

        for (int i = 0; i < res; i++)
        {
            float t = i / (float)(res - 1);
            float x = t * length;

            float baseY = sampleBaseHeight(t);

            float n = Mathf.PerlinNoise(t * 4f, 123.456f);
            float peak = mountainHeight + n * mountainHeight * 0.6f;

            int vi = i * 2;
            verts[vi]     = new Vector3(x, baseY, 0f);
            verts[vi + 1] = new Vector3(x, baseY + peak, -mountainDepth);
        }

        int ti = 0;
        for (int i = 0; i < res - 1; i++)
        {
            int v = i * 2;
            tris[ti++] = v;     tris[ti++] = v + 1; tris[ti++] = v + 2;
            tris[ti++] = v + 1; tris[ti++] = v + 3; tris[ti++] = v + 2;
        }

        Mesh m = new Mesh();
        m.vertices = verts;
        m.triangles = tris;
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }

    public bool IsChunkRendered(Vector3 worldPos)
    {
        Vector2Int coord = WorldToChunkCoord(worldPos);

        if (chunks.TryGetValue(coord, out var chunk))
            return chunk.root.activeSelf;

        return false;
    }

    public bool IsChunkActive(Vector2Int coord)
    {
        return activeChunkCoords.Contains(coord);
    }

    public void SetAllChunksRendered(bool visible)
    {
        foreach (var kv in chunks)
            kv.Value.SetRendered(visible);
    }

    public void RestoreChunkStreamingVisibility()
    {
        foreach (var kv in chunks)
            kv.Value.SetRendered(activeChunkCoords.Contains(kv.Key));
    }

    void EnablePlayerCameraAndDisableSceneCamera(Transform spawnedPlayer)
    {
        // 1) znajdź kamerę gracza
        Camera playerCam = spawnedPlayer.GetComponentInChildren<Camera>(true);
        AudioListener playerListener = spawnedPlayer.GetComponentInChildren<AudioListener>(true);

        if (playerCam == null)
        {
            Debug.LogWarning("[CameraHandover] Player has no Camera in children.");
            return;
        }

        // 2) włącz kamerę gracza
        playerCam.enabled = true;
        if (playerListener != null) playerListener.enabled = true;

        // 3) wyłącz kamerę sceny (fallback)
        // Szukamy po nazwie, bo to stabilne i nie zależy od tagów
        var sceneCamGO = GameObject.Find("GameSceneCamera");
        if (sceneCamGO != null)
        {
            var sceneCam = sceneCamGO.GetComponent<Camera>();
            var sceneListener = sceneCamGO.GetComponent<AudioListener>();

            if (sceneCam != null) sceneCam.enabled = false;
            if (sceneListener != null) sceneListener.enabled = false;
        }
        else
        {
            // fallback: jeśli nie ma po nazwie, spróbuj znaleźć aktywną MainCamera
            var cam = Camera.main;
            if (cam != null && cam != playerCam)
            {
                cam.enabled = false;
                var l = cam.GetComponent<AudioListener>();
                if (l != null) l.enabled = false;
            }
        }

        // 4) upewnij się, że "MainCamera" jest na kamerze gracza (opcjonalnie, ale polecam)
        playerCam.tag = "MainCamera";
    }

    public static void ResetWorldReady()
    {
        WorldReady = false;
    }

    public List<Vector2Int> GetActiveChunks()
    {
        return new List<Vector2Int>(activeChunkCoords);
    }

    public bool TryGetRandomPointInChunk(Vector2Int coord, out Vector3 pos)
    {
        pos = Vector3.zero;

        if (!chunks.TryGetValue(coord, out var chunk))
            return false;

        for (int i = 0; i < 10; i++)
        {
            float x = Random.Range(0f, chunkSize);
            float z = Random.Range(0f, chunkSize);

            float worldX = coord.x * chunkSize + x;
            float worldZ = coord.y * chunkSize + z;

            if (TryGetHeightAtWorld(worldX, worldZ, out float h))
            {
                pos = new Vector3(worldX, h, worldZ);
                return true;
            }
        }

        return false;
    }

    public void SpawnRelicChest(GameObject prefab)
    {
        if (player == null)
            return;

        const int attempts = 20;
        const float chestScaleMultiplier = 0.5f;
        const float minDistanceFromMapBorder = 10f;
        float worldSize = worldSizeInChunks * chunkSize;

        for (int i = 0; i < attempts; i++)
        {
            Vector3 pos = player.position +
                        Random.insideUnitSphere * 25f;

            pos.y += 30f;

            if (Physics.Raycast(
                pos,
                Vector3.down,
                out RaycastHit hit,
                100f,
                LayerMask.GetMask("Ground")))
            {
                Vector3 spawnPos = hit.point + Vector3.up * 0.2f;
                float closestEdgeDistance = Mathf.Min(
                    spawnPos.x,
                    worldSize - spawnPos.x,
                    spawnPos.z,
                    worldSize - spawnPos.z
                );

                if (closestEdgeDistance < minDistanceFromMapBorder)
                    continue;

                GameObject chest = Instantiate(
                    prefab,
                    spawnPos,
                    Quaternion.identity
                );
                if (chest != null)
                    chest.transform.localScale *= chestScaleMultiplier;

                return;
            }
        }

        Debug.LogWarning("[RelicSpawner] Failed to place chest");
    }

    public Vector3 GetRandomValidWorldPosition(float minDistanceFromMapBorder = 0f)
    {
        int attempts = 30;
        float worldSize = worldSizeInChunks * chunkSize;
        float maxAllowedInset = Mathf.Max(0f, worldSize * 0.5f - 0.5f);
        float borderInset = Mathf.Clamp(minDistanceFromMapBorder, 0f, maxAllowedInset);

        while (attempts-- > 0)
        {
            int x = Random.Range(0, worldSizeInChunks);
            int z = Random.Range(0, worldSizeInChunks);

            Vector2Int coord = new Vector2Int(x, z);

            if (!chunks.TryGetValue(coord, out var chunk))
                continue;

            Vector3 basePos = chunk.root.transform.position;

            Vector3 pos = basePos + new Vector3(
                Random.Range(2f, chunkSize - 2f),
                heightMultiplier + 50f,
                Random.Range(2f, chunkSize - 2f)
            );

            if (Physics.Raycast(
                pos,
                Vector3.down,
                out RaycastHit hit,
                heightMultiplier * 5f,
                LayerMask.GetMask("Ground")
            ))
            {
                Vector3 spawnPos = hit.point + Vector3.up * 0.2f;

                if (borderInset > 0f)
                {
                    float closestEdgeDistance = Mathf.Min(
                        spawnPos.x,
                        worldSize - spawnPos.x,
                        spawnPos.z,
                        worldSize - spawnPos.z
                    );

                    if (closestEdgeDistance < borderInset)
                        continue;
                }

                return spawnPos;
            }
        }

        Debug.LogWarning($"[World] Failed to find valid chest position (border inset: {borderInset:0.##}m).");

        float center = worldSize * 0.5f;
        if (TryGetHeightAtWorld(center, center, out float centerHeight))
            return new Vector3(center, centerHeight + 0.2f, center);

        return Vector3.zero;
    }

    private IEnumerator PrewarmChunkTerrainMaterialsAsync()
    {
        HashSet<Material> uniqueMaterials = new();

        foreach (var kv in chunks)
        {
            ChunkData chunk = kv.Value;
            if (chunk == null || chunk.chunkBiome == null)
                continue;

            Material mat = chunk.chunkBiome.terrainMaterial;
            if (mat != null)
                uniqueMaterials.Add(mat);
        }

        if (uniqueMaterials.Count == 0)
            yield break;

        GameObject camGo = new GameObject("MaterialPrewarmCamera");
        GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);

        camGo.hideFlags = HideFlags.HideAndDontSave;
        quad.hideFlags = HideFlags.HideAndDontSave;

        Camera cam = camGo.AddComponent<Camera>();
        cam.enabled = false;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.black;
        cam.nearClipPlane = 0.01f;
        cam.farClipPlane = 10f;
        cam.fieldOfView = 30f;
        cam.transform.position = Vector3.zero;
        cam.transform.rotation = Quaternion.identity;

        RenderTexture rt = new RenderTexture(64, 64, 16);
        cam.targetTexture = rt;

        MeshRenderer quadRenderer = quad.GetComponent<MeshRenderer>();
        Collider quadCollider = quad.GetComponent<Collider>();
        if (quadCollider != null)
            Destroy(quadCollider);

        quad.transform.position = new Vector3(0f, 0f, 2f);
        quad.transform.rotation = Quaternion.identity;
        quad.transform.localScale = Vector3.one;

        int ops = 0;
        int opsBudget = Mathf.Max(1, materialPrewarmOpsPerFrame);

        foreach (Material mat in uniqueMaterials)
        {
            if (mat == null)
                continue;

            TouchMaterialTextures(mat);
            quadRenderer.sharedMaterial = mat;
            cam.Render();

            ops++;
            if (ops >= opsBudget)
            {
                ops = 0;
                yield return null;
            }
        }

        cam.targetTexture = null;
        rt.Release();
        Destroy(rt);
        Destroy(quad);
        Destroy(camGo);
    }

    private static void TouchMaterialTextures(Material material)
    {
        if (material == null || material.shader == null)
            return;

        int propertyCount = material.shader.GetPropertyCount();
        for (int i = 0; i < propertyCount; i++)
        {
            if (material.shader.GetPropertyType(i) != UnityEngine.Rendering.ShaderPropertyType.Texture)
                continue;

            string propertyName = material.shader.GetPropertyName(i);
            _ = material.GetTexture(propertyName);
        }
    }
}
