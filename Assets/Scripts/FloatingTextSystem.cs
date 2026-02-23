using UnityEngine;
using System.Collections.Generic;
using GrassSim.Core;
using TMPro;

public class FloatingTextSystem : MonoBehaviour
{
    public static FloatingTextSystem Instance;

    public FloatingText prefab;

    [Header("Canvas")]
    public float canvasScale = 0.01f;
    public int sortingOrder = 200;

    [Header("Pooling/Budgets")]
    [SerializeField, Min(8)] private int maxPoolSize = 220;
    [SerializeField, Min(8)] private int maxActiveTexts = 180;
    [SerializeField, Min(1)] private int maxSpawnsPerFrame = 24;

    private Canvas canvas;
    private Camera cam;
    private bool cameraLocked;
    private readonly Queue<FloatingText> pool = new();
    private int activeTextCount;
    private int spawnFrame = -1;
    private int spawnsThisFrame;
    private GameObject runtimeFallbackPrefabRoot;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Instance = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        EnsureInstance();
    }

    public static FloatingTextSystem EnsureInstance()
    {
        if (Instance != null)
            return Instance;

        FloatingTextSystem existing = Object.FindFirstObjectByType<FloatingTextSystem>();
        if (existing != null)
        {
            Instance = existing;
            return existing;
        }

        GameObject go = new GameObject("FloatingTextSystem_Auto");
        return go.AddComponent<FloatingTextSystem>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        GameObject go = new GameObject("FloatingTextCanvas");
        canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = sortingOrder;

        go.transform.SetParent(transform, false);
        go.transform.localScale = Vector3.one * canvasScale;

        EnsurePrefabAssigned();
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;

        while (pool.Count > 0)
        {
            FloatingText text = pool.Dequeue();
            if (text != null)
                Destroy(text.gameObject);
        }

        if (runtimeFallbackPrefabRoot != null)
            Destroy(runtimeFallbackPrefabRoot);

        activeTextCount = 0;
        spawnsThisFrame = 0;
    }

    void LateUpdate()
    {
        if (cameraLocked)
            return;

        Transform player = PlayerLocator.GetTransform();
        if (player == null)
            return;

        cam = player.GetComponentInChildren<Camera>(true);
        if (cam == null)
            return;

        canvas.worldCamera = cam;
        cameraLocked = true;
    }

    public void Spawn(
        Vector3 worldPos,
        float value,
        Color color,
        float fontSize = 36f
    )
    {
        SpawnText(worldPos, value.ToString("0.##"), color, fontSize);
    }

    public void SpawnText(
        Vector3 worldPos,
        string value,
        Color color,
        float fontSize = 36f
    )
    {
        if (prefab == null)
            return;

        if (cam == null)
        {
            cam = canvas != null && canvas.worldCamera != null
                ? canvas.worldCamera
                : Camera.main;

            if (cam == null)
                cam = Object.FindFirstObjectByType<Camera>();

            if (cam != null && canvas != null)
                canvas.worldCamera = cam;
        }

        if (cam == null)
            return;

        int frame = Time.frameCount;
        if (spawnFrame != frame)
        {
            spawnFrame = frame;
            spawnsThisFrame = 0;
        }

        if (activeTextCount >= Mathf.Max(1, maxActiveTexts))
            return;

        if (spawnsThisFrame >= Mathf.Max(1, maxSpawnsPerFrame))
            return;

        FloatingText ft = RentText();
        if (ft == null)
            return;

        spawnsThisFrame++;
        activeTextCount++;

        ft.transform.position = worldPos;
        ft.Init(cam, value, color, fontSize, ReturnTextToPool);
    }

    private FloatingText RentText()
    {
        while (pool.Count > 0)
        {
            FloatingText cached = pool.Dequeue();
            if (cached == null)
                continue;

            cached.gameObject.SetActive(true);
            return cached;
        }

        if (prefab == null || canvas == null)
            return null;

        FloatingText created = Instantiate(prefab, canvas.transform);
        if (created != null && !created.gameObject.activeSelf)
            created.gameObject.SetActive(true);

        return created;
    }

    private void ReturnTextToPool(FloatingText ft)
    {
        if (ft == null)
            return;

        activeTextCount = Mathf.Max(0, activeTextCount - 1);

        if (pool.Count >= Mathf.Max(1, maxPoolSize))
        {
            Destroy(ft.gameObject);
            return;
        }

        ft.gameObject.SetActive(false);
        pool.Enqueue(ft);
    }

    private void EnsurePrefabAssigned()
    {
        if (prefab != null)
            return;

        runtimeFallbackPrefabRoot = BuildRuntimeFallbackPrefab();
        prefab = runtimeFallbackPrefabRoot != null ? runtimeFallbackPrefabRoot.GetComponent<FloatingText>() : null;

        if (prefab == null)
            Debug.LogError("[FloatingTextSystem] Missing FloatingText prefab and failed to build runtime fallback.", this);
    }

    private GameObject BuildRuntimeFallbackPrefab()
    {
        GameObject root = new GameObject("FloatingTextRuntimePrefab");
        root.transform.SetParent(transform, false);

        FloatingText fallback = root.AddComponent<FloatingText>();

        GameObject textRoot = new GameObject("Text");
        textRoot.transform.SetParent(root.transform, false);
        TextMeshPro tmp = textRoot.AddComponent<TextMeshPro>();
        tmp.text = string.Empty;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.overflowMode = TextOverflowModes.Overflow;
        tmp.fontSize = 36f;
        tmp.raycastTarget = false;
        if (TMP_Settings.defaultFontAsset != null)
            tmp.font = TMP_Settings.defaultFontAsset;

        fallback.text = tmp;
        root.SetActive(false);
        return root;
    }
}
