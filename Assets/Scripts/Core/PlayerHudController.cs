using UnityEngine;
using UnityEngine.UI;
using TMPro;
using GrassSim.Core;
using System.Collections;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class PlayerHUDController : MonoBehaviour
{
    [Header("Fills")]
    public Image healthFill;
    public Image healthOverhealFill;
    public Image healthOverhealPulseFill;
    public Image staminaFill;
    public Image expFill;

    [Header("Texts")]
    public TMP_Text levelText;

    [Header("Overheal")]
    [SerializeField] private bool autoCreateOverhealFill = true;
    [SerializeField] private Color overhealColor = new Color(1f, 0.78f, 0.2f, 0.9f);
    [SerializeField] private Color overhealPulseColor = new Color(1f, 0.96f, 0.58f, 0.95f);
    [SerializeField, Min(0f)] private float minVisibleOverhealFraction = 0.02f;
    [SerializeField, Min(0.05f)] private float overhealPulseDecaySeconds = 0.7f;
    [SerializeField, Min(0f)] private float overhealPulseGainScale = 1f;

    [Header("Debug Boss Readability")]
    [SerializeField] private bool showBossReadabilityDebug;
    [SerializeField] private TMP_Text bossReadabilityText;
    [SerializeField, Min(0.05f)] private float bossReadabilityRefreshInterval = 0.25f;

    [Header("Low Health Screen FX")]
    [SerializeField] private bool enableLowHealthScreenFx = true;
    [SerializeField, Range(0.05f, 1f)] private float lowHealthFxThreshold = 0.5f;
    [SerializeField] private Color lowHealthVignetteColor = new Color(0.85f, 0.08f, 0.08f, 1f);
    [SerializeField, Range(0f, 1f)] private float lowHealthMaxVignetteIntensity = 0.38f;
    [SerializeField, Range(0f, 1f)] private float lowHealthMaxVignetteSmoothness = 0.56f;
    [SerializeField, Min(0.1f)] private float lowHealthFxBlendSpeed = 5f;
    [SerializeField, Min(0.1f)] private float lowHealthFxResolveInterval = 0.75f;

    [Header("Low Health Edge Overlay")]
    [SerializeField] private bool enableLowHealthEdgeOverlay = true;
    [SerializeField, Range(0f, 1f)] private float lowHealthMaxEdgeOverlayAlpha = 0.42f;
    [SerializeField, Min(64)] private int lowHealthOverlayTextureSize = 256;

    private PlayerProgressionController player;
    private PlayerRelicController playerRelics;
    private BossEncounterController bossEncounter;
    private float nextBossReadabilityRefreshAt;
    private Volume lowHealthVolume;
    private Vignette lowHealthVignette;
    private Color baseVignetteColor = Color.black;
    private float baseVignetteIntensity;
    private float baseVignetteSmoothness;
    private bool lowHealthVignetteReady;
    private float currentLowHealthFxWeight;
    private float nextLowHealthFxResolveAt;
    private Image lowHealthEdgeOverlayImage;
    private Texture2D lowHealthEdgeOverlayTexture;
    private Sprite lowHealthEdgeOverlaySprite;
    private float overhealPulseFraction;
    private static readonly Color DefaultOverhealColor = new(1f, 0.78f, 0.2f, 0.9f);
    private static readonly Color DefaultOverhealPulseColor = new(1f, 0.96f, 0.58f, 0.95f);
    private const float DefaultMinVisibleOverhealFraction = 0.02f;
    private const float DefaultOverhealPulseDecaySeconds = 0.7f;
    private const float DefaultOverhealPulseGainScale = 1f;

    void Start()
    {
        StartCoroutine(WaitForPlayer());
    }

    IEnumerator WaitForPlayer()
    {
        while (player == null)
        {
            player = PlayerLocator.GetProgression();
            yield return null;
        }

        RefreshRelicHook();
        EnsureOverhealRuntimeDefaults();
        EnsureOverhealFillReferences();
        ConfigureOverhealFill(healthOverhealFill, overhealColor);
        ConfigureOverhealFill(healthOverhealPulseFill, overhealPulseColor);
        EnsureOverhealFillRenderOrder();
        InitializeLowHealthVignette();
        EnsureLowHealthEdgeOverlay();

        Debug.Log("[HUD] Player hooked");
    }

    void Update()
    {
        if (player == null)
            return;

        UpdateBars();
        UpdateLevel();
        UpdateBossReadabilityDebug();
        UpdateLowHealthScreenFx();
    }

    void UpdateBars()
    {
        RefreshRelicHook();
        EnsureOverhealRuntimeDefaults();
        EnsureOverhealFillReferences();
        EnsureOverhealFillRenderOrder();

        float maxHealth = Mathf.Max(1f, player.MaxHealth);
        float currentHealth = Mathf.Clamp(player.CurrentHealth, 0f, maxHealth);
        float barrier = Mathf.Max(0f, player.CurrentBarrier);

        if (healthFill && maxHealth > 0f)
            healthFill.fillAmount = Mathf.Clamp01(currentHealth / maxHealth);

        float barrierFraction = maxHealth > 0f ? Mathf.Clamp01(barrier / maxHealth) : 0f;
        bool hasBarrier = barrier > 0.01f && barrierFraction > 0f;
        if (hasBarrier)
            barrierFraction = Mathf.Max(barrierFraction, Mathf.Clamp01(minVisibleOverhealFraction));

        if (healthOverhealFill != null)
        {
            healthOverhealFill.enabled = hasBarrier;
            healthOverhealFill.fillAmount = hasBarrier ? barrierFraction : 0f;
        }

        UpdateOverhealPulseFill();

        if (staminaFill && player.MaxStamina > 0f)
            staminaFill.fillAmount = player.CurrentStamina / player.MaxStamina;

        if (expFill && player.xp.expToNext > 0)
            expFill.fillAmount = (float)player.xp.exp / player.xp.expToNext;
    }

    void UpdateLevel()
    {
        if (levelText)
            levelText.text = "LVL: " + player.xp.level.ToString();
    }

    void UpdateBossReadabilityDebug()
    {
        if (!showBossReadabilityDebug || bossReadabilityText == null)
            return;

        if (Time.unscaledTime < nextBossReadabilityRefreshAt)
            return;

        nextBossReadabilityRefreshAt = Time.unscaledTime + bossReadabilityRefreshInterval;

        if (bossEncounter == null)
            bossEncounter = FindFirstObjectByType<BossEncounterController>();

        BossEnemyController boss = bossEncounter != null
            ? bossEncounter.ActiveBoss
            : FindFirstObjectByType<BossEnemyController>();

        if (boss == null || !boss.IsAlive)
        {
            bossReadabilityText.text = "Boss readability: --";
            return;
        }

        if (!boss.TryGetReadabilitySnapshot(out float dodgeRate, out float avgReactionSeconds, out float durationMultiplier))
        {
            bossReadabilityText.text = $"Boss readability [{boss.ArchetypeLabel}] samples: --";
            return;
        }

        bossReadabilityText.text =
            $"Boss readability [{boss.ArchetypeLabel}] dodge {dodgeRate * 100f:0.#}% | react {avgReactionSeconds * 1000f:0} ms | tele x{durationMultiplier:0.00}";
    }

    private void EnsureOverhealFillReferences()
    {
        if (healthFill == null)
            return;

        bool shouldAutoCreate = autoCreateOverhealFill || healthOverhealFill == null || healthOverhealPulseFill == null;
        if (!shouldAutoCreate)
            return;

        if (healthOverhealFill == null)
            healthOverhealFill = CreateOverhealFill("HealthOverhealFill", overhealColor);

        if (healthOverhealPulseFill == null)
            healthOverhealPulseFill = CreateOverhealFill("HealthOverhealPulseFill", overhealPulseColor);
    }

    private Image CreateOverhealFill(string name, Color color)
    {
        if (healthFill == null)
            return null;

        RectTransform source = healthFill.rectTransform;
        if (source == null || source.parent == null)
            return null;

        Transform existing = source.parent.Find(name);
        if (existing != null && existing.TryGetComponent(out Image existingImage))
        {
            ConfigureOverhealFill(existingImage, color);
            return existingImage;
        }

        var go = new GameObject(
            name,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image)
        );
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.SetParent(source.parent, false);
        rt.anchorMin = source.anchorMin;
        rt.anchorMax = source.anchorMax;
        rt.pivot = source.pivot;
        rt.anchoredPosition = source.anchoredPosition;
        rt.sizeDelta = source.sizeDelta;
        rt.localRotation = source.localRotation;
        rt.localScale = source.localScale;

        Image created = go.GetComponent<Image>();
        created.sprite = healthFill.sprite;
        created.preserveAspect = healthFill.preserveAspect;
        created.material = healthFill.material;
        ConfigureOverhealFill(created, color);
        return created;
    }

    private void EnsureOverhealRuntimeDefaults()
    {
        if (healthFill == null)
            return;

        if (healthOverhealFill == null || healthOverhealPulseFill == null)
            autoCreateOverhealFill = true;

        if (overhealColor.a <= 0.001f)
            overhealColor = DefaultOverhealColor;

        if (overhealPulseColor.a <= 0.001f)
            overhealPulseColor = DefaultOverhealPulseColor;

        if (minVisibleOverhealFraction <= 0f)
            minVisibleOverhealFraction = DefaultMinVisibleOverhealFraction;

        if (overhealPulseDecaySeconds <= 0.01f)
            overhealPulseDecaySeconds = DefaultOverhealPulseDecaySeconds;

        if (overhealPulseGainScale <= 0f)
            overhealPulseGainScale = DefaultOverhealPulseGainScale;
    }

    private void ConfigureOverhealFill(Image fill, Color color)
    {
        if (fill == null)
            return;

        fill.type = Image.Type.Filled;
        fill.fillMethod = Image.FillMethod.Horizontal;
        fill.fillOrigin = (int)Image.OriginHorizontal.Right;
        fill.fillClockwise = true;
        fill.color = color;
        fill.raycastTarget = false;
        fill.enabled = false;
        fill.fillAmount = 0f;
    }

    private void EnsureOverhealFillRenderOrder()
    {
        if (healthFill == null)
            return;

        RectTransform baseFill = healthFill.rectTransform;
        if (baseFill == null || baseFill.parent == null)
            return;

        int nextIndex = baseFill.GetSiblingIndex() + 1;
        nextIndex = SetFillSiblingIndex(healthOverhealFill, baseFill, nextIndex);
        SetFillSiblingIndex(healthOverhealPulseFill, baseFill, nextIndex);
    }

    private static int SetFillSiblingIndex(Image fill, RectTransform baseFill, int desiredIndex)
    {
        if (fill == null)
            return desiredIndex;

        RectTransform fillRect = fill.rectTransform;
        if (fillRect == null || fillRect.parent != baseFill.parent)
            return desiredIndex;

        int clampedIndex = Mathf.Clamp(desiredIndex, 0, baseFill.parent.childCount - 1);
        if (fillRect.GetSiblingIndex() != clampedIndex)
            fillRect.SetSiblingIndex(clampedIndex);

        return clampedIndex + 1;
    }

    private void UpdateOverhealPulseFill()
    {
        if (overhealPulseFraction > 0f)
        {
            float decay = Time.unscaledDeltaTime / Mathf.Max(0.05f, overhealPulseDecaySeconds);
            overhealPulseFraction = Mathf.Max(0f, overhealPulseFraction - decay);
        }

        if (healthOverhealPulseFill == null)
            return;

        bool showPulse = overhealPulseFraction > 0.001f;
        healthOverhealPulseFill.enabled = showPulse;
        if (!showPulse)
        {
            healthOverhealPulseFill.fillAmount = 0f;
            return;
        }

        float minVisible = Mathf.Clamp01(minVisibleOverhealFraction);
        float pulseFraction = Mathf.Max(overhealPulseFraction, minVisible);
        healthOverhealPulseFill.fillAmount = Mathf.Clamp01(pulseFraction);

        Color pulseColor = overhealPulseColor;
        float fade = Mathf.Clamp01(overhealPulseFraction * 3f);
        pulseColor.a *= Mathf.Lerp(0.35f, 1f, fade);
        healthOverhealPulseFill.color = pulseColor;
    }

    private void RefreshRelicHook()
    {
        if (player == null)
            return;

        PlayerRelicController resolved = player.GetComponent<PlayerRelicController>();
        if (resolved == playerRelics)
            return;

        if (playerRelics != null)
            playerRelics.OnHealed -= HandlePlayerHealed;

        playerRelics = resolved;
        if (playerRelics != null)
            playerRelics.OnHealed += HandlePlayerHealed;
    }

    private void HandlePlayerHealed(float amount, float overheal)
    {
        if (overheal <= 0f || player == null)
            return;

        float maxHealth = Mathf.Max(1f, player.MaxHealth);
        float gainScale = Mathf.Max(0f, overhealPulseGainScale);
        float normalized = Mathf.Clamp01((overheal / maxHealth) * gainScale);
        if (normalized <= 0f)
            return;

        overhealPulseFraction = Mathf.Clamp01(overhealPulseFraction + normalized);
    }

    private void InitializeLowHealthVignette()
    {
        if (!enableLowHealthScreenFx)
            return;

        EnsureGameplayCameraPostProcessing();

        Volume[] volumes = FindObjectsByType<Volume>(FindObjectsSortMode.None);
        for (int i = 0; i < volumes.Length; i++)
        {
            if (volumes[i] != null && volumes[i].isGlobal)
            {
                lowHealthVolume = volumes[i];
                break;
            }
        }

        if (lowHealthVolume == null)
            return;

        VolumeProfile runtimeProfile = lowHealthVolume.profile;
        if (runtimeProfile == null)
            return;

        if (!runtimeProfile.TryGet(out lowHealthVignette) || lowHealthVignette == null)
            lowHealthVignette = runtimeProfile.Add<Vignette>(true);

        if (lowHealthVignette == null)
            return;

        baseVignetteColor = lowHealthVignette.color.value;
        baseVignetteIntensity = lowHealthVignette.intensity.value;
        baseVignetteSmoothness = lowHealthVignette.smoothness.value;

        lowHealthVignette.color.overrideState = true;
        lowHealthVignette.intensity.overrideState = true;
        lowHealthVignette.smoothness.overrideState = true;
        lowHealthVignetteReady = true;
    }

    private static void EnsureGameplayCameraPostProcessing()
    {
        Camera targetCamera = Camera.main;
        if (targetCamera == null)
        {
            Camera[] cameras = FindObjectsByType<Camera>(FindObjectsSortMode.None);
            for (int i = 0; i < cameras.Length; i++)
            {
                Camera candidate = cameras[i];
                if (candidate == null || candidate.cameraType != CameraType.Game)
                    continue;

                targetCamera = candidate;
                break;
            }
        }

        if (targetCamera == null)
            return;

        if (!targetCamera.TryGetComponent(out UniversalAdditionalCameraData cameraData))
            return;

        if (!cameraData.renderPostProcessing)
            cameraData.renderPostProcessing = true;
    }

    private void EnsureLowHealthEdgeOverlay()
    {
        if (!enableLowHealthEdgeOverlay || lowHealthEdgeOverlayImage != null)
            return;

        Canvas targetCanvas = GetComponentInParent<Canvas>();
        if (targetCanvas == null)
            return;

        RectTransform parent = targetCanvas.transform as RectTransform;
        if (parent == null)
            return;

        GameObject overlayGo = new GameObject(
            "LowHealthEdgeOverlay",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image)
        );
        RectTransform rt = overlayGo.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        Image overlay = overlayGo.GetComponent<Image>();
        overlay.sprite = GetLowHealthEdgeOverlaySprite();
        overlay.color = new Color(lowHealthVignetteColor.r, lowHealthVignetteColor.g, lowHealthVignetteColor.b, 0f);
        overlay.raycastTarget = false;
        overlay.enabled = false;
        overlay.preserveAspect = false;

        rt.SetAsLastSibling();
        lowHealthEdgeOverlayImage = overlay;
    }

    private Sprite GetLowHealthEdgeOverlaySprite()
    {
        if (lowHealthEdgeOverlaySprite != null)
            return lowHealthEdgeOverlaySprite;

        int size = Mathf.Max(64, lowHealthOverlayTextureSize);
        lowHealthEdgeOverlayTexture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "LowHealthEdgeOverlayTexture",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };

        Vector2 center = new Vector2((size - 1) * 0.5f, (size - 1) * 0.5f);
        float radius = size * 0.5f;
        Color32[] pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float nx = (x - center.x) / Mathf.Max(1f, radius);
                float ny = (y - center.y) / Mathf.Max(1f, radius);
                float distance01 = Mathf.Clamp01(Mathf.Sqrt(nx * nx + ny * ny));
                float edge = Mathf.InverseLerp(0.58f, 0.98f, distance01);
                edge = edge * edge;
                byte a = (byte)Mathf.RoundToInt(Mathf.Clamp01(edge) * 255f);
                pixels[y * size + x] = new Color32(255, 255, 255, a);
            }
        }

        lowHealthEdgeOverlayTexture.SetPixels32(pixels);
        lowHealthEdgeOverlayTexture.Apply(false, false);
        lowHealthEdgeOverlaySprite = Sprite.Create(
            lowHealthEdgeOverlayTexture,
            new Rect(0f, 0f, size, size),
            new Vector2(0.5f, 0.5f),
            size
        );
        return lowHealthEdgeOverlaySprite;
    }

    private void UpdateLowHealthScreenFx()
    {
        if (!enableLowHealthScreenFx || player == null)
            return;

        if (!lowHealthVignetteReady)
        {
            if (Time.unscaledTime >= nextLowHealthFxResolveAt)
            {
                nextLowHealthFxResolveAt = Time.unscaledTime + Mathf.Max(0.1f, lowHealthFxResolveInterval);
                InitializeLowHealthVignette();
            }
        }

        float maxHealth = Mathf.Max(1f, player.MaxHealth);
        float health01 = Mathf.Clamp01(player.CurrentHealth / maxHealth);
        float threshold = Mathf.Clamp(lowHealthFxThreshold, 0.05f, 1f);

        float targetWeight = health01 < threshold
            ? Mathf.InverseLerp(threshold, 0f, health01)
            : 0f;

        currentLowHealthFxWeight = Mathf.MoveTowards(
            currentLowHealthFxWeight,
            targetWeight,
            Time.unscaledDeltaTime * Mathf.Max(0.1f, lowHealthFxBlendSpeed)
        );

        float w = Mathf.Clamp01(currentLowHealthFxWeight);
        if (lowHealthVignetteReady && lowHealthVignette != null)
        {
            Color color = Color.Lerp(baseVignetteColor, lowHealthVignetteColor, w);
            float intensity = Mathf.Lerp(baseVignetteIntensity, Mathf.Max(baseVignetteIntensity, lowHealthMaxVignetteIntensity), w);
            float smoothness = Mathf.Lerp(baseVignetteSmoothness, Mathf.Max(baseVignetteSmoothness, lowHealthMaxVignetteSmoothness), w);

            lowHealthVignette.color.value = color;
            lowHealthVignette.intensity.value = intensity;
            lowHealthVignette.smoothness.value = smoothness;
        }

        EnsureLowHealthEdgeOverlay();
        if (lowHealthEdgeOverlayImage != null)
        {
            float overlayAlpha = Mathf.Clamp01(w) * Mathf.Clamp01(lowHealthMaxEdgeOverlayAlpha);
            Color overlayColor = lowHealthVignetteColor;
            overlayColor.a = overlayAlpha;
            lowHealthEdgeOverlayImage.color = overlayColor;
            lowHealthEdgeOverlayImage.enabled = overlayAlpha > 0.001f;
        }
    }

    private void OnDestroy()
    {
        if (playerRelics != null)
            playerRelics.OnHealed -= HandlePlayerHealed;

        if (lowHealthEdgeOverlaySprite != null)
            Destroy(lowHealthEdgeOverlaySprite);

        if (lowHealthEdgeOverlayTexture != null)
            Destroy(lowHealthEdgeOverlayTexture);
    }
}
