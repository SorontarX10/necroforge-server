using System.Collections.Generic;
using UnityEngine;
using GrassSim.Combat;
using GrassSim.Core;

[CreateAssetMenu(
    menuName = "GrassSim/Relics/Effects/Common/Sigil Of Rot",
    fileName = "Relic_SigilOfRot"
)]
public class SigilOfRot : RelicEffect
{
    [Header("Trigger")]
    [Min(1)] public int hitsPerMark = 5;

    [Header("Mark")]
    public float markDuration = 4f;
    [Range(0f, 1f)] public float baseDamageReduction = 0.12f;
    [Range(0f, 1f)] public float extraDamageReductionPerStack = 0.02f;
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

    private SigilOfRotRuntime Attach(PlayerRelicController player)
    {
        if (player == null) return null;
        var rt = player.GetComponent<SigilOfRotRuntime>();
        if (rt == null) rt = player.gameObject.AddComponent<SigilOfRotRuntime>();
        return rt;
    }
}

public class SigilOfRotRuntime : MonoBehaviour, IRelicBatchedUpdate, IRelicBatchedCadence
{
    private static readonly Color RotColor = new(0.66f, 0.9f, 0.42f, 0.95f);

    private readonly Dictionary<Combatant, float> markedUntil = new();

    private PlayerRelicController player;
    private SigilOfRot cfg;
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

    public void Configure(SigilOfRot config, int stackCount)
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
        CleanupExpiredMarks(now);
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

        // Ignore player as target.
        if (target.GetComponent<PlayerProgressionController>() != null)
            return;

        hitCounter++;
        if (hitCounter < Mathf.Max(1, cfg.hitsPerMark))
            return;

        hitCounter = 0;
        ApplyMark(target, cfg.markDuration);
    }

    private void OnMeleeKill(Combatant target, float damage, bool isCrit)
    {
        if (cfg == null || target == null)
            return;

        if (!markedUntil.TryGetValue(target, out float expiry))
            return;

        markedUntil.Remove(target);

        float remaining = expiry - Time.time;
        if (remaining <= 0f)
            return;

        var next = FindNearestEnemy(target.transform.position, target);
        if (next != null)
            ApplyMark(next, remaining);
    }

    private void ApplyMark(Combatant target, float duration)
    {
        if (target == null || target.IsDead || duration <= 0f)
            return;

        var debuff = target.GetComponent<RelicOutgoingDamageDebuff>();
        if (debuff == null)
            debuff = target.gameObject.AddComponent<RelicOutgoingDamageDebuff>();

        float reduction = Mathf.Clamp01(
            cfg.baseDamageReduction + cfg.extraDamageReductionPerStack * Mathf.Max(0, stacks - 1)
        );

        debuff.Apply(reduction, duration);
        markedUntil[target] = Time.time + duration;
        RelicGeneratedVfx.SpawnAttachedMarker(
            target.transform,
            0.74f,
            RotColor,
            Mathf.Max(0.2f, duration),
            new Vector3(0f, 0.045f, 0f),
            "SigilOfRot_Mark"
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

    private void CleanupExpiredMarks(float now)
    {
        if (markedUntil.Count == 0)
            return;

        var toRemove = ListPool<Combatant>.Get();
        foreach (var kv in markedUntil)
        {
            var combatant = kv.Key;
            if (combatant == null || now >= kv.Value)
                toRemove.Add(combatant);
        }

        for (int i = 0; i < toRemove.Count; i++)
            markedUntil.Remove(toRemove[i]);

        ListPool<Combatant>.Release(toRemove);
    }
}

internal static class ListPool<T>
{
    private static readonly Stack<List<T>> pool = new();

    public static List<T> Get()
    {
        return pool.Count > 0 ? pool.Pop() : new List<T>(8);
    }

    public static void Release(List<T> list)
    {
        list.Clear();
        pool.Push(list);
    }
}

