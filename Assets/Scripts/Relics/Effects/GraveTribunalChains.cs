using System.Collections.Generic;
using UnityEngine;
using GrassSim.Combat;
using GrassSim.Core;

[CreateAssetMenu(
    menuName = "GrassSim/Relics/Effects/Legendary/Grave Tribunal Chains",
    fileName = "Relic_GraveTribunalChains"
)]
public class GraveTribunalChains : RelicEffect
{
    [Header("Timing")]
    public float baseCooldown = 20f;
    public float cooldownReductionPerStack = 1f;
    public float baseDuration = 5f;
    public float durationPerStack = 0.3f;

    [Header("Binding")]
    [Min(1)] public int maxTargets = 6;
    public float searchRadius = 12f;
    [Range(0f, 1f)] public float slowPercent = 0.2f;
    [Range(0f, 1f)] public float outgoingDamageReduction = 0.25f;
    public LayerMask enemyMask;

    [Header("Sustain")]
    [Range(0f, 1f)] public float healPerBoundKillPercent = 0.02f;
    [Range(0f, 1f)] public float baseHealCapPercent = 0.1f;
    [Range(0f, 1f)] public float healCapPercentPerStack = 0.01f;

    public override void OnAcquire(PlayerRelicController player, int stacks)
    {
        Attach(player)?.Configure(this, stacks);
    }

    public override void OnStack(PlayerRelicController player, int stacks)
    {
        Attach(player)?.Configure(this, stacks);
    }

    private GraveTribunalChainsRuntime Attach(PlayerRelicController player)
    {
        if (player == null)
            return null;

        var rt = player.GetComponent<GraveTribunalChainsRuntime>();
        if (rt == null)
            rt = player.gameObject.AddComponent<GraveTribunalChainsRuntime>();

        return rt;
    }
}

public class GraveTribunalChainsRuntime : MonoBehaviour, IRelicBatchedUpdate, IRelicBatchedCadence
{
    private struct Candidate
    {
        public Combatant combatant;
        public float sqrDistance;
    }

    private readonly Dictionary<int, float> boundUntil = new();
    private readonly HashSet<int> seenIds = new();
    private readonly List<Candidate> candidateBuffer = new(64);
    private readonly List<Combatant> targetBuffer = new(16);
    private readonly List<int> expiredBindIds = new(32);

    private PlayerRelicController player;
    private GraveTribunalChains cfg;
    private int stacks;
    private bool subscribed;

    private float activeEndsAt;
    private float nextCastAt;
    private float healedThisCast;
    private float healCapThisCast;

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
        boundUntil.Clear();
    }

    public void Configure(GraveTribunalChains config, int stackCount)
    {
        cfg = config;
        stacks = Mathf.Max(1, stackCount);
        EnemyQueryService.ConfigureOwnerBudget(this, RelicQueryBudgetProfiles.For(BatchedTickArchetype));
        if (nextCastAt <= 0f)
            nextCastAt = Time.time + 1.5f;

        TrySubscribe();
    }

    public bool IsBatchedUpdateActive => isActiveAndEnabled && cfg != null;

    public float BatchedUpdateInterval => 0.1f;

    public RelicTickArchetype BatchedTickArchetype => RelicTickArchetype.PlayerAura;

    public void TickFromRelicBatch(float now, float deltaTime)
    {
        if (cfg == null)
            return;

        if (!IsActive(now) && now >= nextCastAt)
            ActivateChains();

        if (boundUntil.Count > 0)
            CleanupExpiredBinds(now);
    }

    private bool IsActive(float now)
    {
        return now < activeEndsAt;
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

    private void ActivateChains()
    {
        float duration = cfg.baseDuration + cfg.durationPerStack * Mathf.Max(0, stacks - 1);
        float cooldown = cfg.baseCooldown - cfg.cooldownReductionPerStack * Mathf.Max(0, stacks - 1);

        activeEndsAt = Time.time + Mathf.Max(0.2f, duration);
        nextCastAt = Time.time + Mathf.Max(2f, cooldown);

        boundUntil.Clear();
        healedThisCast = 0f;

        if (player != null && player.Progression != null)
        {
            float capPct = cfg.baseHealCapPercent + cfg.healCapPercentPerStack * Mathf.Max(0, stacks - 1);
            healCapThisCast = player.Progression.MaxHealth * Mathf.Clamp(capPct, 0f, 1f);
        }
        else
        {
            healCapThisCast = 0f;
        }

        var targets = FindNearestTargets(Mathf.Max(1, cfg.maxTargets));
        for (int i = 0; i < targets.Count; i++)
        {
            var target = targets[i];
            if (target == null || target.IsDead)
                continue;

            var slow = target.GetComponent<RelicMoveSpeedDebuff>();
            if (slow == null)
                slow = target.gameObject.AddComponent<RelicMoveSpeedDebuff>();
            slow.Apply(Mathf.Clamp01(cfg.slowPercent), Mathf.Max(0.1f, duration));

            var outgoing = target.GetComponent<RelicOutgoingDamageDebuff>();
            if (outgoing == null)
                outgoing = target.gameObject.AddComponent<RelicOutgoingDamageDebuff>();
            outgoing.Apply(Mathf.Clamp01(cfg.outgoingDamageReduction), Mathf.Max(0.1f, duration));

            boundUntil[target.GetInstanceID()] = activeEndsAt;
        }
    }

    private void OnMeleeKill(Combatant target, float damage, bool isCrit)
    {
        if (cfg == null || target == null || player == null || player.Progression == null)
            return;

        int id = target.GetInstanceID();
        if (!boundUntil.TryGetValue(id, out float expiry) || Time.time >= expiry)
            return;

        if (healedThisCast >= healCapThisCast)
            return;

        float heal = player.Progression.MaxHealth * Mathf.Clamp01(cfg.healPerBoundKillPercent);
        float allowed = Mathf.Min(heal, healCapThisCast - healedThisCast);
        if (allowed <= 0f)
            return;

        player.Progression.Heal(allowed);
        healedThisCast += allowed;
    }

    private List<Combatant> FindNearestTargets(int count)
    {
        LayerMask mask = cfg.enemyMask.value != 0 ? cfg.enemyMask : LayerMask.GetMask("Enemy", "Zombie");

        Collider[] hits;
        if (mask.value != 0)
            hits = EnemyQueryService.OverlapSphere(transform.position, cfg.searchRadius, mask, QueryTriggerInteraction.Ignore, this);
        else
            hits = EnemyQueryService.OverlapSphere(transform.position, cfg.searchRadius, ~0, QueryTriggerInteraction.Ignore, this);

        seenIds.Clear();
        candidateBuffer.Clear();
        targetBuffer.Clear();

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

            int id = combatant.GetInstanceID();
            if (!seenIds.Add(id))
                continue;

            candidateBuffer.Add(new Candidate
            {
                combatant = combatant,
                sqrDistance = (combatant.transform.position - transform.position).sqrMagnitude
            });
        }

        candidateBuffer.Sort((a, b) => a.sqrDistance.CompareTo(b.sqrDistance));

        int take = Mathf.Min(count, candidateBuffer.Count);
        for (int i = 0; i < take; i++)
            targetBuffer.Add(candidateBuffer[i].combatant);

        return targetBuffer;
    }

    private void CleanupExpiredBinds(float now)
    {
        if (boundUntil.Count == 0)
            return;
        expiredBindIds.Clear();

        foreach (var kv in boundUntil)
        {
            if (now >= kv.Value)
                expiredBindIds.Add(kv.Key);
        }

        for (int i = 0; i < expiredBindIds.Count; i++)
            boundUntil.Remove(expiredBindIds[i]);

        expiredBindIds.Clear();
    }
}

