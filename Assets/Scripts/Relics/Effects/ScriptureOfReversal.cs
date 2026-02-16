using System.Collections.Generic;
using UnityEngine;
using GrassSim.Combat;

[CreateAssetMenu(
    menuName = "GrassSim/Relics/Effects/Legendary/Scripture Of Reversal",
    fileName = "Relic_ScriptureOfReversal"
)]
public class ScriptureOfReversal : RelicEffect, IIncomingHitModifier
{
    [Header("Trigger")]
    public float cooldown = 60f;

    [Header("Rewind")]
    public float rewindSeconds = 2f;
    [Range(0.05f, 1f)] public float healthRestorePercent = 0.35f;
    public float sampleInterval = 0.1f;

    public override void OnAcquire(PlayerRelicController player, int stacks)
    {
        Attach(player)?.Configure(this, stacks);
    }

    public override void OnStack(PlayerRelicController player, int stacks)
    {
        Attach(player)?.Configure(this, stacks);
    }

    public float ModifyIncomingDamage(PlayerRelicController player, Combatant attacker, float damage, int stacks)
    {
        var rt = player != null ? player.GetComponent<ScriptureOfReversalRuntime>() : null;
        if (rt == null)
            return damage;

        return rt.ModifyIncomingDamage(damage);
    }

    private ScriptureOfReversalRuntime Attach(PlayerRelicController player)
    {
        if (player == null)
            return null;

        var rt = player.GetComponent<ScriptureOfReversalRuntime>();
        if (rt == null)
            rt = player.gameObject.AddComponent<ScriptureOfReversalRuntime>();

        return rt;
    }
}

public class ScriptureOfReversalRuntime : MonoBehaviour, IRelicBatchedUpdate, IRelicBatchedCadence
{
    private struct Snapshot
    {
        public float time;
        public Vector3 position;
    }

    private readonly List<Snapshot> history = new();

    private PlayerRelicController player;
    private ScriptureOfReversal cfg;
    private int stacks;

    private float nextReadyAt;

    private void Awake()
    {
        player = GetComponent<PlayerRelicController>();
    }

    private void OnEnable()
    {
        RelicBatchedTickSystem.Register(this);
    }

    private void OnDisable()
    {
        RelicBatchedTickSystem.Unregister(this);
        history.Clear();
    }

    public void Configure(ScriptureOfReversal config, int stackCount)
    {
        cfg = config;
        stacks = Mathf.Max(1, stackCount);
    }

    public bool IsBatchedUpdateActive => isActiveAndEnabled && cfg != null;

    public float BatchedUpdateInterval => Mathf.Max(0.02f, cfg != null ? cfg.sampleInterval : 0.1f);

    public RelicTickArchetype BatchedTickArchetype => RelicTickArchetype.PlayerState;

    public void TickFromRelicBatch(float now, float deltaTime)
    {
        history.Add(new Snapshot
        {
            time = now,
            position = transform.position
        });

        float keepSince = now - Mathf.Max(0.1f, cfg.rewindSeconds + 2f);
        while (history.Count > 0 && history[0].time < keepSince)
            history.RemoveAt(0);
    }

    public float ModifyIncomingDamage(float incomingDamage)
    {
        if (cfg == null || player == null || player.Progression == null || incomingDamage <= 0f)
            return incomingDamage;

        if (Time.time < nextReadyAt)
            return incomingDamage;

        float predicted = PredictFinalDamage(incomingDamage);
        if (predicted < player.Progression.CurrentHealth)
            return incomingDamage;

        Vector3 rewindPos = GetRewindPosition(Time.time - Mathf.Max(0.05f, cfg.rewindSeconds));
        transform.position = rewindPos;

        float restoreTo = player.Progression.MaxHealth * Mathf.Clamp01(cfg.healthRestorePercent);
        float healAmount = Mathf.Max(0f, restoreTo - player.Progression.CurrentHealth);
        if (healAmount > 0f)
            player.Progression.Heal(healAmount);

        nextReadyAt = Time.time + Mathf.Max(0.5f, cfg.cooldown);
        return 0f;
    }

    private float PredictFinalDamage(float rawDamage)
    {
        float reduction = 0f;
        if (player.Progression != null && player.Progression.stats != null)
            reduction = player.Progression.stats.damageReduction;

        reduction += player.GetDamageReductionBonus();
        reduction = CombatBalanceCaps.ClampDamageReduction(reduction);

        float damageAfterReduction = rawDamage * (1f - reduction);
        float damageAfterBarrier = Mathf.Max(0f, damageAfterReduction - player.Progression.CurrentBarrier);
        return damageAfterBarrier;
    }

    private Vector3 GetRewindPosition(float targetTime)
    {
        if (history.Count == 0)
            return transform.position;

        Vector3 best = history[0].position;
        for (int i = 0; i < history.Count; i++)
        {
            if (history[i].time <= targetTime)
                best = history[i].position;
            else
                break;
        }

        return best;
    }
}
