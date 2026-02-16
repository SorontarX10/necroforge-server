using System.Collections.Generic;
using UnityEngine;
using GrassSim.Combat;

[CreateAssetMenu(
    menuName = "GrassSim/Relics/Effects/Common/Undertaker Brazier",
    fileName = "Relic_UndertakerBrazier"
)]
public class UndertakerBrazier : RelicEffect, ISwingSpeedModifier, ISpeedModifier, IStaminaRegenModifier
{
    [Header("Embers")]
    [Min(1)] public int embersToIgnite = 3;
    public float emberLifetime = 6f;

    [Header("Buff")]
    public float baseBuffDuration = 5f;
    public float extraBuffDurationPerStack = 0.5f;
    public float extendOnKill = 1f;
    public float maxExtraDuration = 3f;

    [Header("Bonuses")]
    public float baseSwingSpeedBonus = 0.22f;
    public float extraSwingSpeedPerStack = 0.04f;
    public float baseSpeedBonus = 0.12f;
    public float extraSpeedPerStack = 0.02f;
    public float baseStaminaRegenBonus = 6f;
    public float extraStaminaRegenPerStack = 1f;

    public override void OnAcquire(PlayerRelicController player, int stacks)
    {
        Attach(player)?.Configure(this, stacks);
    }

    public override void OnStack(PlayerRelicController player, int stacks)
    {
        Attach(player)?.Configure(this, stacks);
    }

    public float GetSwingSpeedBonus(PlayerRelicController player, int stacks)
    {
        var rt = player != null ? player.GetComponent<UndertakerBrazierRuntime>() : null;
        if (rt == null || !rt.IsBuffActive)
            return 0f;

        return baseSwingSpeedBonus + extraSwingSpeedPerStack * Mathf.Max(0, stacks - 1);
    }

    public float GetSpeedBonus(PlayerRelicController player, int stacks)
    {
        var rt = player != null ? player.GetComponent<UndertakerBrazierRuntime>() : null;
        if (rt == null || !rt.IsBuffActive)
            return 0f;

        return baseSpeedBonus + extraSpeedPerStack * Mathf.Max(0, stacks - 1);
    }

    public float GetStaminaRegenBonus(PlayerRelicController player, int stacks)
    {
        var rt = player != null ? player.GetComponent<UndertakerBrazierRuntime>() : null;
        if (rt == null || !rt.IsBuffActive)
            return 0f;

        return baseStaminaRegenBonus + extraStaminaRegenPerStack * Mathf.Max(0, stacks - 1);
    }

    private UndertakerBrazierRuntime Attach(PlayerRelicController player)
    {
        if (player == null) return null;
        var rt = player.GetComponent<UndertakerBrazierRuntime>();
        if (rt == null) rt = player.gameObject.AddComponent<UndertakerBrazierRuntime>();
        return rt;
    }
}

public class UndertakerBrazierRuntime : MonoBehaviour, IRelicBatchedUpdate, IRelicBatchedCadence
{
    private readonly List<float> emberExpiresAt = new();

    private PlayerRelicController player;
    private UndertakerBrazier cfg;
    private int stacks;
    private bool subscribed;
    private bool wasActive;
    private float buffEndsAt;
    private float buffHardCapAt;

    public bool IsBuffActive => Time.time < buffEndsAt;

    private void Awake()
    {
        player = GetComponent<PlayerRelicController>();
    }

    private void OnEnable()
    {
        RelicBatchedTickSystem.Register(this);
        TrySubscribe();
    }

    private void OnDisable()
    {
        RelicBatchedTickSystem.Unregister(this);
        TryUnsubscribe();
    }

    public void Configure(UndertakerBrazier config, int stackCount)
    {
        cfg = config;
        stacks = Mathf.Max(1, stackCount);
        TrySubscribe();
    }

    public bool IsBatchedUpdateActive => isActiveAndEnabled && cfg != null;

    public float BatchedUpdateInterval => 0.2f;

    public RelicTickArchetype BatchedTickArchetype => RelicTickArchetype.PlayerState;

    public void TickFromRelicBatch(float now, float deltaTime)
    {
        CleanupExpiredEmbers(now);

        if (!IsBuffActive && emberExpiresAt.Count >= Mathf.Max(1, cfg.embersToIgnite))
            Ignite();

        if (IsBuffActive != wasActive)
        {
            wasActive = IsBuffActive;
            player?.Progression?.NotifyStatsChanged();
        }
    }

    private void TrySubscribe()
    {
        if (subscribed || player == null)
            return;

        player.OnMeleeKill += OnMeleeKill;
        subscribed = true;
    }

    private void TryUnsubscribe()
    {
        if (!subscribed || player == null)
            return;

        player.OnMeleeKill -= OnMeleeKill;
        subscribed = false;
    }

    private void OnMeleeKill(Combatant target, float damage, bool isCrit)
    {
        if (cfg == null)
            return;

        if (IsBuffActive)
        {
            buffEndsAt = Mathf.Min(buffHardCapAt, buffEndsAt + Mathf.Max(0f, cfg.extendOnKill));
            return;
        }

        emberExpiresAt.Add(Time.time + Mathf.Max(0.1f, cfg.emberLifetime));
    }

    private void Ignite()
    {
        int consume = Mathf.Min(Mathf.Max(1, cfg.embersToIgnite), emberExpiresAt.Count);
        emberExpiresAt.RemoveRange(0, consume);

        float duration = cfg.baseBuffDuration + cfg.extraBuffDurationPerStack * Mathf.Max(0, stacks - 1);
        duration = Mathf.Max(0.25f, duration);

        buffEndsAt = Time.time + duration;
        buffHardCapAt = buffEndsAt + Mathf.Max(0f, cfg.maxExtraDuration);
    }

    private void CleanupExpiredEmbers(float now)
    {
        if (emberExpiresAt.Count == 0)
            return;

        for (int i = emberExpiresAt.Count - 1; i >= 0; i--)
        {
            if (now >= emberExpiresAt[i])
                emberExpiresAt.RemoveAt(i);
        }
    }
}
