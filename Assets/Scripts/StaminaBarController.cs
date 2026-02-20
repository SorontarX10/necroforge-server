using UnityEngine;
using UnityEngine.UI;
using GrassSim.Core;

public class StaminaBarController : MonoBehaviour
{
    public Image fillImage;

    private PlayerProgressionController playerProg;
    private BaseCombatAgent agent;

    public void Init(PlayerProgressionController pp, BaseCombatAgent ag)
    {
        playerProg = pp;
        agent = ag;
    }

    void Update()
    {
        if (fillImage == null)
            return;

        float current = 0f;
        float max = 0f;

        if (playerProg != null)
        {
            current = playerProg.CurrentStamina;
            max = playerProg.MaxStamina;
        }
        else if (agent != null)
        {
            current = agent.stamina;
            max = agent.maxStamina;
        }

        if (max > 0f)
            fillImage.fillAmount = Mathf.Clamp01(current / max);
    }
}
