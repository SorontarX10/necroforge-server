using UnityEngine;
using GrassSim.Enemies;
using GrassSim.Core;

public class EnemyDifficultyExpBonus : MonoBehaviour
{
    private EnemyStatsData stats;

    private void Awake()
    {
        var combatant = GetComponent<EnemyCombatant>();
        if (combatant == null)
            return;

        stats = combatant.stats;
    }

    // To MUSI się zgadzać z tym, co wywołuje EnemyRewardOnDeath
    public int GetFinalExpReward()
    {
        if (stats == null)
            return 0;

        float mul = DifficultyContext.ExpMultiplier;
        return Mathf.RoundToInt(stats.expReward * mul);
    }
}
