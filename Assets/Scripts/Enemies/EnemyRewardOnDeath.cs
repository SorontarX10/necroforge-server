using UnityEngine;
using GrassSim.Core;
using GrassSim.Stats;

namespace GrassSim.Enemies
{
    public class EnemyRewardOnDeath : MonoBehaviour
    {
        private EnemyStatsData stats;

        private void Awake()
        {
            stats = GetComponent<EnemyCombatant>()?.stats;
        }

        public void GrantRewards()
        {
            if (stats == null)
                return;

            PlayerProgressionController player = PlayerLocator.GetProgression();
            if (player == null)
                return;

            int finalExp = Mathf.RoundToInt(stats.expReward * DifficultyContext.ExpMultiplier);
            var relics = player.GetComponent<PlayerRelicController>();
            if (relics != null)
                finalExp = Mathf.Max(0, Mathf.RoundToInt(finalExp * relics.GetExpGainMultiplier()));

            player.AddExp(finalExp);
        }
    }
}
