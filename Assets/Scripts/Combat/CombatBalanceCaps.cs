using UnityEngine;

namespace GrassSim.Combat
{
    public static class CombatBalanceCaps
    {
        public const float MaxDodgeChance = 0.75f;
        public const float MaxDamageReduction = 0.85f;
        public const float MaxLifeStealHealPerHitPctOfMaxHealth = 0.1f;

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
    }
}
