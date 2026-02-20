using System;
using System.Collections.Generic;
using UnityEngine;
using GrassSim.Core;
using GrassSim.Combat;

public class PlayerRelicController : MonoBehaviour
{
    public const string RejectReasonNone = "none";
    public const string RejectReasonNull = "null_relic";
    public const string RejectReasonInvalidId = "invalid_id";
    public const string RejectReasonMissingEffect = "missing_effect";
    public const string RejectReasonNonStackableOwned = "non_stackable_owned";
    public const string RejectReasonMaxStacksReached = "max_stacks_reached";

    private readonly Dictionary<string, int> stacks = new();
    private readonly Dictionary<string, RelicDefinition> relics = new();
    private readonly Dictionary<RelicEffect, RelicDefinition> relicsByEffect = new();

    public event Action OnChanged;
    public event Action<Combatant, float, bool> OnMeleeHitDealt;
    public event Action<Combatant, float, bool> OnMeleeKill;
    public event Action<Combatant> OnBeforeMeleeHit;
    public event Action<RelicDefinition, int> OnRelicApplied;
    public event Action OnDodged;
    public event Action<float> OnDamageTaken;
    public event Action<float, float> OnHealed;
    public event Func<bool> OnTryBlockIncomingHit;

    // Single source of truth.
    public PlayerProgressionController Progression { get; private set; }
    public IReadOnlyDictionary<string, RelicDefinition> Relics => relics;

    private void Awake()
    {
        Progression = GetComponent<PlayerProgressionController>();

        if (Progression == null)
        {
            Debug.LogError(
                "[PlayerRelicController] Missing PlayerProgressionController on Player!",
                this
            );
        }
    }

    public bool AddRelic(RelicDefinition relic)
    {
        if (relic == null)
        {
            Debug.LogWarning("[PlayerRelicController] Tried to add null relic.", this);
            return false;
        }

        if (string.IsNullOrWhiteSpace(relic.id))
        {
            Debug.LogWarning($"[PlayerRelicController] Relic '{relic.name}' has empty id.", relic);
            return false;
        }

        if (relic.effect == null)
        {
            Debug.LogWarning($"[PlayerRelicController] Relic '{relic.id}' has no effect.", relic);
            return false;
        }

        bool changed = false;

        if (!relics.ContainsKey(relic.id))
        {
            relics[relic.id] = relic;
            relicsByEffect[relic.effect] = relic;
            stacks[relic.id] = 1;
            relic.effect.OnAcquire(this, 1);
            changed = true;
        }
        else if (relic.stackable && stacks[relic.id] < GetEffectiveMaxStacks(relic))
        {
            stacks[relic.id]++;
            relic.effect.OnStack(this, stacks[relic.id]);
            changed = true;
        }

        if (changed)
        {
            OnRelicApplied?.Invoke(relic, GetStacks(relic.id));
            OnChanged?.Invoke();
            Progression?.NotifyStatsChanged();
        }

        return changed;
    }

    public bool CanAcceptRelic(RelicDefinition relic)
    {
        return GetRelicRejectionReason(relic) == RejectReasonNone;
    }

    public int GetEffectiveMaxStacks(RelicDefinition relic)
    {
        return CombatBalanceCaps.GetRuntimeRelicMaxStacks(relic);
    }

    public string GetRelicRejectionReason(RelicDefinition relic)
    {
        if (relic == null)
            return RejectReasonNull;

        if (string.IsNullOrWhiteSpace(relic.id))
            return RejectReasonInvalidId;

        if (relic.effect == null)
            return RejectReasonMissingEffect;

        if (!relics.ContainsKey(relic.id))
            return RejectReasonNone;

        if (!relic.stackable)
            return RejectReasonNonStackableOwned;

        int stackCount = GetStacks(relic.id);
        int maxStacks = GetEffectiveMaxStacks(relic);
        return stackCount < maxStacks
            ? RejectReasonNone
            : RejectReasonMaxStacksReached;
    }

    public int GetStacks(string relicId)
    {
        return stacks.TryGetValue(relicId, out int v) ? v : 0;
    }

    public bool TryGetRelicDefinitionByEffect(RelicEffect effect, out RelicDefinition definition)
    {
        definition = null;
        if (effect == null)
            return false;

        return relicsByEffect.TryGetValue(effect, out definition) && definition != null;
    }

    public float GetExpGainMultiplier()
    {
        float mul = 1f;

        foreach (var kvp in relics)
        {
            var def = kvp.Value;
            if (def == null || def.effect == null)
                continue;

            if (def.effect is IExpRewardModifier mod)
            {
                int stackCount = stacks.TryGetValue(kvp.Key, out int v) ? v : 0;
                if (stackCount <= 0)
                    continue;

                float m = mod.GetExpGainMultiplier(this, stackCount);
                if (m <= 0f)
                    return 0f;

                mul *= m;
            }
        }

        return Mathf.Max(0f, mul);
    }

    public float ModifyIncomingDamage(Combatant attacker, float damage)
    {
        float value = Mathf.Max(0f, damage);
        if (value <= 0f)
            return 0f;

        foreach (var kvp in relics)
        {
            var def = kvp.Value;
            if (def == null || def.effect == null)
                continue;

            if (def.effect is IIncomingHitModifier mod)
            {
                int stackCount = stacks.TryGetValue(kvp.Key, out int v) ? v : 0;
                if (stackCount <= 0)
                    continue;

                value = mod.ModifyIncomingDamage(this, attacker, value, stackCount);
                if (value <= 0f)
                    return 0f;
            }
        }

        return Mathf.Max(0f, value);
    }

    public float GetDamageMultiplier()
    {
        float mul = 1f;

        foreach (var kvp in relics)
        {
            var def = kvp.Value;
            if (def == null || def.effect == null)
                continue;

            if (def.effect is IDamageModifier mod)
            {
                int stackCount = stacks.TryGetValue(kvp.Key, out int v) ? v : 0;
                if (stackCount <= 0)
                    continue;

                float m = mod.GetDamageMultiplier(this, stackCount);
                if (m <= 0f)
                    return 0f;

                mul *= m;
            }
        }

        return Mathf.Max(0f, mul);
    }

    public float GetCritChanceBonus()
        => SumBonus<ICritChanceModifier>((m, stackCount) => m.GetCritChanceBonus(this, stackCount));

    public float GetCritMultiplierBonus()
        => SumBonus<ICritMultiplierModifier>((m, stackCount) =>
            m.GetCritMultiplierBonus(this, stackCount));

    public float GetLifeStealBonus()
        => SumBonus<ILifeStealModifier>((m, stackCount) => m.GetLifeStealBonus(this, stackCount));

    public float GetSwingSpeedBonus()
        => SumBonus<ISwingSpeedModifier>((m, stackCount) => m.GetSwingSpeedBonus(this, stackCount));

    public float GetSpeedBonus()
        => SumBonus<ISpeedModifier>((m, stackCount) => m.GetSpeedBonus(this, stackCount));

    public float GetDamageReductionBonus()
        => SumBonus<IDamageReductionModifier>((m, stackCount) =>
            m.GetDamageReductionBonus(this, stackCount));

    public float GetDodgeChanceBonus()
        => SumBonus<IDodgeChanceModifier>((m, stackCount) => m.GetDodgeChanceBonus(this, stackCount));

    public float GetMaxHealthBonus()
        => SumBonus<IMaxHealthModifier>((m, stackCount) => m.GetMaxHealthBonus(this, stackCount));

    public float GetStaminaRegenBonus()
        => SumBonus<IStaminaRegenModifier>((m, stackCount) =>
            m.GetStaminaRegenBonus(this, stackCount));

    public float GetSwordLengthBonus()
        => SumBonus<ISwordLengthModifier>((m, stackCount) =>
            m.GetSwordLengthBonus(this, stackCount));

    public float GetStaminaSwingMultiplierOverride()
        => MaxBonus<IStaminaSwingOverrideModifier>((m, stackCount) =>
            m.GetStaminaSwingMultiplierOverride(this, stackCount));

    public void NotifyMeleeHitDealt(Combatant target, float damage, bool isCrit)
    {
        OnMeleeHitDealt?.Invoke(target, damage, isCrit);
    }

    public void NotifyBeforeMeleeHit(Combatant target)
    {
        OnBeforeMeleeHit?.Invoke(target);
    }

    public void NotifyMeleeKill(Combatant target, float damage, bool isCrit)
    {
        OnMeleeKill?.Invoke(target, damage, isCrit);
    }

    public void NotifyDodged()
    {
        OnDodged?.Invoke();
    }

    public void NotifyDamageTaken(float amount)
    {
        if (amount > 0f)
            OnDamageTaken?.Invoke(amount);
    }

    public void NotifyHealed(float amount, float overheal)
    {
        if (amount > 0f || overheal > 0f)
            OnHealed?.Invoke(amount, overheal);
    }

    public bool TryBlockIncomingHit()
    {
        if (OnTryBlockIncomingHit == null)
            return false;

        var handlers = OnTryBlockIncomingHit.GetInvocationList();
        for (int i = 0; i < handlers.Length; i++)
        {
            if (handlers[i] is not Func<bool> handler)
                continue;

            bool blocked = false;
            try
            {
                blocked = handler.Invoke();
            }
            catch (Exception ex)
            {
                Debug.LogException(ex, this);
            }

            if (blocked)
                return true;
        }

        return false;
    }

    private float SumBonus<T>(Func<T, int, float> selector) where T : class
    {
        float sum = 0f;

        foreach (var kvp in relics)
        {
            var def = kvp.Value;
            if (def == null || def.effect == null)
                continue;

            if (def.effect is T mod)
            {
                int stackCount = stacks.TryGetValue(kvp.Key, out int v) ? v : 0;
                if (stackCount <= 0)
                    continue;

                float value = selector(mod, stackCount);
                sum += value;
            }
        }

        return sum;
    }

    private float MaxBonus<T>(Func<T, int, float> selector) where T : class
    {
        float max = 0f;

        foreach (var kvp in relics)
        {
            var def = kvp.Value;
            if (def == null || def.effect == null)
                continue;

            if (def.effect is T mod)
            {
                int stackCount = stacks.TryGetValue(kvp.Key, out int v) ? v : 0;
                if (stackCount <= 0)
                    continue;

                max = Mathf.Max(max, selector(mod, stackCount));
            }
        }

        return max;
    }
}
