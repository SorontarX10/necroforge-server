using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using GrassSim.AI;
using GrassSim.Combat;
using GrassSim.Core;
using GrassSim.Enemies;
using GrassSim.Enhancers;
using GrassSim.Stats;
using GrassSim.UI;
using GrassSim.Upgrades;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GrassSim.Telemetry
{
    [DefaultExecutionOrder(9500)]
    public sealed class GameplayTelemetryRecorder : MonoBehaviour
    {
        private const float ResolveIntervalSeconds = 0.5f;
        private const float SnapshotIntervalSeconds = 10f;
        private const float FlushIntervalSeconds = 1f;
        private const int FlushLineThreshold = 24;
        private const float TargetTrackerMaxIdleSeconds = 45f;
        private const int TelemetrySchemaVersion = 3;
        private const float WarningLowHealthThreshold = 0.35f;
        private const float CriticalLowHealthThreshold = 0.2f;
        private const bool MirrorToProjectResultsInEditor = true;

        private static GameplayTelemetryRecorder instance;
        private static bool shuttingDown;

        [Serializable]
        private sealed class TelemetryRecord
        {
            public int schema_version;
            public string event_type;
            public string run_id;
            public string utc_timestamp;
            public string scene_name;
            public string app_version;
            public string unity_version;
            public string platform;
            public int frame;
            public float run_time_s;
            public float realtime_since_startup_s;
            public string reason;
            public bool has_player_snapshot;
            public bool has_world_snapshot;
            public bool has_difficulty_snapshot;
            public bool has_enemy_pressure_snapshot;
            public bool has_combat_context;
            public string integrity_state;
            public PlayerSnapshot player;
            public WorldSnapshot world;
            public DifficultySnapshot difficulty;
            public CombatContext combat_context;
            public RunCounters counters;
            public UpgradeOptionData[] upgrade_options;
            public UpgradeOptionData upgrade_selected;
            public UpgradeContributionData[] upgrade_contributions;
            public RelicAppliedData relic_applied;
            public EnhancerAppliedData enhancer_applied;
            public MeleeHitData melee_hit;
            public MeleeKillData melee_kill;
            public IncomingDamageData incoming_damage;
            public RelicProcData relic_proc;
            public LifeStealData lifesteal;
            public RelicRollData relic_roll;
            public EnemyLifecycleData enemy_lifecycle;
            public ChoiceQueueData choice_queue;
            public LowHealthData low_health;
            public EnemyPressureSnapshot enemy_pressure;
            public EnemyHitSummary[] enemy_hit_summary;
        }

        [Serializable]
        private sealed class PlayerSnapshot
        {
            public int level;
            public int exp;
            public int exp_to_next;
            public float current_health;
            public float max_health;
            public float health_ratio;
            public float current_stamina;
            public float max_stamina;
            public float barrier;
            public StatBlock base_stats;
            public StatBlock runtime_stats;
            public StatBlock effective_stats;
            public UpgradeContributionData[] upgrade_contributions;
            public RelicStackData[] relics;
            public EnhancerSnapshot[] enhancers;
        }

        [Serializable]
        private sealed class CombatContext
        {
            public int player_level;
            public float player_current_health;
            public float player_max_health;
            public float player_health_ratio;
            public float player_barrier;
            public float player_damage;
            public float player_crit_chance;
            public float player_life_steal;
            public int difficulty;
            public int active_enemy_count;
            public int simulated_enemy_count;
        }

        [Serializable]
        private sealed class StatBlock
        {
            public float max_health;
            public float health_regen;
            public float max_stamina;
            public float stamina_regen;
            public float speed;
            public float damage;
            public float crit_chance;
            public float crit_multiplier;
            public float life_steal;
            public float swing_speed;
            public float damage_reduction;
            public float dodge_chance;
        }

        [Serializable]
        private sealed class WorldSnapshot
        {
            public int difficulty;
            public int enemies_spawned;
            public int enemies_killed;
            public int active_enemy_count;
            public int simulated_enemy_count;
            public int horde_agent_count;
            public int horde_zombie_count;
            public int horde_enemy_count;
        }

        [Serializable]
        private sealed class DifficultySnapshot
        {
            public float enemy_health_multiplier;
            public float enemy_damage_multiplier;
            public float enemy_attack_speed_multiplier;
            public float enemy_move_speed_multiplier;
            public float enemy_detection_multiplier;
            public float exp_multiplier;
            public float spawn_cap_multiplier_base_100;
        }

        [Serializable]
        private sealed class RunCounters
        {
            public int upgrade_rolls;
            public int upgrades_applied;
            public int relics_applied;
            public int enhancers_applied;
            public int melee_hits;
            public int melee_crit_hits;
            public int melee_kills;
            public float total_melee_damage;
            public float total_hits_to_kill;
            public float average_hits_to_kill;
            public int incoming_damage_events;
            public int incoming_damage_dodged;
            public int incoming_damage_blocked;
            public float total_incoming_raw_damage;
            public float total_incoming_final_damage;
            public float total_incoming_barrier_absorbed;
            public float total_incoming_chained_hit_reduction;
            public int relic_rolls;
            public int relic_roll_rejections;
            public int choice_queue_events;
            public int relic_proc_events;
            public int relic_proc_kills;
            public float relic_proc_total_damage;
            public int lifesteal_events;
            public float lifesteal_raw_heal;
            public float lifesteal_per_hit_capped_heal;
            public float lifesteal_per_second_capped_heal;
            public float lifesteal_applied_heal;
            public float lifesteal_overheal;
            public int low_hp_warning_entries;
            public int low_hp_critical_entries;
            public float time_below_warning_s;
            public float time_below_critical_s;
        }

        [Serializable]
        private sealed class UpgradeOptionData
        {
            public string stat;
            public float value;
            public string rarity;
            public string display_name;
            public string description;
        }

        [Serializable]
        private sealed class RelicAppliedData
        {
            public string id;
            public string display_name;
            public string rarity;
            public int new_stacks;
            public int max_stacks;
            public bool stackable;
        }

        [Serializable]
        private sealed class RelicStackData
        {
            public string id;
            public string display_name;
            public string rarity;
            public int stacks;
            public int max_stacks;
            public bool stackable;
            public float damage_multiplier;
            public float crit_chance_bonus;
            public float crit_multiplier_bonus;
            public float life_steal_bonus;
            public float swing_speed_bonus;
            public float speed_bonus;
            public float max_health_bonus;
            public float stamina_regen_bonus;
            public float damage_reduction_bonus;
            public float dodge_chance_bonus;
            public float sword_length_bonus;
            public float stamina_swing_override;
            public float exp_gain_multiplier;
            public int proc_count;
            public int proc_kills;
            public float proc_total_damage;
            public float proc_avg_damage;
            public float proc_peak_damage;
            public float proc_last_time_s;
        }

        [Serializable]
        private sealed class EnhancerAppliedData
        {
            public string enhancer_id;
            public bool refreshed_existing;
            public int stacks;
            public int max_stacks;
            public float remaining_duration_s;
            public float total_duration_s;
            public float strength_01;
        }

        [Serializable]
        private sealed class EnhancerSnapshot
        {
            public string enhancer_id;
            public int stacks;
            public int max_stacks;
            public float remaining_duration_s;
            public float total_duration_s;
            public float strength_01;
            public EnhancerStatContribution[] contributions;
        }

        [Serializable]
        private sealed class EnhancerStatContribution
        {
            public string stat;
            public string math_mode;
            public float max_bonus;
            public float scaled_bonus;
            public float additive_component;
            public float multiplicative_component;
        }

        [Serializable]
        private sealed class MeleeHitData
        {
            public int enemy_instance_id;
            public string enemy_name;
            public string enemy_type;
            public bool is_boss;
            public float damage;
            public bool is_crit;
            public int hit_index_on_enemy;
            public float enemy_current_health;
            public float enemy_max_health;
            public float enemy_attack_damage;
            public float enemy_attack_cooldown;
            public float enemy_threat_score;
        }

        [Serializable]
        private sealed class MeleeKillData
        {
            public int enemy_instance_id;
            public string enemy_name;
            public string enemy_type;
            public bool is_boss;
            public float damage;
            public bool is_crit;
            public int hits_to_kill;
            public float damage_dealt_to_enemy;
            public float enemy_max_health;
            public float enemy_attack_damage;
            public float enemy_attack_cooldown;
            public float enemy_threat_score;
        }

        [Serializable]
        private sealed class LifeStealData
        {
            public float damage_dealt;
            public float life_steal_percent_requested;
            public float life_steal_percent_effective;
            public float raw_heal;
            public float per_hit_capped_heal;
            public float per_second_capped_heal;
            public float applied_heal;
            public float overheal;
            public float life_steal_per_second_cap;
            public float health_before;
            public float health_after;
            public float max_health;
        }

        [Serializable]
        private sealed class IncomingDamageData
        {
            public float raw_damage;
            public float reduction;
            public float damage_after_reduction;
            public float chained_hit_reduction;
            public int chained_hit_count;
            public bool dodged;
            public bool blocked;
            public float barrier_before;
            public float barrier_absorbed;
            public float barrier_after;
            public float final_damage;
            public float health_before;
            public float health_after;
            public float max_health;
        }

        [Serializable]
        private sealed class RelicProcData
        {
            public string relic_id;
            public string display_name;
            public string rarity;
            public float damage;
            public bool caused_kill;
            public int target_instance_id;
            public string target_name;
        }

        [Serializable]
        private sealed class RelicRollData
        {
            public string source;
            public RelicRollOptionData[] offered;
            public RejectedRelicRollOptionData[] rejected;
        }

        [Serializable]
        private sealed class RelicRollOptionData
        {
            public string id;
            public string display_name;
            public string rarity;
            public int current_stacks;
            public int max_stacks;
        }

        [Serializable]
        private sealed class RejectedRelicRollOptionData
        {
            public string id;
            public string display_name;
            public string rarity;
            public string reason;
            public int current_stacks;
            public int max_stacks;
        }

        [Serializable]
        private sealed class EnemyLifecycleData
        {
            public string lifecycle;
            public int sim_id;
            public int enemy_instance_id;
            public string enemy_type;
            public bool is_boss;
        }

        [Serializable]
        private sealed class ChoiceQueueData
        {
            public string source;
            public string action;
            public int pending_count;
            public bool is_showing;
        }

        [Serializable]
        private sealed class LowHealthData
        {
            public float threshold_ratio;
            public bool entered;
            public float current_health;
            public float max_health;
            public float health_ratio;
            public float seconds_since_enter;
            public float total_seconds_under_threshold;
        }

        [Serializable]
        private sealed class UpgradeContributionData
        {
            public string stat;
            public float total_value;
        }

        [Serializable]
        private sealed class EnemyPressureSnapshot
        {
            public int active_enemy_count;
            public int active_boss_count;
            public float total_enemy_max_health;
            public float total_enemy_current_health;
            public float avg_enemy_max_health;
            public float avg_enemy_damage;
            public float avg_enemy_attack_rate;
            public float avg_threat_score;
            public float max_threat_score;
        }

        [Serializable]
        private sealed class EnemyHitSummary
        {
            public string enemy_type;
            public int hits;
            public int kills;
            public float total_damage;
            public float average_hits_per_kill;
            public float average_damage_per_kill;
            public float p50_hits_per_kill;
            public float p90_hits_per_kill;
            public float max_threat_seen;
        }

        private sealed class TargetHitTracker
        {
            public Combatant target;
            public string enemyType;
            public string enemyName;
            public bool isBoss;
            public int hits;
            public float totalDamage;
            public float firstHitRunTime;
            public float lastHitRunTime;
            public float maxThreatSeen;
        }

        private sealed class EnemyHitAggregate
        {
            public int hits;
            public int kills;
            public float totalDamage;
            public float totalHitsToKill;
            public float totalDamageToKill;
            public float maxThreatSeen;
            public readonly List<int> hitsToKillSamples = new(16);
        }

        private sealed class RelicProcAggregate
        {
            public int procCount;
            public int kills;
            public float totalDamage;
            public float peakDamage;
            public float lastProcRunTime;
        }

        private readonly struct EnemyRuntimeInfo
        {
            public readonly int instanceId;
            public readonly string enemyName;
            public readonly string enemyType;
            public readonly bool isBoss;
            public readonly float currentHealth;
            public readonly float maxHealth;
            public readonly float attackDamage;
            public readonly float attackCooldown;
            public readonly float threatScore;

            public EnemyRuntimeInfo(
                int instanceId,
                string enemyName,
                string enemyType,
                bool isBoss,
                float currentHealth,
                float maxHealth,
                float attackDamage,
                float attackCooldown,
                float threatScore
            )
            {
                this.instanceId = instanceId;
                this.enemyName = enemyName;
                this.enemyType = enemyType;
                this.isBoss = isBoss;
                this.currentHealth = currentHealth;
                this.maxHealth = maxHealth;
                this.attackDamage = attackDamage;
                this.attackCooldown = attackCooldown;
                this.threatScore = threatScore;
            }
        }

        private GameTimerController timer;
        private PlayerProgressionController player;
        private PlayerRelicController relics;
        private WeaponController weapon;
        private WeaponEnhancerSystem enhancerSystem;

        private PlayerProgressionController subscribedPlayer;
        private PlayerRelicController subscribedRelics;
        private WeaponEnhancerSystem subscribedEnhancerSystem;
        private bool subscribedToHub;

        private bool runActive;
        private string runId;
        private string persistentLogPath;
        private string projectLogPath;
        private int runSequence;
        private float nextResolveAt;
        private float nextSnapshotAt;
        private float nextFlushAt;
        private float runStartedRealtime = -1f;
        private float lastReliableRunTime;
        private float missingPlayerSince = -1f;

        private int upgradeRolls;
        private int upgradesApplied;
        private int relicsApplied;
        private int enhancersApplied;
        private int meleeHits;
        private int meleeCritHits;
        private int meleeKills;
        private float totalMeleeDamage;
        private float totalHitsToKill;
        private int incomingDamageEvents;
        private int incomingDamageDodged;
        private int incomingDamageBlocked;
        private float totalIncomingRawDamage;
        private float totalIncomingFinalDamage;
        private float totalIncomingBarrierAbsorbed;
        private float totalIncomingChainedHitReduction;
        private int relicRolls;
        private int relicRollRejections;
        private int choiceQueueEvents;
        private int relicProcEvents;
        private int relicProcKills;
        private float totalRelicProcDamage;
        private int lifeStealEvents;
        private float totalLifeStealRaw;
        private float totalLifeStealPerHitCapped;
        private float totalLifeStealPerSecondCapped;
        private float totalLifeStealApplied;
        private float totalLifeStealOverheal;
        private int lowHpWarningEntries;
        private int lowHpCriticalEntries;
        private float lowHpWarningTotalSeconds;
        private float lowHpCriticalTotalSeconds;
        private bool lowHpWarningActive;
        private bool lowHpCriticalActive;
        private float lowHpWarningEnteredAt = -1f;
        private float lowHpCriticalEnteredAt = -1f;
        private string pendingExitReason;
        private PlayerSnapshot lastValidPlayerSnapshot;
        private WorldSnapshot lastValidWorldSnapshot;
        private DifficultySnapshot lastValidDifficultySnapshot;
        private EnemyPressureSnapshot lastValidEnemyPressureSnapshot;
        private CombatContext lastValidCombatContext;

        private readonly Dictionary<int, TargetHitTracker> targetHitTrackers = new(256);
        private readonly Dictionary<string, EnemyHitAggregate> enemyHitAggregates = new(64);
        private readonly Dictionary<string, RelicProcAggregate> relicProcAggregates = new(32);
        private readonly Dictionary<StatType, float> upgradeContributions = new(16);
        private readonly List<int> targetTrackerIdsToRemove = new(128);
        private readonly List<EnemyHitSummary> enemyHitSummaryBuffer = new(64);
        private readonly List<UpgradeContributionData> upgradeContributionBuffer = new(16);
        private readonly List<RelicStackData> relicBuffer = new(16);
        private readonly List<EnhancerSnapshot> enhancerBuffer = new(16);
        private readonly StringBuilder writeBuffer = new(8192);
        private int bufferedLineCount;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            instance = null;
            shuttingDown = false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            EnsureInstance();
        }

        private static GameplayTelemetryRecorder EnsureInstance()
        {
            if (shuttingDown)
                return null;

            if (instance != null)
                return instance;

            instance = FindFirstObjectByType<GameplayTelemetryRecorder>();
            if (instance != null)
                return instance;

            GameObject go = new("GameplayTelemetryRecorder");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<GameplayTelemetryRecorder>();
            return instance;
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void Update()
        {
            if (Time.unscaledTime >= nextResolveAt)
            {
                nextResolveAt = Time.unscaledTime + ResolveIntervalSeconds;
                ResolveReferences();
            }

            if (!runActive)
            {
                if (CanStartRun())
                    StartRun();

                return;
            }

            UpdateSubscriptions();

            if (ShouldEndRun(out string endReason))
            {
                EndRun(endReason);
                return;
            }

            UpdateLowHealthState(GetRunTimeSeconds(), flushOnly: false);

            float runTime = GetRunTimeSeconds();
            if (runTime >= nextSnapshotAt)
            {
                WritePeriodicSnapshot();
                nextSnapshotAt = runTime + SnapshotIntervalSeconds;
            }

            if (bufferedLineCount >= FlushLineThreshold || Time.unscaledTime >= nextFlushAt)
                FlushBufferedLines(force: false);
        }

        private void OnApplicationQuit()
        {
            shuttingDown = true;
            EndRun("application_quit");
        }

        private void OnDestroy()
        {
            EndRun("recorder_destroyed");
            if (instance == this)
                instance = null;
        }

        private void ResolveReferences()
        {
            GameTimerController liveTimer = GameTimerController.Instance;
            if (liveTimer != null)
                timer = liveTimer;
            else if (timer != null && !IsObjectAlive(timer))
                timer = null;

            if (!IsPlayerValid(player))
            {
                player = PlayerLocator.GetProgression();
                if (!IsPlayerValid(player))
                    player = FindFirstObjectByType<PlayerProgressionController>();
            }

            if (IsPlayerValid(player))
            {
                relics = player.GetComponent<PlayerRelicController>();

                if (weapon == null || weapon.transform == null || weapon.transform.root != player.transform)
                    weapon = player.GetComponentInChildren<WeaponController>(true);

                if (enhancerSystem == null || enhancerSystem.transform == null || enhancerSystem.transform.root != player.transform)
                    enhancerSystem = player.GetComponentInChildren<WeaponEnhancerSystem>(true);
            }
            else
            {
                player = null;
                relics = null;
                weapon = null;
                enhancerSystem = null;
            }
        }

        private bool CanStartRun()
        {
            return timer != null
                && player != null
                && !timer.gameEnded
                && !player.IsDead;
        }

        private bool ShouldEndRun(out string reason)
        {
            reason = null;

            if (!string.IsNullOrWhiteSpace(pendingExitReason))
            {
                reason = pendingExitReason;
                pendingExitReason = null;
                return true;
            }

            string activeScene = SceneManager.GetActiveScene().name;
            if (string.Equals(activeScene, "MainMenu", StringComparison.OrdinalIgnoreCase))
            {
                reason = "quit_to_menu";
                return true;
            }

            if (timer == null)
            {
                reason = string.Equals(activeScene, "Loading", StringComparison.OrdinalIgnoreCase)
                    ? "scene_transition_loading"
                    : "timer_missing";
                return true;
            }

            if (player == null || !player.gameObject.activeInHierarchy)
            {
                if (missingPlayerSince < 0f)
                    missingPlayerSince = Time.unscaledTime;

                if (Time.unscaledTime - missingPlayerSince >= 2f)
                {
                    reason = "player_missing";
                    return true;
                }
            }
            else
            {
                missingPlayerSince = -1f;
            }

            if (player != null && player.IsDead)
            {
                reason = "player_dead";
                return true;
            }

            if (timer.gameEnded)
            {
                reason = "game_ended";
                return true;
            }

            return false;
        }

        private void StartRun()
        {
            runSequence++;
            runId = $"{DateTime.UtcNow:yyyyMMdd_HHmmss}_{runSequence:000}";
            InitializeLogPaths();
            ResetRunAccumulators();
            runActive = true;
            missingPlayerSince = -1f;
            runStartedRealtime = Time.realtimeSinceStartup;
            lastReliableRunTime = 0f;
            nextSnapshotAt = GetRunTimeSeconds() + SnapshotIntervalSeconds;
            nextFlushAt = Time.unscaledTime + FlushIntervalSeconds;

            UpdateSubscriptions();

            TelemetryRecord record = CreateRecord("run_started");
            record.player = CapturePlayerSnapshot(allowFallback: false);
            record.world = CaptureWorldSnapshot(allowFallback: false);
            record.difficulty = CaptureDifficultySnapshot(allowFallback: false);
            record.enemy_pressure = CaptureEnemyPressureSnapshot(allowFallback: false);
            record.combat_context = CollectCombatContext();
            record.enemy_hit_summary = BuildEnemyHitSummaryArray();
            record.counters = CollectRunCounters();
            record.upgrade_contributions = CollectUpgradeContributionArray();
            EnqueueRecord(record);
            FlushBufferedLines(force: true);
        }

        private void EndRun(string reason)
        {
            if (!runActive)
            {
                UnsubscribeAll();
                return;
            }

            float runTime = GetRunTimeSeconds();
            UpdateLowHealthState(runTime, flushOnly: true);

            TelemetryRecord record = CreateRecord("run_ended");
            record.run_time_s = runTime;
            record.reason = string.IsNullOrWhiteSpace(reason) ? "unknown" : reason;
            record.player = CapturePlayerSnapshot(allowFallback: true);
            record.world = CaptureWorldSnapshot(allowFallback: true);
            record.difficulty = CaptureDifficultySnapshot(allowFallback: true);
            record.enemy_pressure = CaptureEnemyPressureSnapshot(allowFallback: true);
            record.combat_context = CollectCombatContext();
            record.enemy_hit_summary = BuildEnemyHitSummaryArray();
            record.counters = CollectRunCounters();
            record.upgrade_contributions = CollectUpgradeContributionArray();
            EnqueueRecord(record);

            FlushBufferedLines(force: true);
            UnsubscribeAll();
            runActive = false;
            missingPlayerSince = -1f;
            runStartedRealtime = -1f;
        }

        private void UpdateSubscriptions()
        {
            if (subscribedPlayer != player)
            {
                if (subscribedPlayer != null)
                {
                    subscribedPlayer.OnLevelUpOptionsRolled -= HandleUpgradeOptionsRolled;
                    subscribedPlayer.OnUpgradeApplied -= HandleUpgradeApplied;
                }

                subscribedPlayer = player;
                if (subscribedPlayer != null)
                {
                    subscribedPlayer.OnLevelUpOptionsRolled += HandleUpgradeOptionsRolled;
                    subscribedPlayer.OnUpgradeApplied += HandleUpgradeApplied;
                }
            }

            if (subscribedRelics != relics)
            {
                if (subscribedRelics != null)
                {
                    subscribedRelics.OnRelicApplied -= HandleRelicApplied;
                    subscribedRelics.OnMeleeHitDealt -= HandleMeleeHit;
                    subscribedRelics.OnMeleeKill -= HandleMeleeKill;
                }

                subscribedRelics = relics;
                if (subscribedRelics != null)
                {
                    subscribedRelics.OnRelicApplied += HandleRelicApplied;
                    subscribedRelics.OnMeleeHitDealt += HandleMeleeHit;
                    subscribedRelics.OnMeleeKill += HandleMeleeKill;
                }
            }

            if (subscribedEnhancerSystem != enhancerSystem)
            {
                if (subscribedEnhancerSystem != null)
                    subscribedEnhancerSystem.OnEnhancerAddedOrRefreshed -= HandleEnhancerAddedOrRefreshed;

                subscribedEnhancerSystem = enhancerSystem;
                if (subscribedEnhancerSystem != null)
                    subscribedEnhancerSystem.OnEnhancerAddedOrRefreshed += HandleEnhancerAddedOrRefreshed;
            }

            if (!subscribedToHub)
            {
                GameplayTelemetryHub.OnIncomingDamage += HandleIncomingDamage;
                GameplayTelemetryHub.OnRelicOptionsRolled += HandleRelicOptionsRolled;
                GameplayTelemetryHub.OnEnemyLifecycle += HandleEnemyLifecycle;
                GameplayTelemetryHub.OnLifeStealApplied += HandleLifeStealApplied;
                GameplayTelemetryHub.OnChoiceQueueChanged += HandleChoiceQueueChanged;
                GameplayTelemetryHub.OnRunExitRequested += HandleRunExitRequested;
                GameplayTelemetryHub.OnRelicProc += HandleRelicProc;
                subscribedToHub = true;
            }
        }

        private void UnsubscribeAll()
        {
            if (subscribedPlayer != null)
            {
                subscribedPlayer.OnLevelUpOptionsRolled -= HandleUpgradeOptionsRolled;
                subscribedPlayer.OnUpgradeApplied -= HandleUpgradeApplied;
                subscribedPlayer = null;
            }

            if (subscribedRelics != null)
            {
                subscribedRelics.OnRelicApplied -= HandleRelicApplied;
                subscribedRelics.OnMeleeHitDealt -= HandleMeleeHit;
                subscribedRelics.OnMeleeKill -= HandleMeleeKill;
                subscribedRelics = null;
            }

            if (subscribedEnhancerSystem != null)
            {
                subscribedEnhancerSystem.OnEnhancerAddedOrRefreshed -= HandleEnhancerAddedOrRefreshed;
                subscribedEnhancerSystem = null;
            }

            if (subscribedToHub)
            {
                GameplayTelemetryHub.OnIncomingDamage -= HandleIncomingDamage;
                GameplayTelemetryHub.OnRelicOptionsRolled -= HandleRelicOptionsRolled;
                GameplayTelemetryHub.OnEnemyLifecycle -= HandleEnemyLifecycle;
                GameplayTelemetryHub.OnLifeStealApplied -= HandleLifeStealApplied;
                GameplayTelemetryHub.OnChoiceQueueChanged -= HandleChoiceQueueChanged;
                GameplayTelemetryHub.OnRunExitRequested -= HandleRunExitRequested;
                GameplayTelemetryHub.OnRelicProc -= HandleRelicProc;
                subscribedToHub = false;
            }
        }

        private void HandleUpgradeOptionsRolled(List<UpgradeOption> options)
        {
            if (!runActive)
                return;

            upgradeRolls++;

            TelemetryRecord record = CreateRecord("upgrade_options_rolled");
            record.upgrade_options = ConvertUpgradeOptions(options);
            record.player = CapturePlayerSnapshot();
            record.counters = CollectRunCounters();
            record.upgrade_contributions = CollectUpgradeContributionArray();
            EnqueueRecord(record);
        }

        private void HandleUpgradeApplied(UpgradeOption option)
        {
            if (!runActive || option == null)
                return;

            upgradesApplied++;
            if (upgradeContributions.TryGetValue(option.stat, out float existingValue))
                upgradeContributions[option.stat] = existingValue + option.value;
            else
                upgradeContributions[option.stat] = option.value;

            TelemetryRecord record = CreateRecord("upgrade_applied");
            record.upgrade_selected = ConvertUpgradeOption(option);
            record.player = CapturePlayerSnapshot();
            record.counters = CollectRunCounters();
            record.upgrade_contributions = CollectUpgradeContributionArray();
            EnqueueRecord(record);
        }

        private void HandleRelicApplied(RelicDefinition relic, int newStacks)
        {
            if (!runActive || relic == null)
                return;

            relicsApplied++;

            TelemetryRecord record = CreateRecord("relic_applied");
            record.relic_applied = new RelicAppliedData
            {
                id = relic.id,
                display_name = string.IsNullOrWhiteSpace(relic.displayName) ? relic.name : relic.displayName,
                rarity = relic.rarity.ToString(),
                new_stacks = Mathf.Max(0, newStacks),
                max_stacks = relics != null ? relics.GetEffectiveMaxStacks(relic) : Mathf.Max(1, relic.maxStacks),
                stackable = relic.stackable
            };
            record.player = CapturePlayerSnapshot();
            record.counters = CollectRunCounters();
            record.upgrade_contributions = CollectUpgradeContributionArray();
            EnqueueRecord(record);
        }

        private void HandleEnhancerAddedOrRefreshed(ActiveEnhancer enhancer, bool refreshedExisting)
        {
            if (!runActive || enhancer == null)
                return;

            enhancersApplied++;

            EnhancerDefinition def = enhancer.Definition;
            TelemetryRecord record = CreateRecord("enhancer_applied");
            record.enhancer_applied = new EnhancerAppliedData
            {
                enhancer_id = def != null ? def.enhancerId : "unknown",
                refreshed_existing = refreshedExisting,
                stacks = enhancer.Stacks,
                max_stacks = def != null ? Mathf.Max(1, def.maxStacks) : enhancer.Stacks,
                remaining_duration_s = Mathf.Max(0f, enhancer.RemainingDuration),
                total_duration_s = Mathf.Max(0f, enhancer.TotalDuration),
                strength_01 = Mathf.Clamp01(enhancer.GetStrength01())
            };
            record.player = CapturePlayerSnapshot();
            record.counters = CollectRunCounters();
            record.upgrade_contributions = CollectUpgradeContributionArray();
            EnqueueRecord(record);
        }

        private void HandleMeleeHit(Combatant target, float damage, bool isCrit)
        {
            if (!runActive || target == null)
                return;

            float runTime = GetRunTimeSeconds();
            float clampedDamage = Mathf.Max(0f, damage);
            meleeHits++;
            totalMeleeDamage += clampedDamage;
            if (isCrit)
                meleeCritHits++;

            EnemyRuntimeInfo enemyInfo = ResolveEnemyRuntimeInfo(target);
            TargetHitTracker tracker = GetOrCreateTargetTracker(enemyInfo, target, runTime);
            tracker.hits++;
            tracker.totalDamage += clampedDamage;
            tracker.lastHitRunTime = runTime;
            tracker.maxThreatSeen = Mathf.Max(tracker.maxThreatSeen, enemyInfo.threatScore);

            EnemyHitAggregate aggregate = GetOrCreateEnemyAggregate(enemyInfo.enemyType);
            aggregate.hits++;
            aggregate.totalDamage += clampedDamage;
            aggregate.maxThreatSeen = Mathf.Max(aggregate.maxThreatSeen, enemyInfo.threatScore);

            TelemetryRecord record = CreateRecord("melee_hit");
            record.melee_hit = new MeleeHitData
            {
                enemy_instance_id = enemyInfo.instanceId,
                enemy_name = enemyInfo.enemyName,
                enemy_type = enemyInfo.enemyType,
                is_boss = enemyInfo.isBoss,
                damage = clampedDamage,
                is_crit = isCrit,
                hit_index_on_enemy = tracker.hits,
                enemy_current_health = enemyInfo.currentHealth,
                enemy_max_health = enemyInfo.maxHealth,
                enemy_attack_damage = enemyInfo.attackDamage,
                enemy_attack_cooldown = enemyInfo.attackCooldown,
                enemy_threat_score = enemyInfo.threatScore
            };
            record.combat_context = CollectCombatContext();
            EnqueueRecord(record);
        }

        private void HandleMeleeKill(Combatant target, float damage, bool isCrit)
        {
            if (!runActive || target == null)
                return;

            meleeKills++;

            EnemyRuntimeInfo enemyInfo = ResolveEnemyRuntimeInfo(target);
            targetHitTrackers.TryGetValue(enemyInfo.instanceId, out TargetHitTracker tracker);

            int hitsToKill = tracker != null ? Mathf.Max(1, tracker.hits) : 1;
            float damageToKill = tracker != null ? Mathf.Max(0f, tracker.totalDamage) : Mathf.Max(0f, damage);
            totalHitsToKill += hitsToKill;

            EnemyHitAggregate aggregate = GetOrCreateEnemyAggregate(enemyInfo.enemyType);
            aggregate.kills++;
            aggregate.totalHitsToKill += hitsToKill;
            aggregate.totalDamageToKill += damageToKill;
            aggregate.maxThreatSeen = Mathf.Max(aggregate.maxThreatSeen, enemyInfo.threatScore);
            aggregate.hitsToKillSamples.Add(hitsToKill);

            TelemetryRecord record = CreateRecord("melee_kill");
            record.melee_kill = new MeleeKillData
            {
                enemy_instance_id = enemyInfo.instanceId,
                enemy_name = enemyInfo.enemyName,
                enemy_type = enemyInfo.enemyType,
                is_boss = enemyInfo.isBoss,
                damage = Mathf.Max(0f, damage),
                is_crit = isCrit,
                hits_to_kill = hitsToKill,
                damage_dealt_to_enemy = damageToKill,
                enemy_max_health = enemyInfo.maxHealth,
                enemy_attack_damage = enemyInfo.attackDamage,
                enemy_attack_cooldown = enemyInfo.attackCooldown,
                enemy_threat_score = enemyInfo.threatScore
            };
            record.combat_context = CollectCombatContext();
            EnqueueRecord(record);

            targetHitTrackers.Remove(enemyInfo.instanceId);
        }

        private void HandleIncomingDamage(GameplayTelemetryHub.IncomingDamageSample sample)
        {
            if (!runActive)
                return;

            incomingDamageEvents++;
            totalIncomingRawDamage += Mathf.Max(0f, sample.rawDamage);
            totalIncomingFinalDamage += Mathf.Max(0f, sample.finalDamage);
            totalIncomingBarrierAbsorbed += Mathf.Max(0f, sample.barrierAbsorbed);
            totalIncomingChainedHitReduction += Mathf.Max(0f, sample.chainedHitReduction);
            if (sample.dodged)
                incomingDamageDodged++;
            if (sample.blocked)
                incomingDamageBlocked++;

            TelemetryRecord record = CreateRecord("incoming_damage");
            record.run_time_s = ResolveSampleRunTime(sample.runTimeSeconds);
            record.incoming_damage = new IncomingDamageData
            {
                raw_damage = sample.rawDamage,
                reduction = sample.reduction,
                damage_after_reduction = sample.damageAfterReduction,
                chained_hit_reduction = sample.chainedHitReduction,
                chained_hit_count = sample.chainedHitCount,
                dodged = sample.dodged,
                blocked = sample.blocked,
                barrier_before = sample.barrierBefore,
                barrier_absorbed = sample.barrierAbsorbed,
                barrier_after = sample.barrierAfter,
                final_damage = sample.finalDamage,
                health_before = sample.healthBefore,
                health_after = sample.healthAfter,
                max_health = sample.maxHealth
            };
            record.combat_context = CollectCombatContext();
            EnqueueRecord(record);
        }

        private void HandleRelicOptionsRolled(GameplayTelemetryHub.RelicOptionsRolledSample sample)
        {
            if (!runActive)
                return;

            relicRolls++;
            int rejectedCount = sample.rejected != null ? sample.rejected.Length : 0;
            relicRollRejections += Mathf.Max(0, rejectedCount);

            TelemetryRecord record = CreateRecord("relic_options_rolled");
            record.run_time_s = ResolveSampleRunTime(sample.runTimeSeconds);
            record.relic_roll = new RelicRollData
            {
                source = string.IsNullOrWhiteSpace(sample.source) ? "unknown" : sample.source,
                offered = ConvertRelicRollOffered(sample.offered),
                rejected = ConvertRelicRollRejected(sample.rejected)
            };
            record.counters = CollectRunCounters();
            EnqueueRecord(record);
        }

        private void HandleEnemyLifecycle(GameplayTelemetryHub.EnemyLifecycleSample sample)
        {
            if (!runActive)
                return;

            TelemetryRecord record = CreateRecord("enemy_lifecycle");
            record.run_time_s = ResolveSampleRunTime(sample.runTimeSeconds);
            record.enemy_lifecycle = new EnemyLifecycleData
            {
                lifecycle = string.IsNullOrWhiteSpace(sample.lifecycle) ? "unknown" : sample.lifecycle,
                sim_id = sample.simId,
                enemy_instance_id = sample.enemyInstanceId,
                enemy_type = string.IsNullOrWhiteSpace(sample.enemyType) ? "Enemy/Unknown" : sample.enemyType,
                is_boss = sample.isBoss
            };
            record.combat_context = CollectCombatContext();
            EnqueueRecord(record);
        }

        private void HandleChoiceQueueChanged(GameplayTelemetryHub.ChoiceQueueSample sample)
        {
            if (!runActive)
                return;

            choiceQueueEvents++;

            TelemetryRecord record = CreateRecord("choice_queue_changed");
            record.run_time_s = ResolveSampleRunTime(sample.runTimeSeconds);
            record.choice_queue = new ChoiceQueueData
            {
                source = string.IsNullOrWhiteSpace(sample.source) ? "unknown" : sample.source,
                action = string.IsNullOrWhiteSpace(sample.action) ? "unknown" : sample.action,
                pending_count = Mathf.Max(0, sample.pendingCount),
                is_showing = sample.isShowing
            };
            record.counters = CollectRunCounters();
            EnqueueRecord(record);
        }

        private void HandleRunExitRequested(GameplayTelemetryHub.RunExitSample sample)
        {
            if (!runActive)
                return;

            string reason = string.IsNullOrWhiteSpace(sample.reason) ? "unknown_exit" : sample.reason;
            pendingExitReason = reason;

            TelemetryRecord record = CreateRecord("run_exit_requested");
            record.run_time_s = ResolveSampleRunTime(sample.runTimeSeconds);
            record.reason = reason;
            EnqueueRecord(record);
        }

        private void HandleLifeStealApplied(GameplayTelemetryHub.LifeStealAppliedSample sample)
        {
            if (!runActive)
                return;

            lifeStealEvents++;
            totalLifeStealRaw += Mathf.Max(0f, sample.rawHeal);
            totalLifeStealPerHitCapped += Mathf.Max(0f, sample.perHitCappedHeal);
            totalLifeStealPerSecondCapped += Mathf.Max(0f, sample.perSecondCappedHeal);
            totalLifeStealApplied += Mathf.Max(0f, sample.appliedHeal);
            totalLifeStealOverheal += Mathf.Max(0f, sample.overheal);

            TelemetryRecord record = CreateRecord("lifesteal_applied");
            record.run_time_s = ResolveSampleRunTime(sample.runTimeSeconds);
            record.lifesteal = new LifeStealData
            {
                damage_dealt = sample.damageDealt,
                life_steal_percent_requested = sample.lifeStealPercentRequested,
                life_steal_percent_effective = sample.lifeStealPercentEffective,
                raw_heal = sample.rawHeal,
                per_hit_capped_heal = sample.perHitCappedHeal,
                per_second_capped_heal = sample.perSecondCappedHeal,
                applied_heal = sample.appliedHeal,
                overheal = sample.overheal,
                life_steal_per_second_cap = sample.lifeStealPerSecondCap,
                health_before = sample.healthBefore,
                health_after = sample.healthAfter,
                max_health = sample.maxHealth
            };
            record.combat_context = CollectCombatContext();
            EnqueueRecord(record);
        }

        private void HandleRelicProc(GameplayTelemetryHub.RelicProcSample sample)
        {
            if (!runActive)
                return;

            string resolvedRelicId = ResolveRelicProcId(sample.relicId, sample.displayName);
            float damage = Mathf.Max(0f, sample.damage);
            bool causedKill = sample.causedKill;

            relicProcEvents++;
            totalRelicProcDamage += damage;
            if (causedKill)
                relicProcKills++;

            float runTime = ResolveSampleRunTime(sample.runTimeSeconds);
            RelicProcAggregate aggregate = GetOrCreateRelicProcAggregate(resolvedRelicId);
            aggregate.procCount++;
            aggregate.totalDamage += damage;
            aggregate.peakDamage = Mathf.Max(aggregate.peakDamage, damage);
            aggregate.lastProcRunTime = Mathf.Max(aggregate.lastProcRunTime, runTime);
            if (causedKill)
                aggregate.kills++;

            TelemetryRecord record = CreateRecord("relic_proc");
            record.run_time_s = runTime;
            record.relic_proc = new RelicProcData
            {
                relic_id = resolvedRelicId,
                display_name = string.IsNullOrWhiteSpace(sample.displayName) ? "Relic" : sample.displayName,
                rarity = string.IsNullOrWhiteSpace(sample.rarity) ? "Common" : sample.rarity,
                damage = damage,
                caused_kill = causedKill,
                target_instance_id = sample.targetInstanceId,
                target_name = string.IsNullOrWhiteSpace(sample.targetName) ? "Unknown" : sample.targetName
            };
            record.combat_context = CollectCombatContext();
            record.counters = CollectRunCounters();
            EnqueueRecord(record);
        }

        private void WritePeriodicSnapshot()
        {
            CleanupTargetHitTrackers(GetRunTimeSeconds());

            TelemetryRecord record = CreateRecord("periodic_snapshot");
            record.player = CapturePlayerSnapshot(allowFallback: false);
            record.world = CaptureWorldSnapshot(allowFallback: false);
            record.difficulty = CaptureDifficultySnapshot(allowFallback: false);
            record.enemy_pressure = CaptureEnemyPressureSnapshot(allowFallback: false);
            record.combat_context = CollectCombatContext();
            record.enemy_hit_summary = BuildEnemyHitSummaryArray();
            record.counters = CollectRunCounters();
            record.upgrade_contributions = CollectUpgradeContributionArray();
            EnqueueRecord(record);
        }

        private TelemetryRecord CreateRecord(string eventType)
        {
            return new TelemetryRecord
            {
                schema_version = TelemetrySchemaVersion,
                event_type = eventType,
                run_id = runId,
                utc_timestamp = DateTime.UtcNow.ToString("O"),
                scene_name = SceneManager.GetActiveScene().name,
                app_version = Application.version,
                unity_version = Application.unityVersion,
                platform = Application.platform.ToString(),
                frame = Time.frameCount,
                run_time_s = GetRunTimeSeconds(),
                realtime_since_startup_s = Time.realtimeSinceStartup
            };
        }

        private void EnqueueRecord(TelemetryRecord record)
        {
            if (record == null || string.IsNullOrWhiteSpace(persistentLogPath))
                return;

            record.run_time_s = NormalizeRunTime(record.run_time_s);
            PopulateIntegrityFlags(record);

            string json = JsonUtility.ToJson(record);
            if (string.IsNullOrWhiteSpace(json))
                return;

            writeBuffer.AppendLine(json);
            bufferedLineCount++;
        }

        private void FlushBufferedLines(bool force)
        {
            if (bufferedLineCount <= 0)
                return;

            if (!force && bufferedLineCount < FlushLineThreshold && Time.unscaledTime < nextFlushAt)
                return;

            string payload = writeBuffer.ToString();
            writeBuffer.Clear();
            bufferedLineCount = 0;
            nextFlushAt = Time.unscaledTime + FlushIntervalSeconds;

            AppendTextSafe(persistentLogPath, payload);
            if (!string.IsNullOrWhiteSpace(projectLogPath))
                AppendTextSafe(projectLogPath, payload);
        }

        private float NormalizeRunTime(float candidate)
        {
            float clamped = Mathf.Max(0f, candidate);
            if (clamped > 0f)
                lastReliableRunTime = Mathf.Max(lastReliableRunTime, clamped);

            if (clamped <= 0f && lastReliableRunTime > 0f)
                clamped = lastReliableRunTime;

            return clamped;
        }

        private float ResolveSampleRunTime(float sampleRunTime)
        {
            if (sampleRunTime > 0f)
                return NormalizeRunTime(sampleRunTime);

            return GetRunTimeSeconds();
        }

        private void PopulateIntegrityFlags(TelemetryRecord record)
        {
            record.has_player_snapshot = IsPlayerSnapshotValid(record.player);
            record.has_world_snapshot = IsWorldSnapshotValid(record.world);
            record.has_difficulty_snapshot = IsDifficultySnapshotValid(record.difficulty);
            record.has_enemy_pressure_snapshot = IsEnemyPressureSnapshotValid(record.enemy_pressure);
            record.has_combat_context = IsCombatContextValid(record.combat_context);

            int validCount = 0;
            if (record.has_player_snapshot)
                validCount++;
            if (record.has_world_snapshot)
                validCount++;
            if (record.has_difficulty_snapshot)
                validCount++;
            if (record.has_enemy_pressure_snapshot)
                validCount++;
            if (record.has_combat_context)
                validCount++;

            record.integrity_state = validCount switch
            {
                >= 5 => "complete",
                >= 2 => "partial",
                >= 1 => "minimal",
                _ => "missing"
            };
        }

        private PlayerSnapshot CapturePlayerSnapshot(bool allowFallback = true)
        {
            PlayerSnapshot snapshot = CollectPlayerSnapshot();
            if (IsPlayerSnapshotValid(snapshot))
            {
                lastValidPlayerSnapshot = snapshot;
                return snapshot;
            }

            return allowFallback ? lastValidPlayerSnapshot : snapshot;
        }

        private WorldSnapshot CaptureWorldSnapshot(bool allowFallback = true)
        {
            WorldSnapshot snapshot = CollectWorldSnapshot();
            if (IsWorldSnapshotValid(snapshot))
            {
                bool useFallback = allowFallback
                    && IsWorldSnapshotLowSignal(snapshot)
                    && IsWorldSnapshotValid(lastValidWorldSnapshot)
                    && lastReliableRunTime > 0f;
                if (!useFallback)
                    lastValidWorldSnapshot = snapshot;
                return useFallback ? lastValidWorldSnapshot : snapshot;
            }

            return allowFallback ? lastValidWorldSnapshot : snapshot;
        }

        private DifficultySnapshot CaptureDifficultySnapshot(bool allowFallback = true)
        {
            DifficultySnapshot snapshot = CollectDifficultySnapshot();
            if (IsDifficultySnapshotValid(snapshot))
            {
                lastValidDifficultySnapshot = snapshot;
                return snapshot;
            }

            return allowFallback ? lastValidDifficultySnapshot : snapshot;
        }

        private EnemyPressureSnapshot CaptureEnemyPressureSnapshot(bool allowFallback = true)
        {
            EnemyPressureSnapshot snapshot = CollectEnemyPressureSnapshot();
            if (IsEnemyPressureSnapshotValid(snapshot))
            {
                bool useFallback = allowFallback
                    && IsEnemyPressureLowSignal(snapshot)
                    && IsEnemyPressureSnapshotValid(lastValidEnemyPressureSnapshot)
                    && lastReliableRunTime > 0f;
                if (!useFallback)
                    lastValidEnemyPressureSnapshot = snapshot;
                return useFallback ? lastValidEnemyPressureSnapshot : snapshot;
            }

            return allowFallback ? lastValidEnemyPressureSnapshot : snapshot;
        }

        private PlayerSnapshot CollectPlayerSnapshot()
        {
            if (player == null)
                return null;

            RuntimeStats runtime = player.stats;
            PlayerStatsData baseStats = player.baseStats;

            return new PlayerSnapshot
            {
                level = player.xp != null ? player.xp.level : 0,
                exp = player.xp != null ? player.xp.exp : 0,
                exp_to_next = player.xp != null ? player.xp.expToNext : 0,
                current_health = player.CurrentHealth,
                max_health = player.MaxHealth,
                health_ratio = player.MaxHealth > 0f ? Mathf.Clamp01(player.CurrentHealth / player.MaxHealth) : 0f,
                current_stamina = player.CurrentStamina,
                max_stamina = player.MaxStamina,
                barrier = player.CurrentBarrier,
                base_stats = BuildStatBlockFromBase(baseStats),
                runtime_stats = BuildStatBlockFromRuntime(runtime),
                effective_stats = BuildEffectiveStatBlock(runtime),
                upgrade_contributions = CollectUpgradeContributionArray(),
                relics = CollectRelicStacks(),
                enhancers = CollectEnhancerSnapshots()
            };
        }

        private StatBlock BuildEffectiveStatBlock(RuntimeStats runtime)
        {
            float runtimeDamage = runtime != null ? runtime.damage : 0f;
            float runtimeCritChance = runtime != null ? runtime.critChance : 0f;
            float runtimeCritMultiplier = runtime != null ? runtime.critMultiplier : 1f;
            float runtimeLifeSteal = runtime != null ? runtime.lifeSteal : 0f;
            float runtimeSwingSpeed = runtime != null ? runtime.swingSpeed : 1f;
            float runtimeSpeed = runtime != null ? runtime.speed : 0f;
            float runtimeHealthRegen = runtime != null ? runtime.healthRegen : 0f;
            float runtimeStaminaRegen = runtime != null ? runtime.staminaRegen : 0f;
            float runtimeMaxStamina = runtime != null ? runtime.maxStamina : player.MaxStamina;
            float runtimeDamageReduction = runtime != null ? runtime.damageReduction : 0f;
            float runtimeDodgeChance = runtime != null ? runtime.dodgeChance : 0f;

            float relicDamageMultiplier = 1f;
            float relicCritChanceBonus = 0f;
            float relicCritMultiplierBonus = 0f;
            float relicLifeStealBonus = 0f;
            float relicSwingSpeedBonus = 0f;
            float relicSpeedBonus = 0f;
            float relicDamageReductionBonus = 0f;
            float relicDodgeChanceBonus = 0f;

            if (relics != null)
            {
                relicDamageMultiplier = relics.GetDamageMultiplier();
                relicCritChanceBonus = relics.GetCritChanceBonus();
                relicCritMultiplierBonus = relics.GetCritMultiplierBonus();
                relicLifeStealBonus = relics.GetLifeStealBonus();
                relicSwingSpeedBonus = relics.GetSwingSpeedBonus();
                relicSpeedBonus = relics.GetSpeedBonus();
                relicDamageReductionBonus = relics.GetDamageReductionBonus();
                relicDodgeChanceBonus = relics.GetDodgeChanceBonus();
            }

            float effectiveDamage = runtimeDamage * relicDamageMultiplier;
            float effectiveCritChance = CombatBalanceCaps.ClampCritChance(runtimeCritChance + relicCritChanceBonus);
            float effectiveCritMultiplier = CombatBalanceCaps.ClampCritMultiplier(runtimeCritMultiplier + relicCritMultiplierBonus);
            float effectiveLifeSteal = runtimeLifeSteal + relicLifeStealBonus;
            bool lifeStealAlreadyEffective = false;
            float effectiveSwingSpeed = Mathf.Max(0.05f, runtimeSwingSpeed + relicSwingSpeedBonus);
            float effectiveSpeed = runtimeSpeed + relicSpeedBonus;
            float effectiveMaxHealth = player.MaxHealth;
            float effectiveStaminaRegen = player.GetEffectiveStaminaRegen();
            float effectiveDamageReduction = CombatBalanceCaps.ClampDamageReduction(runtimeDamageReduction + relicDamageReductionBonus);
            float effectiveDodgeChance = CombatBalanceCaps.ClampDodgeChance(runtimeDodgeChance + relicDodgeChanceBonus);

            if (weapon != null)
            {
                effectiveDamage = weapon.GetDamageMultiplier();
                effectiveCritChance = weapon.GetCritChance();
                effectiveCritMultiplier = weapon.GetCritMultiplier();
                effectiveLifeSteal = weapon.GetLifeSteal();
                lifeStealAlreadyEffective = true;
                effectiveSwingSpeed = weapon.GetSwingSpeedMultiplier();
            }
            else if (enhancerSystem != null)
            {
                effectiveDamage = enhancerSystem.GetEffectiveValue(StatType.Damage, effectiveDamage);
                effectiveCritChance = enhancerSystem.GetEffectiveValue(StatType.CritChance, effectiveCritChance);
                effectiveCritMultiplier = enhancerSystem.GetEffectiveValue(StatType.CritMultiplier, effectiveCritMultiplier);
                effectiveLifeSteal = enhancerSystem.GetEffectiveValue(StatType.LifeSteal, effectiveLifeSteal);
                effectiveSwingSpeed = enhancerSystem.GetEffectiveValue(StatType.SwingSpeed, effectiveSwingSpeed);
            }

            if (enhancerSystem != null)
            {
                effectiveSpeed = enhancerSystem.GetEffectiveValue(StatType.Speed, effectiveSpeed);
                effectiveMaxHealth = enhancerSystem.GetEffectiveValue(StatType.MaxHealth, effectiveMaxHealth);
                effectiveStaminaRegen = enhancerSystem.GetEffectiveValue(StatType.StaminaRegen, effectiveStaminaRegen);
            }

            if (!lifeStealAlreadyEffective)
                effectiveLifeSteal = CombatBalanceCaps.ApplyLifeStealDiminishing(effectiveLifeSteal);

            effectiveCritMultiplier = CombatBalanceCaps.ClampCritMultiplier(effectiveCritMultiplier);

            return new StatBlock
            {
                max_health = effectiveMaxHealth,
                health_regen = runtimeHealthRegen,
                max_stamina = runtimeMaxStamina,
                stamina_regen = effectiveStaminaRegen,
                speed = Mathf.Max(0f, effectiveSpeed),
                damage = Mathf.Max(0f, effectiveDamage),
                crit_chance = CombatBalanceCaps.ClampCritChance(effectiveCritChance),
                crit_multiplier = effectiveCritMultiplier,
                life_steal = Mathf.Clamp01(Mathf.Max(0f, effectiveLifeSteal)),
                swing_speed = Mathf.Max(0.05f, effectiveSwingSpeed),
                damage_reduction = Mathf.Clamp01(Mathf.Max(0f, effectiveDamageReduction)),
                dodge_chance = Mathf.Clamp01(Mathf.Max(0f, effectiveDodgeChance))
            };
        }

        private static StatBlock BuildStatBlockFromBase(PlayerStatsData baseStats)
        {
            if (baseStats == null)
                return null;

            return new StatBlock
            {
                max_health = baseStats.maxHealth,
                health_regen = baseStats.healthRegen,
                max_stamina = baseStats.maxStamina,
                stamina_regen = baseStats.staminaRegen,
                speed = baseStats.speed,
                damage = baseStats.damage,
                crit_chance = baseStats.critChance,
                crit_multiplier = baseStats.critMultiplier,
                life_steal = baseStats.lifeSteal,
                swing_speed = baseStats.swingSpeed,
                damage_reduction = baseStats.damageReduction,
                dodge_chance = baseStats.dodgeChance
            };
        }

        private static StatBlock BuildStatBlockFromRuntime(RuntimeStats runtime)
        {
            if (runtime == null)
                return null;

            return new StatBlock
            {
                max_health = runtime.maxHealth,
                health_regen = runtime.healthRegen,
                max_stamina = runtime.maxStamina,
                stamina_regen = runtime.staminaRegen,
                speed = runtime.speed,
                damage = runtime.damage,
                crit_chance = runtime.critChance,
                crit_multiplier = runtime.critMultiplier,
                life_steal = runtime.lifeSteal,
                swing_speed = runtime.swingSpeed,
                damage_reduction = runtime.damageReduction,
                dodge_chance = runtime.dodgeChance
            };
        }

        private RelicStackData[] CollectRelicStacks()
        {
            if (relics == null || relics.Relics == null || relics.Relics.Count == 0)
                return Array.Empty<RelicStackData>();

            relicBuffer.Clear();

            foreach (KeyValuePair<string, RelicDefinition> kv in relics.Relics)
            {
                string id = kv.Key;
                RelicDefinition def = kv.Value;
                if (def == null)
                    continue;

                int stackCount = relics.GetStacks(id);
                RelicEffect effect = def.effect;

                float damageMultiplier = 1f;
                if (effect is IDamageModifier damageModifier)
                    damageMultiplier = damageModifier.GetDamageMultiplier(relics, stackCount);

                float critChanceBonus = effect is ICritChanceModifier critChanceModifier
                    ? critChanceModifier.GetCritChanceBonus(relics, stackCount)
                    : 0f;

                float critMultiplierBonus = effect is ICritMultiplierModifier critMultiplierModifier
                    ? critMultiplierModifier.GetCritMultiplierBonus(relics, stackCount)
                    : 0f;

                float lifeStealBonus = effect is ILifeStealModifier lifeStealModifier
                    ? lifeStealModifier.GetLifeStealBonus(relics, stackCount)
                    : 0f;

                float swingSpeedBonus = effect is ISwingSpeedModifier swingSpeedModifier
                    ? swingSpeedModifier.GetSwingSpeedBonus(relics, stackCount)
                    : 0f;

                float speedBonus = effect is ISpeedModifier speedModifier
                    ? speedModifier.GetSpeedBonus(relics, stackCount)
                    : 0f;

                float maxHealthBonus = effect is IMaxHealthModifier maxHealthModifier
                    ? maxHealthModifier.GetMaxHealthBonus(relics, stackCount)
                    : 0f;

                float staminaRegenBonus = effect is IStaminaRegenModifier staminaRegenModifier
                    ? staminaRegenModifier.GetStaminaRegenBonus(relics, stackCount)
                    : 0f;

                float damageReductionBonus = effect is IDamageReductionModifier damageReductionModifier
                    ? damageReductionModifier.GetDamageReductionBonus(relics, stackCount)
                    : 0f;

                float dodgeChanceBonus = effect is IDodgeChanceModifier dodgeChanceModifier
                    ? dodgeChanceModifier.GetDodgeChanceBonus(relics, stackCount)
                    : 0f;

                float swordLengthBonus = effect is ISwordLengthModifier swordLengthModifier
                    ? swordLengthModifier.GetSwordLengthBonus(relics, stackCount)
                    : 0f;

                float staminaSwingOverride = effect is IStaminaSwingOverrideModifier staminaSwingModifier
                    ? staminaSwingModifier.GetStaminaSwingMultiplierOverride(relics, stackCount)
                    : 0f;

                float expMultiplier = effect is IExpRewardModifier expModifier
                    ? expModifier.GetExpGainMultiplier(relics, stackCount)
                    : 1f;

                RelicProcAggregate procAggregate = GetRelicProcAggregate(id);
                int procCount = procAggregate != null ? procAggregate.procCount : 0;
                float procTotalDamage = procAggregate != null ? procAggregate.totalDamage : 0f;

                relicBuffer.Add(new RelicStackData
                {
                    id = id,
                    display_name = string.IsNullOrWhiteSpace(def.displayName) ? def.name : def.displayName,
                    rarity = def.rarity.ToString(),
                    stacks = stackCount,
                    max_stacks = relics.GetEffectiveMaxStacks(def),
                    stackable = def.stackable,
                    damage_multiplier = damageMultiplier,
                    crit_chance_bonus = critChanceBonus,
                    crit_multiplier_bonus = critMultiplierBonus,
                    life_steal_bonus = lifeStealBonus,
                    swing_speed_bonus = swingSpeedBonus,
                    speed_bonus = speedBonus,
                    max_health_bonus = maxHealthBonus,
                    stamina_regen_bonus = staminaRegenBonus,
                    damage_reduction_bonus = damageReductionBonus,
                    dodge_chance_bonus = dodgeChanceBonus,
                    sword_length_bonus = swordLengthBonus,
                    stamina_swing_override = staminaSwingOverride,
                    exp_gain_multiplier = expMultiplier,
                    proc_count = procCount,
                    proc_kills = procAggregate != null ? procAggregate.kills : 0,
                    proc_total_damage = procTotalDamage,
                    proc_avg_damage = procCount > 0 ? procTotalDamage / procCount : 0f,
                    proc_peak_damage = procAggregate != null ? procAggregate.peakDamage : 0f,
                    proc_last_time_s = procAggregate != null ? procAggregate.lastProcRunTime : 0f
                });
            }

            relicBuffer.Sort((a, b) => string.CompareOrdinal(a.id, b.id));
            return relicBuffer.ToArray();
        }

        private EnhancerSnapshot[] CollectEnhancerSnapshots()
        {
            if (enhancerSystem == null || enhancerSystem.Active == null || enhancerSystem.Active.Count == 0)
                return Array.Empty<EnhancerSnapshot>();

            enhancerBuffer.Clear();

            for (int i = 0; i < enhancerSystem.Active.Count; i++)
            {
                ActiveEnhancer activeEnhancer = enhancerSystem.Active[i];
                if (activeEnhancer == null)
                    continue;

                EnhancerDefinition def = activeEnhancer.Definition;
                enhancerBuffer.Add(new EnhancerSnapshot
                {
                    enhancer_id = def != null ? def.enhancerId : "unknown",
                    stacks = activeEnhancer.Stacks,
                    max_stacks = def != null ? Mathf.Max(1, def.maxStacks) : activeEnhancer.Stacks,
                    remaining_duration_s = Mathf.Max(0f, activeEnhancer.RemainingDuration),
                    total_duration_s = Mathf.Max(0f, activeEnhancer.TotalDuration),
                    strength_01 = Mathf.Clamp01(activeEnhancer.GetStrength01()),
                    contributions = BuildEnhancerContributions(activeEnhancer)
                });
            }

            enhancerBuffer.Sort((a, b) => string.CompareOrdinal(a.enhancer_id, b.enhancer_id));
            return enhancerBuffer.ToArray();
        }

        private static WorldSnapshot CollectWorldSnapshot()
        {
            int difficulty = 1;
            int enemiesSpawned = 0;
            int enemiesKilled = 0;

            if (WorldStats.Instance != null)
            {
                difficulty = Mathf.Max(1, WorldStats.Instance.difficulty);
                enemiesSpawned = Mathf.Max(0, WorldStats.Instance.enemiesSpawned);
                enemiesKilled = Mathf.Max(0, WorldStats.Instance.enemiesKilled);
            }

            int activeCount = EnemyActivationController.Instance != null
                ? EnemyActivationController.Instance.ActiveCount
                : 0;
            int simulatedCount = EnemySimulationManager.Instance != null
                ? EnemySimulationManager.Instance.SimulatedCount
                : 0;

            HordeAISystem.RuntimeSnapshot hordeSnapshot = default;
            HordeAISystem.TryGetRuntimeSnapshot(out hordeSnapshot);

            return new WorldSnapshot
            {
                difficulty = difficulty,
                enemies_spawned = enemiesSpawned,
                enemies_killed = enemiesKilled,
                active_enemy_count = Mathf.Max(0, activeCount),
                simulated_enemy_count = Mathf.Max(0, simulatedCount),
                horde_agent_count = Mathf.Max(0, hordeSnapshot.agentCount),
                horde_zombie_count = Mathf.Max(0, hordeSnapshot.zombieCount),
                horde_enemy_count = Mathf.Max(0, hordeSnapshot.enemyCount)
            };
        }

        private static DifficultySnapshot CollectDifficultySnapshot()
        {
            return new DifficultySnapshot
            {
                enemy_health_multiplier = DifficultyContext.EnemyHealthMultiplier,
                enemy_damage_multiplier = DifficultyContext.EnemyDamageMultiplier,
                enemy_attack_speed_multiplier = DifficultyContext.EnemyAttackSpeedMultiplier,
                enemy_move_speed_multiplier = DifficultyContext.EnemyMoveSpeedMultiplier,
                enemy_detection_multiplier = DifficultyContext.EnemyDetectionRangeMultiplier,
                exp_multiplier = DifficultyContext.ExpMultiplier,
                spawn_cap_multiplier_base_100 = DifficultyContext.ScaleSpawnCap(100) / 100f
            };
        }

        private EnemyPressureSnapshot CollectEnemyPressureSnapshot()
        {
            EnemyCombatant[] enemies = FindObjectsByType<EnemyCombatant>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None
            );

            int count = 0;
            int bossCount = 0;
            float totalMaxHealth = 0f;
            float totalCurrentHealth = 0f;
            float totalDamage = 0f;
            float totalAttackRate = 0f;
            float totalThreat = 0f;
            float maxThreat = 0f;

            for (int i = 0; i < enemies.Length; i++)
            {
                EnemyCombatant enemy = enemies[i];
                if (enemy == null || enemy.stats == null)
                    continue;

                Combatant combatant = enemy.GetComponent<Combatant>();
                if (combatant == null || combatant.IsDead)
                    continue;

                bool isBoss = enemy.GetComponent<BossEnemyController>() != null;
                float maxHealth = Mathf.Max(1f, enemy.stats.maxHealth);
                float currentHealth = Mathf.Clamp(combatant.CurrentHealth, 0f, maxHealth);
                float attackDamage = Mathf.Max(0f, enemy.stats.damage);
                float attackCooldown = Mathf.Max(0.01f, enemy.stats.attackCooldown);
                float attackRate = 1f / attackCooldown;
                float threat = ComputeThreatScore(maxHealth, attackDamage, attackCooldown, isBoss);

                count++;
                if (isBoss)
                    bossCount++;

                totalMaxHealth += maxHealth;
                totalCurrentHealth += currentHealth;
                totalDamage += attackDamage;
                totalAttackRate += attackRate;
                totalThreat += threat;
                maxThreat = Mathf.Max(maxThreat, threat);
            }

            if (count <= 0)
            {
                return new EnemyPressureSnapshot
                {
                    active_enemy_count = 0,
                    active_boss_count = 0,
                    total_enemy_max_health = 0f,
                    total_enemy_current_health = 0f,
                    avg_enemy_max_health = 0f,
                    avg_enemy_damage = 0f,
                    avg_enemy_attack_rate = 0f,
                    avg_threat_score = 0f,
                    max_threat_score = 0f
                };
            }

            return new EnemyPressureSnapshot
            {
                active_enemy_count = count,
                active_boss_count = bossCount,
                total_enemy_max_health = totalMaxHealth,
                total_enemy_current_health = totalCurrentHealth,
                avg_enemy_max_health = totalMaxHealth / count,
                avg_enemy_damage = totalDamage / count,
                avg_enemy_attack_rate = totalAttackRate / count,
                avg_threat_score = totalThreat / count,
                max_threat_score = maxThreat
            };
        }

        private RunCounters CollectRunCounters()
        {
            float averageHitsToKill = meleeKills > 0
                ? totalHitsToKill / meleeKills
                : 0f;

            float warningLowHpSeconds = lowHpWarningTotalSeconds;
            if (lowHpWarningActive && lowHpWarningEnteredAt >= 0f)
                warningLowHpSeconds += Mathf.Max(0f, GetRunTimeSeconds() - lowHpWarningEnteredAt);

            float criticalLowHpSeconds = lowHpCriticalTotalSeconds;
            if (lowHpCriticalActive && lowHpCriticalEnteredAt >= 0f)
                criticalLowHpSeconds += Mathf.Max(0f, GetRunTimeSeconds() - lowHpCriticalEnteredAt);

            return new RunCounters
            {
                upgrade_rolls = upgradeRolls,
                upgrades_applied = upgradesApplied,
                relics_applied = relicsApplied,
                enhancers_applied = enhancersApplied,
                melee_hits = meleeHits,
                melee_crit_hits = meleeCritHits,
                melee_kills = meleeKills,
                total_melee_damage = totalMeleeDamage,
                total_hits_to_kill = totalHitsToKill,
                average_hits_to_kill = averageHitsToKill,
                incoming_damage_events = incomingDamageEvents,
                incoming_damage_dodged = incomingDamageDodged,
                incoming_damage_blocked = incomingDamageBlocked,
                total_incoming_raw_damage = totalIncomingRawDamage,
                total_incoming_final_damage = totalIncomingFinalDamage,
                total_incoming_barrier_absorbed = totalIncomingBarrierAbsorbed,
                total_incoming_chained_hit_reduction = totalIncomingChainedHitReduction,
                relic_rolls = relicRolls,
                relic_roll_rejections = relicRollRejections,
                choice_queue_events = choiceQueueEvents,
                relic_proc_events = relicProcEvents,
                relic_proc_kills = relicProcKills,
                relic_proc_total_damage = totalRelicProcDamage,
                lifesteal_events = lifeStealEvents,
                lifesteal_raw_heal = totalLifeStealRaw,
                lifesteal_per_hit_capped_heal = totalLifeStealPerHitCapped,
                lifesteal_per_second_capped_heal = totalLifeStealPerSecondCapped,
                lifesteal_applied_heal = totalLifeStealApplied,
                lifesteal_overheal = totalLifeStealOverheal,
                low_hp_warning_entries = lowHpWarningEntries,
                low_hp_critical_entries = lowHpCriticalEntries,
                time_below_warning_s = warningLowHpSeconds,
                time_below_critical_s = criticalLowHpSeconds
            };
        }

        private EnemyHitSummary[] BuildEnemyHitSummaryArray()
        {
            enemyHitSummaryBuffer.Clear();

            foreach (KeyValuePair<string, EnemyHitAggregate> kv in enemyHitAggregates)
            {
                EnemyHitAggregate aggregate = kv.Value;
                if (aggregate == null)
                    continue;

                enemyHitSummaryBuffer.Add(new EnemyHitSummary
                {
                    enemy_type = kv.Key,
                    hits = aggregate.hits,
                    kills = aggregate.kills,
                    total_damage = aggregate.totalDamage,
                    average_hits_per_kill = aggregate.kills > 0
                        ? aggregate.totalHitsToKill / aggregate.kills
                        : 0f,
                    average_damage_per_kill = aggregate.kills > 0
                        ? aggregate.totalDamageToKill / aggregate.kills
                        : 0f,
                    p50_hits_per_kill = ComputePercentileHits(aggregate.hitsToKillSamples, 0.5f),
                    p90_hits_per_kill = ComputePercentileHits(aggregate.hitsToKillSamples, 0.9f),
                    max_threat_seen = aggregate.maxThreatSeen
                });
            }

            enemyHitSummaryBuffer.Sort((a, b) =>
            {
                int byHits = b.hits.CompareTo(a.hits);
                if (byHits != 0)
                    return byHits;
                return string.CompareOrdinal(a.enemy_type, b.enemy_type);
            });

            return enemyHitSummaryBuffer.ToArray();
        }

        private UpgradeContributionData[] CollectUpgradeContributionArray()
        {
            if (upgradeContributions.Count == 0)
                return Array.Empty<UpgradeContributionData>();

            upgradeContributionBuffer.Clear();
            foreach (KeyValuePair<StatType, float> kv in upgradeContributions)
            {
                upgradeContributionBuffer.Add(new UpgradeContributionData
                {
                    stat = kv.Key.ToString(),
                    total_value = kv.Value
                });
            }

            upgradeContributionBuffer.Sort((a, b) => string.CompareOrdinal(a.stat, b.stat));
            return upgradeContributionBuffer.ToArray();
        }

        private CombatContext CollectCombatContext()
        {
            if (player == null)
                return lastValidCombatContext;

            WorldSnapshot worldSnapshot = CaptureWorldSnapshot();
            int level = player.xp != null ? player.xp.level : 0;
            float maxHealth = Mathf.Max(1f, player.MaxHealth);
            RuntimeStats runtime = player.stats;

            CombatContext context = new CombatContext
            {
                player_level = level,
                player_current_health = player.CurrentHealth,
                player_max_health = maxHealth,
                player_health_ratio = Mathf.Clamp01(player.CurrentHealth / maxHealth),
                player_barrier = player.CurrentBarrier,
                player_damage = runtime != null ? runtime.damage : 0f,
                player_crit_chance = weapon != null ? weapon.GetCritChance() : 0f,
                player_life_steal = weapon != null ? weapon.GetLifeSteal() : 0f,
                difficulty = worldSnapshot != null ? worldSnapshot.difficulty : 1,
                active_enemy_count = worldSnapshot != null ? worldSnapshot.active_enemy_count : 0,
                simulated_enemy_count = worldSnapshot != null ? worldSnapshot.simulated_enemy_count : 0
            };

            if (IsCombatContextValid(context))
                lastValidCombatContext = context;

            return context;
        }

        private EnhancerStatContribution[] BuildEnhancerContributions(ActiveEnhancer activeEnhancer)
        {
            if (activeEnhancer == null || activeEnhancer.Definition == null || activeEnhancer.Definition.statEffects == null)
                return Array.Empty<EnhancerStatContribution>();

            List<EnhancerStatContribution> contributions = new(activeEnhancer.Definition.statEffects.Count);
            float strength = Mathf.Clamp01(activeEnhancer.GetStrength01());
            float timePower = GetEnhancerTimePowerMultiplier();

            for (int i = 0; i < activeEnhancer.Definition.statEffects.Count; i++)
            {
                EnhancerStatEffect effect = activeEnhancer.Definition.statEffects[i];
                float scaledBonus = effect.maxBonus * strength * timePower;

                float additive = 0f;
                float multiplicative = 1f;
                switch (effect.mathMode)
                {
                    case EnhancerMathMode.Additive:
                        additive = scaledBonus;
                        break;
                    case EnhancerMathMode.Multiplicative:
                        if (UsesZeroBaseAdditiveFallback(effect.stat))
                            additive = scaledBonus;
                        else
                            multiplicative = 1f + scaledBonus;
                        break;
                    case EnhancerMathMode.AdditiveThenMultiplicative:
                        additive = scaledBonus;
                        multiplicative = 1f + scaledBonus;
                        break;
                }

                contributions.Add(new EnhancerStatContribution
                {
                    stat = effect.stat.ToString(),
                    math_mode = effect.mathMode.ToString(),
                    max_bonus = effect.maxBonus,
                    scaled_bonus = scaledBonus,
                    additive_component = additive,
                    multiplicative_component = multiplicative
                });
            }

            return contributions.ToArray();
        }

        private float GetEnhancerTimePowerMultiplier()
        {
            if (enhancerSystem == null)
                return 1f;

            float start = Mathf.Max(0f, enhancerSystem.latePowerStartSeconds);
            float full = Mathf.Max(start + 0.01f, enhancerSystem.latePowerFullSeconds);
            float now = GetRunTimeSeconds();
            float t = Mathf.Clamp01((now - start) / (full - start));
            return Mathf.Lerp(enhancerSystem.earlyPowerMultiplier, enhancerSystem.latePowerMultiplier, t);
        }

        private static bool UsesZeroBaseAdditiveFallback(StatType stat)
        {
            return stat == StatType.CritChance
                || stat == StatType.LifeSteal
                || stat == StatType.DodgeChance
                || stat == StatType.DamageReduction;
        }

        private static RelicRollOptionData[] ConvertRelicRollOffered(GameplayTelemetryHub.RelicOptionSample[] offered)
        {
            if (offered == null || offered.Length == 0)
                return Array.Empty<RelicRollOptionData>();

            RelicRollOptionData[] converted = new RelicRollOptionData[offered.Length];
            for (int i = 0; i < offered.Length; i++)
            {
                GameplayTelemetryHub.RelicOptionSample option = offered[i];
                converted[i] = new RelicRollOptionData
                {
                    id = option.id,
                    display_name = option.displayName,
                    rarity = option.rarity,
                    current_stacks = option.currentStacks,
                    max_stacks = option.maxStacks
                };
            }

            return converted;
        }

        private static RejectedRelicRollOptionData[] ConvertRelicRollRejected(GameplayTelemetryHub.RejectedRelicOptionSample[] rejected)
        {
            if (rejected == null || rejected.Length == 0)
                return Array.Empty<RejectedRelicRollOptionData>();

            RejectedRelicRollOptionData[] converted = new RejectedRelicRollOptionData[rejected.Length];
            for (int i = 0; i < rejected.Length; i++)
            {
                GameplayTelemetryHub.RejectedRelicOptionSample option = rejected[i];
                converted[i] = new RejectedRelicRollOptionData
                {
                    id = option.id,
                    display_name = option.displayName,
                    rarity = option.rarity,
                    reason = option.reason,
                    current_stacks = option.currentStacks,
                    max_stacks = option.maxStacks
                };
            }

            return converted;
        }

        private static float ComputePercentileHits(List<int> samples, float percentile)
        {
            if (samples == null || samples.Count == 0)
                return 0f;

            samples.Sort();
            float clamped = Mathf.Clamp01(percentile);
            float index = (samples.Count - 1) * clamped;
            int lowerIndex = Mathf.FloorToInt(index);
            int upperIndex = Mathf.CeilToInt(index);
            if (lowerIndex == upperIndex)
                return samples[lowerIndex];

            float t = index - lowerIndex;
            return Mathf.Lerp(samples[lowerIndex], samples[upperIndex], t);
        }

        private void UpdateLowHealthState(float runTime, bool flushOnly)
        {
            if (player == null)
                return;

            float maxHealth = Mathf.Max(1f, player.MaxHealth);
            float health = Mathf.Clamp(player.CurrentHealth, 0f, maxHealth);
            float healthRatio = Mathf.Clamp01(health / maxHealth);

            ProcessLowHealthThreshold(
                WarningLowHealthThreshold,
                ref lowHpWarningActive,
                ref lowHpWarningEnteredAt,
                ref lowHpWarningTotalSeconds,
                ref lowHpWarningEntries,
                health,
                maxHealth,
                healthRatio,
                runTime,
                flushOnly
            );

            ProcessLowHealthThreshold(
                CriticalLowHealthThreshold,
                ref lowHpCriticalActive,
                ref lowHpCriticalEnteredAt,
                ref lowHpCriticalTotalSeconds,
                ref lowHpCriticalEntries,
                health,
                maxHealth,
                healthRatio,
                runTime,
                flushOnly
            );
        }

        private void ProcessLowHealthThreshold(
            float threshold,
            ref bool active,
            ref float enteredAt,
            ref float totalSeconds,
            ref int entries,
            float currentHealth,
            float maxHealth,
            float healthRatio,
            float runTime,
            bool flushOnly
        )
        {
            bool underThreshold = healthRatio <= threshold;

            if (!flushOnly && underThreshold && !active)
            {
                active = true;
                enteredAt = runTime;
                entries++;
                EmitLowHealthTransition(threshold, entered: true, currentHealth, maxHealth, healthRatio, 0f, totalSeconds, runTime);
                return;
            }

            if (!active)
                return;

            if (flushOnly || !underThreshold)
            {
                float elapsed = enteredAt >= 0f ? Mathf.Max(0f, runTime - enteredAt) : 0f;
                totalSeconds += elapsed;
                EmitLowHealthTransition(
                    threshold,
                    entered: false,
                    currentHealth,
                    maxHealth,
                    healthRatio,
                    elapsed,
                    totalSeconds,
                    runTime
                );
                active = false;
                enteredAt = -1f;
            }
        }

        private void EmitLowHealthTransition(
            float threshold,
            bool entered,
            float currentHealth,
            float maxHealth,
            float healthRatio,
            float secondsSinceEnter,
            float totalSecondsUnder,
            float runTime
        )
        {
            if (!runActive)
                return;

            TelemetryRecord record = CreateRecord("low_health_state");
            record.run_time_s = runTime;
            record.low_health = new LowHealthData
            {
                threshold_ratio = threshold,
                entered = entered,
                current_health = currentHealth,
                max_health = maxHealth,
                health_ratio = healthRatio,
                seconds_since_enter = secondsSinceEnter,
                total_seconds_under_threshold = totalSecondsUnder
            };
            record.counters = CollectRunCounters();
            EnqueueRecord(record);
        }

        private void CleanupTargetHitTrackers(float runTime)
        {
            targetTrackerIdsToRemove.Clear();

            foreach (KeyValuePair<int, TargetHitTracker> kv in targetHitTrackers)
            {
                TargetHitTracker tracker = kv.Value;
                if (tracker == null)
                {
                    targetTrackerIdsToRemove.Add(kv.Key);
                    continue;
                }

                bool invalidTarget = tracker.target == null || tracker.target.IsDead;
                bool stale = runTime - tracker.lastHitRunTime > TargetTrackerMaxIdleSeconds;
                if (invalidTarget || stale)
                    targetTrackerIdsToRemove.Add(kv.Key);
            }

            for (int i = 0; i < targetTrackerIdsToRemove.Count; i++)
                targetHitTrackers.Remove(targetTrackerIdsToRemove[i]);
        }

        private TargetHitTracker GetOrCreateTargetTracker(EnemyRuntimeInfo enemyInfo, Combatant target, float runTime)
        {
            if (!targetHitTrackers.TryGetValue(enemyInfo.instanceId, out TargetHitTracker tracker) || tracker == null)
            {
                tracker = new TargetHitTracker
                {
                    target = target,
                    enemyType = enemyInfo.enemyType,
                    enemyName = enemyInfo.enemyName,
                    isBoss = enemyInfo.isBoss,
                    hits = 0,
                    totalDamage = 0f,
                    firstHitRunTime = runTime,
                    lastHitRunTime = runTime,
                    maxThreatSeen = enemyInfo.threatScore
                };

                targetHitTrackers[enemyInfo.instanceId] = tracker;
            }

            return tracker;
        }

        private EnemyHitAggregate GetOrCreateEnemyAggregate(string enemyType)
        {
            if (string.IsNullOrWhiteSpace(enemyType))
                enemyType = "Enemy/Unknown";

            if (!enemyHitAggregates.TryGetValue(enemyType, out EnemyHitAggregate aggregate) || aggregate == null)
            {
                aggregate = new EnemyHitAggregate();
                enemyHitAggregates[enemyType] = aggregate;
            }

            return aggregate;
        }

        private RelicProcAggregate GetOrCreateRelicProcAggregate(string relicId)
        {
            string key = NormalizeRelicTelemetryId(relicId);
            if (!relicProcAggregates.TryGetValue(key, out RelicProcAggregate aggregate) || aggregate == null)
            {
                aggregate = new RelicProcAggregate();
                relicProcAggregates[key] = aggregate;
            }

            return aggregate;
        }

        private RelicProcAggregate GetRelicProcAggregate(string relicId)
        {
            string key = NormalizeRelicTelemetryId(relicId);
            if (!relicProcAggregates.TryGetValue(key, out RelicProcAggregate aggregate))
                return null;

            return aggregate;
        }

        private string ResolveRelicProcId(string relicId, string displayName)
        {
            if (!string.IsNullOrWhiteSpace(relicId))
                return NormalizeRelicTelemetryId(relicId);

            if (relics != null && !string.IsNullOrWhiteSpace(displayName) && relics.Relics != null)
            {
                foreach (KeyValuePair<string, RelicDefinition> kv in relics.Relics)
                {
                    RelicDefinition def = kv.Value;
                    if (def == null)
                        continue;

                    string candidateName = string.IsNullOrWhiteSpace(def.displayName) ? def.name : def.displayName;
                    if (string.Equals(candidateName, displayName, StringComparison.OrdinalIgnoreCase))
                        return NormalizeRelicTelemetryId(kv.Key);
                }
            }

            return NormalizeRelicTelemetryId(displayName);
        }

        private static UpgradeOptionData[] ConvertUpgradeOptions(List<UpgradeOption> options)
        {
            if (options == null || options.Count == 0)
                return Array.Empty<UpgradeOptionData>();

            List<UpgradeOptionData> converted = new(options.Count);
            for (int i = 0; i < options.Count; i++)
            {
                UpgradeOption option = options[i];
                if (option == null)
                    continue;

                converted.Add(ConvertUpgradeOption(option));
            }

            return converted.ToArray();
        }

        private static UpgradeOptionData ConvertUpgradeOption(UpgradeOption option)
        {
            if (option == null)
                return null;

            return new UpgradeOptionData
            {
                stat = option.stat.ToString(),
                value = option.value,
                rarity = option.rarity.ToString(),
                display_name = option.displayName,
                description = option.description
            };
        }

        private static EnemyRuntimeInfo ResolveEnemyRuntimeInfo(Combatant target)
        {
            int instanceId = target.GetInstanceID();
            string enemyName = NormalizeName(target.gameObject.name);
            string enemyType = "Enemy/Unknown";
            bool isBoss = false;
            float currentHealth = Mathf.Max(0f, target.CurrentHealth);
            float maxHealth = Mathf.Max(1f, target.MaxHealth);
            float attackDamage = 0f;
            float attackCooldown = 0f;

            EnemyCombatant enemyCombatant = target.GetComponent<EnemyCombatant>();
            if (enemyCombatant == null)
                enemyCombatant = target.GetComponentInParent<EnemyCombatant>();

            BossEnemyController boss = target.GetComponent<BossEnemyController>();
            if (boss == null)
                boss = target.GetComponentInParent<BossEnemyController>();

            isBoss = boss != null;

            if (enemyCombatant != null && enemyCombatant.stats != null)
            {
                EnemyStatsData stats = enemyCombatant.stats;
                attackDamage = Mathf.Max(0f, stats.damage);
                attackCooldown = Mathf.Max(0.01f, stats.attackCooldown);
                maxHealth = Mathf.Max(1f, stats.maxHealth);

                string statsName = NormalizeName(stats.name);
                enemyType = string.IsNullOrWhiteSpace(statsName)
                    ? "Enemy/Unknown"
                    : $"Enemy/{statsName}";
            }
            else
            {
                EnemyDamageDealer dealer = target.GetComponentInChildren<EnemyDamageDealer>(true);
                if (dealer != null)
                    attackCooldown = Mathf.Max(0.01f, dealer.hitCooldown);
            }

            if (boss != null)
            {
                string archetype = NormalizeName(boss.ArchetypeLabel);
                if (string.IsNullOrWhiteSpace(archetype))
                    archetype = "Unknown";
                enemyType = $"Boss/{archetype}";
            }

            float threatScore = ComputeThreatScore(maxHealth, attackDamage, attackCooldown, isBoss);
            return new EnemyRuntimeInfo(
                instanceId,
                enemyName,
                enemyType,
                isBoss,
                currentHealth,
                maxHealth,
                attackDamage,
                attackCooldown,
                threatScore
            );
        }

        private static float ComputeThreatScore(float maxHealth, float attackDamage, float attackCooldown, bool isBoss)
        {
            float safeHealth = Mathf.Max(0f, maxHealth);
            float safeDamage = Mathf.Max(0f, attackDamage);
            float safeCooldown = Mathf.Max(0.05f, attackCooldown);
            float attackRate = 1f / safeCooldown;
            float score = (safeHealth * 0.025f) + (safeDamage * attackRate * 6f);
            if (isBoss)
                score *= 1.35f;
            return Mathf.Max(0f, score);
        }

        private static string NormalizeName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "Unknown";

            string normalized = value
                .Replace("(Clone)", string.Empty)
                .Replace("_RuntimeDifficulty", string.Empty)
                .Replace("_BossRuntime", string.Empty)
                .Trim();

            return string.IsNullOrWhiteSpace(normalized) ? "Unknown" : normalized;
        }

        private static string NormalizeRelicTelemetryId(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "unknown";

            return value.Trim();
        }

        private static bool IsPlayerSnapshotValid(PlayerSnapshot snapshot)
        {
            return snapshot != null
                && snapshot.level >= 0
                && snapshot.max_health > 0f;
        }

        private static bool IsWorldSnapshotValid(WorldSnapshot snapshot)
        {
            return snapshot != null && snapshot.difficulty > 0;
        }

        private static bool IsDifficultySnapshotValid(DifficultySnapshot snapshot)
        {
            return snapshot != null;
        }

        private static bool IsEnemyPressureSnapshotValid(EnemyPressureSnapshot snapshot)
        {
            return snapshot != null;
        }

        private static bool IsCombatContextValid(CombatContext context)
        {
            return context != null && context.player_max_health > 0f;
        }

        private static bool IsWorldSnapshotLowSignal(WorldSnapshot snapshot)
        {
            if (snapshot == null)
                return true;

            return snapshot.enemies_spawned <= 0
                && snapshot.enemies_killed <= 0
                && snapshot.active_enemy_count <= 0
                && snapshot.simulated_enemy_count <= 0
                && snapshot.horde_agent_count <= 0
                && snapshot.horde_zombie_count <= 0
                && snapshot.horde_enemy_count <= 0;
        }

        private static bool IsEnemyPressureLowSignal(EnemyPressureSnapshot snapshot)
        {
            if (snapshot == null)
                return true;

            return snapshot.active_enemy_count <= 0
                && snapshot.active_boss_count <= 0
                && snapshot.total_enemy_max_health <= 0f
                && snapshot.total_enemy_current_health <= 0f
                && snapshot.max_threat_score <= 0f;
        }

        private void InitializeLogPaths()
        {
            string fileName = $"gameplay_{runId}.jsonl";
            persistentLogPath = Path.Combine(Application.persistentDataPath, "telemetry", fileName);
            EnsureDirectoryForPath(persistentLogPath);

            projectLogPath = null;
            if (Application.isEditor && MirrorToProjectResultsInEditor)
            {
                projectLogPath = Path.GetFullPath(Path.Combine("results", "telemetry", fileName));
                EnsureDirectoryForPath(projectLogPath);
            }
        }

        private void ResetRunAccumulators()
        {
            upgradeRolls = 0;
            upgradesApplied = 0;
            relicsApplied = 0;
            enhancersApplied = 0;
            meleeHits = 0;
            meleeCritHits = 0;
            meleeKills = 0;
            totalMeleeDamage = 0f;
            totalHitsToKill = 0f;
            incomingDamageEvents = 0;
            incomingDamageDodged = 0;
            incomingDamageBlocked = 0;
            totalIncomingRawDamage = 0f;
            totalIncomingFinalDamage = 0f;
            totalIncomingBarrierAbsorbed = 0f;
            totalIncomingChainedHitReduction = 0f;
            relicRolls = 0;
            relicRollRejections = 0;
            choiceQueueEvents = 0;
            relicProcEvents = 0;
            relicProcKills = 0;
            totalRelicProcDamage = 0f;
            lifeStealEvents = 0;
            totalLifeStealRaw = 0f;
            totalLifeStealPerHitCapped = 0f;
            totalLifeStealPerSecondCapped = 0f;
            totalLifeStealApplied = 0f;
            totalLifeStealOverheal = 0f;
            lowHpWarningEntries = 0;
            lowHpCriticalEntries = 0;
            lowHpWarningTotalSeconds = 0f;
            lowHpCriticalTotalSeconds = 0f;
            lowHpWarningActive = false;
            lowHpCriticalActive = false;
            lowHpWarningEnteredAt = -1f;
            lowHpCriticalEnteredAt = -1f;
            pendingExitReason = null;
            runStartedRealtime = -1f;
            lastReliableRunTime = 0f;
            lastValidPlayerSnapshot = null;
            lastValidWorldSnapshot = null;
            lastValidDifficultySnapshot = null;
            lastValidEnemyPressureSnapshot = null;
            lastValidCombatContext = null;

            targetHitTrackers.Clear();
            enemyHitAggregates.Clear();
            relicProcAggregates.Clear();
            upgradeContributions.Clear();
            targetTrackerIdsToRemove.Clear();
            enemyHitSummaryBuffer.Clear();
            upgradeContributionBuffer.Clear();
            writeBuffer.Clear();
            bufferedLineCount = 0;
        }

        private float GetRunTimeSeconds()
        {
            float runTime = -1f;

            if (timer != null)
                runTime = Mathf.Max(0f, timer.elapsedTime);
            else if (runActive && runStartedRealtime >= 0f)
                runTime = Mathf.Max(0f, Time.realtimeSinceStartup - runStartedRealtime);

            if (runTime >= 0f)
            {
                if (runTime > 0f)
                    lastReliableRunTime = Mathf.Max(lastReliableRunTime, runTime);
                return runTime;
            }

            return Mathf.Max(0f, lastReliableRunTime);
        }

        private static void AppendTextSafe(string path, string payload)
        {
            if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(payload))
                return;

            try
            {
                File.AppendAllText(path, payload);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[GameplayTelemetry] Failed to write '{path}': {ex.Message}");
            }
        }

        private static void EnsureDirectoryForPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

            string directory = Path.GetDirectoryName(path);
            if (string.IsNullOrWhiteSpace(directory))
                return;

            Directory.CreateDirectory(directory);
        }

        private static bool IsPlayerValid(PlayerProgressionController playerRef)
        {
            if (playerRef == null)
                return false;

            GameObject go = playerRef.gameObject;
            return go != null && go.scene.IsValid();
        }

        private static bool IsObjectAlive(UnityEngine.Object obj)
        {
            if (obj == null)
                return false;

            if (obj is MonoBehaviour behaviour)
            {
                GameObject go = behaviour.gameObject;
                return go != null && go.scene.IsValid();
            }

            return true;
        }
    }
}
