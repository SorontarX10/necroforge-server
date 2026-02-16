using UnityEngine;

namespace GrassSim.Enemies
{
    [CreateAssetMenu(
        menuName = "GrassSim/Enemies/EnemyStatsData",
        fileName = "EnemyStatsData"
    )]
    public class EnemyStatsData : ScriptableObject
    {
        [Header("Vitals")]
        public float maxHealth = 50f;

        [Header("Combat")]
        public float damage = 10f;
        public float attackCooldown = 1.2f;

        [Header("Rewards")]
        public int expReward = 10;
    }
}
