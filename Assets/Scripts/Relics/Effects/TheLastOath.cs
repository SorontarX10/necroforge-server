using UnityEngine;

[CreateAssetMenu(
    menuName = "GrassSim/Relics/Effects/The Last Oath",
    fileName = "Relic_TheLastOath"
)]
public class TheLastOath : RelicEffect, IDamageReductionModifier, ISwingSpeedModifier
{
    [Header("Trigger")]
    [Range(0.05f, 0.8f)]
    public float hpThreshold = 0.25f; // 25%

    [Header("Bonuses while under threshold")]
    [Tooltip("+ damage reduction per stack while low HP (0..1)")]
    public float damageReductionPerStack = 0.06f; // +6%

    [Tooltip("+ swing speed per stack while low HP")]
    public float swingSpeedPerStack = 0.16f;

    public override void OnAcquire(PlayerRelicController player, int stacks)
    {
        // Computed dynamically via modifiers.
    }

    public override void OnStack(PlayerRelicController player, int stacks)
    {
        // Computed dynamically via modifiers.
    }

    public float GetDamageReductionBonus(PlayerRelicController player, int stacks)
    {
        return IsActive(player) ? damageReductionPerStack * stacks : 0f;
    }

    public float GetSwingSpeedBonus(PlayerRelicController player, int stacks)
    {
        return IsActive(player) ? swingSpeedPerStack * stacks : 0f;
    }

    private bool IsActive(PlayerRelicController player)
    {
        if (player == null)
            return false;

        var prog = player.Progression;
        if (prog == null || prog.stats == null || prog.MaxHealth <= 0f)
            return false;

        float hp01 = Mathf.Clamp01(prog.CurrentHealth / prog.MaxHealth);
        return hp01 <= hpThreshold;
    }
}
