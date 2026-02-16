using UnityEngine;
using GrassSim.Enemies;

public class EnemyDifficultyApplier : MonoBehaviour
{
    private EnemyCombatant combatant;
    private EnemyStatsData baseStats;
    private EnemyStatsData runtimeStats;

    private void Awake()
    {
        combatant = GetComponent<EnemyCombatant>();
        if (combatant == null || combatant.stats == null)
            return;

        baseStats = combatant.stats;
        ApplyDifficulty();
    }

    private void OnEnable()
    {
        ApplyDifficulty();
    }

    private void ApplyDifficulty()
    {
        if (combatant == null || baseStats == null)
            return;

        if (runtimeStats == null)
        {
            runtimeStats = Instantiate(baseStats);
            runtimeStats.name = baseStats.name + "_RuntimeDifficulty";
        }

        runtimeStats.maxHealth = baseStats.maxHealth * DifficultyContext.EnemyHealthMultiplier;
        runtimeStats.damage = baseStats.damage * DifficultyContext.EnemyDamageMultiplier;
        runtimeStats.attackCooldown = baseStats.attackCooldown / DifficultyContext.EnemyAttackSpeedMultiplier;
        runtimeStats.expReward = baseStats.expReward;
        combatant.stats = runtimeStats;
    }
}
