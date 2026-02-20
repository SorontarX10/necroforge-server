using System;

namespace GrassSim.Telemetry
{
    public static class GameplayTelemetryHub
    {
        public readonly struct LifeStealAppliedSample
        {
            public readonly float runTimeSeconds;
            public readonly float damageDealt;
            public readonly float lifeStealPercent;
            public readonly float rawHeal;
            public readonly float cappedHeal;
            public readonly float appliedHeal;
            public readonly float overheal;
            public readonly float healthBefore;
            public readonly float healthAfter;
            public readonly float maxHealth;

            public LifeStealAppliedSample(
                float runTimeSeconds,
                float damageDealt,
                float lifeStealPercent,
                float rawHeal,
                float cappedHeal,
                float appliedHeal,
                float overheal,
                float healthBefore,
                float healthAfter,
                float maxHealth
            )
            {
                this.runTimeSeconds = runTimeSeconds;
                this.damageDealt = damageDealt;
                this.lifeStealPercent = lifeStealPercent;
                this.rawHeal = rawHeal;
                this.cappedHeal = cappedHeal;
                this.appliedHeal = appliedHeal;
                this.overheal = overheal;
                this.healthBefore = healthBefore;
                this.healthAfter = healthAfter;
                this.maxHealth = maxHealth;
            }
        }

        public static event Action<LifeStealAppliedSample> OnLifeStealApplied;

        public static void ReportLifeStealApplied(LifeStealAppliedSample sample)
        {
            OnLifeStealApplied?.Invoke(sample);
        }
    }
}
