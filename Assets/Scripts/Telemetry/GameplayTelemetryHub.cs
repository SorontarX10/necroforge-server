using System;

namespace GrassSim.Telemetry
{
    public static class GameplayTelemetryHub
    {
        public readonly struct RelicOptionSample
        {
            public readonly string id;
            public readonly string displayName;
            public readonly string rarity;
            public readonly int currentStacks;
            public readonly int maxStacks;

            public RelicOptionSample(
                string id,
                string displayName,
                string rarity,
                int currentStacks,
                int maxStacks
            )
            {
                this.id = id;
                this.displayName = displayName;
                this.rarity = rarity;
                this.currentStacks = currentStacks;
                this.maxStacks = maxStacks;
            }
        }

        public readonly struct RejectedRelicOptionSample
        {
            public readonly string id;
            public readonly string displayName;
            public readonly string rarity;
            public readonly string reason;
            public readonly int currentStacks;
            public readonly int maxStacks;

            public RejectedRelicOptionSample(
                string id,
                string displayName,
                string rarity,
                string reason,
                int currentStacks,
                int maxStacks
            )
            {
                this.id = id;
                this.displayName = displayName;
                this.rarity = rarity;
                this.reason = reason;
                this.currentStacks = currentStacks;
                this.maxStacks = maxStacks;
            }
        }

        public readonly struct RelicOptionsRolledSample
        {
            public readonly float runTimeSeconds;
            public readonly string source;
            public readonly RelicOptionSample[] offered;
            public readonly RejectedRelicOptionSample[] rejected;

            public RelicOptionsRolledSample(
                float runTimeSeconds,
                string source,
                RelicOptionSample[] offered,
                RejectedRelicOptionSample[] rejected
            )
            {
                this.runTimeSeconds = runTimeSeconds;
                this.source = source;
                this.offered = offered;
                this.rejected = rejected;
            }
        }

        public readonly struct IncomingDamageSample
        {
            public readonly float runTimeSeconds;
            public readonly float rawDamage;
            public readonly float reduction;
            public readonly float damageAfterReduction;
            public readonly bool dodged;
            public readonly bool blocked;
            public readonly float barrierBefore;
            public readonly float barrierAbsorbed;
            public readonly float barrierAfter;
            public readonly float finalDamage;
            public readonly float healthBefore;
            public readonly float healthAfter;
            public readonly float maxHealth;

            public IncomingDamageSample(
                float runTimeSeconds,
                float rawDamage,
                float reduction,
                float damageAfterReduction,
                bool dodged,
                bool blocked,
                float barrierBefore,
                float barrierAbsorbed,
                float barrierAfter,
                float finalDamage,
                float healthBefore,
                float healthAfter,
                float maxHealth
            )
            {
                this.runTimeSeconds = runTimeSeconds;
                this.rawDamage = rawDamage;
                this.reduction = reduction;
                this.damageAfterReduction = damageAfterReduction;
                this.dodged = dodged;
                this.blocked = blocked;
                this.barrierBefore = barrierBefore;
                this.barrierAbsorbed = barrierAbsorbed;
                this.barrierAfter = barrierAfter;
                this.finalDamage = finalDamage;
                this.healthBefore = healthBefore;
                this.healthAfter = healthAfter;
                this.maxHealth = maxHealth;
            }
        }

        public readonly struct EnemyLifecycleSample
        {
            public readonly float runTimeSeconds;
            public readonly string lifecycle;
            public readonly int simId;
            public readonly int enemyInstanceId;
            public readonly string enemyType;
            public readonly bool isBoss;

            public EnemyLifecycleSample(
                float runTimeSeconds,
                string lifecycle,
                int simId,
                int enemyInstanceId,
                string enemyType,
                bool isBoss
            )
            {
                this.runTimeSeconds = runTimeSeconds;
                this.lifecycle = lifecycle;
                this.simId = simId;
                this.enemyInstanceId = enemyInstanceId;
                this.enemyType = enemyType;
                this.isBoss = isBoss;
            }
        }

        public readonly struct LifeStealAppliedSample
        {
            public readonly float runTimeSeconds;
            public readonly float damageDealt;
            public readonly float lifeStealPercentRequested;
            public readonly float lifeStealPercentEffective;
            public readonly float rawHeal;
            public readonly float perHitCappedHeal;
            public readonly float perSecondCappedHeal;
            public readonly float appliedHeal;
            public readonly float overheal;
            public readonly float lifeStealPerSecondCap;
            public readonly float healthBefore;
            public readonly float healthAfter;
            public readonly float maxHealth;

            public LifeStealAppliedSample(
                float runTimeSeconds,
                float damageDealt,
                float lifeStealPercentRequested,
                float lifeStealPercentEffective,
                float rawHeal,
                float perHitCappedHeal,
                float perSecondCappedHeal,
                float appliedHeal,
                float overheal,
                float lifeStealPerSecondCap,
                float healthBefore,
                float healthAfter,
                float maxHealth
            )
            {
                this.runTimeSeconds = runTimeSeconds;
                this.damageDealt = damageDealt;
                this.lifeStealPercentRequested = lifeStealPercentRequested;
                this.lifeStealPercentEffective = lifeStealPercentEffective;
                this.rawHeal = rawHeal;
                this.perHitCappedHeal = perHitCappedHeal;
                this.perSecondCappedHeal = perSecondCappedHeal;
                this.appliedHeal = appliedHeal;
                this.overheal = overheal;
                this.lifeStealPerSecondCap = lifeStealPerSecondCap;
                this.healthBefore = healthBefore;
                this.healthAfter = healthAfter;
                this.maxHealth = maxHealth;
            }
        }

        public readonly struct ChoiceQueueSample
        {
            public readonly float runTimeSeconds;
            public readonly string source;
            public readonly string action;
            public readonly int pendingCount;
            public readonly bool isShowing;

            public ChoiceQueueSample(
                float runTimeSeconds,
                string source,
                string action,
                int pendingCount,
                bool isShowing
            )
            {
                this.runTimeSeconds = runTimeSeconds;
                this.source = source;
                this.action = action;
                this.pendingCount = pendingCount;
                this.isShowing = isShowing;
            }
        }

        public readonly struct RunExitSample
        {
            public readonly float runTimeSeconds;
            public readonly string reason;

            public RunExitSample(float runTimeSeconds, string reason)
            {
                this.runTimeSeconds = runTimeSeconds;
                this.reason = reason;
            }
        }

        public static event Action<IncomingDamageSample> OnIncomingDamage;
        public static event Action<RelicOptionsRolledSample> OnRelicOptionsRolled;
        public static event Action<EnemyLifecycleSample> OnEnemyLifecycle;
        public static event Action<LifeStealAppliedSample> OnLifeStealApplied;
        public static event Action<ChoiceQueueSample> OnChoiceQueueChanged;
        public static event Action<RunExitSample> OnRunExitRequested;

        public static void ReportIncomingDamage(IncomingDamageSample sample)
        {
            OnIncomingDamage?.Invoke(sample);
        }

        public static void ReportRelicOptionsRolled(RelicOptionsRolledSample sample)
        {
            OnRelicOptionsRolled?.Invoke(sample);
        }

        public static void ReportEnemyLifecycle(EnemyLifecycleSample sample)
        {
            OnEnemyLifecycle?.Invoke(sample);
        }

        public static void ReportLifeStealApplied(LifeStealAppliedSample sample)
        {
            OnLifeStealApplied?.Invoke(sample);
        }

        public static void ReportChoiceQueueChanged(ChoiceQueueSample sample)
        {
            OnChoiceQueueChanged?.Invoke(sample);
        }

        public static void ReportRunExitRequested(RunExitSample sample)
        {
            OnRunExitRequested?.Invoke(sample);
        }
    }
}
