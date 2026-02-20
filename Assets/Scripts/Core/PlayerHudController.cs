using UnityEngine;
using UnityEngine.UI;
using TMPro;
using GrassSim.Core;
using System.Collections;

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

    private PlayerProgressionController player;
    private BossEncounterController bossEncounter;
    private float nextBossReadabilityRefreshAt;

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

        Debug.Log("[HUD] Player hooked");
    }

    void Update()
    {
        if (player == null)
            return;

        UpdateBars();
        UpdateLevel();
        UpdateBossReadabilityDebug();
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
}
