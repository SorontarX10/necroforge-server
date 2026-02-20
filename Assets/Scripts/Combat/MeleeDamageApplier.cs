using UnityEngine;
using GrassSim.Core;
using GrassSim.Combat;

namespace GrassSim.Combat
{
    public class MeleeDamageApplier : MonoBehaviour
    {
        public float baseDamageFallback = 10f;

        public DamageResult ComputeDamage(PlayerProgressionController attacker)
        {
            float dmg = baseDamageFallback;
            float critChance = 0f;
            float critMultiplier = 1f;
            float lifeSteal = 0f;

            if (attacker != null && attacker.stats != null)
            {
                dmg = attacker.stats.damage;
                critChance = attacker.stats.critChance;
                critMultiplier = attacker.stats.critMultiplier;
                lifeSteal = attacker.stats.lifeSteal;
            }

            if (attacker != null)
            {
                var relics = attacker.GetComponent<PlayerRelicController>();
                if (relics != null)
                {
                    dmg *= relics.GetDamageMultiplier();
                    critChance += relics.GetCritChanceBonus();
                    critMultiplier += relics.GetCritMultiplierBonus();
                    lifeSteal += relics.GetLifeStealBonus();
                }
            }

            critChance = CombatBalanceCaps.ClampCritChance(critChance);
            critMultiplier = Mathf.Max(1f, critMultiplier);

            bool isCrit = Random.value < critChance;
            if (isCrit)
                dmg *= critMultiplier;

            float heal = 0f;
            if (lifeSteal > 0f)
            {
                float rawHeal = dmg * CombatBalanceCaps.ApplyLifeStealDiminishing(lifeSteal);
                float maxHealth = attacker != null ? attacker.MaxHealth : 1f;
                heal = CombatBalanceCaps.ClampLifeStealHealPerHit(rawHeal, maxHealth);
            }

            return new DamageResult
            {
                finalDamage = dmg,
                isCrit = isCrit,
                lifestealHeal = heal
            };
        }

        public float ApplyTargetMitigation(float rawDamage, PlayerProgressionController target)
        {
            if (target == null || target.stats == null)
                return rawDamage;

            // Dodge check
            float dodgeChance = target.stats.dodgeChance;
            var relics = target.GetComponent<PlayerRelicController>();
            if (relics != null)
                dodgeChance += relics.GetDodgeChanceBonus();

            dodgeChance = CombatBalanceCaps.ClampDodgeChance(dodgeChance);

            if (dodgeChance > 0f && Random.value < dodgeChance)
                return 0f;

            float reduction = target.stats.damageReduction;
            if (relics != null)
                reduction += relics.GetDamageReductionBonus();

            reduction = CombatBalanceCaps.ClampDamageReduction(reduction);

            float mitigated = rawDamage * (1f - reduction);
            return Mathf.Max(0f, mitigated);
        }
    }
}
