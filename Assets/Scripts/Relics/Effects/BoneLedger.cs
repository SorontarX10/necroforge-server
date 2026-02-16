using UnityEngine;

[CreateAssetMenu(
    menuName = "GrassSim/Relics/Effects/Uncommon/Bone Ledger",
    fileName = "Relic_BoneLedger"
)]
public class BoneLedger : RelicEffect, IExpRewardModifier, IMaxHealthModifier
{
    [Header("Bonus")]
    public float expMultiplierPerStack = 0.2f;

    [Header("Penalty")]
    [Range(0f, 1f)] public float maxHealthPenaltyPctPerStack = 0.08f;

    public override void OnAcquire(PlayerRelicController player, int stacks)
    {
        player?.Progression?.NotifyStatsChanged();
    }

    public override void OnStack(PlayerRelicController player, int stacks)
    {
        player?.Progression?.NotifyStatsChanged();
    }

    public float GetExpGainMultiplier(PlayerRelicController player, int stacks)
    {
        if (stacks <= 0)
            return 1f;

        return 1f + Mathf.Max(0f, expMultiplierPerStack) * stacks;
    }

    public float GetMaxHealthBonus(PlayerRelicController player, int stacks)
    {
        if (player == null || player.Progression == null || player.Progression.stats == null || stacks <= 0)
            return 0f;

        float baseMaxHealth = Mathf.Max(1f, player.Progression.stats.maxHealth);
        float penaltyPct = Mathf.Clamp(maxHealthPenaltyPctPerStack * stacks, 0f, 0.95f);
        return -baseMaxHealth * penaltyPct;
    }
}
