using UnityEngine;
using GrassSim.AI;

public class EnemyStats : MonoBehaviour
{
    public float baseHealth = 100f;
    public float baseDamage = 10f;

    private float currentHealth;

    public void Init(EnemySimState sim, PlayerStats player)
    {
        currentHealth = baseHealth * player.EffectiveDifficulty;
    }

    public float GetDamage(PlayerStats player)
    {
        return baseDamage * player.EffectiveDifficulty;
    }
}
