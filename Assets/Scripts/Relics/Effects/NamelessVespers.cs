using System.Collections.Generic;
using UnityEngine;
using GrassSim.Combat;
using GrassSim.Core;

[CreateAssetMenu(
    menuName = "GrassSim/Relics/Effects/Rare/Nameless Vespers",
    fileName = "Relic_NamelessVespers"
)]
public class NamelessVespers : RelicEffect
{
    [Header("Trigger")]
    [Min(1)] public int uniqueHitsRequired = 6;
    public float uniqueWindow = 8f;

    [Header("Bell")]
    public float bellRadius = 7f;
    public float silenceDuration = 1.8f;
    public float baseStaminaRestore = 25f;
    public float staminaRestorePerStack = 3f;
    public LayerMask enemyMask;

    public override void OnAcquire(PlayerRelicController player, int stacks)
    {
        Attach(player)?.Configure(this, stacks);
    }

    public override void OnStack(PlayerRelicController player, int stacks)
    {
        Attach(player)?.Configure(this, stacks);
    }

    private NamelessVespersRuntime Attach(PlayerRelicController player)
    {
        if (player == null)
            return null;

        var rt = player.GetComponent<NamelessVespersRuntime>();
        if (rt == null)
            rt = player.gameObject.AddComponent<NamelessVespersRuntime>();

        return rt;
    }
}

public class NamelessVespersRuntime : MonoBehaviour, IRelicBatchedUpdate, IRelicBatchedCadence
{
    private readonly Dictionary<int, float> uniqueHitExpiry = new();
    private readonly List<int> expiredKeys = new(16);

    private PlayerRelicController player;
    private NamelessVespers cfg;
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
        uniqueHitExpiry.Clear();
        expiredKeys.Clear();
    }

    public void Configure(NamelessVespers config, int stackCount)
    {
        cfg = config;
        stacks = Mathf.Max(1, stackCount);
        EnemyQueryService.ConfigureOwnerBudget(this, 20);
        TrySubscribe();
    }

    public bool IsBatchedUpdateActive => isActiveAndEnabled && cfg != null;

    public float BatchedUpdateInterval => 0.3f;

    public RelicTickArchetype BatchedTickArchetype => RelicTickArchetype.EnemyDebuff;

    public void TickFromRelicBatch(float now, float deltaTime)
    {
        CleanupExpired(now);
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
        if (cfg == null || target == null || target.IsDead)
            return;

        int id = target.GetInstanceID();
        uniqueHitExpiry[id] = Time.time + Mathf.Max(0.2f, cfg.uniqueWindow);

        CleanupExpired(Time.time);
        if (uniqueHitExpiry.Count < Mathf.Max(1, cfg.uniqueHitsRequired))
            return;

        RingBell();
        uniqueHitExpiry.Clear();
    }

    private void RingBell()
    {
        if (cfg == null)
            return;

        LayerMask mask = cfg.enemyMask.value != 0 ? cfg.enemyMask : LayerMask.GetMask("Enemy", "Zombie");
        Collider[] hits;
        if (mask.value != 0)
            hits = EnemyQueryService.OverlapSphere(transform.position, cfg.bellRadius, mask, QueryTriggerInteraction.Ignore, this);
        else
            hits = EnemyQueryService.OverlapSphere(transform.position, cfg.bellRadius, ~0, QueryTriggerInteraction.Ignore, this);

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

            var silence = combatant.GetComponent<RelicSilenceDebuff>();
            if (silence == null)
                silence = combatant.gameObject.AddComponent<RelicSilenceDebuff>();

            silence.Apply(cfg.silenceDuration);
        }

        float staminaGain = cfg.baseStaminaRestore + cfg.staminaRestorePerStack * Mathf.Max(0, stacks - 1);
        player?.Progression?.AddStamina(staminaGain);
    }

    private void CleanupExpired(float now)
    {
        if (uniqueHitExpiry.Count == 0)
            return;

        expiredKeys.Clear();
        foreach (var kv in uniqueHitExpiry)
        {
            if (now >= kv.Value)
                expiredKeys.Add(kv.Key);
        }

        for (int i = 0; i < expiredKeys.Count; i++)
            uniqueHitExpiry.Remove(expiredKeys[i]);
    }
}

