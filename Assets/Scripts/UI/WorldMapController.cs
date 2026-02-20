using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using GrassSim.Enhancers;
using GrassSim.Core;
using GrassSim.UI;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Scripting;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[Preserve]
public class WorldMapController : MonoBehaviour
{
    [Header("Input")]
#if ENABLE_LEGACY_INPUT_MANAGER
    [SerializeField] private KeyCode legacyToggleKey = KeyCode.M;
#endif

    [Header("Map Bake")]
    [SerializeField, Min(256)] private int mapTextureSize = 1024;
    [SerializeField] private LayerMask mapCullingMask = ~0;
    [SerializeField, Min(0f)] private float mapCapturePadding = 12f;
    [SerializeField] private Color mapBackgroundColor = new Color(0.08f, 0.1f, 0.12f, 1f);

    [Header("Fog Of War")]
    [SerializeField, Min(64)] private int fogTextureSize = 512;
    [SerializeField, Min(1f)] private float revealRadiusMeters = 100f;
    [SerializeField, Min(0f)] private float revealEdgeSoftnessMeters = 8f;
    [SerializeField, Min(0.02f)] private float revealUpdateInterval = 0.12f;
    [SerializeField, Min(0.1f)] private float revealMinMoveDistance = 1.5f;
    [SerializeField, Range(0f, 1f)] private float unexploredAlpha = 0.95f;

    [Header("UI")]
    [SerializeField] private int canvasSortingOrder = 350;
    [SerializeField] private Color backdropColor = new Color(0f, 0f, 0f, 0.62f);
    [SerializeField] private Color frameColor = new Color(0.06f, 0.08f, 0.1f, 0.96f);
    [SerializeField] private Color markerColor = new Color(1f, 0.25f, 0.16f, 1f);
    [SerializeField] private Color captureOverlayColor = Color.black;
    [SerializeField] private Color playerHeadingColor = Color.white;

    [Header("Map Markers")]
    [SerializeField] private Color chestMarkerColor = new Color(1f, 0.83f, 0.2f, 1f);
    [SerializeField] private Color enhancerMarkerColor = new Color(0.35f, 0.9f, 1f, 1f);
    [SerializeField, Min(4f)] private float chestMarkerSize = 10f;
    [SerializeField, Min(4f)] private float enhancerMarkerSize = 8f;

    [Header("Minimap")]
    [SerializeField] private bool showMinimap = true;
    [SerializeField, Min(96f)] private float minimapSize = 240f;
    [SerializeField, Min(0f)] private float minimapPadding = 24f;
    [SerializeField, Min(0f)] private float minimapInnerPadding = 8f;
    [SerializeField] private Color minimapBackdropColor = new Color(0.04f, 0.05f, 0.07f, 0.9f);

    private ChunkedProceduralLevelGenerator worldGenerator;
    private Transform player;
    private Rect worldRectXZ;
    private Rect mapCaptureRectXZ;

    private GameObject captureOverlay;
    private GameObject mapRoot;
    private RawImage mapImage;
    private RawImage fogImage;
    private RectTransform markerLayer;
    private RectTransform playerMarker;
    private GameObject minimapRoot;
    private RawImage minimapImage;
    private RawImage minimapFogImage;
    private RectTransform minimapMarkerLayer;
    private RectTransform minimapPlayerMarker;

    private Texture2D mapTexture;
    private Texture2D fogTexture;
    private Color32[] fogPixels;

    private bool initialized;
    private bool mapVisible;
    private bool hasRevealSeed;
    private Vector3 lastRevealPosition;
    private float nextRevealTime;
    private byte unexploredAlphaByte;
    private bool collectibleMarkersDirty = true;
    private bool legacyFallbackChecked;
    private bool legacyFallbackAvailable = true;

    private readonly Dictionary<int, MapTargetMarker> chestMarkers = new Dictionary<int, MapTargetMarker>();
    private readonly Dictionary<int, MapTargetMarker> enhancerMarkers = new Dictionary<int, MapTargetMarker>();
    private readonly Dictionary<int, MapTargetMarker> minimapChestMarkers = new Dictionary<int, MapTargetMarker>();
    private readonly Dictionary<int, MapTargetMarker> minimapEnhancerMarkers = new Dictionary<int, MapTargetMarker>();
    private readonly HashSet<int> markerActiveIds = new HashSet<int>(128);
    private readonly List<int> markerIdsToRemove = new List<int>(64);
    private readonly List<ChestRelicTrigger> chestSceneBuffer = new List<ChestRelicTrigger>(64);
    private readonly List<EnhancerPickup> enhancerSceneBuffer = new List<EnhancerPickup>(64);

    private struct MapCaptureState
    {
        public bool fogEnabled;
        public FogMode fogMode;
        public float fogDensity;
        public float fogStartDistance;
        public float fogEndDistance;
        public DynamicFogController[] dynamicFogControllers;
        public bool[] dynamicFogControllerEnabled;
        public List<ScriptableRendererFeature> rendererFeatures;
        public List<bool> rendererFeatureEnabled;
    }

    private sealed class MapTargetMarker
    {
        public Transform target;
        public RectTransform rect;
    }

    [Preserve]
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (FindFirstObjectByType<WorldMapController>() != null)
            return;

        var go = new GameObject("WorldMapController");
        go.AddComponent<WorldMapController>();
    }

    private void Start()
    {
        StartCoroutine(InitializeRoutine());
    }

    private void OnEnable()
    {
        MapCollectibleRegistry.RegistryChanged += HandleCollectibleRegistryChanged;
    }

    private void OnDisable()
    {
        MapCollectibleRegistry.RegistryChanged -= HandleCollectibleRegistryChanged;
    }

    private IEnumerator InitializeRoutine()
    {
        yield return new WaitUntil(() => ChunkedProceduralLevelGenerator.WorldReady);

        while (worldGenerator == null)
        {
            worldGenerator = FindFirstObjectByType<ChunkedProceduralLevelGenerator>();
            yield return null;
        }

        while (player == null)
        {
            player = PlayerLocator.GetTransform();
            yield return null;
        }

        BuildWorldRect();
        BuildUi();
        EnsureInitialChestWaveSpawnedBeforeMapBake();
        collectibleMarkersDirty = true;
        RefreshCollectibleMarkersFromRegistry(forceRefresh: true);

        if (mapRoot != null)
            mapRoot.SetActive(false);

        if (minimapRoot != null)
            minimapRoot.SetActive(showMinimap);

        mapVisible = false;
        SetCaptureOverlayVisible(true);

        yield return StartCoroutine(BakeMapTextureRoutine());
        BuildFogTexture();
        SetCaptureOverlayVisible(false);
        collectibleMarkersDirty = true;
        RefreshCollectibleMarkersFromRegistry(forceRefresh: true);

        if (player != null)
        {
            RevealAround(player.position);
            hasRevealSeed = true;
            lastRevealPosition = player.position;
            UpdatePlayerMarker();
            UpdateCollectibleMarkerPositions();
        }

        initialized = true;
    }

    private void Update()
    {
        if (!initialized)
            return;

        if (GameTimerController.Instance != null && GameTimerController.Instance.gameEnded)
        {
            SetMapVisible(false);
            return;
        }

        if (player == null)
        {
            player = PlayerLocator.GetTransform();
            if (player != null)
            {
                hasRevealSeed = false;
            }
        }

        HandleToggleInput();
        UpdateFog();

        bool anyMapVisible = mapVisible || (minimapRoot != null && minimapRoot.activeSelf);
        if (anyMapVisible)
        {
            RefreshCollectibleMarkersFromRegistry(forceRefresh: false);
            UpdatePlayerMarker();
            UpdateCollectibleMarkerPositions();
        }
    }

    private void HandleToggleInput()
    {
        bool toggleRequested = false;

#if ENABLE_INPUT_SYSTEM
        var keyboard = Keyboard.current;
        if (keyboard != null)
            toggleRequested = keyboard.mKey.wasPressedThisFrame;
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
        if (!toggleRequested)
            toggleRequested = Input.GetKeyDown(legacyToggleKey);
#else
        if (!toggleRequested)
            toggleRequested = TryLegacyToggleFallback();
#endif

        if (toggleRequested)
            SetMapVisible(!mapVisible);
    }

    private bool TryLegacyToggleFallback()
    {
        if (!legacyFallbackAvailable)
            return false;

        if (!legacyFallbackChecked)
        {
            legacyFallbackChecked = true;
            try
            {
                _ = Input.anyKey;
            }
            catch
            {
                legacyFallbackAvailable = false;
                return false;
            }
        }

        try
        {
            return Input.GetKeyDown(KeyCode.M);
        }
        catch
        {
            legacyFallbackAvailable = false;
            return false;
        }
    }

    private void SetMapVisible(bool visible)
    {
        mapVisible = visible && mapRoot != null;
        if (mapRoot != null)
            mapRoot.SetActive(mapVisible);

        if (mapVisible)
        {
            collectibleMarkersDirty = true;
            RefreshCollectibleMarkersFromRegistry(forceRefresh: true);
            UpdatePlayerMarker();
            UpdateCollectibleMarkerPositions();
        }
    }

    private void SetCaptureOverlayVisible(bool visible)
    {
        if (captureOverlay != null)
            captureOverlay.SetActive(visible);
    }

    private void EnsureInitialChestWaveSpawnedBeforeMapBake()
    {
        var spawners = FindObjectsByType<RelicChestSpawner>(FindObjectsSortMode.None);
        for (int i = 0; i < spawners.Length; i++)
            spawners[i].EnsureInitialWaveSpawnedNow();
    }

    private void RefreshCollectibleMarkersFromRegistry(bool forceRefresh)
    {
        if (!forceRefresh && !collectibleMarkersDirty)
            return;

        MapCollectibleRegistry.GetActiveChests(chestSceneBuffer);
        SyncMarkers(chestSceneBuffer, chestMarkers, markerLayer, chestMarkerColor, chestMarkerSize);
        SyncMarkers(chestSceneBuffer, minimapChestMarkers, minimapMarkerLayer, chestMarkerColor, chestMarkerSize);

        MapCollectibleRegistry.GetActiveEnhancers(enhancerSceneBuffer);
        SyncMarkers(enhancerSceneBuffer, enhancerMarkers, markerLayer, enhancerMarkerColor, enhancerMarkerSize);
        SyncMarkers(enhancerSceneBuffer, minimapEnhancerMarkers, minimapMarkerLayer, enhancerMarkerColor, enhancerMarkerSize);
        collectibleMarkersDirty = false;
    }

    private void SyncMarkers<T>(
        IReadOnlyList<T> sceneTargets,
        Dictionary<int, MapTargetMarker> markerSet,
        RectTransform targetLayer,
        Color markerTint,
        float markerSize
    ) where T : Component
    {
        if (targetLayer == null)
        {
            ClearMarkerSet(markerSet);
            return;
        }

        if (sceneTargets == null)
            return;

        markerActiveIds.Clear();

        for (int i = 0; i < sceneTargets.Count; i++)
        {
            var target = sceneTargets[i];
            if (target == null)
                continue;

            if (!target.gameObject.activeSelf)
                continue;

            int id = target.GetInstanceID();
            markerActiveIds.Add(id);

            if (markerSet.TryGetValue(id, out var marker))
            {
                marker.target = target.transform;
                if (marker.rect != null)
                    marker.rect.sizeDelta = new Vector2(markerSize, markerSize);
                continue;
            }

            markerSet[id] = new MapTargetMarker
            {
                target = target.transform,
                rect = CreateMapMarker(targetLayer, markerTint, markerSize, target.name)
            };
        }

        if (markerSet.Count == 0)
            return;

        markerIdsToRemove.Clear();
        foreach (var kv in markerSet)
        {
            bool remove = !markerActiveIds.Contains(kv.Key) || kv.Value == null || kv.Value.rect == null;
            if (remove)
                markerIdsToRemove.Add(kv.Key);
        }

        for (int i = 0; i < markerIdsToRemove.Count; i++)
        {
            int id = markerIdsToRemove[i];
            if (!markerSet.TryGetValue(id, out var marker))
                continue;

            if (marker != null && marker.rect != null)
                Destroy(marker.rect.gameObject);

            markerSet.Remove(id);
        }

        markerIdsToRemove.Clear();
    }

    private static void ClearMarkerSet(Dictionary<int, MapTargetMarker> markerSet)
    {
        if (markerSet == null || markerSet.Count == 0)
            return;

        foreach (var kv in markerSet)
        {
            MapTargetMarker marker = kv.Value;
            if (marker != null && marker.rect != null)
                Object.Destroy(marker.rect.gameObject);
        }

        markerSet.Clear();
    }

    private RectTransform CreateMapMarker(RectTransform parent, Color tint, float size, string targetName)
    {
        var go = new GameObject($"Marker_{targetName}", typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);

        var rect = (RectTransform)go.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.zero;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(size, size);

        var image = go.GetComponent<Image>();
        image.color = tint;
        image.raycastTarget = false;

        return rect;
    }

    private void UpdateCollectibleMarkerPositions()
    {
        UpdateMarkerDictionaryPositions(chestMarkers, mapImage);
        UpdateMarkerDictionaryPositions(enhancerMarkers, mapImage);
        UpdateMarkerDictionaryPositions(minimapChestMarkers, minimapImage);
        UpdateMarkerDictionaryPositions(minimapEnhancerMarkers, minimapImage);
    }

    private void UpdateMarkerDictionaryPositions(
        Dictionary<int, MapTargetMarker> markerSet,
        RawImage targetMapImage
    )
    {
        if (markerSet == null || markerSet.Count == 0 || targetMapImage == null)
            return;

        Rect mapRect = targetMapImage.rectTransform.rect;

        foreach (var kv in markerSet)
        {
            var marker = kv.Value;
            if (marker == null || marker.rect == null)
                continue;

            bool visible = marker.target != null
                && marker.target.gameObject.scene.IsValid()
                && marker.target.gameObject.activeSelf;

            if (visible)
                visible = IsWorldPositionRevealed(marker.target.position);

            marker.rect.gameObject.SetActive(visible);
            if (!visible)
                continue;

            Vector2 uv = WorldToMapUv(marker.target.position);
            marker.rect.anchoredPosition = new Vector2(
                uv.x * mapRect.width,
                uv.y * mapRect.height
            );
        }
    }

    private void HandleCollectibleRegistryChanged()
    {
        collectibleMarkersDirty = true;
    }

    private bool IsWorldPositionRevealed(Vector3 worldPosition)
    {
        if (fogPixels == null || fogPixels.Length == 0 || fogTextureSize <= 0)
            return false;

        Vector2 uv = WorldToMapUv(worldPosition);
        int x = Mathf.Clamp(Mathf.RoundToInt(uv.x * (fogTextureSize - 1)), 0, fogTextureSize - 1);
        int y = Mathf.Clamp(Mathf.RoundToInt(uv.y * (fogTextureSize - 1)), 0, fogTextureSize - 1);
        int idx = y * fogTextureSize + x;

        return idx >= 0
            && idx < fogPixels.Length
            && fogPixels[idx].a < unexploredAlphaByte;
    }

    private MapCaptureState BeginMapCaptureState()
    {
        MapCaptureState state = new MapCaptureState
        {
            fogEnabled = RenderSettings.fog,
            fogMode = RenderSettings.fogMode,
            fogDensity = RenderSettings.fogDensity,
            fogStartDistance = RenderSettings.fogStartDistance,
            fogEndDistance = RenderSettings.fogEndDistance,
            dynamicFogControllers = FindObjectsByType<DynamicFogController>(FindObjectsSortMode.None),
            rendererFeatures = new List<ScriptableRendererFeature>(4),
            rendererFeatureEnabled = new List<bool>(4)
        };

        if (state.dynamicFogControllers != null && state.dynamicFogControllers.Length > 0)
        {
            state.dynamicFogControllerEnabled = new bool[state.dynamicFogControllers.Length];

            for (int i = 0; i < state.dynamicFogControllers.Length; i++)
            {
                var controller = state.dynamicFogControllers[i];
                if (controller == null)
                    continue;

                state.dynamicFogControllerEnabled[i] = controller.enabled;
                controller.enabled = false;
            }
        }

        RenderSettings.fog = false;
        DisableHeightFogRendererFeatures(state.rendererFeatures, state.rendererFeatureEnabled);

        return state;
    }

    private void EndMapCaptureState(MapCaptureState state)
    {
        RenderSettings.fog = state.fogEnabled;
        RenderSettings.fogMode = state.fogMode;
        RenderSettings.fogDensity = state.fogDensity;
        RenderSettings.fogStartDistance = state.fogStartDistance;
        RenderSettings.fogEndDistance = state.fogEndDistance;

        if (state.rendererFeatures != null && state.rendererFeatureEnabled != null)
        {
            int count = Mathf.Min(state.rendererFeatures.Count, state.rendererFeatureEnabled.Count);
            for (int i = 0; i < count; i++)
            {
                var feature = state.rendererFeatures[i];
                if (feature != null)
                    feature.SetActive(state.rendererFeatureEnabled[i]);
            }
        }

        if (state.dynamicFogControllers != null && state.dynamicFogControllerEnabled != null)
        {
            int count = Mathf.Min(state.dynamicFogControllers.Length, state.dynamicFogControllerEnabled.Length);
            for (int i = 0; i < count; i++)
            {
                var controller = state.dynamicFogControllers[i];
                if (controller != null)
                    controller.enabled = state.dynamicFogControllerEnabled[i];
            }
        }
    }

    private void DisableHeightFogRendererFeatures(
        List<ScriptableRendererFeature> features,
        List<bool> previousStates
    )
    {
        if (features == null || previousStates == null)
            return;

        var urpAsset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        if (urpAsset == null)
            urpAsset = QualitySettings.renderPipeline as UniversalRenderPipelineAsset;
        if (urpAsset == null)
            return;

        var rendererDataList = GetRendererDataList(urpAsset);
        if (rendererDataList == null || rendererDataList.Length == 0)
            return;

        for (int i = 0; i < rendererDataList.Length; i++)
        {
            var rendererData = rendererDataList[i];
            if (rendererData == null || rendererData.rendererFeatures == null)
                continue;

            foreach (var feature in rendererData.rendererFeatures)
            {
                if (feature == null || !IsHeightFogRendererFeature(feature))
                    continue;

                features.Add(feature);
                previousStates.Add(feature.isActive);
                feature.SetActive(false);
            }
        }
    }

    private ScriptableRendererData[] GetRendererDataList(UniversalRenderPipelineAsset urpAsset)
    {
        if (urpAsset == null)
            return new ScriptableRendererData[0];

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var type = urpAsset.GetType();
        var collected = new List<ScriptableRendererData>(4);

        void AddIfValid(ScriptableRendererData data)
        {
            if (data == null)
                return;

            if (!collected.Contains(data))
                collected.Add(data);
        }

        // Try optional property first (available only in some URP versions).
        var singleProperty = type.GetProperty("scriptableRendererData", flags);
        if (singleProperty != null)
        {
            try
            {
                if (singleProperty.GetValue(urpAsset, null) is ScriptableRendererData singlePropertyData)
                    AddIfValid(singlePropertyData);
            }
            catch
            {
                // Ignore and keep fallback data.
            }
        }

        // Prefer serialized fields over reflection property calls.
        // Some Unity/URP versions expose rendererDataList as ReadOnlySpan<T>,
        // which throws when accessed via reflection GetValue (boxing IsByRefLike).
        var listField = type.GetField("m_RendererDataList", flags);
        if (listField != null)
        {
            try
            {
                if (listField.GetValue(urpAsset) is ScriptableRendererData[] fromFieldList)
                {
                    for (int i = 0; i < fromFieldList.Length; i++)
                        AddIfValid(fromFieldList[i]);
                }
            }
            catch
            {
                // Ignore and keep fallback data.
            }
        }

        var singleField = type.GetField("m_RendererData", flags);
        if (singleField != null)
        {
            try
            {
                if (singleField.GetValue(urpAsset) is ScriptableRendererData singleData)
                    AddIfValid(singleData);
            }
            catch
            {
                // Ignore and keep fallback data.
            }
        }

        return collected.ToArray();
    }

    private bool IsHeightFogRendererFeature(ScriptableRendererFeature feature)
    {
        Material passMaterial = ExtractPassMaterial(feature);
        if (passMaterial == null)
            return false;

        string matName = passMaterial.name ?? string.Empty;
        string shaderName = passMaterial.shader != null ? passMaterial.shader.name : string.Empty;

        return matName.IndexOf("HeightFog", System.StringComparison.OrdinalIgnoreCase) >= 0
            || shaderName.IndexOf("HeightFog", System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private Material ExtractPassMaterial(ScriptableRendererFeature feature)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var type = feature.GetType();

        var property = type.GetProperty("passMaterial", flags);
        if (property != null && property.GetValue(feature, null) is Material propertyMaterial)
            return propertyMaterial;

        var field = type.GetField("passMaterial", flags);
        if (field != null && field.GetValue(feature) is Material fieldMaterial)
            return fieldMaterial;

        var privateField = type.GetField("m_PassMaterial", flags);
        if (privateField != null && privateField.GetValue(feature) is Material privateMaterial)
            return privateMaterial;

        return null;
    }

    private void UpdateFog()
    {
        if (player == null || fogTexture == null || fogPixels == null)
            return;

        if (Time.unscaledTime < nextRevealTime)
            return;

        nextRevealTime = Time.unscaledTime + revealUpdateInterval;

        if (!hasRevealSeed)
        {
            RevealAround(player.position);
            lastRevealPosition = player.position;
            hasRevealSeed = true;
            return;
        }

        float minMoveSqr = revealMinMoveDistance * revealMinMoveDistance;
        if ((player.position - lastRevealPosition).sqrMagnitude < minMoveSqr)
            return;

        RevealAround(player.position);
        lastRevealPosition = player.position;
    }

    private void BuildWorldRect()
    {
        float worldSize = Mathf.Max(1f, worldGenerator.worldSizeInChunks * worldGenerator.chunkSize);
        worldRectXZ = new Rect(0f, 0f, worldSize, worldSize);

        float mapRadius = (worldSize * 0.5f) + Mathf.Max(0f, mapCapturePadding);
        Vector2 center = worldRectXZ.center;
        mapCaptureRectXZ = Rect.MinMaxRect(
            center.x - mapRadius,
            center.y - mapRadius,
            center.x + mapRadius,
            center.y + mapRadius
        );
    }

    private void BuildUi()
    {
        var canvasGo = new GameObject("WorldMapCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = canvasSortingOrder;

        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        captureOverlay = new GameObject("MapCaptureOverlay", typeof(RectTransform), typeof(Image));
        captureOverlay.transform.SetParent(canvasGo.transform, false);

        var overlayRect = (RectTransform)captureOverlay.transform;
        overlayRect.anchorMin = Vector2.zero;
        overlayRect.anchorMax = Vector2.one;
        overlayRect.offsetMin = Vector2.zero;
        overlayRect.offsetMax = Vector2.zero;

        var overlayImage = captureOverlay.GetComponent<Image>();
        overlayImage.color = captureOverlayColor;
        overlayImage.raycastTarget = false;

        mapRoot = new GameObject("MapRoot", typeof(RectTransform), typeof(Image));
        mapRoot.transform.SetParent(canvasGo.transform, false);

        var rootRect = (RectTransform)mapRoot.transform;
        rootRect.anchorMin = Vector2.zero;
        rootRect.anchorMax = Vector2.one;
        rootRect.offsetMin = Vector2.zero;
        rootRect.offsetMax = Vector2.zero;
        rootRect.pivot = new Vector2(0.5f, 0.5f);

        var backdrop = mapRoot.GetComponent<Image>();
        backdrop.color = backdropColor;

        var frameGo = new GameObject("MapFrame", typeof(RectTransform), typeof(Image), typeof(AspectRatioFitter));
        frameGo.transform.SetParent(mapRoot.transform, false);

        var frameRect = (RectTransform)frameGo.transform;
        frameRect.anchorMin = new Vector2(0.08f, 0.08f);
        frameRect.anchorMax = new Vector2(0.92f, 0.92f);
        frameRect.offsetMin = Vector2.zero;
        frameRect.offsetMax = Vector2.zero;

        var frameFitter = frameGo.GetComponent<AspectRatioFitter>();
        frameFitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        frameFitter.aspectRatio = 1f;

        var frameImage = frameGo.GetComponent<Image>();
        frameImage.color = frameColor;

        var mapGo = new GameObject("MapImage", typeof(RectTransform), typeof(RawImage));
        mapGo.transform.SetParent(frameGo.transform, false);

        var mapRect = (RectTransform)mapGo.transform;
        mapRect.anchorMin = new Vector2(0.03f, 0.03f);
        mapRect.anchorMax = new Vector2(0.97f, 0.97f);
        mapRect.offsetMin = Vector2.zero;
        mapRect.offsetMax = Vector2.zero;

        mapImage = mapGo.GetComponent<RawImage>();
        mapImage.color = Color.white;
        mapImage.raycastTarget = false;

        var fogGo = new GameObject("MapFog", typeof(RectTransform), typeof(RawImage));
        fogGo.transform.SetParent(mapGo.transform, false);

        var fogRect = (RectTransform)fogGo.transform;
        fogRect.anchorMin = Vector2.zero;
        fogRect.anchorMax = Vector2.one;
        fogRect.offsetMin = Vector2.zero;
        fogRect.offsetMax = Vector2.zero;

        fogImage = fogGo.GetComponent<RawImage>();
        fogImage.color = Color.white;
        fogImage.raycastTarget = false;

        var markersGo = new GameObject("MarkersLayer", typeof(RectTransform));
        markersGo.transform.SetParent(mapGo.transform, false);
        markerLayer = (RectTransform)markersGo.transform;
        markerLayer.anchorMin = Vector2.zero;
        markerLayer.anchorMax = Vector2.one;
        markerLayer.offsetMin = Vector2.zero;
        markerLayer.offsetMax = Vector2.zero;

        var markerGo = new GameObject("PlayerMarker", typeof(RectTransform), typeof(Image));
        markerGo.transform.SetParent(mapGo.transform, false);

        playerMarker = (RectTransform)markerGo.transform;
        playerMarker.anchorMin = Vector2.zero;
        playerMarker.anchorMax = Vector2.zero;
        playerMarker.pivot = new Vector2(0.5f, 0.5f);
        playerMarker.sizeDelta = new Vector2(12f, 12f);
        playerMarker.anchoredPosition = Vector2.zero;

        var markerImage = markerGo.GetComponent<Image>();
        markerImage.color = markerColor;
        markerImage.raycastTarget = false;

        var headingGo = new GameObject("PlayerHeading", typeof(RectTransform), typeof(Image));
        headingGo.transform.SetParent(markerGo.transform, false);

        var headingRect = (RectTransform)headingGo.transform;
        headingRect.anchorMin = new Vector2(0.5f, 0.5f);
        headingRect.anchorMax = new Vector2(0.5f, 0.5f);
        headingRect.pivot = new Vector2(0.5f, 0f);
        headingRect.sizeDelta = new Vector2(3f, 14f);
        headingRect.anchoredPosition = new Vector2(0f, 5f);

        var headingImage = headingGo.GetComponent<Image>();
        headingImage.color = playerHeadingColor;
        headingImage.raycastTarget = false;

        if (showMinimap)
            BuildMinimap(canvasGo.transform);
        else
            minimapRoot = null;
    }

    private void BuildMinimap(Transform canvasRoot)
    {
        minimapRoot = new GameObject("MiniMapRoot", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
        minimapRoot.transform.SetParent(canvasRoot, false);

        RectTransform minimapRect = (RectTransform)minimapRoot.transform;
        minimapRect.anchorMin = new Vector2(1f, 0f);
        minimapRect.anchorMax = new Vector2(1f, 0f);
        minimapRect.pivot = new Vector2(1f, 0f);
        minimapRect.sizeDelta = new Vector2(minimapSize, minimapSize);
        minimapRect.anchoredPosition = new Vector2(-minimapPadding, minimapPadding);

        Image minimapBackdrop = minimapRoot.GetComponent<Image>();
        minimapBackdrop.color = minimapBackdropColor;
        minimapBackdrop.raycastTarget = false;

        CanvasGroup group = minimapRoot.GetComponent<CanvasGroup>();
        group.interactable = false;
        group.blocksRaycasts = false;

        var maskGo = new GameObject("MiniMapMask", typeof(RectTransform), typeof(Image), typeof(Mask));
        maskGo.transform.SetParent(minimapRoot.transform, false);

        RectTransform maskRect = (RectTransform)maskGo.transform;
        maskRect.anchorMin = Vector2.zero;
        maskRect.anchorMax = Vector2.one;
        maskRect.offsetMin = Vector2.one * minimapInnerPadding;
        maskRect.offsetMax = -Vector2.one * minimapInnerPadding;

        Image maskImage = maskGo.GetComponent<Image>();
        maskImage.color = frameColor;
        maskImage.raycastTarget = false;

        Mask mask = maskGo.GetComponent<Mask>();
        mask.showMaskGraphic = true;

        var mapGo = new GameObject("MiniMapImage", typeof(RectTransform), typeof(RawImage));
        mapGo.transform.SetParent(maskGo.transform, false);

        RectTransform mapRect = (RectTransform)mapGo.transform;
        mapRect.anchorMin = Vector2.zero;
        mapRect.anchorMax = Vector2.one;
        mapRect.offsetMin = Vector2.zero;
        mapRect.offsetMax = Vector2.zero;

        minimapImage = mapGo.GetComponent<RawImage>();
        minimapImage.color = Color.white;
        minimapImage.raycastTarget = false;

        var fogGo = new GameObject("MiniMapFog", typeof(RectTransform), typeof(RawImage));
        fogGo.transform.SetParent(mapGo.transform, false);

        RectTransform fogRect = (RectTransform)fogGo.transform;
        fogRect.anchorMin = Vector2.zero;
        fogRect.anchorMax = Vector2.one;
        fogRect.offsetMin = Vector2.zero;
        fogRect.offsetMax = Vector2.zero;

        minimapFogImage = fogGo.GetComponent<RawImage>();
        minimapFogImage.color = Color.white;
        minimapFogImage.raycastTarget = false;

        var markersGo = new GameObject("MiniMapMarkersLayer", typeof(RectTransform));
        markersGo.transform.SetParent(mapGo.transform, false);
        minimapMarkerLayer = (RectTransform)markersGo.transform;
        minimapMarkerLayer.anchorMin = Vector2.zero;
        minimapMarkerLayer.anchorMax = Vector2.one;
        minimapMarkerLayer.offsetMin = Vector2.zero;
        minimapMarkerLayer.offsetMax = Vector2.zero;

        var markerGo = new GameObject("MiniMapPlayerMarker", typeof(RectTransform), typeof(Image));
        markerGo.transform.SetParent(mapGo.transform, false);

        minimapPlayerMarker = (RectTransform)markerGo.transform;
        minimapPlayerMarker.anchorMin = Vector2.zero;
        minimapPlayerMarker.anchorMax = Vector2.zero;
        minimapPlayerMarker.pivot = new Vector2(0.5f, 0.5f);
        minimapPlayerMarker.sizeDelta = new Vector2(9f, 9f);
        minimapPlayerMarker.anchoredPosition = Vector2.zero;

        Image markerImage = markerGo.GetComponent<Image>();
        markerImage.color = markerColor;
        markerImage.raycastTarget = false;

        var headingGo = new GameObject("MiniMapPlayerHeading", typeof(RectTransform), typeof(Image));
        headingGo.transform.SetParent(markerGo.transform, false);

        RectTransform headingRect = (RectTransform)headingGo.transform;
        headingRect.anchorMin = new Vector2(0.5f, 0.5f);
        headingRect.anchorMax = new Vector2(0.5f, 0.5f);
        headingRect.pivot = new Vector2(0.5f, 0f);
        headingRect.sizeDelta = new Vector2(2f, 10f);
        headingRect.anchoredPosition = new Vector2(0f, 4f);

        Image headingImage = headingGo.GetComponent<Image>();
        headingImage.color = playerHeadingColor;
        headingImage.raycastTarget = false;
    }

    private IEnumerator BakeMapTextureRoutine()
    {
        if (mapImage == null || worldGenerator == null)
        {
            SetCaptureOverlayVisible(false);
            yield break;
        }

        SetCaptureOverlayVisible(true);
        worldGenerator.SetAllChunksRendered(true);
        yield return null;

        MapCaptureState captureState = default;
        bool hasCaptureState = false;

        try
        {
            captureState = BeginMapCaptureState();
            hasCaptureState = true;
            yield return null;
            mapTexture = CaptureTopDownTexture();
            mapImage.texture = mapTexture;
            if (minimapImage != null)
                minimapImage.texture = mapTexture;
        }
        finally
        {
            if (hasCaptureState)
                EndMapCaptureState(captureState);
            worldGenerator.RestoreChunkStreamingVisibility();
            SetCaptureOverlayVisible(false);
        }
    }

    private Texture2D CaptureTopDownTexture()
    {
        var camGo = new GameObject("WorldMapCaptureCamera");
        var camera = camGo.AddComponent<Camera>();
        camera.enabled = false;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = mapBackgroundColor;
        camera.cullingMask = mapCullingMask;
        camera.orthographic = true;
        camera.aspect = 1f;

        float mapRadius = Mathf.Max(mapCaptureRectXZ.width, mapCaptureRectXZ.height) * 0.5f;
        float worldSize = Mathf.Max(worldRectXZ.width, worldRectXZ.height);
        float maxTerrainHeight = Mathf.Max(
            worldGenerator.heightMultiplier + worldGenerator.mountainHeight,
            worldGenerator.invisibleWallHeight
        );
        float cameraHeight = maxTerrainHeight + worldSize + 100f;

        camera.orthographicSize = mapRadius;
        camera.nearClipPlane = 0.3f;
        camera.farClipPlane = cameraHeight + maxTerrainHeight + 300f;

        camera.transform.position = new Vector3(
            mapCaptureRectXZ.center.x,
            cameraHeight,
            mapCaptureRectXZ.center.y
        );
        camera.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

        var renderTexture = RenderTexture.GetTemporary(
            mapTextureSize,
            mapTextureSize,
            24,
            RenderTextureFormat.ARGB32
        );

        camera.targetTexture = renderTexture;

        var oldActive = RenderTexture.active;

        RenderTexture.active = renderTexture;
        camera.Render();

        var texture = new Texture2D(
            mapTextureSize,
            mapTextureSize,
            TextureFormat.RGBA32,
            false,
            false
        );
        texture.ReadPixels(new Rect(0f, 0f, mapTextureSize, mapTextureSize), 0, 0);
        texture.Apply(false, true);

        RenderTexture.active = oldActive;

        camera.targetTexture = null;
        RenderTexture.ReleaseTemporary(renderTexture);
        Destroy(camGo);

        return texture;
    }

    private void BuildFogTexture()
    {
        fogTexture = new Texture2D(fogTextureSize, fogTextureSize, TextureFormat.RGBA32, false, false);
        fogTexture.wrapMode = TextureWrapMode.Clamp;
        fogTexture.filterMode = FilterMode.Bilinear;

        unexploredAlphaByte = (byte)Mathf.RoundToInt(Mathf.Clamp01(unexploredAlpha) * 255f);
        fogPixels = new Color32[fogTextureSize * fogTextureSize];

        var fill = new Color32(0, 0, 0, unexploredAlphaByte);
        for (int i = 0; i < fogPixels.Length; i++)
            fogPixels[i] = fill;

        fogTexture.SetPixels32(fogPixels);
        fogTexture.Apply(false, false);

        if (fogImage != null)
            fogImage.texture = fogTexture;
        if (minimapFogImage != null)
            minimapFogImage.texture = fogTexture;
    }

    private void RevealAround(Vector3 worldPosition)
    {
        if (fogPixels == null || fogTexture == null)
            return;

        Vector2 uv = WorldToMapUv(worldPosition);
        int centerX = Mathf.RoundToInt(uv.x * (fogTextureSize - 1));
        int centerY = Mathf.RoundToInt(uv.y * (fogTextureSize - 1));

        float pixelsPerMeterX = (fogTextureSize - 1) / Mathf.Max(1f, mapCaptureRectXZ.width);
        float pixelsPerMeterZ = (fogTextureSize - 1) / Mathf.Max(1f, mapCaptureRectXZ.height);
        float pixelsPerMeter = 0.5f * (pixelsPerMeterX + pixelsPerMeterZ);

        float radiusPx = Mathf.Max(1f, revealRadiusMeters * pixelsPerMeter);
        float softnessPx = Mathf.Max(0f, revealEdgeSoftnessMeters * pixelsPerMeter);
        float innerPx = Mathf.Max(0f, radiusPx - softnessPx);

        int minX = Mathf.Max(0, Mathf.FloorToInt(centerX - radiusPx));
        int maxX = Mathf.Min(fogTextureSize - 1, Mathf.CeilToInt(centerX + radiusPx));
        int minY = Mathf.Max(0, Mathf.FloorToInt(centerY - radiusPx));
        int maxY = Mathf.Min(fogTextureSize - 1, Mathf.CeilToInt(centerY + radiusPx));

        float radiusSqr = radiusPx * radiusPx;
        bool anyChanged = false;

        for (int y = minY; y <= maxY; y++)
        {
            int row = y * fogTextureSize;
            float dy = y - centerY;

            for (int x = minX; x <= maxX; x++)
            {
                float dx = x - centerX;
                float distSqr = dx * dx + dy * dy;
                if (distSqr > radiusSqr)
                    continue;

                float dist = Mathf.Sqrt(distSqr);
                byte targetAlpha = 0;

                if (dist > innerPx && radiusPx > innerPx)
                {
                    float t = Mathf.InverseLerp(innerPx, radiusPx, dist);
                    targetAlpha = (byte)Mathf.RoundToInt(Mathf.Lerp(0f, unexploredAlphaByte, t));
                }

                int idx = row + x;
                if (targetAlpha < fogPixels[idx].a)
                {
                    fogPixels[idx].a = targetAlpha;
                    anyChanged = true;
                }
            }
        }

        if (!anyChanged)
            return;

        fogTexture.SetPixels32(fogPixels);
        fogTexture.Apply(false, false);
    }

    private Vector2 WorldToMapUv(Vector3 worldPosition)
    {
        float u = Mathf.InverseLerp(mapCaptureRectXZ.xMin, mapCaptureRectXZ.xMax, worldPosition.x);
        float v = Mathf.InverseLerp(mapCaptureRectXZ.yMin, mapCaptureRectXZ.yMax, worldPosition.z);
        return new Vector2(Mathf.Clamp01(u), Mathf.Clamp01(v));
    }

    private void UpdatePlayerMarker()
    {
        if (player == null)
            return;

        UpdatePlayerMarker(playerMarker, mapImage);
        UpdatePlayerMarker(minimapPlayerMarker, minimapImage);
    }

    private void UpdatePlayerMarker(RectTransform marker, RawImage targetMapImage)
    {
        if (marker == null || targetMapImage == null)
            return;

        Vector2 uv = WorldToMapUv(player.position);
        Rect rect = targetMapImage.rectTransform.rect;

        marker.anchoredPosition = new Vector2(
            uv.x * rect.width,
            uv.y * rect.height
        );
        marker.localRotation = Quaternion.Euler(0f, 0f, -player.eulerAngles.y);
    }

    private void OnDestroy()
    {
        MapCollectibleRegistry.RegistryChanged -= HandleCollectibleRegistryChanged;

        ClearMarkerSet(chestMarkers);
        ClearMarkerSet(enhancerMarkers);
        ClearMarkerSet(minimapChestMarkers);
        ClearMarkerSet(minimapEnhancerMarkers);

        if (mapTexture != null)
            Destroy(mapTexture);
        if (fogTexture != null)
            Destroy(fogTexture);
    }
}
