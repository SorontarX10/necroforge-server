using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

using GrassSim.Core;
using GrassSim.Combat;
using GrassSim.Stats;
using GrassSim.Enhancers;
using GrassSim.AI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class StatsPanelController : MonoBehaviour
{
    [Header("Layout")]
    [SerializeField] private RectTransform panelRoot;
    [SerializeField] private RectTransform headerRect;
    [SerializeField] private RectTransform contentRect;

    [Header("Sections (Global always on; others only when expanded)")]
    [SerializeField] private GameObject globalSection;
    [SerializeField] private GameObject combatSection;
    [SerializeField] private GameObject survivalSection;

    [Header("Header UI")]
    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private TextMeshProUGUI chevronText;

    [Header("Game References")]
    [SerializeField] private WorldStats world;
    [SerializeField] private PlayerProgressionController player;
    [SerializeField] private WeaponController weapon;

    [Header("Global Rows")]
    [SerializeField] private StatRowUI difficultyRow;
    [SerializeField] private StatRowUI enemiesSpawnedRow;
    [SerializeField] private StatRowUI enemiesKilledRow;

    [Header("Combat Rows")]
    [SerializeField] private StatRowUI damageRow;
    [SerializeField] private StatRowUI swingSpeedRow;
    [SerializeField] private StatRowUI critChanceRow;
    [SerializeField] private StatRowUI critDamageRow;
    [SerializeField] private StatRowUI lifeStealRow;
    [SerializeField] private StatRowUI speedRow;

    [Header("Survival Rows (MAX VALUES ONLY)")]
    [SerializeField] private StatRowUI healthRow;      // MAX HEALTH
    [SerializeField] private StatRowUI staminaRow;     // MAX STAMINA
    [SerializeField] private StatRowUI healthRegenRow;
    [SerializeField] private StatRowUI staminaRegenRow;
    [SerializeField] private StatRowUI damageReductionRow;
    [SerializeField] private StatRowUI dodgeChanceRow;

    [Header("Animation")]
    [SerializeField] private float animationSpeed = 12f;
    [SerializeField] private float heightSnapEpsilon = 0.5f;

    [Header("UI Refresh Limits")]
    [SerializeField] private float statsRefreshInterval = 0.25f;

    [Header("Resolve Player")]
    [SerializeField] private float resolveRetryInterval = 0.25f;

    [Header("Sizing")]
    [Tooltip("Dodatkowy margines w px, żeby ramka nie 'ścinała' dołu.")]
    [SerializeField] private float bottomPadding = 8f;

    private bool isExpanded;
    private float collapsedHeight;
    private float targetHeight;
    private float lastStatsRefreshTime;
    private float lastPeriodicRefreshTime;
    private bool rowsResolved;
    [SerializeField] private bool debugStatsPanel;
    private float lastDebugTime;

    private Coroutine resolveRoutine;
    private Coroutine sizeRoutine;
    private bool subscribed;
    [SerializeField] private WeaponEnhancerSystem enhancers; // optional override
    private EnemyActivationController enemyActivationController;

    // ================= UNITY =================

    private void Awake()
    {
        collapsedHeight = headerRect != null ? headerRect.rect.height : 0f;

        SetExpandedVisual(false);
        SetSectionsExpanded(false);

        if (panelRoot != null)
            panelRoot.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, collapsedHeight);

        targetHeight = collapsedHeight;
    }

    private void OnEnable()
    {
        EnsureWorld();
        if (world != null)
            world.OnChanged += OnWorldChanged;

        globalSection?.SetActive(true);

        if (!IsValidPlayerRef(player))
            player = null;

        if (player == null)
            resolveRoutine = StartCoroutine(ResolvePlayerRoutine());
        else
            AttachToPlayer(player);

        RefreshWorldStats();
    }

    private void OnDisable()
    {
        if (world != null)
            world.OnChanged -= OnWorldChanged;

        if (resolveRoutine != null)
        {
            StopCoroutine(resolveRoutine);
            resolveRoutine = null;
        }

        if (sizeRoutine != null)
        {
            StopCoroutine(sizeRoutine);
            sizeRoutine = null;
        }

        DetachFromPlayer();
    }

    private void Update()
    {
        HandleInput();
        AnimateHeightIfNeeded();
        LogDebug();

        if (!IsValidPlayerRef(player) && resolveRoutine == null)
            resolveRoutine = StartCoroutine(ResolvePlayerRoutine());

        if (isExpanded && Time.unscaledTime - lastPeriodicRefreshTime >= statsRefreshInterval)
        {
            lastPeriodicRefreshTime = Time.unscaledTime;
            RefreshWorldStats();
            RefreshCombatAndSurvivalStats();
        }
    }

    // ================= PLAYER RESOLVE =================

    private IEnumerator ResolvePlayerRoutine()
    {
        while (true)
        {
            var candidate = PlayerLocator.GetProgression();
            if (IsValidPlayerRef(candidate))
            {
                player = candidate;
                break;
            }

            yield return new WaitForSecondsRealtime(resolveRetryInterval);
        }

        AttachToPlayer(player);
        resolveRoutine = null;
    }

    private void AttachToPlayer(PlayerProgressionController p)
    {
        if (!IsValidPlayerRef(p)) return;

        DetachFromPlayer();

        player = p;

        if (weapon == null)
            weapon = player.GetComponentInChildren<WeaponController>(true);

        if (enhancers == null)
        {
            if (weapon != null)
                enhancers = weapon.GetComponentInChildren<WeaponEnhancerSystem>(true);

            if (enhancers == null)
                enhancers = player.GetComponentInChildren<WeaponEnhancerSystem>(true);
        }

        player.OnStatsChanged += OnPlayerStatsChanged;

        if (enhancers != null)
            enhancers.OnChanged += OnEnhancersChanged;

        subscribed = true;

        // 🔥 WAŻNE: wymuś pełny refresh po podpięciu gracza
        RefreshWorldStats();
        RefreshCombatAndSurvivalStats();

        if (isExpanded)
        {
            RefreshAllExpanded();
            RecalculateExpandedHeightOnce();
            ScheduleRecalcExpandedHeight();
        }
        else
        {
            RefreshWorldStats();
        }
    }

    private void DetachFromPlayer()
    {
        if (!subscribed) return;

        if (player != null)
            player.OnStatsChanged -= OnPlayerStatsChanged;

        if (enhancers != null)
            enhancers.OnChanged -= OnEnhancersChanged;

        subscribed = false;
        player = null;
        weapon = null;
        enhancers = null;
    }

    private void OnEnhancersChanged()
    {
        RefreshCombatAndSurvivalStats();

        if (isExpanded)
            ScheduleRecalcExpandedHeight();
    }

    // ================= INPUT =================

    private void HandleInput()
    {
#if ENABLE_INPUT_SYSTEM
        var keyboard = Keyboard.current;
        if (keyboard == null) return;

        if (keyboard.tabKey.wasPressedThisFrame)
            Expand();

        if (keyboard.tabKey.wasReleasedThisFrame)
            Collapse();
#else
        if (Input.GetKeyDown(KeyCode.Tab))
            Expand();

        if (Input.GetKeyUp(KeyCode.Tab))
            Collapse();
#endif
    }

    private bool IsValidPlayerRef(PlayerProgressionController p)
    {
        if (p == null) return false;
        if (!p.gameObject.scene.IsValid()) return false; // prefab asset ref
        return p.stats != null;
    }

    private void EnsureWorld()
    {
        if (world != null) return;
        world = WorldStats.Instance != null
            ? WorldStats.Instance
            : FindFirstObjectByType<WorldStats>();
    }


    private void EnsureRowRefs()
    {
        if (rowsResolved) return;
        var root = contentRect != null ? contentRect : panelRoot;
        if (root == null) return;

        var rows = root.GetComponentsInChildren<StatRowUI>(true);
        if (rows == null || rows.Length == 0) return;

        StatRowUI Find(string name)
        {
            foreach (var r in rows)
            {
                if (r != null && r.gameObject.name == name)
                    return r;
            }
            return null;
        }

        if (difficultyRow == null) difficultyRow = Find("StatRow_Difficulty");
        if (enemiesSpawnedRow == null) enemiesSpawnedRow = Find("StatRow_EnemiesSpawned");
        if (enemiesKilledRow == null) enemiesKilledRow = Find("StatRow_EnemiesKilled");

        if (damageRow == null) damageRow = Find("StatRow_Damage");
        if (swingSpeedRow == null) swingSpeedRow = Find("StatRow_SwingSpeed");
        if (critChanceRow == null) critChanceRow = Find("StatRow_CritChance");
        if (critDamageRow == null) critDamageRow = Find("StatRow_CritDamage");
        if (lifeStealRow == null) lifeStealRow = Find("StatRow_LifeSteal");
        if (speedRow == null) speedRow = Find("StatRow_Speed");

        if (healthRow == null) healthRow = Find("StatRow_Health");
        if (staminaRow == null) staminaRow = Find("StatRow_Stamina");
        if (healthRegenRow == null) healthRegenRow = Find("StatRow_HealthRegen");
        if (staminaRegenRow == null) staminaRegenRow = Find("StatRow_StaminaRegen");
        if (damageReductionRow == null) damageReductionRow = Find("StatRow_DamageReduction");
        if (dodgeChanceRow == null) dodgeChanceRow = Find("StatRow_DodgeChance");

        rowsResolved = difficultyRow != null
            && enemiesSpawnedRow != null
            && enemiesKilledRow != null
            && damageRow != null
            && swingSpeedRow != null
            && critChanceRow != null
            && critDamageRow != null
            && lifeStealRow != null
            && healthRow != null
            && staminaRow != null
            && healthRegenRow != null
            && staminaRegenRow != null
            && damageReductionRow != null
            && dodgeChanceRow != null;
    }

    private void LogDebug()
    {
        if (!debugStatsPanel) return;
        if (Time.unscaledTime - lastDebugTime < 1f) return;
        lastDebugTime = Time.unscaledTime;

        // string RowInfo(StatRowUI row)
        // {
        //     if (row == null) return "<null row>";
        //     var val = row.DebugValueText;
        //     return $"val=\"{val}\" hasValue={row.HasValueText}";
        // }

        // Debug.Log($"[StatsPanel] world={(world != null)} player={(player != null)} stats={(player != null && player.stats != null)} rowsResolved={rowsResolved} expanded={isExpanded}");
        // Debug.Log($"[StatsPanel] difficulty {RowInfo(difficultyRow)} enemiesSpawned {RowInfo(enemiesSpawnedRow)} enemiesKilled {RowInfo(enemiesKilledRow)}");
        // Debug.Log($"[StatsPanel] damage {RowInfo(damageRow)} swing {RowInfo(swingSpeedRow)} critChance {RowInfo(critChanceRow)} critDamage {RowInfo(critDamageRow)} lifeSteal {RowInfo(lifeStealRow)}");
        // Debug.Log($"[StatsPanel] health {RowInfo(healthRow)} stamina {RowInfo(staminaRow)} hpRegen {RowInfo(healthRegenRow)} staRegen {RowInfo(staminaRegenRow)} dmgRed {RowInfo(damageReductionRow)} dodge {RowInfo(dodgeChanceRow)}");
    }

    // ================= EXPAND / COLLAPSE =================

    private void Expand()
    {
        if (isExpanded) return;
        isExpanded = true;

        SetSectionsExpanded(true);
        SetExpandedVisual(true);

        EnsureRowRefs();
        RefreshAllExpanded();

        // KLUCZ: wysokość liczymy po tym jak TMP/Layout policzy preferred sizes
        ScheduleRecalcExpandedHeight();
    }

    private void Collapse()
    {
        if (!isExpanded) return;
        isExpanded = false;

        SetSectionsExpanded(false);
        targetHeight = collapsedHeight;

        SetExpandedVisual(false);
        RefreshWorldStats();
    }

    private void SetSectionsExpanded(bool expanded)
    {
        globalSection?.SetActive(true);
        combatSection?.SetActive(expanded);
        survivalSection?.SetActive(expanded);
    }

    private void SetExpandedVisual(bool expanded)
    {
        if (titleText != null) titleText.text = "STATS";
        if (chevronText != null) chevronText.text = expanded ? "▼" : "▶";
    }

    // ================= ANIMATION =================

    private void AnimateHeightIfNeeded()
    {
        if (panelRoot == null) return;

        float current = panelRoot.rect.height;
        float diff = Mathf.Abs(current - targetHeight);

        if (diff <= heightSnapEpsilon)
        {
            if (diff > 0.01f)
                panelRoot.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, targetHeight);
            return;
        }

        float next = Mathf.Lerp(current, targetHeight, Time.unscaledDeltaTime * animationSpeed);
        panelRoot.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, next);
    }

    // ================= SIZING (FIX) =================

    private void ScheduleRecalcExpandedHeight()
    {
        if (!isExpanded) return;

        if (sizeRoutine != null)
            StopCoroutine(sizeRoutine);

        sizeRoutine = StartCoroutine(RecalcExpandedHeightDeferred());
    }

    private IEnumerator RecalcExpandedHeightDeferred()
    {
        // 1) poczekaj 1 klatkę, żeby TMP przeliczył mesh / preferred size
        yield return null;
        // 2) i jeszcze do końca klatki (pewność dla LayoutGroup)
        yield return new WaitForEndOfFrame();

        if (!isExpanded || headerRect == null || contentRect == null)
            yield break;

        // Rebuild layout tylko contentu (lekko) — bez Canvas.ForceUpdateCanvases
        LayoutRebuilder.ForceRebuildLayoutImmediate(contentRect);

        // Najważniejsze: używamy PreferredHeight, nie rect.height
        float preferred = LayoutUtility.GetPreferredHeight(contentRect);

        targetHeight = headerRect.rect.height + preferred + bottomPadding;
    }

    // ================= EVENTS =================

    private void OnWorldChanged()
    {
        RefreshWorldStats();
    }

    private void OnPlayerStatsChanged()
    {
        if (Time.unscaledTime - lastStatsRefreshTime < statsRefreshInterval)
            return;

        lastStatsRefreshTime = Time.unscaledTime;

        // 🔥 ZAWSZE aktualizuj dane
        RefreshCombatAndSurvivalStats();

        // 📐 layout tylko gdy expanded
        if (isExpanded)
            ScheduleRecalcExpandedHeight();
    }

    // ================= REFRESH =================

    private void RefreshAllExpanded()
    {
        RefreshWorldStats();
        RefreshCombatAndSurvivalStats();
    }

    private void RefreshWorldStats()
    {
        EnsureWorld();
        EnsureRowRefs();
        if (world == null) return;
        int currentEnemies = ResolveCurrentEnemyCount();

        difficultyRow?.SetInt(world.difficulty);
        difficultyRow?.SetBoosted(false);
        enemiesSpawnedRow?.SetInt(currentEnemies);
        enemiesSpawnedRow?.SetBoosted(false);
        enemiesKilledRow?.SetInt(world.enemiesKilled);
        enemiesKilledRow?.SetBoosted(false);
    }

    private int ResolveCurrentEnemyCount()
    {
        if (enemyActivationController == null)
            enemyActivationController = EnemyActivationController.Instance != null
                ? EnemyActivationController.Instance
                : FindFirstObjectByType<EnemyActivationController>();

        if (enemyActivationController != null)
            return Mathf.Max(0, enemyActivationController.ActiveCount);

        return Mathf.Max(0, world != null ? world.enemiesSpawned : 0);
    }

    private void RefreshCombatAndSurvivalStats()
    {
        EnsureRowRefs();
        if (player == null || player.stats == null)
            return;

        var s = player.stats;
        var e = enhancers;
        var relics = player.GetComponent<PlayerRelicController>();

        // ===========================
        // COMBAT – BASE
        // ===========================
        float baseDamage = s.damage;
        float baseCritChance = s.critChance;
        float baseCritMult = s.critMultiplier;
        float baseLifeSteal = s.lifeSteal;
        float baseSpeed = s.speed;

        // ===========================
        // COMBAT – EFFECTIVE
        // ===========================
        float dmg = baseDamage;
        float critChance = baseCritChance;
        float critMult = baseCritMult;
        float lifeSteal = baseLifeSteal;
        float speed = baseSpeed;

        if (e != null)
        {
            dmg = e.GetEffectiveValue(StatType.Damage, dmg);
            critChance = e.GetEffectiveValue(StatType.CritChance, critChance);
            critMult = e.GetEffectiveValue(StatType.CritMultiplier, critMult);
            lifeSteal = e.GetEffectiveValue(StatType.LifeSteal, lifeSteal);
        }

        if (relics != null)
        {
            dmg *= relics.GetDamageMultiplier();
            critChance += relics.GetCritChanceBonus();
            critMult += relics.GetCritMultiplierBonus();
            lifeSteal += relics.GetLifeStealBonus();
            speed += relics.GetSpeedBonus();
        }

        critChance = CombatBalanceCaps.ClampCritChance(critChance);
        lifeSteal = CombatBalanceCaps.ApplyLifeStealDiminishing(lifeSteal);
        critMult = CombatBalanceCaps.ClampCritMultiplier(critMult);

        damageRow?.SetFloat(dmg);
        critChanceRow?.SetPercent(critChance);
        critDamageRow?.SetPercent(Mathf.Max(0f, critMult - 1f));
        lifeStealRow?.SetPercent(lifeSteal);
        speedRow?.SetFloat(speed);

        // ===========================
        // SWING SPEED (FROM WEAPON)
        // ===========================
        float swingMul = 1f;

        if (weapon == null)
            weapon = player.GetComponentInChildren<WeaponController>(true);

        if (weapon != null)
            swingMul = weapon.GetSwingSpeedMultiplier();

        swingSpeedRow?.SetMultiplier(swingMul);

        // ===========================
        // SURVIVAL – BASE
        // ===========================
        float baseMaxHp = s.maxHealth;
        float baseMaxStam = s.maxStamina;
        float baseHpRegen = s.healthRegen;
        float baseStaRegen = s.staminaRegen;
        float baseDmgRed = s.damageReduction;
        float baseDodge = s.dodgeChance;

        // ===========================
        // SURVIVAL – EFFECTIVE
        // ===========================
        float maxHp = baseMaxHp;
        float maxStam = baseMaxStam;
        float hpRegen = baseHpRegen;
        float staRegen = baseStaRegen;
        float dmgRed = baseDmgRed;
        float dodge = baseDodge;

        if (e != null)
        {
            maxHp = e.GetEffectiveValue(StatType.MaxHealth, maxHp);
            maxStam = e.GetEffectiveValue(StatType.MaxStamina, maxStam);
            hpRegen = e.GetEffectiveValue(StatType.HealthRegen, hpRegen);
            staRegen = e.GetEffectiveValue(StatType.StaminaRegen, staRegen);
            dmgRed = e.GetEffectiveValue(StatType.DamageReduction, dmgRed);
            dodge = e.GetEffectiveValue(StatType.DodgeChance, dodge);
        }

        if (relics != null)
        {
            dmgRed += relics.GetDamageReductionBonus();
            dodge += relics.GetDodgeChanceBonus();
            maxHp += relics.GetMaxHealthBonus();
            staRegen += relics.GetStaminaRegenBonus();
        }

        dmgRed = CombatBalanceCaps.ClampDamageReduction(dmgRed);
        dodge = CombatBalanceCaps.ClampDodgeChance(dodge);

        healthRow?.SetFloat(maxHp);
        staminaRow?.SetFloat(maxStam);
        healthRegenRow?.SetPerSecond(hpRegen);
        staminaRegenRow?.SetPerSecond(staRegen);
        damageReductionRow?.SetPercent(dmgRed);
        dodgeChanceRow?.SetPercent(dodge);

        // ===========================
        // BOOST HIGHLIGHT (ONLY TRUTH)
        // ===========================
        bool Boosted(float baseVal, float effVal)
            => !Mathf.Approximately(baseVal, effVal);

        damageRow?.SetBoosted(Boosted(baseDamage, dmg));
        critChanceRow?.SetBoosted(Boosted(baseCritChance, critChance));
        critDamageRow?.SetBoosted(Boosted(baseCritMult, critMult));
        lifeStealRow?.SetBoosted(Boosted(baseLifeSteal, lifeSteal));
        speedRow?.SetBoosted(Boosted(baseSpeed, speed));

        swingSpeedRow?.SetBoosted(
            weapon != null && !Mathf.Approximately(1f, swingMul)
        );

        healthRow?.SetBoosted(Boosted(baseMaxHp, maxHp));
        staminaRow?.SetBoosted(Boosted(baseMaxStam, maxStam));
        healthRegenRow?.SetBoosted(Boosted(baseHpRegen, hpRegen));
        staminaRegenRow?.SetBoosted(Boosted(baseStaRegen, staRegen));
        damageReductionRow?.SetBoosted(Boosted(baseDmgRed, dmgRed));
        dodgeChanceRow?.SetBoosted(Boosted(baseDodge, dodge));
    }

    private void RecalculateExpandedHeightOnce()
    {
        if (panelRoot == null)
            return;

        // Zakładamy, że masz VerticalLayoutGroup / ContentSizeFitter
        LayoutRebuilder.ForceRebuildLayoutImmediate(panelRoot);
    }
}
