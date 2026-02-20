using System;
using UnityEngine;
using GrassSim.Combat;

namespace GrassSim.Stats
{
    [Serializable]
    public class RuntimeStats
    {
        public PlayerStatsData baseData;

        // runtime values
        public float maxHealth;
        public float healthRegen;

        public float maxStamina;
        public float staminaRegen;

        public float damage;
        public float critChance;
        public float critMultiplier;
        public float lifeSteal;
        public float swingSpeed;
        public float speed;

        public float damageReduction;
        public float dodgeChance;

        public RuntimeStats(PlayerStatsData data)
        {
            baseData = data;
            ResetToBase();
        }

        public void ResetToBase()
        {
            if (baseData == null)
                throw new InvalidOperationException("RuntimeStats: baseData is null.");

            maxHealth = baseData.maxHealth;
            healthRegen = baseData.healthRegen;

            maxStamina = baseData.maxStamina;
            staminaRegen = baseData.staminaRegen;

            damage = baseData.damage;
            critChance = baseData.critChance;
            critMultiplier = CombatBalanceCaps.ClampCritMultiplier(baseData.critMultiplier);
            lifeSteal = baseData.lifeSteal;
            swingSpeed = baseData.swingSpeed;
            speed = baseData.speed;

            damageReduction = baseData.damageReduction;
            dodgeChance = baseData.dodgeChance;
        }

        public void Apply(StatType stat, float value)
        {
            switch (stat)
            {
                case StatType.MaxHealth: maxHealth += value; break;
                case StatType.HealthRegen: healthRegen += value; break;

                case StatType.MaxStamina: maxStamina += value; break;
                case StatType.StaminaRegen: staminaRegen += value; break;

                case StatType.Damage: damage += value; break;
                case StatType.SwingSpeed: swingSpeed += value; break;
                case StatType.Speed: speed += value; break;

                case StatType.CritChance:
                    critChance = Mathf.Clamp01(critChance + value);
                    break;

                case StatType.CritMultiplier:
                    critMultiplier = CombatBalanceCaps.ClampCritMultiplier(critMultiplier + value);
                    break;

                case StatType.LifeSteal:
                    lifeSteal = Mathf.Clamp01(lifeSteal + value);
                    break;

                case StatType.DamageReduction:
                    damageReduction = Mathf.Clamp01(damageReduction + value);
                    break;

                case StatType.DodgeChance:
                    dodgeChance = Mathf.Clamp01(dodgeChance + value);
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(stat), stat, "Unknown StatType");
            }
        }
    }
}
