using UnityEngine;

[CreateAssetMenu(
    menuName = "GrassSim/Relics/Effects/Uncommon/Grim Oil",
    fileName = "Relic_GrimOil"
)]
public class GrimOil : RelicEffect, ISwingSpeedModifier, IStaminaRegenModifier
{
    [Header("Bonuses")]
    public float swingSpeedPerStack = 0.08f;
    public float staminaRegenPerStack = 3f;

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

    public float GetStaminaRegenBonus(PlayerRelicController player, int stacks)
    {
        return stacks > 0 ? staminaRegenPerStack * stacks : 0f;
    }
}
