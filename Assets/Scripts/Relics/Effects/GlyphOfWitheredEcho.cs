using System.Collections.Generic;
using UnityEngine;
using GrassSim.Combat;
using GrassSim.Core;

[CreateAssetMenu(
    menuName = "GrassSim/Relics/Effects/Legendary/Glyph Of Withered Echo",
    fileName = "Relic_GlyphOfWitheredEcho"
)]
public class GlyphOfWitheredEcho : RelicEffect
{
    [Header("Trigger")]
    [Min(1)] public int hitsRequired = 5;

    [Header("Echo")]
    public float echoDelay = 0.35f;
    public float echoDamagePercent = 0.8f;
    public float jumpSearchRadius = 6f;
    public LayerMask enemyMask;

    public override void OnAcquire(PlayerRelicController player, int stacks)
    {
        Attach(player)?.Configure(this, stacks);
    }

    public override void OnStack(PlayerRelicController player, int stacks)
    {
        Attach(player)?.Configure(this, stacks);
    }

    private GlyphOfWitheredEchoRuntime Attach(PlayerRelicController player)
    {
        if (player == null)
            return null;

        var rt = player.GetComponent<GlyphOfWitheredEchoRuntime>();
        if (rt == null)
            rt = player.gameObject.AddComponent<GlyphOfWitheredEchoRuntime>();

        return rt;
    }
}

public class GlyphOfWitheredEchoRuntime : MonoBehaviour, IRelicBatchedUpdate, IRelicBatchedCadence
{
    private static readonly Color EchoColor = new(0.7f, 0.55f, 1f, 0.95f);

    private struct PendingEcho
    {
        public Combatant target;
        public Vector3 fallbackPosition;
        public float sourceDamage;
        public float executeAt;
    }

    private readonly List<PendingEcho> pending = new();

    private PlayerRelicController player;
    private GlyphOfWitheredEcho cfg;
    private bool subscribed;
    private int hitCounter;

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
        pending.Clear();
    }

    public void Configure(GlyphOfWitheredEcho config, int stackCount)
    {
        cfg = config;
        EnemyQueryService.ConfigureOwnerBudget(this, RelicQueryBudgetProfiles.For(RelicTickArchetype.EnemyDebuff));
        TrySubscribe();
    }

    public bool IsBatchedUpdateActive => isActiveAndEnabled && cfg != null && pending.Count > 0;

    public float BatchedUpdateInterval => 0.04f;

    public RelicTickArchetype BatchedTickArchetype => RelicTickArchetype.EnemyDebuff;

    public void TickFromRelicBatch(float now, float deltaTime)
    {
        for (int i = pending.Count - 1; i >= 0; i--)
        {
            var echo = pending[i];
            if (now < echo.executeAt)
                continue;

            ResolveEcho(echo);
            pending.RemoveAt(i);
        }
    }

    private void TrySubscribe()
    {
        if (subscribed || player == null)
            return;

        player.OnMeleeHitDealt += OnMeleeHit;
        subscribed = true;
    }

    private void TryUnsubscribe()
    {
        if (!subscribed || player == null)
            return;

        player.OnMeleeHitDealt -= OnMeleeHit;
        subscribed = false;
    }

    private void OnMeleeHit(Combatant target, float damage, bool isCrit)
    {
        if (cfg == null || target == null || target.IsDead || damage <= 0f)
            return;

        hitCounter++;
        if (hitCounter < Mathf.Max(1, cfg.hitsRequired))
            return;

        hitCounter = 0;
        pending.Add(new PendingEcho
        {
            target = target,
            fallbackPosition = target.transform.position,
            sourceDamage = damage,
            executeAt = Time.time + Mathf.Max(0.01f, cfg.echoDelay)
        });
    }

    private void ResolveEcho(PendingEcho echo)
    {
        Combatant chosen = null;

        if (echo.target != null && !echo.target.IsDead)
        {
            chosen = echo.target;
        }
        else
        {
            chosen = FindNearestEnemy(echo.fallbackPosition);
        }

        if (chosen == null || chosen.IsDead)
            return;

        Vector3 start = transform.position + Vector3.up * 1.05f;
        Vector3 end = chosen.transform.position + Vector3.up * 1.05f;
        RelicGeneratedVfx.SpawnTravelOrb(start, end, 0.24f, EchoColor, 0.24f, "GlyphWitheredEcho_Orb");
        RelicGeneratedVfx.SpawnBeam(start, end, 0.05f, EchoColor, 0.16f, "GlyphWitheredEcho_Beam");

        float damage = Mathf.Max(1f, echo.sourceDamage * Mathf.Max(0f, cfg.echoDamagePercent));
        RelicDamageText.Deal(chosen, damage, transform, cfg);
    }

    private Combatant FindNearestEnemy(Vector3 center)
    {
        LayerMask mask = cfg.enemyMask.value != 0 ? cfg.enemyMask : LayerMask.GetMask("Enemy", "Zombie");

        Collider[] hits;
        if (mask.value != 0)
            hits = EnemyQueryService.OverlapSphere(center, cfg.jumpSearchRadius, mask, QueryTriggerInteraction.Ignore, this);
        else
            hits = EnemyQueryService.OverlapSphere(center, cfg.jumpSearchRadius, ~0, QueryTriggerInteraction.Ignore, this);

        Combatant best = null;
        float bestSqr = float.PositiveInfinity;

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

            float sqr = (combatant.transform.position - center).sqrMagnitude;
            if (sqr < bestSqr)
            {
                bestSqr = sqr;
                best = combatant;
            }
        }

        return best;
    }
}

