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
    public Image staminaFill;
    public Image expFill;

    [Header("Texts")]
    public TMP_Text levelText;

    [Header("Overheal")]
    [SerializeField] private bool autoCreateOverhealFill = true;
    [SerializeField] private Color overhealColor = new Color(1f, 0.78f, 0.2f, 0.9f);

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

    private PlayerProgressionController player;
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

        EnsureOverhealFillReference();
        ConfigureOverhealFill(healthOverhealFill);
        InitializeLowHealthVignette();

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
        float maxHealth = Mathf.Max(1f, player.MaxHealth);
        float currentHealth = Mathf.Clamp(player.CurrentHealth, 0f, maxHealth);
        float barrier = Mathf.Max(0f, player.CurrentBarrier);
        float combinedMax = maxHealth + barrier;

        if (healthFill && combinedMax > 0f)
            healthFill.fillAmount = Mathf.Clamp01(currentHealth / combinedMax);

        if (healthOverhealFill)
        {
            bool hasBarrier = barrier > 0.01f && combinedMax > 0f;
            healthOverhealFill.enabled = hasBarrier;

            if (hasBarrier)
            {
                float barrierRatio = barrier / combinedMax;
                healthOverhealFill.fillAmount = Mathf.Clamp01(barrierRatio);
            }
            else
            {
                healthOverhealFill.fillAmount = 0f;
            }
        }

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

    private void EnsureOverhealFillReference()
    {
        if (healthOverhealFill != null || !autoCreateOverhealFill || healthFill == null)
            return;

        RectTransform source = healthFill.rectTransform;
        if (source == null || source.parent == null)
            return;

        var go = new GameObject(
            "HealthOverhealFill",
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
        created.type = Image.Type.Filled;
        created.fillMethod = Image.FillMethod.Horizontal;
        created.fillOrigin = (int)Image.OriginHorizontal.Right;
        created.fillClockwise = true;
        created.fillAmount = 0f;
        created.preserveAspect = healthFill.preserveAspect;
        created.material = null;
        created.raycastTarget = false;
        created.color = overhealColor;

        int healthIndex = source.GetSiblingIndex();
        rt.SetSiblingIndex(Mathf.Min(healthIndex + 1, source.parent.childCount - 1));

        healthOverhealFill = created;
        ConfigureOverhealFill(healthOverhealFill);
    }

    private void ConfigureOverhealFill(Image fill)
    {
        if (fill == null)
            return;

        fill.type = Image.Type.Filled;
        fill.fillMethod = Image.FillMethod.Horizontal;
        fill.fillOrigin = (int)Image.OriginHorizontal.Right;
        fill.fillClockwise = true;
        fill.color = overhealColor;
        fill.raycastTarget = false;
        fill.enabled = false;
        fill.fillAmount = 0f;
    }

    private void InitializeLowHealthVignette()
    {
        if (!enableLowHealthScreenFx)
            return;

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

        if (!lowHealthVignetteReady || lowHealthVignette == null)
            return;

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
        Color color = Color.Lerp(baseVignetteColor, lowHealthVignetteColor, w);
        float intensity = Mathf.Lerp(baseVignetteIntensity, Mathf.Max(baseVignetteIntensity, lowHealthMaxVignetteIntensity), w);
        float smoothness = Mathf.Lerp(baseVignetteSmoothness, Mathf.Max(baseVignetteSmoothness, lowHealthMaxVignetteSmoothness), w);

        lowHealthVignette.color.value = color;
        lowHealthVignette.intensity.value = intensity;
        lowHealthVignette.smoothness.value = smoothness;
    }
}
