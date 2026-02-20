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
        private const bool MirrorToProjectResultsInEditor = true;

        private static GameplayTelemetryRecorder instance;
        private static bool shuttingDown;

        [Serializable]
        private sealed class TelemetryRecord
        {
            public string event_type;
            public string run_id;
            public string utc_timestamp;
            public string scene_name;
            public int frame;
            public float run_time_s;
            public float realtime_since_startup_s;
            public string reason;
            public PlayerSnapshot player;
            public WorldSnapshot world;
            public DifficultySnapshot difficulty;
            public RunCounters counters;
            public UpgradeOptionData[] upgrade_options;
            public UpgradeOptionData upgrade_selected;
            public RelicAppliedData relic_applied;
            public EnhancerAppliedData enhancer_applied;
            public MeleeHitData melee_hit;
            public MeleeKillData melee_kill;
            public LifeStealData lifesteal;
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
            public float current_stamina;
            public float max_stamina;
            public float barrier;
            public StatBlock base_stats;
            public StatBlock runtime_stats;
            public StatBlock effective_stats;
            public RelicStackData[] relics;
            public EnhancerSnapshot[] enhancers;
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
            public int lifesteal_events;
            public float lifesteal_raw_heal;
            public float lifesteal_capped_heal;
            public float lifesteal_applied_heal;
            public float lifesteal_overheal;
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
            public float life_steal_percent;
            public float raw_heal;
            public float capped_heal;
            public float applied_heal;
            public float overheal;
            public float health_before;
            public float health_after;
            public float max_health;
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
        private int lifeStealEvents;
        private float totalLifeStealRaw;
        private float totalLifeStealCapped;
        private float totalLifeStealApplied;
        private float totalLifeStealOverheal;

        private readonly Dictionary<int, TargetHitTracker> targetHitTrackers = new(256);
        private readonly Dictionary<string, EnemyHitAggregate> enemyHitAggregates = new(64);
        private readonly List<int> targetTrackerIdsToRemove = new(128);
        private readonly List<EnemyHitSummary> enemyHitSummaryBuffer = new(64);
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

            if (timer == null)
            {
                reason = "timer_missing";
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
            nextSnapshotAt = GetRunTimeSeconds() + SnapshotIntervalSeconds;
            nextFlushAt = Time.unscaledTime + FlushIntervalSeconds;

            UpdateSubscriptions();

            TelemetryRecord record = CreateRecord("run_started");
            record.player = CollectPlayerSnapshot();
            record.world = CollectWorldSnapshot();
            record.difficulty = CollectDifficultySnapshot();
            record.enemy_pressure = CollectEnemyPressureSnapshot();
            record.enemy_hit_summary = BuildEnemyHitSummaryArray();
            record.counters = CollectRunCounters();
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

            TelemetryRecord record = CreateRecord("run_ended");
            record.reason = string.IsNullOrWhiteSpace(reason) ? "unknown" : reason;
            record.player = CollectPlayerSnapshot();
            record.world = CollectWorldSnapshot();
            record.difficulty = CollectDifficultySnapshot();
            record.enemy_pressure = CollectEnemyPressureSnapshot();
            record.enemy_hit_summary = BuildEnemyHitSummaryArray();
            record.counters = CollectRunCounters();
            EnqueueRecord(record);

            FlushBufferedLines(force: true);
            UnsubscribeAll();
            runActive = false;
            missingPlayerSince = -1f;
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
                GameplayTelemetryHub.OnLifeStealApplied += HandleLifeStealApplied;
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
                GameplayTelemetryHub.OnLifeStealApplied -= HandleLifeStealApplied;
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
            record.player = CollectPlayerSnapshot();
            record.counters = CollectRunCounters();
            EnqueueRecord(record);
        }

        private void HandleUpgradeApplied(UpgradeOption option)
        {
            if (!runActive || option == null)
                return;

            upgradesApplied++;

            TelemetryRecord record = CreateRecord("upgrade_applied");
            record.upgrade_selected = ConvertUpgradeOption(option);
            record.player = CollectPlayerSnapshot();
            record.counters = CollectRunCounters();
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
                max_stacks = Mathf.Max(1, relic.maxStacks),
                stackable = relic.stackable
            };
            record.player = CollectPlayerSnapshot();
            record.counters = CollectRunCounters();
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
            record.player = CollectPlayerSnapshot();
            record.counters = CollectRunCounters();
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
            EnqueueRecord(record);

            targetHitTrackers.Remove(enemyInfo.instanceId);
        }

        private void HandleLifeStealApplied(GameplayTelemetryHub.LifeStealAppliedSample sample)
        {
            if (!runActive)
                return;

            lifeStealEvents++;
            totalLifeStealRaw += Mathf.Max(0f, sample.rawHeal);
            totalLifeStealCapped += Mathf.Max(0f, sample.cappedHeal);
            totalLifeStealApplied += Mathf.Max(0f, sample.appliedHeal);
            totalLifeStealOverheal += Mathf.Max(0f, sample.overheal);

            TelemetryRecord record = CreateRecord("lifesteal_applied");
            record.run_time_s = Mathf.Max(0f, sample.runTimeSeconds);
            record.lifesteal = new LifeStealData
            {
                damage_dealt = sample.damageDealt,
                life_steal_percent = sample.lifeStealPercent,
                raw_heal = sample.rawHeal,
                capped_heal = sample.cappedHeal,
                applied_heal = sample.appliedHeal,
                overheal = sample.overheal,
                health_before = sample.healthBefore,
                health_after = sample.healthAfter,
                max_health = sample.maxHealth
            };
            EnqueueRecord(record);
        }

        private void WritePeriodicSnapshot()
        {
            CleanupTargetHitTrackers(GetRunTimeSeconds());

            TelemetryRecord record = CreateRecord("periodic_snapshot");
            record.player = CollectPlayerSnapshot();
            record.world = CollectWorldSnapshot();
            record.difficulty = CollectDifficultySnapshot();
            record.enemy_pressure = CollectEnemyPressureSnapshot();
            record.enemy_hit_summary = BuildEnemyHitSummaryArray();
            record.counters = CollectRunCounters();
            EnqueueRecord(record);
        }

        private TelemetryRecord CreateRecord(string eventType)
        {
            return new TelemetryRecord
            {
                event_type = eventType,
                run_id = runId,
                utc_timestamp = DateTime.UtcNow.ToString("O"),
                scene_name = SceneManager.GetActiveScene().name,
                frame = Time.frameCount,
                run_time_s = GetRunTimeSeconds(),
                realtime_since_startup_s = Time.realtimeSinceStartup
            };
        }

        private void EnqueueRecord(TelemetryRecord record)
        {
            if (record == null || string.IsNullOrWhiteSpace(persistentLogPath))
                return;

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
                current_stamina = player.CurrentStamina,
                max_stamina = player.MaxStamina,
                barrier = player.CurrentBarrier,
                base_stats = BuildStatBlockFromBase(baseStats),
                runtime_stats = BuildStatBlockFromRuntime(runtime),
                effective_stats = BuildEffectiveStatBlock(runtime),
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
            float effectiveCritChance = Mathf.Clamp01(runtimeCritChance + relicCritChanceBonus);
            float effectiveCritMultiplier = Mathf.Max(1f, runtimeCritMultiplier + relicCritMultiplierBonus);
            float effectiveLifeSteal = Mathf.Clamp01(runtimeLifeSteal + relicLifeStealBonus);
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

            return new StatBlock
            {
                max_health = effectiveMaxHealth,
                health_regen = runtimeHealthRegen,
                max_stamina = runtimeMaxStamina,
                stamina_regen = effectiveStaminaRegen,
                speed = Mathf.Max(0f, effectiveSpeed),
                damage = Mathf.Max(0f, effectiveDamage),
                crit_chance = Mathf.Clamp01(effectiveCritChance),
                crit_multiplier = Mathf.Max(1f, effectiveCritMultiplier),
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

                relicBuffer.Add(new RelicStackData
                {
                    id = id,
                    display_name = string.IsNullOrWhiteSpace(def.displayName) ? def.name : def.displayName,
                    rarity = def.rarity.ToString(),
                    stacks = relics.GetStacks(id),
                    max_stacks = Mathf.Max(1, def.maxStacks),
                    stackable = def.stackable
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
                    strength_01 = Mathf.Clamp01(activeEnhancer.GetStrength01())
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
                lifesteal_events = lifeStealEvents,
                lifesteal_raw_heal = totalLifeStealRaw,
                lifesteal_capped_heal = totalLifeStealCapped,
                lifesteal_applied_heal = totalLifeStealApplied,
                lifesteal_overheal = totalLifeStealOverheal
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
            lifeStealEvents = 0;
            totalLifeStealRaw = 0f;
            totalLifeStealCapped = 0f;
            totalLifeStealApplied = 0f;
            totalLifeStealOverheal = 0f;

            targetHitTrackers.Clear();
            enemyHitAggregates.Clear();
            targetTrackerIdsToRemove.Clear();
            enemyHitSummaryBuffer.Clear();
            writeBuffer.Clear();
            bufferedLineCount = 0;
        }

        private float GetRunTimeSeconds()
        {
            if (timer != null)
                return Mathf.Max(0f, timer.elapsedTime);

            return 0f;
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
