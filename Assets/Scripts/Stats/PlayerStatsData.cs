using UnityEngine;

namespace GrassSim.Stats
{
    [CreateAssetMenu(menuName = "GrassSim/Stats/PlayerStatsData", fileName = "PlayerStatsData")]
    public class PlayerStatsData : ScriptableObject
    {
        [Header("Health")]
        public float maxHealth = 100f;
        public float healthRegen = 0f;

        [Header("Stamina")]
        public float maxStamina = 100f;
        public float staminaRegen = 10f;
        public float speed = 5f;

        [Header("Offense")]
        public float damage = 10f;
        [Range(0f, 1f)] public float critChance = 0.05f;
        public float critMultiplier = 1.5f;
        [Range(0f, 1f)] public float lifeSteal = 0f;
        public float swingSpeed = 1f;

        [Header("Defense")]
        [Range(0f, 1f)] public float damageReduction = 0f;
        [Range(0f, 1f)] public float dodgeChance = 0f;
    }
}
