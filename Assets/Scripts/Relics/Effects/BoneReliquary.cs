using UnityEngine;

[CreateAssetMenu(
    menuName = "GrassSim/Relics/Effects/Common/Bone Reliquary",
    fileName = "Relic_BoneReliquary"
)]
public class BoneReliquary : RelicEffect, IMaxHealthModifier
{
    [Header("Bonus")]
    [Tooltip("Flat max health bonus per stack.")]
    public float maxHealthPerStack = 18f;

    public override void OnAcquire(PlayerRelicController player, int stacks)
    {
        player?.Progression?.NotifyStatsChanged();
    }

    public override void OnStack(PlayerRelicController player, int stacks)
    {
        player?.Progression?.NotifyStatsChanged();
    }

    public float GetMaxHealthBonus(PlayerRelicController player, int stacks)
    {
        return stacks > 0 ? maxHealthPerStack * stacks : 0f;
    }
}
