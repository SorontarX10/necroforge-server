using UnityEngine;
using UnityEngine.UI;
using TMPro;
using GrassSim.Core;
using System.Collections;

public class PlayerHUDController : MonoBehaviour
{
    [Header("Fills")]
    public Image healthFill;
    public Image staminaFill;
    public Image expFill;

    [Header("Texts")]
    public TMP_Text levelText;

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
        if (healthFill && player.stats.maxHealth > 0f)
            healthFill.fillAmount = player.currentHealth / player.stats.maxHealth;

        if (staminaFill && player.stats.maxStamina > 0f)
            staminaFill.fillAmount = player.currentStamina / player.stats.maxStamina;

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
}
