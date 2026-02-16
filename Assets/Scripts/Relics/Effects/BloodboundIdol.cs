using UnityEngine;

[CreateAssetMenu(
    menuName = "GrassSim/Relics/Effects/Bloodbound Idol",
    fileName = "Relic_BloodboundIdol"
)]
public class BloodboundIdol : RelicEffect, ILifeStealModifier
{
    [Header("LifeSteal")]
    [Tooltip("Flat lifesteal added per stack (0..1)")]
    public float baseLifeStealPerStack = 0.01f; // +1%

    [Tooltip("Extra lifesteal at 0% HP, scaled by missing health and stacks (0..1)")]
    public float maxExtraLifeStealPerStack = 0.03f; // +3% at 0 HP

    public override void OnAcquire(PlayerRelicController player, int stacks)
    {
        // Computed dynamically via ILifeStealModifier.
    }

    public override void OnStack(PlayerRelicController player, int stacks)
    {
        // Computed dynamically via ILifeStealModifier.
    }

    public float GetLifeStealBonus(PlayerRelicController player, int stacks)
    {
        if (player == null || stacks <= 0)
            return 0f;

        var prog = player.Progression;
        if (prog == null || prog.stats == null || prog.MaxHealth <= 0f)
            return baseLifeStealPerStack * stacks;

        float hp01 = Mathf.Clamp01(prog.CurrentHealth / prog.MaxHealth);
        float missing = 1f - hp01;

        float desired =
            (baseLifeStealPerStack * stacks) +
            (maxExtraLifeStealPerStack * stacks * missing);

        return Mathf.Max(0f, desired);
    }
}
