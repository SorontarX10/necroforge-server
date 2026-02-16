using UnityEngine;

[CreateAssetMenu(
    menuName = "GrassSim/Relics/Effects/Common/Gravepath Greaves",
    fileName = "Relic_GravepathGreaves"
)]
public class GravepathGreaves : RelicEffect, ISpeedModifier
{
    [Header("Bonus")]
    [Tooltip("Percent movement speed bonus per stack (0.05 = +5%).")]
    public float speedPercentPerStack = 0.05f;

    public override void OnAcquire(PlayerRelicController player, int stacks) { }
    public override void OnStack(PlayerRelicController player, int stacks) { }

    public float GetSpeedBonus(PlayerRelicController player, int stacks)
    {
        if (player == null || stacks <= 0)
            return 0f;

        float baseSpeed = 0f;
        if (player.Progression != null && player.Progression.stats != null && player.Progression.stats.baseData != null)
            baseSpeed = player.Progression.stats.baseData.speed;

        return Mathf.Max(0f, baseSpeed * speedPercentPerStack * stacks);
    }
}
