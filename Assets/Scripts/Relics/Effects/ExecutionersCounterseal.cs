using System.Collections.Generic;
using UnityEngine;
using GrassSim.Combat;
using GrassSim.Core;

[CreateAssetMenu(
    menuName = "GrassSim/Relics/Effects/Rare/Executioner's Counterseal",
    fileName = "Relic_ExecutionersCounterseal"
)]
public class ExecutionersCounterseal : RelicEffect, IIncomingHitModifier
{
    [Header("Counterseal")]
    [Range(0f, 1f)] public float firstHitReduction = 0.5f;
    public float recordDuration = 10f;

    [Header("Execution Wave")]
    public float waveRadius = 6f;
    public float baseWaveDamage = 35f;
    public float waveDamageFromKillMultiplier = 0.8f;
    public float waveDamagePerStackMultiplier = 0.1f;
    public LayerMask enemyMask;

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
        var rt = player != null ? player.GetComponent<ExecutionersCountersealRuntime>() : null;
        if (rt == null)
            return damage;

        return rt.ModifyIncomingDamage(attacker, damage);
    }

    private ExecutionersCountersealRuntime Attach(PlayerRelicController player)
    {
        if (player == null)
            return null;

        var rt = player.GetComponent<ExecutionersCountersealRuntime>();
        if (rt == null)
            rt = player.gameObject.AddComponent<ExecutionersCountersealRuntime>();

        return rt;
    }
}

public class ExecutionersCountersealRuntime : MonoBehaviour, IRelicBatchedUpdate, IRelicBatchedCadence
{
    private static readonly Color ExecutionWaveColor = new(1f, 0.38f, 0.3f, 0.95f);

    private readonly HashSet<int> reducedAttackers = new();
    private readonly Dictionary<int, float> recordedUntil = new();
    private readonly List<int> expiredAttackers = new(16);

    private PlayerRelicController player;
    private ExecutionersCounterseal cfg;
    private int stacks;
    private bool subscribed;

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

    public void Configure(ExecutionersCounterseal config, int stackCount)
    {
        cfg = config;
        stacks = Mathf.Max(1, stackCount);
        EnemyQueryService.ConfigureOwnerBudget(this, RelicQueryBudgetProfiles.For(RelicTickArchetype.EnemyDebuff));
        TrySubscribe();
    }

    public float ModifyIncomingDamage(Combatant attacker, float damage)
    {
        if (cfg == null || attacker == null || damage <= 0f)
            return damage;

        int id = attacker.GetInstanceID();
        if (!reducedAttackers.Contains(id))
        {
            reducedAttackers.Add(id);
            recordedUntil[id] = Time.time + Mathf.Max(0.1f, cfg.recordDuration);
            return damage * (1f - Mathf.Clamp01(cfg.firstHitReduction));
        }

        if (recordedUntil.TryGetValue(id, out float expiry) && Time.time >= expiry)
            recordedUntil.Remove(id);

        return damage;
    }

    public bool IsBatchedUpdateActive => isActiveAndEnabled;

    public float BatchedUpdateInterval => 0.5f;

    public RelicTickArchetype BatchedTickArchetype => RelicTickArchetype.EnemyDebuff;

    public void TickFromRelicBatch(float now, float deltaTime)
    {
        CleanupExpiredRecords(now);
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
        if (cfg == null || target == null)
            return;

        int id = target.GetInstanceID();
        if (!recordedUntil.TryGetValue(id, out float expiry) || Time.time >= expiry)
            return;

        recordedUntil.Remove(id);
        TriggerExecutionWave(target.transform.position, damage);
    }

    private void TriggerExecutionWave(Vector3 center, float killDamage)
    {
        float multiplier = cfg.waveDamageFromKillMultiplier +
            cfg.waveDamagePerStackMultiplier * Mathf.Max(0, stacks - 1);
        float waveDamage = Mathf.Max(cfg.baseWaveDamage, killDamage * Mathf.Max(0f, multiplier));

        RelicGeneratedVfx.SpawnGroundCircle(
            center + Vector3.up * 0.04f,
            Mathf.Max(0.8f, cfg.waveRadius),
            ExecutionWaveColor,
            0.45f,
            null,
            default,
            "ExecutionersCounterseal_Wave"
        );

        LayerMask mask = cfg.enemyMask.value != 0 ? cfg.enemyMask : LayerMask.GetMask("Enemy", "Zombie");
        Collider[] hits;
        if (mask.value != 0)
            hits = EnemyQueryService.OverlapSphere(center, cfg.waveRadius, mask, QueryTriggerInteraction.Ignore, this);
        else
            hits = EnemyQueryService.OverlapSphere(center, cfg.waveRadius, ~0, QueryTriggerInteraction.Ignore, this);

        for (int i = 0, hitCount = EnemyQueryService.GetLastHitCount(this); i < hitCount; i++)
        {
            var col = hits[i];
            if (col == null)
                continue;

            var combatant = EnemyQueryService.GetCombatant(col);
            if (combatant == null || combatant.IsDead)
                continue;

            if (combatant.GetComponent<PlayerProgressionController>() != null)
                continue;

            RelicDamageText.Deal(combatant, waveDamage, transform, cfg);
        }
    }

    private void CleanupExpiredRecords(float now)
    {
        if (recordedUntil.Count == 0)
            return;

        expiredAttackers.Clear();
        foreach (var kv in recordedUntil)
        {
            if (now >= kv.Value)
                expiredAttackers.Add(kv.Key);
        }

        for (int i = 0; i < expiredAttackers.Count; i++)
            recordedUntil.Remove(expiredAttackers[i]);
    }
}

