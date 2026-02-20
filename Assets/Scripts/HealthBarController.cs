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
        playerProg = c != null ? c.GetComponentInParent<PlayerProgressionController>() : null;
        enemyCombatant = c != null ? c.GetComponentInParent<EnemyCombatant>() : null;
        agent = c != null ? c.GetComponentInParent<BaseCombatAgent>() : null;
    }

    void Update()
    {
        if (combatant == null || fillImage == null)
            return;

        float current = combatant.CurrentHealth;
        float max = Mathf.Max(1f, combatant.MaxHealth);

        fillImage.fillAmount = Mathf.Clamp01(current / max);
    }
}
