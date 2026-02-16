using UnityEngine;

namespace GrassSim.Combat
{
    public static class CombatBalanceCaps
    {
        public const float MaxDodgeChance = 0.75f;
        public const float MaxDamageReduction = 0.85f;

        public static float ClampDodgeChance(float value)
        {
            return Mathf.Clamp(value, 0f, MaxDodgeChance);
        }

        public static float ClampDamageReduction(float value)
        {
            return Mathf.Clamp(value, 0f, MaxDamageReduction);
        }
    }
}
