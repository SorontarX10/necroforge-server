using UnityEngine;
using GrassSim.Combat;
using GrassSim.Core;

[CreateAssetMenu(
    menuName = "GrassSim/Relics/Effects/Mythic/Throne Of The Pale Regent",
    fileName = "Relic_ThroneOfThePaleRegent"
)]
public class ThroneOfThePaleRegent : RelicEffect, IDamageModifier, ISpeedModifier
{
    [Header("Mark")]
    public float retargetInterval = 12f;
    public float markDuration = 10f;
    public float markSearchRadius = 40f;
    public float eliteHealthThreshold = 260f;

    [Header("Mirror")]
    [Range(0f, 1f)] public float mirrorDamagePercent = 0.4f;

    [Header("Regent Buff")]
    public float buffDuration = 6f;
    public float baseDamageBonus = 0.25f;
    public float damageBonusPerStack = 0.04f;
    public float baseSpeedBonus = 0.25f;
    public float speedBonusPerStack = 0.03f;
    public LayerMask enemyMask;

    public override void OnAcquire(PlayerRelicController player, int stacks)
    {
        Attach(player)?.Configure(this, stacks);
    }

    public override void OnStack(PlayerRelicController player, int stacks)
    {
        Attach(player)?.Configure(this, stacks);
    }

    public float GetDamageMultiplier(PlayerRelicController player, int stacks)
    {
        var rt = player != null ? player.GetComponent<ThroneOfThePaleRegentRuntime>() : null;
        if (rt == null || !rt.IsBuffActive)
            return 1f;

        return 1f + baseDamageBonus + damageBonusPerStack * Mathf.Max(0, stacks - 1);
    }

    public float GetSpeedBonus(PlayerRelicController player, int stacks)
    {
        var rt = player != null ? player.GetComponent<ThroneOfThePaleRegentRuntime>() : null;
        if (rt == null || !rt.IsBuffActive)
            return 0f;

        return baseSpeedBonus + speedBonusPerStack * Mathf.Max(0, stacks - 1);
    }

    private ThroneOfThePaleRegentRuntime Attach(PlayerRelicController player)
    {
        if (player == null)
            return null;

        var rt = player.GetComponent<ThroneOfThePaleRegentRuntime>();
        if (rt == null)
            rt = player.gameObject.AddComponent<ThroneOfThePaleRegentRuntime>();

        return rt;
    }
}

public class ThroneOfThePaleRegentRuntime : MonoBehaviour, IRelicBatchedUpdate, IRelicBatchedCadence
{
    private PlayerRelicController player;
    private ThroneOfThePaleRegent cfg;
    private int stacks;
    private bool subscribed;
    private bool wasBuffActive;

    private Combatant markedTarget;
    private float markEndsAt;
    private float nextRetargetAt;
    private float buffEndsAt;

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

    public void Configure(ThroneOfThePaleRegent config, int stackCount)
    {
        cfg = config;
        stacks = Mathf.Max(1, stackCount);
        EnemyQueryService.ConfigureOwnerBudget(this, RelicQueryBudgetProfiles.For(BatchedTickArchetype));
        if (nextRetargetAt <= 0f)
            nextRetargetAt = Time.time + 1.5f;

        TrySubscribe();
    }

    public bool IsBatchedUpdateActive => isActiveAndEnabled && cfg != null;

    public float BatchedUpdateInterval => 0.1f;

    public RelicTickArchetype BatchedTickArchetype => RelicTickArchetype.PlayerState;

    public void TickFromRelicBatch(float now, float deltaTime)
    {
        if (cfg == null)
            return;

        if (now >= nextRetargetAt)
        {
            nextRetargetAt = now + Mathf.Max(0.5f, cfg.retargetInterval);
            PickMark();
        }

        if (markedTarget != null && (markedTarget.IsDead || now >= markEndsAt))
            markedTarget = null;

        bool nowBuff = IsBuffActive;
        if (nowBuff != wasBuffActive)
        {
            wasBuffActive = nowBuff;
            player?.Progression?.NotifyStatsChanged();
        }
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
        if (cfg == null || damage <= 0f || target == null || target.IsDead)
            return;

        if (markedTarget == null || markedTarget.IsDead || Time.time >= markEndsAt)
            return;

        if (target == markedTarget)
            return;

        float mirror = damage * Mathf.Clamp01(cfg.mirrorDamagePercent);
        if (mirror > 0f)
            RelicDamageText.Deal(markedTarget, mirror, transform, cfg);
    }

    private void OnMeleeKill(Combatant target, float damage, bool isCrit)
    {
        if (cfg == null || target == null || target != markedTarget)
            return;

        buffEndsAt = Time.time + Mathf.Max(0.2f, cfg.buffDuration);
        markedTarget = null;
    }

    private void PickMark()
    {
        if (cfg == null)
            return;

        Collider[] hits;
        LayerMask mask = cfg.enemyMask.value != 0 ? cfg.enemyMask : LayerMask.GetMask("Enemy", "Zombie");
        if (mask.value != 0)
            hits = EnemyQueryService.OverlapSphere(transform.position, cfg.markSearchRadius, mask, QueryTriggerInteraction.Ignore, this);
        else
            hits = EnemyQueryService.OverlapSphere(transform.position, cfg.markSearchRadius, ~0, QueryTriggerInteraction.Ignore, this);

        Combatant best = null;
        float bestSqr = float.PositiveInfinity;

        for (int i = 0, hitCount = EnemyQueryService.GetLastHitCount(this); i < hitCount; i++)
        {
            var col = hits[i];
            if (col == null)
                continue;

            var c = EnemyQueryService.GetCombatant(col);
            if (c == null || c.IsDead)
                continue;

            if (c.GetComponent<PlayerProgressionController>() != null)
                continue;

            if (!IsEliteOrBoss(c))
                continue;

            float sqr = (c.transform.position - transform.position).sqrMagnitude;
            if (sqr < bestSqr)
            {
                bestSqr = sqr;
                best = c;
            }
        }

        markedTarget = best;
        markEndsAt = best != null ? Time.time + Mathf.Max(0.2f, cfg.markDuration) : 0f;
    }

    private bool IsEliteOrBoss(Combatant c)
    {
        if (c == null)
            return false;

        if (c.GetComponent<BossEnemyController>() != null)
            return true;

        return c.MaxHealth >= Mathf.Max(1f, cfg.eliteHealthThreshold);
    }
}

