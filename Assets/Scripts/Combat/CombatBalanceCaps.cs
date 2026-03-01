using UnityEngine;

namespace GrassSim.Combat
{
    public static class CombatBalanceCaps
    {
        public const float MaxDodgeChance = 0.80f;
        public const float MaxDamageReduction = 0.85f;
        public const float CritSoftCapStart = 0.85f;
        public const float CritSoftCapStrength = 0.35f;
        public const float CritHardCap = 0.90f;
        public const float MaxCritMultiplier = 4f;

        public const float LifeStealDiminishingThreshold = 0.08f;
        public const float LifeStealDiminishingSlope = 0.35f;
        public const float MaxLifeStealHealPerHitPctOfMaxHealth = 0.1f;
        public const float MaxLifeStealHealPerSecondPctOfMaxHealth = 0.07f;

        public static float ClampDodgeChance(float value)
        {
            return Mathf.Clamp(value, 0f, MaxDodgeChance);
        }

        public static float ClampDamageReduction(float value)
        {
            return Mathf.Clamp(value, 0f, MaxDamageReduction);
        }

        public static float ClampLifeStealHealPerHit(float heal, float maxHealth)
        {
            if (heal <= 0f)
                return 0f;

            float cap = Mathf.Max(1f, maxHealth) * MaxLifeStealHealPerHitPctOfMaxHealth;
            return Mathf.Clamp(heal, 0f, cap);
        }

        public static float ClampCritChance(float value)
        {
            float clamped = Mathf.Clamp01(value);
            if (clamped <= CritSoftCapStart)
                return clamped;

            float overflow = clamped - CritSoftCapStart;
            float softened = CritSoftCapStart + overflow * CritSoftCapStrength;
            return Mathf.Clamp(softened, 0f, CritHardCap);
        }

        public static float ClampCritMultiplier(float value)
        {
            return Mathf.Clamp(Mathf.Max(1f, value), 1f, MaxCritMultiplier);
        }

        public static float ApplyLifeStealDiminishing(float lifeSteal)
        {
            float clamped = Mathf.Clamp01(Mathf.Max(0f, lifeSteal));
            if (clamped <= LifeStealDiminishingThreshold)
                return clamped;

            float overflow = clamped - LifeStealDiminishingThreshold;
            float diminished = LifeStealDiminishingThreshold + overflow * LifeStealDiminishingSlope;
            return Mathf.Clamp01(diminished);
        }

        public static float GetLifeStealHealPerSecondCap(float maxHealth)
        {
            return Mathf.Max(1f, maxHealth) * MaxLifeStealHealPerSecondPctOfMaxHealth;
        }

        public static int GetRuntimeRelicMaxStacks(RelicDefinition relic)
        {
            if (relic == null)
                return 1;

            if (!relic.stackable)
                return 1;

            int maxStacks = Mathf.Max(1, relic.maxStacks);
            if (relic.rarity == RelicRarity.Mythic)
                return Mathf.Min(maxStacks, 2);

            if (relic.rarity == RelicRarity.Legendary)
                return Mathf.Min(maxStacks, 3);

            return maxStacks;
        }
    }
}
