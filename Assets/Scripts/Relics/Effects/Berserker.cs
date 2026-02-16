using UnityEngine;

[CreateAssetMenu(
    menuName = "GrassSim/Relics/Effects/Berserker",
    fileName = "Relic_Berserker"
)]
public class Berserker : RelicEffect, IDamageModifier
{
    [Header("Scaling")]
    [Tooltip("Damage bonus per stack at 0% HP (e.g. 0.15 = +15%)")]
    public float damagePerStack = 0.15f;

    public override void OnAcquire(PlayerRelicController player, int stacks)
    {
        // Damage is computed dynamically via IDamageModifier.
    }

    public override void OnStack(PlayerRelicController player, int stacks)
    {
        // No-op; scaling is dynamic.
    }

    public float GetDamageMultiplier(PlayerRelicController player, int stacks)
    {
        if (player == null || stacks <= 0)
            return 1f;

        var prog = player.Progression;
        if (prog == null || prog.stats == null || prog.MaxHealth <= 0f)
            return 1f;

        float hp01 = Mathf.Clamp01(prog.CurrentHealth / prog.MaxHealth);
        float missing = 1f - hp01;

        return 1f + missing * damagePerStack * stacks;
    }
}
