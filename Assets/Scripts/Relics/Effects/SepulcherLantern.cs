using System.Collections.Generic;
using UnityEngine;
using GrassSim.Combat;
using GrassSim.Core;

[CreateAssetMenu(
    menuName = "GrassSim/Relics/Effects/Uncommon/Sepulcher Lantern",
    fileName = "Relic_SepulcherLantern"
)]
public class SepulcherLantern : RelicEffect
{
    [Header("Trigger")]
    [Min(1)] public int hitsPerWither = 4;

    [Header("Wither")]
    public float witherDuration = 4f;
    [Range(0f, 1f)] public float baseOutgoingReduction = 0.16f;
    [Range(0f, 1f)] public float outgoingReductionPerStack = 0.03f;
    [Range(0f, 1f)] public float baseSlowPercent = 0.18f;
    [Range(0f, 1f)] public float slowPercentPerStack = 0.03f;
    public float transferRadius = 10f;
    public LayerMask enemyMask;

    public override void OnAcquire(PlayerRelicController player, int stacks)
    {
        Attach(player)?.Configure(this, stacks);
    }

    public override void OnStack(PlayerRelicController player, int stacks)
    {
        Attach(player)?.Configure(this, stacks);
    }

    private SepulcherLanternRuntime Attach(PlayerRelicController player)
    {
        if (player == null)
            return null;

        var rt = player.GetComponent<SepulcherLanternRuntime>();
        if (rt == null)
            rt = player.gameObject.AddComponent<SepulcherLanternRuntime>();

        return rt;
    }
}

public class SepulcherLanternRuntime : MonoBehaviour, IRelicBatchedUpdate, IRelicBatchedCadence
{
    private static readonly Color WitherColor = new(0.58f, 0.95f, 0.8f, 0.95f);

    private readonly Dictionary<Combatant, float> markedUntil = new();

    private PlayerRelicController player;
    private SepulcherLantern cfg;
    private int stacks;
    private int hitCounter;
    private bool subscribed;
    private float nextCleanupAt;

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

    public void Configure(SepulcherLantern config, int stackCount)
    {
        cfg = config;
        stacks = Mathf.Max(1, stackCount);
        EnemyQueryService.ConfigureOwnerBudget(this, RelicQueryBudgetProfiles.For(BatchedTickArchetype));
        TrySubscribe();
    }

    public bool IsBatchedUpdateActive => isActiveAndEnabled && cfg != null;

    public float BatchedUpdateInterval => 0.2f;

    public RelicTickArchetype BatchedTickArchetype => RelicTickArchetype.PlayerAura;

    public void TickFromRelicBatch(float now, float deltaTime)
    {
        if (now < nextCleanupAt)
            return;

        nextCleanupAt = now + 0.5f;
        CleanupExpired(now);
    }

    private void TrySubscribe()
    {
        if (subscribed || player == null)
            return;

        player.OnMeleeHitDealt += OnMeleeHit;
        player.OnMeleeKill += OnMeleeKill;
        subscribed = true;
    }

    private void TryUnsubscribe()
    {
        if (!subscribed || player == null)
            return;

        player.OnMeleeHitDealt -= OnMeleeHit;
        player.OnMeleeKill -= OnMeleeKill;
        subscribed = false;
    }

    private void OnMeleeHit(Combatant target, float damage, bool isCrit)
    {
        if (cfg == null || target == null || target.IsDead)
            return;

        if (target.GetComponent<PlayerProgressionController>() != null)
            return;

        hitCounter++;
        if (hitCounter < Mathf.Max(1, cfg.hitsPerWither))
            return;

        hitCounter = 0;
        ApplyWither(target, cfg.witherDuration);
    }

    private void OnMeleeKill(Combatant target, float damage, bool isCrit)
    {
        if (cfg == null || target == null)
            return;

        if (!markedUntil.TryGetValue(target, out float expiresAt))
            return;

        markedUntil.Remove(target);

        float remaining = expiresAt - Time.time;
        if (remaining <= 0f)
            return;

        var nextTarget = FindNearestEnemy(target.transform.position, target);
        if (nextTarget != null)
            ApplyWither(nextTarget, remaining);
    }

    private void ApplyWither(Combatant target, float duration)
    {
        if (target == null || target.IsDead || duration <= 0f)
            return;

        float outgoingReduction = Mathf.Clamp01(
            cfg.baseOutgoingReduction + cfg.outgoingReductionPerStack * Mathf.Max(0, stacks - 1)
        );

        float slowPercent = Mathf.Clamp01(
            cfg.baseSlowPercent + cfg.slowPercentPerStack * Mathf.Max(0, stacks - 1)
        );

        var outgoingDebuff = target.GetComponent<RelicOutgoingDamageDebuff>();
        if (outgoingDebuff == null)
            outgoingDebuff = target.gameObject.AddComponent<RelicOutgoingDamageDebuff>();
        outgoingDebuff.Apply(outgoingReduction, duration);

        var slowDebuff = target.GetComponent<RelicMoveSpeedDebuff>();
        if (slowDebuff == null)
            slowDebuff = target.gameObject.AddComponent<RelicMoveSpeedDebuff>();
        slowDebuff.Apply(slowPercent, duration);

        markedUntil[target] = Time.time + duration;
        RelicGeneratedVfx.SpawnAttachedMarker(
            target.transform,
            0.76f,
            WitherColor,
            Mathf.Max(0.2f, duration),
            new Vector3(0f, 0.045f, 0f),
            "SepulcherLantern_Wither"
        );
    }

    private Combatant FindNearestEnemy(Vector3 origin, Combatant exclude)
    {
        float radius = Mathf.Max(0.1f, cfg.transferRadius);
        LayerMask mask = cfg.enemyMask.value != 0 ? cfg.enemyMask : LayerMask.GetMask("Enemy", "Zombie");

        Collider[] hits;
        if (mask.value != 0)
            hits = EnemyQueryService.OverlapSphere(origin, radius, mask, QueryTriggerInteraction.Ignore, this);
        else
            hits = EnemyQueryService.OverlapSphere(origin, radius, ~0, QueryTriggerInteraction.Ignore, this);

        Combatant best = null;
        float bestSqr = float.PositiveInfinity;

        for (int i = 0, hitCount = EnemyQueryService.GetLastHitCount(this); i < hitCount; i++)
        {
            var col = hits[i];
            if (col == null)
                continue;

            var combatant = EnemyQueryService.GetCombatant(col);
            if (combatant == null || combatant == exclude || combatant.IsDead)
                continue;

            if (combatant.GetComponent<PlayerProgressionController>() != null)
                continue;

            float sqr = (combatant.transform.position - origin).sqrMagnitude;
            if (sqr < bestSqr)
            {
                bestSqr = sqr;
                best = combatant;
            }
        }

        return best;
    }

    private void CleanupExpired(float now)
    {
        if (markedUntil.Count == 0)
            return;
        
        var expired = ListPool<Combatant>.Get();
        foreach (var kv in markedUntil)
        {
            if (kv.Key == null || now >= kv.Value)
                expired.Add(kv.Key);
        }

        for (int i = 0; i < expired.Count; i++)
            markedUntil.Remove(expired[i]);

        ListPool<Combatant>.Release(expired);
    }
}

