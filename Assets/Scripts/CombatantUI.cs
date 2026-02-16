using UnityEngine;
using UnityEngine.UI;
using GrassSim.Combat;
using GrassSim.Core;

public class CombatantUI : MonoBehaviour
{
    [Header("Screen UI Prefabs")]
    public GameObject healthBarPrefab;
    public GameObject staminaBarPrefab;

    private GameObject healthGO;
    private GameObject staminaGO;

    private ScreenSpaceWorldUI healthFollower;
    private ScreenSpaceWorldUI staminaFollower;

    private Image healthFill;
    private Image staminaFill;

    private Combatant combatant;
    private PlayerProgressionController playerProg;
    private BaseCombatAgent agent;

    private bool initializedUI = false;

    void Awake()
    {
        combatant = GetComponentInParent<Combatant>();
        playerProg = GetComponentInParent<PlayerProgressionController>();
        agent = GetComponentInParent<BaseCombatAgent>();

        // Jeżeli nie jest graczem — NIE wykrywamy UI
        if (playerProg == null && agent == null)
        {
            enabled = false;
            return;
        }

        if (combatant == null)
        {
            Debug.LogWarning("[CombatantUI] No Combatant found on parent!", this);
            enabled = false;
            return;
        }

        InitializeCombatantStats();
        SetupUI();
    }

    private void InitializeCombatantStats()
    {
        if (combatant.MaxHealth > 0f)
            return;

        float statMax = 0f;
        if (playerProg != null && playerProg.stats != null)
            statMax = playerProg.stats.maxHealth;
        else if (agent != null)
            statMax = agent.maxHealth;

        if (statMax > 0f)
            combatant.Initialize(statMax);
    }

    private void SetupUI()
    {
        Canvas uiCanvas = Object.FindFirstObjectByType<Canvas>();
        if (uiCanvas == null)
        {
            Debug.LogError("[CombatantUI] No Canvas found in scene!", this);
            return;
        }

        // HEALTH BAR — tylko dla Playera
        if (playerProg != null && healthBarPrefab != null)
        {
            healthGO = Instantiate(healthBarPrefab, uiCanvas.transform);

            healthFollower = healthGO.GetComponent<ScreenSpaceWorldUI>();

            Transform fillTf = healthGO.transform.Find("Fill");
            if (fillTf != null)
                healthFill = fillTf.GetComponent<Image>();
            else
                Debug.LogError("[CombatantUI] Could not find health bar Fill child!", healthGO);

            if (healthFollower == null || healthFill == null)
                Debug.LogError("[CombatantUI] Health bar prefab missing required components!", healthGO);
            else
            {
                healthFollower.target = transform;
                healthFollower.worldOffset = Vector3.up * GetHealthbarOffset();
            }
        }

        // STAMINA BAR — tylko dla Playera
        if (playerProg != null && staminaBarPrefab != null)
        {
            staminaGO = Instantiate(staminaBarPrefab, uiCanvas.transform);

            staminaFollower = staminaGO.GetComponent<ScreenSpaceWorldUI>();

            Transform stamFillTf = staminaGO.transform.Find("StaminaBarFill");
            if (stamFillTf != null)
                staminaFill = stamFillTf.GetComponent<Image>();
            else
                Debug.LogError("[CombatantUI] Could not find stamina bar Fill child!", staminaGO);

            if (staminaFollower == null || staminaFill == null)
                Debug.LogError("[CombatantUI] Stamina bar prefab missing required components!", staminaGO);
            else
            {
                staminaFollower.target = transform;
                staminaFollower.worldOffset = Vector3.up * (GetHealthbarOffset() + 0.35f);
            }
        }

        initializedUI = true;
    }

    void Update()
    {
        if (!initializedUI || combatant == null)
            return;

        UpdateHealthUI();
        UpdateStaminaUI();
    }

    private void UpdateHealthUI()
    {
        if (healthFill == null) return;

        float currentHP = combatant.currentHealth;
        float maxHP = combatant.MaxHealth;
        if (maxHP <= 0f) return;

        healthFill.fillAmount = Mathf.Clamp01(currentHP / maxHP);
    }

    private void UpdateStaminaUI()
    {
        if (staminaFill == null) return;

        float currentS = playerProg != null ? playerProg.currentStamina : 0f;
        float maxS = playerProg != null && playerProg.stats != null ? playerProg.stats.maxStamina : 0f;

        if (maxS > 0f)
            staminaFill.fillAmount = Mathf.Clamp01(currentS / maxS);
    }

    private float GetHealthbarOffset()
    {
        var cap = GetComponentInParent<CapsuleCollider>();
        if (cap != null) return cap.height + 0.2f;

        var cc = GetComponentInParent<CharacterController>();
        if (cc != null) return cc.height + 0.2f;

        var rend = GetComponentInChildren<Renderer>();
        if (rend != null) return rend.bounds.size.y + 0.2f;

        return 2f;
    }

    void OnDestroy()
    {
        if (healthGO != null) Destroy(healthGO);
        if (staminaGO != null) Destroy(staminaGO);
    }
}
