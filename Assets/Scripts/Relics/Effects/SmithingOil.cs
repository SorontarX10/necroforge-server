using UnityEngine;

[CreateAssetMenu(
    menuName = "GrassSim/Relics/Effects/Common/Smithing Oil",
    fileName = "Relic_SmithingOil"
)]
public class SmithingOil : RelicEffect, ISwingSpeedModifier
{
    [Header("Bonus")]
    [Tooltip("Flat swing speed bonus per stack (0.06 = +6%).")]
    public float swingSpeedPerStack = 0.06f;

    public override void OnAcquire(PlayerRelicController player, int stacks)
    {
        player?.Progression?.NotifyStatsChanged();
    }

    public override void OnStack(PlayerRelicController player, int stacks)
    {
        player?.Progression?.NotifyStatsChanged();
    }

    public float GetSwingSpeedBonus(PlayerRelicController player, int stacks)
    {
        return stacks > 0 ? swingSpeedPerStack * stacks : 0f;
    }
}
