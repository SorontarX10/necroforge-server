using System.Collections.Generic;
using UnityEngine;
using GrassSim.Combat;
using GrassSim.Core;

[CreateAssetMenu(
    menuName = "GrassSim/Relics/Effects/Legendary/Mortis Tithe Seal",
    fileName = "Relic_MortisTitheSeal"
)]
public class MortisTitheSeal : RelicEffect
{
    [Header("Tithe")]
    [Range(0f, 1f)] public float damageToTithePercent = 0.12f;
    [Range(0f, 1f)] public float baseThresholdPercent = 0.3f;
    [Range(0f, 1f)] public float thresholdPercentPerStack = 0.04f;

    [Header("Soul Bolts")]
    public float boltRadius = 8f;
    [Min(1)] public int maxBoltTargets = 6;
    public float baseBoltDamage = 32f;
    public float boltDamagePerStack = 6f;
    public float titheToDamageMultiplier = 0.35f;
    public LayerMask enemyMask;

    [Header("Heal")]
    [Range(0f, 1f)] public float baseHealPercent = 0.08f;
    [Range(0f, 1f)] public float healPercentPerStack = 0.01f;

    public override void OnAcquire(PlayerRelicController player, int stacks)
    {
        Attach(player)?.Configure(this, stacks);
    }

    public override void OnStack(PlayerRelicController player, int stacks)
    {
        Attach(player)?.Configure(this, stacks);
    }

    private MortisTitheSealRuntime Attach(PlayerRelicController player)
    {
        if (player == null)
            return null;

        var rt = player.GetComponent<MortisTitheSealRuntime>();
        if (rt == null)
            rt = player.gameObject.AddComponent<MortisTitheSealRuntime>();

        return rt;
    }
}

public class MortisTitheSealRuntime : MonoBehaviour
{
    private struct Candidate
    {
        public Combatant combatant;
        public float sqrDistance;
    }

    private PlayerRelicController player;
    private MortisTitheSeal cfg;
    private int stacks;
    private bool subscribed;

    private float storedTithe;

    private void Awake()
    {
        player = GetComponent<PlayerRelicController>();
    }

    private void OnEnable()
    {
        TrySubscribe();
    }

    private void OnDisable()
    {
        TryUnsubscribe();
    }

    public void Configure(MortisTitheSeal config, int stackCount)
    {
        cfg = config;
        stacks = Mathf.Max(1, stackCount);
        EnemyQueryService.ConfigureOwnerBudget(this, RelicQueryBudgetProfiles.For(RelicTickArchetype.EnemyDebuff));
        TrySubscribe();
    }

    private void TrySubscribe()
    {
        if (subscribed || player == null)
            return;

        player.OnDamageTaken += OnDamageTaken;
        subscribed = true;
    }

    private void TryUnsubscribe()
    {
        if (!subscribed || player == null)
            return;

        player.OnDamageTaken -= OnDamageTaken;
        subscribed = false;
    }

    private void OnDamageTaken(float amount)
    {
        if (cfg == null || player == null || player.Progression == null || amount <= 0f)
            return;

        storedTithe += amount * Mathf.Clamp01(cfg.damageToTithePercent);

        float thresholdPct = cfg.baseThresholdPercent + cfg.thresholdPercentPerStack * Mathf.Max(0, stacks - 1);
        float threshold = player.Progression.MaxHealth * Mathf.Clamp(thresholdPct, 0.01f, 1f);

        if (storedTithe < threshold)
            return;

        ReleaseTithe();
        storedTithe = 0f;
    }

    private void ReleaseTithe()
    {
        if (cfg == null || player == null || player.Progression == null)
            return;

        float damage = cfg.baseBoltDamage + cfg.boltDamagePerStack * Mathf.Max(0, stacks - 1);
        damage += storedTithe * Mathf.Max(0f, cfg.titheToDamageMultiplier);
        damage = Mathf.Max(1f, damage);

        var targets = FindNearestTargets(Mathf.Max(1, cfg.maxBoltTargets));
        for (int i = 0; i < targets.Count; i++)
        {
            if (targets[i] != null && !targets[i].IsDead)
                RelicDamageText.Deal(targets[i], damage, transform, cfg);
        }

        float healPct = cfg.baseHealPercent + cfg.healPercentPerStack * Mathf.Max(0, stacks - 1);
        float heal = player.Progression.MaxHealth * Mathf.Clamp(healPct, 0f, 1f);
        if (heal > 0f)
            player.Progression.Heal(heal);
    }

    private List<Combatant> FindNearestTargets(int count)
    {
        LayerMask mask = cfg.enemyMask.value != 0 ? cfg.enemyMask : LayerMask.GetMask("Enemy", "Zombie");

        Collider[] hits;
        if (mask.value != 0)
            hits = EnemyQueryService.OverlapSphere(transform.position, cfg.boltRadius, mask, QueryTriggerInteraction.Ignore, this);
        else
            hits = EnemyQueryService.OverlapSphere(transform.position, cfg.boltRadius, ~0, QueryTriggerInteraction.Ignore, this);

        var seen = new HashSet<int>();
        var candidates = new List<Candidate>();

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
            if (!seen.Add(id))
                continue;

            candidates.Add(new Candidate
            {
                combatant = combatant,
                sqrDistance = (combatant.transform.position - transform.position).sqrMagnitude
            });
        }

        candidates.Sort((a, b) => a.sqrDistance.CompareTo(b.sqrDistance));

        var result = new List<Combatant>();
        int take = Mathf.Min(count, candidates.Count);
        for (int i = 0; i < take; i++)
            result.Add(candidates[i].combatant);

        return result;
    }
}

