using UnityEngine;
using GrassSim.Combat;

[CreateAssetMenu(
    menuName = "GrassSim/Relics/Effects/Common/First Blood Rune",
    fileName = "Relic_FirstBloodRune"
)]
public class FirstBloodRune : RelicEffect, ICritChanceModifier
{
    [Header("Trigger Buff")]
    public float baseCritChanceBonus = 0.15f;
    public float extraCritChanceBonusPerStack = 0.03f;
    public float baseBuffDuration = 2f;
    public float extraDurationPerStack = 0.2f;

    [Header("Resource")]
    public float baseStaminaGain = 10f;
    public float extraStaminaGainPerStack = 2f;

    public override void OnAcquire(PlayerRelicController player, int stacks)
    {
        Attach(player)?.Configure(this, stacks);
    }

    public override void OnStack(PlayerRelicController player, int stacks)
    {
        Attach(player)?.Configure(this, stacks);
    }

    public float GetCritChanceBonus(PlayerRelicController player, int stacks)
    {
        var rt = player != null ? player.GetComponent<FirstBloodRuneRuntime>() : null;
        if (rt == null || !rt.IsCritBuffActive)
            return 0f;

        return baseCritChanceBonus + extraCritChanceBonusPerStack * Mathf.Max(0, stacks - 1);
    }

    private FirstBloodRuneRuntime Attach(PlayerRelicController player)
    {
        if (player == null) return null;
        var rt = player.GetComponent<FirstBloodRuneRuntime>();
        if (rt == null) rt = player.gameObject.AddComponent<FirstBloodRuneRuntime>();
        return rt;
    }
}

public class FirstBloodRuneRuntime : MonoBehaviour, IRelicBatchedUpdate, IRelicBatchedCadence
{
    private PlayerRelicController player;
    private FirstBloodRune cfg;
    private int stacks;
    private bool subscribed;
    private int lastTargetId = int.MinValue;
    private float critBuffUntil;
    private bool wasActive;

    public bool IsCritBuffActive => Time.time < critBuffUntil;

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

    public void Configure(FirstBloodRune config, int stackCount)
    {
        cfg = config;
        stacks = Mathf.Max(1, stackCount);
        TrySubscribe();
    }

    public bool IsBatchedUpdateActive => isActiveAndEnabled && player != null;

    public float BatchedUpdateInterval => 0.05f;

    public RelicTickArchetype BatchedTickArchetype => RelicTickArchetype.PlayerState;

    public void TickFromRelicBatch(float now, float deltaTime)
    {
        bool active = IsCritBuffActive;
        if (active == wasActive)
            return;

        wasActive = active;
        player?.Progression?.NotifyStatsChanged();
    }

    private void TrySubscribe()
    {
        if (subscribed || player == null)
            return;

        player.OnBeforeMeleeHit += OnBeforeMeleeHit;
        subscribed = true;
    }

    private void TryUnsubscribe()
    {
        if (!subscribed || player == null)
            return;

        player.OnBeforeMeleeHit -= OnBeforeMeleeHit;
        subscribed = false;
    }

    private void OnBeforeMeleeHit(Combatant target)
    {
        if (cfg == null || target == null)
            return;

        int targetId = target.GetInstanceID();
        if (targetId == lastTargetId)
            return;

        lastTargetId = targetId;
        critBuffUntil = Time.time + cfg.baseBuffDuration + cfg.extraDurationPerStack * Mathf.Max(0, stacks - 1);

        float staminaGain = cfg.baseStaminaGain + cfg.extraStaminaGainPerStack * Mathf.Max(0, stacks - 1);
        player?.Progression?.AddStamina(staminaGain);
        player?.Progression?.NotifyStatsChanged();
    }
}
