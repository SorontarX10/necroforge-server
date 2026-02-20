
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using GrassSim.Combat;
using GrassSim.Core;
using GrassSim.Enemies;

[DisallowMultipleComponent]
[RequireComponent(typeof(Combatant), typeof(EnemyCombatant))]
public partial class BossEnemyController : MonoBehaviour
{
    public enum BossArchetype { Zombie, Quick, Tank, Dog }
    private enum AttackCycleState { Charging, Attacking }

    [Header("Boss Identity")]
    [SerializeField] private string bossName = "Boss";

    [Header("Teleport")]
    [SerializeField] private float teleportTriggerDistance = 28f;
    [SerializeField] private float teleportCooldown = 4f;
    [SerializeField] private float teleportMinDistanceFromPlayer = 4f;
    [SerializeField] private float teleportMaxDistanceFromPlayer = 8f;
    [SerializeField, Min(0.02f)] private float teleportCheckInterval = 0.1f;
    [SerializeField, Min(0.05f)] private float playerResolveInterval = 0.25f;
    [SerializeField] private float postTeleportGraceDuration = 0.55f;

    [Header("Attack Cycle")]
    [SerializeField] private float chargeDuration = 2.4f;
    [SerializeField] private float attackDuration = 4.2f;
    [SerializeField] private float chargeTurnSpeed = 9f;

    [Header("Phases (HP %)")]
    [SerializeField, Range(0.05f, 0.95f)] private float phase2Threshold = 0.75f;
    [SerializeField, Range(0.05f, 0.9f)] private float phase3Threshold = 0.5f;
    [SerializeField, Range(0.05f, 0.85f)] private float phase4Threshold = 0.25f;
    [SerializeField, Min(0.05f)] private float phaseCheckInterval = 0.15f;
    [SerializeField] private float phase2ChargeMultiplier = 0.92f;
    [SerializeField] private float phase3ChargeMultiplier = 0.82f;
    [SerializeField] private float phase4ChargeMultiplier = 0.7f;
    [SerializeField] private float phase2AttackMultiplier = 1.1f;
    [SerializeField] private float phase3AttackMultiplier = 1.25f;
    [SerializeField] private float phase4AttackMultiplier = 1.4f;
    [SerializeField] private float phase2SkillCooldownMultiplier = 0.92f;
    [SerializeField] private float phase3SkillCooldownMultiplier = 0.8f;
    [SerializeField] private float phase4SkillCooldownMultiplier = 0.68f;
    [SerializeField] private float phase2MoveSpeedMultiplier = 1.06f;
    [SerializeField] private float phase3MoveSpeedMultiplier = 1.16f;
    [SerializeField] private float phase4MoveSpeedMultiplier = 1.28f;

    [Header("Enrage")]
    [SerializeField, Range(0.05f, 0.75f)] private float enrageThreshold = 0.3f;
    [SerializeField] private float enrageDamageMultiplier = 1.3f;
    [SerializeField] private float enrageMoveSpeedMultiplier = 1.2f;
    [SerializeField] private float enrageSkillCooldownMultiplier = 0.78f;
    [SerializeField] private float enrageChargeMultiplier = 0.85f;
    [SerializeField] private float enrageAttackMultiplier = 1.18f;
    [SerializeField] private float enrageEyeIntensityMultiplier = 1.55f;

    [Header("Archetype Stat Profiles")]
    [SerializeField] private float zombieHealthScale = 1f;
    [SerializeField] private float zombieDamageScale = 1f;
    [SerializeField] private float zombieMoveSpeedScale = 1f;
    [SerializeField] private float zombieAttackCooldownScale = 1f;
    [SerializeField] private float quickHealthScale = 0.95f;
    [SerializeField] private float quickDamageScale = 1f;
    [SerializeField] private float quickMoveSpeedScale = 1.38f;
    [SerializeField] private float quickAttackCooldownScale = 0.75f;
    [SerializeField] private float tankHealthScale = 2.1f;
    [SerializeField] private float tankDamageScale = 1.2f;
    [SerializeField] private float tankMoveSpeedScale = 0.75f;
    [SerializeField] private float tankAttackCooldownScale = 1.25f;
    [SerializeField] private float dogHealthScale = 0.95f;
    [SerializeField] private float dogDamageScale = 1.05f;
    [SerializeField] private float dogMoveSpeedScale = 1.62f;
    [SerializeField] private float dogAttackCooldownScale = 0.85f;

    [Header("Zombie Skill - Poison")]
    [SerializeField, Range(0f, 1f)] private float zombiePoisonChance = 0.55f;
    [SerializeField] private float zombiePoisonDuration = 6f;
    [SerializeField] private float zombiePoisonDps = 8f;

    [Header("Quick Skill - Dash Attack")]
    [SerializeField] private float quickDashInitialDelay = 1.5f;
    [SerializeField] private float quickDashCooldown = 4.2f;
    [SerializeField] private float quickDashDuration = 0.32f;
    [SerializeField] private float quickDashSpeed = 18f;
    [SerializeField] private float quickDashMinDistance = 4f;
    [SerializeField] private float quickDashMaxDistance = 18f;
    [SerializeField] private float quickDashAttackRange = 2.1f;
    [SerializeField] private float quickDashAttackDamageMultiplier = 1.45f;

    [Header("Tank Skill - Temporary Immunity")]
    [SerializeField] private float tankShieldInitialDelay = 1f;
    [SerializeField] private float tankShieldCooldown = 8f;
    [SerializeField] private float tankShieldDuration = 2.5f;

    [Header("Dog Skill - Root Bite")]
    [SerializeField, Range(0f, 1f)] private float dogRootChance = 0.42f;
    [SerializeField] private float dogRootDuration = 1.25f;
    [SerializeField] private float dogRootPerTargetCooldown = 4.5f;

    [Header("Telegraphs")]
    [SerializeField] private float quickDashTelegraphDuration = 0.42f;
    [SerializeField] private float quickDashTelegraphRadius = 2.2f;
    [SerializeField] private Color quickDashTelegraphColor = new(1f, 0.22f, 0.22f, 0.9f);
    [SerializeField] private float poisonTelegraphDuration = 0.35f;
    [SerializeField] private float poisonTelegraphRadius = 2.4f;
    [SerializeField] private Color poisonTelegraphColor = new(0.3f, 1f, 0.35f, 0.88f);
    [SerializeField] private float rootTelegraphDuration = 0.45f;
    [SerializeField] private float rootTelegraphRadius = 2.4f;
    [SerializeField] private Color rootTelegraphColor = new(1f, 0.75f, 0.2f, 0.9f);
    [SerializeField] private float telegraphYOffset = 0.05f;

    [Header("Presentation (Optional Assets)")]
    [SerializeField] private GameObject spawnVfxPrefab;
    [SerializeField] private GameObject teleportVfxPrefab;
    [SerializeField] private GameObject enrageVfxPrefab;
    [SerializeField] private GameObject telegraphMarkerPrefab;
    [SerializeField] private GameObject shockwavePrefab;
    [SerializeField] private GameObject auraPrefabOverride;
    [SerializeField] private Material telegraphMaterialOverride;
    [SerializeField] private Material auraMaterialOverride;
    [SerializeField] private BossEmissiveLutProfile emissiveLutProfile;
    [SerializeField] private AudioClip spawnSfx;
    [SerializeField] private AudioClip teleportSfx;
    [SerializeField] private AudioClip enrageSfx;
    [SerializeField] private AudioClip quickDashWarningSfx;
    [SerializeField] private AudioClip poisonWarningSfx;
    [SerializeField] private AudioClip rootWarningSfx;
    [SerializeField] private AudioClip dogAuraGrowlSfx;
    [SerializeField, Range(0f, 1f)] private float warningSfxVolume = 0.9f;
    [SerializeField, Range(0f, 1f)] private float presentationSfxVolume = 1f;

    [Header("Presentation Fallback")]
    [SerializeField] private float spawnShockwaveRadius = 5.5f;
    [SerializeField] private float spawnShockwaveDuration = 0.6f;
    [SerializeField] private Color spawnShockwaveColor = new(1f, 0.25f, 0.25f, 0.9f);
    [SerializeField] private float teleportShockwaveRadius = 3.2f;
    [SerializeField] private float teleportShockwaveDuration = 0.35f;
    [SerializeField] private Color teleportShockwaveColor = new(1f, 0.35f, 0.25f, 0.88f);
    [SerializeField] private float enrageShockwaveRadius = 6.8f;
    [SerializeField] private float enrageShockwaveDuration = 0.55f;
    [SerializeField] private Color enrageShockwaveColor = new(1f, 0f, 0f, 0.95f);
    [SerializeField] private float auraYOffset = 0.05f;
    [SerializeField] private float dogAuraGrowlIntervalMin = 4f;
    [SerializeField] private float dogAuraGrowlIntervalMax = 7f;

    private EnemyCombatant enemyCombatant;
    private Combatant combatant;
    private Rigidbody rb;
    private Transform player;
    private Combatant playerCombatant;
    private PlayerRelicController playerRelics;
    private BossArchetype archetype;
    private ZombieAI zombieAI;
    private EnemyAI enemyAI;
    private BossEncounterController owner;
    private RelicLibrary relicLibrary;
    private RelicSelectionUI relicSelectionUI;
    private int rewardChoices = 3;
    private LayerMask groundMask;
    private float groundRayHeight = 45f;
    private float groundSnapOffset = 0.6f;
    private float nextTeleportAt;
    private float nextTeleportCheckAt;
    private float nextPlayerResolveAt;
    private float nextQuickDashAt;
    private float nextTankShieldAt;
    private float nextPhaseCheckAt;
    private float damageSuppressedUntil;
    private bool quickDashActive;
    private Coroutine quickDashRoutine;
    private AttackCycleState attackCycleState;
    private float attackCycleStateEndsAt;
    private BossSpecialEffects specialEffects;
    private BossTankDamageGate tankDamageGate;
    private BossHealthBarUI bossHealthBarUI;
    private EnemyDamageDealer[] damageDealers;
    private ZombieEyeEmissionController eyeEmissionController;
    private EnemyStatsData runtimeBossStats;
    private float baseBossDamage;
    private float baseBossAttackCooldown;
    private float baseChargeDuration;
    private float baseAttackDuration;
    private float baseEyeMaxIntensity;
    private float baseEyeMinIntensity;
    private float attackZombieMoveSpeed;
    private float attackEnemyWanderSpeed;
    private float attackEnemyChaseSpeed;
    private bool attackSpeedsCached;
    private int currentPhaseTier = 1;
    private bool enrageApplied;
    private GameObject auraVisual;
    private Renderer[] auraRenderers;
    private Vector3 auraBaseScale = Vector3.one;
    private Transform auraTransform;
    private float auraSeed;
    private float nextDogAuraGrowlAt;
    private bool initialized;
    private bool deathHandled;
    private bool combatantHealthSubscribed;
    private MaterialPropertyBlock tintPropertyBlock;

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    public bool IsAlive => combatant != null && !combatant.IsDead;
    public bool CanDealDamage => (!initialized || attackCycleState == AttackCycleState.Attacking) && Time.time >= damageSuppressedUntil;
    public bool IsCharging => initialized && attackCycleState == AttackCycleState.Charging;
    public bool IsEnraged => enrageApplied;
    public string ArchetypeLabel => archetype.ToString();

    public void Initialize(
        BossEncounterController owner,
        RelicLibrary relicLibrary,
        RelicSelectionUI relicSelectionUI,
        int rewardChoices,
        float healthMultiplier,
        float damageMultiplier,
        int expMultiplier,
        LayerMask groundMask,
        float groundRayHeight,
        float groundSnapOffset
    )
    {
        if (initialized)
            return;

        this.owner = owner;
        this.relicLibrary = relicLibrary;
        this.relicSelectionUI = relicSelectionUI;
        this.rewardChoices = Mathf.Max(1, rewardChoices);
        this.groundMask = groundMask;
        this.groundRayHeight = Mathf.Max(5f, groundRayHeight);
        this.groundSnapOffset = Mathf.Max(0f, groundSnapOffset);

        baseChargeDuration = Mathf.Max(0.2f, chargeDuration);
        baseAttackDuration = Mathf.Max(0.2f, attackDuration);

        CacheComponents();
        CachePlayer(force: true);
        InitializeDisplayNameAndArchetype();

        ApplyBossStats(healthMultiplier, damageMultiplier, expMultiplier);
        ConfigureBossEyes();
        ConfigureBossEffects();
        ConfigureBossHealthBar();
        ConfigureArchetypeSkillRuntime();
        SetupAuraVisual();
        PlaySpawnPresentation();

        BeginChargingPhase();

        if (enemyCombatant != null)
            enemyCombatant.OnDied += HandleDeath;

        SubscribeCombatantHealth();
        EvaluatePhaseAndEnrage(force: true);

        initialized = true;
    }

    private void OnValidate()
    {
        teleportCheckInterval = Mathf.Max(0.02f, teleportCheckInterval);
        postTeleportGraceDuration = Mathf.Max(0f, postTeleportGraceDuration);
        teleportMinDistanceFromPlayer = Mathf.Max(1f, teleportMinDistanceFromPlayer);
        teleportMaxDistanceFromPlayer = Mathf.Max(teleportMinDistanceFromPlayer + 0.1f, teleportMaxDistanceFromPlayer);
        playerResolveInterval = Mathf.Max(0.05f, playerResolveInterval);

        phase2Threshold = Mathf.Clamp(phase2Threshold, 0.1f, 0.95f);
        phase3Threshold = Mathf.Clamp(phase3Threshold, 0.08f, phase2Threshold - 0.05f);
        phase4Threshold = Mathf.Clamp(phase4Threshold, 0.05f, phase3Threshold - 0.05f);
        phaseCheckInterval = Mathf.Max(0.05f, phaseCheckInterval);

        enrageThreshold = Mathf.Clamp(enrageThreshold, 0.05f, 0.75f);
        enrageDamageMultiplier = Mathf.Max(1f, enrageDamageMultiplier);
        enrageMoveSpeedMultiplier = Mathf.Max(1f, enrageMoveSpeedMultiplier);
        enrageSkillCooldownMultiplier = Mathf.Clamp(enrageSkillCooldownMultiplier, 0.25f, 1f);

        quickDashTelegraphDuration = Mathf.Max(0f, quickDashTelegraphDuration);
        poisonTelegraphDuration = Mathf.Max(0f, poisonTelegraphDuration);
        rootTelegraphDuration = Mathf.Max(0f, rootTelegraphDuration);
        quickDashTelegraphRadius = Mathf.Max(0.2f, quickDashTelegraphRadius);
        poisonTelegraphRadius = Mathf.Max(0.2f, poisonTelegraphRadius);
        rootTelegraphRadius = Mathf.Max(0.2f, rootTelegraphRadius);

        dogAuraGrowlIntervalMin = Mathf.Max(0.25f, dogAuraGrowlIntervalMin);
        dogAuraGrowlIntervalMax = Mathf.Max(dogAuraGrowlIntervalMin, dogAuraGrowlIntervalMax);
    }

    private void OnDestroy()
    {
        if (enemyCombatant != null)
            enemyCombatant.OnDied -= HandleDeath;

        UnsubscribeCombatantHealth();
        CancelQuickDash();
        CleanupAuraVisual();
        CleanupPresentationPools();

        if (!deathHandled && owner != null)
            owner.NotifyBossDeath(this);
    }

    private void Update()
    {
        if (!initialized || combatant == null || combatant.IsDead)
            return;

        if (player == null || playerCombatant == null || !player.gameObject.activeInHierarchy)
            CachePlayer(force: false);

        if (player == null)
            return;

        HandleTeleport();
        EvaluatePhaseAndEnrage(force: false);
        UpdateAuraVisual();
        UpdateReadabilityTelemetry();
        HandleAttackCycle();

        if (attackCycleState == AttackCycleState.Charging)
        {
            HandleChargingRotation();
            return;
        }

        HandleArchetypeSkill();
    }

    private void CacheComponents()
    {
        combatant = GetComponent<Combatant>();
        enemyCombatant = GetComponent<EnemyCombatant>();
        rb = GetComponent<Rigidbody>();
        specialEffects = GetComponent<BossSpecialEffects>();
        zombieAI = GetComponent<ZombieAI>();
        enemyAI = GetComponent<EnemyAI>();
        damageDealers = GetComponentsInChildren<EnemyDamageDealer>(true);
        tintPropertyBlock ??= new MaterialPropertyBlock();
    }

    private void CachePlayer(bool force)
    {
        if (!force && Time.time < nextPlayerResolveAt)
            return;

        nextPlayerResolveAt = Time.time + Mathf.Max(0.05f, playerResolveInterval);

        Transform resolved = PlayerLocator.GetTransform();
        if (resolved == player && playerCombatant != null)
            return;

        player = resolved;
        playerCombatant = player != null ? player.GetComponent<Combatant>() : null;
        playerRelics = playerCombatant != null ? playerCombatant.GetComponent<PlayerRelicController>() : null;
    }

    private void SubscribeCombatantHealth()
    {
        if (combatantHealthSubscribed || combatant == null)
            return;

        combatant.OnHealthChanged += HandleCombatantHealthChanged;
        combatantHealthSubscribed = true;
    }

    private void UnsubscribeCombatantHealth()
    {
        if (!combatantHealthSubscribed || combatant == null)
            return;

        combatant.OnHealthChanged -= HandleCombatantHealthChanged;
        combatantHealthSubscribed = false;
    }

    private void HandleCombatantHealthChanged()
    {
        EvaluatePhaseAndEnrage(force: true);
    }

    private void InitializeDisplayNameAndArchetype()
    {
        string sourceName = gameObject.name.Replace("(Clone)", "").Trim();
        if (string.IsNullOrWhiteSpace(sourceName))
            sourceName = "Zombie";

        archetype = ResolveArchetype(sourceName);
        bossName = sourceName + " Boss";
    }

    private BossArchetype ResolveArchetype(string sourceName)
    {
        string n = sourceName.ToLowerInvariant();
        if (n.Contains("dog")) return BossArchetype.Dog;
        if (n.Contains("tank")) return BossArchetype.Tank;
        if (n.Contains("quick")) return BossArchetype.Quick;
        return BossArchetype.Zombie;
    }

    private void ApplyBossStats(float healthMultiplier, float damageMultiplier, int expMultiplier)
    {
        if (enemyCombatant == null || enemyCombatant.stats == null || combatant == null)
            return;

        EnemyStatsData baseStats = enemyCombatant.stats;
        runtimeBossStats = Instantiate(baseStats);
        runtimeBossStats.name = baseStats.name + "_BossRuntime";

        runtimeBossStats.maxHealth = Mathf.Max(1f, baseStats.maxHealth * Mathf.Max(1f, healthMultiplier) * GetArchetypeHealthScale());
        runtimeBossStats.damage = Mathf.Max(1f, baseStats.damage * Mathf.Max(1f, damageMultiplier) * GetArchetypeDamageScale());
        runtimeBossStats.attackCooldown = Mathf.Max(0.15f, baseStats.attackCooldown * GetArchetypeAttackCooldownScale());
        runtimeBossStats.expReward = Mathf.Max(0, Mathf.RoundToInt(baseStats.expReward * Mathf.Max(1, expMultiplier)));

        enemyCombatant.stats = runtimeBossStats;
        combatant.Initialize(runtimeBossStats.maxHealth);

        baseBossDamage = runtimeBossStats.damage;
        baseBossAttackCooldown = runtimeBossStats.attackCooldown;

        float moveScale = GetArchetypeMoveSpeedScale();
        if (zombieAI != null)
        {
            zombieAI.moveSpeed *= moveScale;
            zombieAI.rotationSpeed *= Mathf.Clamp(moveScale, 0.75f, 1.45f);
            zombieAI.detectionRadius = Mathf.Max(zombieAI.detectionRadius, teleportTriggerDistance);
        }

        if (enemyAI != null)
        {
            enemyAI.wanderSpeed *= moveScale;
            enemyAI.chaseSpeed *= moveScale;
            enemyAI.attackCooldown = runtimeBossStats.attackCooldown;
            enemyAI.detectionRadius = Mathf.Max(enemyAI.detectionRadius, teleportTriggerDistance);
        }

        ApplyRuntimeCombatTuning();
    }

    private float GetArchetypeHealthScale() => archetype switch
    {
        BossArchetype.Quick => quickHealthScale,
        BossArchetype.Tank => tankHealthScale,
        BossArchetype.Dog => dogHealthScale,
        _ => zombieHealthScale
    };

    private float GetArchetypeDamageScale() => archetype switch
    {
        BossArchetype.Quick => quickDamageScale,
        BossArchetype.Tank => tankDamageScale,
        BossArchetype.Dog => dogDamageScale,
        _ => zombieDamageScale
    };

    private float GetArchetypeMoveSpeedScale() => archetype switch
    {
        BossArchetype.Quick => quickMoveSpeedScale,
        BossArchetype.Tank => tankMoveSpeedScale,
        BossArchetype.Dog => dogMoveSpeedScale,
        _ => zombieMoveSpeedScale
    };

    private float GetArchetypeAttackCooldownScale() => archetype switch
    {
        BossArchetype.Quick => quickAttackCooldownScale,
        BossArchetype.Tank => tankAttackCooldownScale,
        BossArchetype.Dog => dogAttackCooldownScale,
        _ => zombieAttackCooldownScale
    };

    private void ConfigureBossEyes()
    {
        eyeEmissionController = GetComponentInChildren<ZombieEyeEmissionController>(true);
        if (eyeEmissionController == null)
            return;

        eyeEmissionController.fullHealthEmissionColor = new Color(1f, 0.12f, 0.12f, 1f);
        eyeEmissionController.zeroHealthEmissionColor = new Color(0.2f, 0f, 0f, 1f);
        eyeEmissionController.maxEmissionIntensity = Mathf.Max(eyeEmissionController.maxEmissionIntensity, 8f);
        eyeEmissionController.minEmissionIntensity = Mathf.Max(eyeEmissionController.minEmissionIntensity, 1.2f);

        baseEyeMaxIntensity = eyeEmissionController.maxEmissionIntensity;
        baseEyeMinIntensity = eyeEmissionController.minEmissionIntensity;
    }

    private void ConfigureBossEffects()
    {
        if (specialEffects == null)
            specialEffects = gameObject.AddComponent<BossSpecialEffects>();

        RefreshSpecialEffectsForCurrentState();
    }

    private void ConfigureBossHealthBar()
    {
        bossHealthBarUI = GetComponent<BossHealthBarUI>();
        if (bossHealthBarUI == null)
            bossHealthBarUI = gameObject.AddComponent<BossHealthBarUI>();

        bossHealthBarUI.Initialize(combatant, transform);
        bossHealthBarUI.SetChargingState(false);
    }

    private void ConfigureArchetypeSkillRuntime()
    {
        if (archetype == BossArchetype.Quick)
            nextQuickDashAt = Time.time + Mathf.Max(0f, quickDashInitialDelay);
        else if (archetype == BossArchetype.Tank)
        {
            tankDamageGate = GetComponent<BossTankDamageGate>();
            if (tankDamageGate == null)
                tankDamageGate = gameObject.AddComponent<BossTankDamageGate>();

            combatant?.RefreshIncomingDamageGatesCache();

            nextTankShieldAt = Time.time + Mathf.Max(0f, tankShieldInitialDelay);
        }
    }


    private void EvaluatePhaseAndEnrage(bool force)
    {
        if (combatant == null || combatant.MaxHealth <= 0f)
            return;

        if (!force && Time.time < nextPhaseCheckAt)
            return;

        nextPhaseCheckAt = Time.time + phaseCheckInterval;

        float hp01 = Mathf.Clamp01(combatant.CurrentHealth / combatant.MaxHealth);
        int desiredPhase = hp01 <= phase4Threshold ? 4 : hp01 <= phase3Threshold ? 3 : hp01 <= phase2Threshold ? 2 : 1;

        bool changed = false;

        if (desiredPhase != currentPhaseTier)
        {
            currentPhaseTier = desiredPhase;
            changed = true;
        }

        if (!enrageApplied && hp01 <= enrageThreshold)
        {
            enrageApplied = true;
            changed = true;
            PlayEnragePresentation();
        }

        if (changed)
            ApplyTuningForCurrentState();
    }

    private void ApplyTuningForCurrentState()
    {
        ApplyRuntimeCombatTuning();
        RefreshSpecialEffectsForCurrentState();
        RefreshEyeIntensityForState();
        if (archetype == BossArchetype.Quick)
            nextQuickDashAt = Mathf.Min(nextQuickDashAt, Time.time + 0.3f);
        if (archetype == BossArchetype.Tank)
            nextTankShieldAt = Mathf.Min(nextTankShieldAt, Time.time + 0.3f);
    }

    private void ApplyRuntimeCombatTuning()
    {
        if (runtimeBossStats != null)
        {
            runtimeBossStats.damage = Mathf.Max(1f, baseBossDamage * (enrageApplied ? enrageDamageMultiplier : 1f));
            runtimeBossStats.attackCooldown = Mathf.Max(0.12f, baseBossAttackCooldown * GetCurrentSkillCooldownMultiplier());

            if (enemyAI != null)
                enemyAI.attackCooldown = runtimeBossStats.attackCooldown;

            if (damageDealers != null)
            {
                for (int i = 0; i < damageDealers.Length; i++)
                {
                    if (damageDealers[i] != null)
                        damageDealers[i].hitCooldown = runtimeBossStats.attackCooldown;
                }
            }
        }

        if (attackSpeedsCached)
            SetMovementPaused(attackCycleState == AttackCycleState.Charging);
    }

    private void RefreshSpecialEffectsForCurrentState()
    {
        if (specialEffects == null)
            return;

        specialEffects.ClearAllEffects();

        float phaseBoost = 1f + 0.12f * Mathf.Max(0, currentPhaseTier - 1);
        if (enrageApplied)
            phaseBoost += 0.18f;

        if (archetype == BossArchetype.Zombie)
        {
            specialEffects.ConfigurePoison(
                chance: Mathf.Clamp01(zombiePoisonChance * (1f + 0.08f * Mathf.Max(0, currentPhaseTier - 1) + (enrageApplied ? 0.1f : 0f))),
                duration: zombiePoisonDuration * (1f + 0.06f * Mathf.Max(0, currentPhaseTier - 1) + (enrageApplied ? 0.08f : 0f)),
                dps: zombiePoisonDps * phaseBoost,
                tickInterval: 1f
            );
        }
        else if (archetype == BossArchetype.Dog)
        {
            specialEffects.ConfigureRoot(
                chance: Mathf.Clamp01(dogRootChance * (1f + 0.1f * Mathf.Max(0, currentPhaseTier - 1) + (enrageApplied ? 0.12f : 0f))),
                duration: dogRootDuration * (1f + 0.07f * Mathf.Max(0, currentPhaseTier - 1) + (enrageApplied ? 0.1f : 0f)),
                perTargetCooldown: Mathf.Max(0.25f, dogRootPerTargetCooldown * (1f / Mathf.Max(0.2f, phaseBoost)))
            );
        }
    }

    private void RefreshEyeIntensityForState()
    {
        if (eyeEmissionController == null)
            return;

        if (!enrageApplied)
        {
            eyeEmissionController.maxEmissionIntensity = baseEyeMaxIntensity;
            eyeEmissionController.minEmissionIntensity = baseEyeMinIntensity;
            return;
        }

        eyeEmissionController.maxEmissionIntensity = baseEyeMaxIntensity * enrageEyeIntensityMultiplier;
        eyeEmissionController.minEmissionIntensity = baseEyeMinIntensity * Mathf.Lerp(1f, enrageEyeIntensityMultiplier, 0.35f);
    }

    private float GetCurrentChargeDuration()
    {
        float phaseMul = currentPhaseTier switch { 2 => phase2ChargeMultiplier, 3 => phase3ChargeMultiplier, 4 => phase4ChargeMultiplier, _ => 1f };
        float enrageMul = enrageApplied ? enrageChargeMultiplier : 1f;
        return Mathf.Max(0.2f, baseChargeDuration * phaseMul * enrageMul);
    }

    private float GetCurrentAttackDuration()
    {
        float phaseMul = currentPhaseTier switch { 2 => phase2AttackMultiplier, 3 => phase3AttackMultiplier, 4 => phase4AttackMultiplier, _ => 1f };
        float enrageMul = enrageApplied ? enrageAttackMultiplier : 1f;
        return Mathf.Max(0.2f, baseAttackDuration * phaseMul * enrageMul);
    }

    private float GetCurrentMoveSpeedMultiplier()
    {
        float phaseMul = currentPhaseTier switch { 2 => phase2MoveSpeedMultiplier, 3 => phase3MoveSpeedMultiplier, 4 => phase4MoveSpeedMultiplier, _ => 1f };
        return Mathf.Max(0.01f, phaseMul * (enrageApplied ? enrageMoveSpeedMultiplier : 1f));
    }

    private float GetCurrentSkillCooldownMultiplier()
    {
        float phaseMul = currentPhaseTier switch { 2 => phase2SkillCooldownMultiplier, 3 => phase3SkillCooldownMultiplier, 4 => phase4SkillCooldownMultiplier, _ => 1f };
        return Mathf.Max(0.1f, phaseMul * (enrageApplied ? enrageSkillCooldownMultiplier : 1f));
    }


}
