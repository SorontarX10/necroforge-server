using UnityEngine;
using GrassSim.Combat;
using GrassSim.Core;
using GrassSim.Enemies;

public class HealthBarBinder : MonoBehaviour
{
    public Combatant combatant;

    private PlayerProgressionController playerProg;
    private EnemyCombatant enemyCombatant;

    private void Awake()
    {
        if (!combatant) combatant = GetComponentInParent<Combatant>();

        playerProg = GetComponentInParent<PlayerProgressionController>();
        enemyCombatant = GetComponentInParent<EnemyCombatant>();
    }

    private void Update()
    {
        if (!combatant) return;

        float maxHp = GetMaxHp();
        float frac = maxHp > 0f ? combatant.currentHealth / maxHp : 0f;

        // TODO: ustaw slider/fill, np:
        // slider.value = frac;
    }

    private float GetMaxHp()
    {
        if (playerProg != null && playerProg.stats != null) return playerProg.stats.maxHealth;
        if (enemyCombatant != null && enemyCombatant.stats != null) return enemyCombatant.stats.maxHealth;
        return Mathf.Max(1f, combatant.currentHealth);
    }
}
