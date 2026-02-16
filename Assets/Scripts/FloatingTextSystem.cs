using UnityEngine;
using System.Collections.Generic;
using GrassSim.Core;

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

    void Awake()
    {
        Instance = this;

        GameObject go = new GameObject("FloatingTextCanvas");
        canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = sortingOrder;

        go.transform.SetParent(transform, false);
        go.transform.localScale = Vector3.one * canvasScale;
    }

    void OnDestroy()
    {
        while (pool.Count > 0)
        {
            FloatingText text = pool.Dequeue();
            if (text != null)
                Destroy(text.gameObject);
        }

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

        return Instantiate(prefab, canvas.transform);
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
}
