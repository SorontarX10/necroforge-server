using UnityEngine;
using GrassSim.Core;

public class StaminaBarBinder : MonoBehaviour
{
    private PlayerProgressionController playerProg;
    private BaseCombatAgent agent;

    private void Awake()
    {
        playerProg = GetComponentInParent<PlayerProgressionController>();
        agent = GetComponentInParent<BaseCombatAgent>();
    }

    private void Update()
    {
        float cur = GetCur();
        float max = GetMax();
        float frac = max > 0f ? cur / max : 0f;

        // TODO: ustaw slider/fill, np:
        // slider.value = frac;
    }

    private float GetCur()
    {
        if (playerProg != null) return playerProg.currentStamina;
        if (agent != null) return agent.stamina;
        return 0f;
    }

    private float GetMax()
    {
        if (playerProg != null && playerProg.stats != null) return playerProg.stats.maxStamina;
        if (agent != null) return agent.maxStamina;
        return 0f;
    }
}
