using UnityEngine;
using UnityEngine.UI;
using GrassSim.Combat;
using GrassSim.Enemies;
using GrassSim.Core;

public class HealthBarController : MonoBehaviour
{
    public Image fillImage;

    private Combatant combatant;
    private PlayerProgressionController playerProg;
    private EnemyCombatant enemyCombatant;
    private BaseCombatAgent agent;

    public void Init(Combatant c)
    {
        combatant = c;
        playerProg = c.GetComponentInParent<PlayerProgressionController>();
        enemyCombatant = c.GetComponentInParent<EnemyCombatant>();
        agent = c.GetComponentInParent<BaseCombatAgent>();
    }

    void Update()
    {
        if (combatant == null || fillImage == null)
            return;

        float current = combatant.currentHealth;
        float max = 1f;

        if (playerProg != null && playerProg.stats != null)
            max = playerProg.stats.maxHealth;
        else if (enemyCombatant != null && enemyCombatant.stats != null)
            max = enemyCombatant.stats.maxHealth;
        else if (agent != null)
            max = agent.maxHealth;

        fillImage.fillAmount = Mathf.Clamp01(current / max);
    }
}
