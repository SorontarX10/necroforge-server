using System.Collections.Generic;
using UnityEngine;
using GrassSim.Combat;
using GrassSim.Core;

[CreateAssetMenu(
    menuName = "GrassSim/Relics/Effects/Legendary/Cathedral Emberheart",
    fileName = "Relic_CathedralEmberheart"
)]
public class CathedralEmberheart : RelicEffect, ISpeedModifier
{
    [Header("Embers")]
    [Min(1)] public int embersToIgnite = 4;

    [Header("Ashen State")]
    public float baseAshenDuration = 7f;
    public float ashenDurationPerStack = 0.5f;
    public float baseSpeedBonus = 0.2f;
    public float speedBonusPerStack = 0.03f;

    [Header("Burn Trail")]
    public float trailDuration = 1.6f;
    public float trailTickInterval = 0.2f;
    public float trailRadius = 1.1f;
    public float baseTrailDamagePerSecond = 18f;
    public float trailDamagePerStack = 3f;
    public LayerMask enemyMask;

    public override void OnAcquire(PlayerRelicController player, int stacks)
    {
        Attach(player)?.Configure(this, stacks);
    }

    public override void OnStack(PlayerRelicController player, int stacks)
    {
        Attach(player)?.Configure(this, stacks);
    }

    public float GetSpeedBonus(PlayerRelicController player, int stacks)
    {
        var rt = player != null ? player.GetComponent<CathedralEmberheartRuntime>() : null;
        if (rt == null || !rt.IsAshenActive)
            return 0f;

        return baseSpeedBonus + speedBonusPerStack * Mathf.Max(0, stacks - 1);
    }

    private CathedralEmberheartRuntime Attach(PlayerRelicController player)
    {
        if (player == null)
            return null;

        var rt = player.GetComponent<CathedralEmberheartRuntime>();
        if (rt == null)
            rt = player.gameObject.AddComponent<CathedralEmberheartRuntime>();

        return rt;
    }
}

public class CathedralEmberheartRuntime : MonoBehaviour, IRelicBatchedUpdate, IRelicBatchedCadence
{
    private static readonly Color EmberColor = new(1f, 0.42f, 0.14f, 0.95f);

    private struct TrailSegment
    {
        public Vector3 position;
        public float expiresAt;
    }

    private readonly List<TrailSegment> segments = new();
    private readonly HashSet<Combatant> trailHitSet = new();

    private PlayerRelicController player;
    private CathedralEmberheart cfg;
    private int stacks;
    private bool subscribed;
    private bool wasActive;

    private int embers;
    private float ashenEndsAt;
    private float nextTrailTickAt;

    public bool IsAshenActive => Time.time < ashenEndsAt;

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

    public void Configure(CathedralEmberheart config, int stackCount)
    {
        cfg = config;
        stacks = Mathf.Max(1, stackCount);
        EnemyQueryService.ConfigureOwnerBudget(this, 10);
        TrySubscribe();
    }

    public bool IsBatchedUpdateActive => isActiveAndEnabled && cfg != null;

    public float BatchedUpdateInterval => 0.05f;

    public RelicTickArchetype BatchedTickArchetype => RelicTickArchetype.PlayerAura;

    public void TickFromRelicBatch(float now, float deltaTime)
    {
        if (cfg == null)
            return;

        CleanupExpiredSegments(now);

        if (IsAshenActive && now >= nextTrailTickAt)
        {
            nextTrailTickAt = now + Mathf.Max(0.05f, cfg.trailTickInterval);
            TickTrailDamage();
        }

        bool nowActive = IsAshenActive;
        if (nowActive != wasActive)
        {
            wasActive = nowActive;
            player?.Progression?.NotifyStatsChanged();
        }
    }

    private void TrySubscribe()
    {
        if (subscribed || player == null)
            return;

        player.OnMeleeKill += OnMeleeKill;
        player.OnMeleeHitDealt += OnMeleeHit;
        subscribed = true;
    }

    private void TryUnsubscribe()
    {
        if (!subscribed || player == null)
            return;

        player.OnMeleeKill -= OnMeleeKill;
        player.OnMeleeHitDealt -= OnMeleeHit;
        subscribed = false;
    }

    private void OnMeleeKill(Combatant target, float damage, bool isCrit)
    {
        if (cfg == null)
            return;

        if (IsAshenActive)
            return;

        embers = Mathf.Min(Mathf.Max(1, cfg.embersToIgnite), embers + 1);
        if (embers >= Mathf.Max(1, cfg.embersToIgnite))
            ActivateAshenState();
    }

    private void OnMeleeHit(Combatant target, float damage, bool isCrit)
    {
        if (cfg == null || !IsAshenActive || target == null || target.IsDead)
            return;

        AddTrailSegment(target.transform.position + Vector3.up * 0.1f);
    }

    private void ActivateAshenState()
    {
        embers = 0;
        float duration = cfg.baseAshenDuration + cfg.ashenDurationPerStack * Mathf.Max(0, stacks - 1);
        ashenEndsAt = Time.time + Mathf.Max(0.2f, duration);

        RelicGeneratedVfx.SpawnGroundCircle(
            transform.position + Vector3.up * 0.04f,
            Mathf.Max(0.9f, cfg.trailRadius * 1.12f),
            EmberColor,
            Mathf.Min(1.2f, Mathf.Max(0.35f, duration)),
            transform,
            new Vector3(0f, 0.04f, 0f),
            "CathedralEmberheart_AshenAura"
        );
        RelicDamageText.PlayGeneratedEventFeedback(transform, RelicRarity.Legendary, 1.12f);
    }

    private void AddTrailSegment(Vector3 position)
    {
        segments.Add(new TrailSegment
        {
            position = position,
            expiresAt = Time.time + Mathf.Max(0.1f, cfg.trailDuration)
        });

        RelicGeneratedVfx.SpawnGroundCircle(
            position + Vector3.up * 0.02f,
            Mathf.Max(0.3f, cfg.trailRadius * 0.58f),
            EmberColor,
            Mathf.Min(0.85f, Mathf.Max(0.25f, cfg.trailDuration)),
            null,
            default,
            "CathedralEmberheart_EmberTrail"
        );
    }

    private void TickTrailDamage()
    {
        if (segments.Count == 0)
            return;

        LayerMask mask = cfg.enemyMask.value != 0 ? cfg.enemyMask : LayerMask.GetMask("Enemy", "Zombie");
        float tick = Mathf.Max(0.05f, cfg.trailTickInterval);
        float dps = cfg.baseTrailDamagePerSecond + cfg.trailDamagePerStack * Mathf.Max(0, stacks - 1);
        float damage = Mathf.Max(1f, dps * tick);
        float radius = Mathf.Max(0.1f, cfg.trailRadius);

        trailHitSet.Clear();

        for (int i = 0; i < segments.Count; i++)
        {
            Collider[] hits;
            if (mask.value != 0)
                hits = EnemyQueryService.OverlapSphere(segments[i].position, radius, mask, QueryTriggerInteraction.Ignore, this);
            else
                hits = EnemyQueryService.OverlapSphere(segments[i].position, radius, ~0, QueryTriggerInteraction.Ignore, this);

            for (int h = 0, hitCount = EnemyQueryService.GetLastHitCount(this); h < hitCount; h++)
            {
                var col = hits[h];
                if (col == null)
                    continue;

                var combatant = EnemyQueryService.GetCombatant(col);
                if (combatant == null || combatant.IsDead)
                    continue;

                if (combatant.GetComponent<PlayerProgressionController>() != null)
                    continue;

                if (!trailHitSet.Add(combatant))
                    continue;

                RelicDamageText.Deal(combatant, damage, transform, cfg);
            }
        }
    }

    private void CleanupExpiredSegments(float now)
    {
        if (segments.Count == 0)
            return;
        for (int i = segments.Count - 1; i >= 0; i--)
        {
            if (now >= segments[i].expiresAt)
                segments.RemoveAt(i);
        }
    }
}

