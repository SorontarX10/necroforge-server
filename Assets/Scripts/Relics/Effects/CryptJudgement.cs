using System.Collections.Generic;
using UnityEngine;
using GrassSim.Combat;
using GrassSim.Core;

[CreateAssetMenu(
    menuName = "GrassSim/Relics/Effects/Uncommon/Crypt Judgement",
    fileName = "Relic_CryptJudgement"
)]
public class CryptJudgement : RelicEffect
{
    [Header("Combo")]
    [Min(2)] public int requiredHits = 3;
    public float comboWindow = 2.5f;

    [Header("Judgment Slash")]
    public float baseDamagePercent = 0.6f;
    public float extraDamagePercentPerStack = 0.08f;
    public float range = 10f;
    public float radius = 1f;
    public LayerMask enemyMask;

    public override void OnAcquire(PlayerRelicController player, int stacks)
    {
        Attach(player)?.Configure(this, stacks);
    }

    public override void OnStack(PlayerRelicController player, int stacks)
    {
        Attach(player)?.Configure(this, stacks);
    }

    private CryptJudgementRuntime Attach(PlayerRelicController player)
    {
        if (player == null)
            return null;

        var rt = player.GetComponent<CryptJudgementRuntime>();
        if (rt == null)
            rt = player.gameObject.AddComponent<CryptJudgementRuntime>();

        return rt;
    }
}

public class CryptJudgementRuntime : MonoBehaviour
{
    private static readonly Color SlashWaveColor = new(0.42f, 0.92f, 1f, 0.95f);

    private PlayerRelicController player;
    private CryptJudgement cfg;
    private int stacks;
    private bool subscribed;

    private Combatant lastTarget;
    private int hitCount;
    private float lastHitAt;

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

    public void Configure(CryptJudgement config, int stackCount)
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

    private void OnMeleeHit(Combatant target, float hitDamage, bool isCrit)
    {
        if (cfg == null || target == null || target.IsDead || hitDamage <= 0f)
            return;

        bool sameTarget = target == lastTarget;
        bool withinWindow = Time.time - lastHitAt <= Mathf.Max(0.1f, cfg.comboWindow);

        if (sameTarget && withinWindow)
            hitCount++;
        else
            hitCount = 1;

        lastTarget = target;
        lastHitAt = Time.time;

        if (hitCount < Mathf.Max(2, cfg.requiredHits))
            return;

        hitCount = 0;
        FireJudgmentSlash(target, hitDamage);
    }

    private void FireJudgmentSlash(Combatant pivotTarget, float sourceDamage)
    {
        LayerMask mask = cfg.enemyMask.value != 0 ? cfg.enemyMask : LayerMask.GetMask("Enemy", "Zombie");

        Vector3 start = transform.position + Vector3.up * 1.1f;
        Vector3 dir = Vector3.ProjectOnPlane(pivotTarget.transform.position - transform.position, Vector3.up);
        if (dir.sqrMagnitude < 0.0001f)
            dir = transform.forward;

        dir.Normalize();

        float lineRange = Mathf.Max(1f, cfg.range);
        float lineRadius = Mathf.Max(0.2f, cfg.radius);
        Vector3 end = start + dir * lineRange;

        RelicGeneratedVfx.SpawnLineWave(
            start + Vector3.up * 0.02f,
            dir,
            lineRange,
            lineRadius,
            SlashWaveColor,
            0.36f,
            "CryptJudgement_SlashWave"
        );

        Collider[] hits;
        if (mask.value != 0)
            hits = EnemyQueryService.OverlapCapsule(start, end, lineRadius, mask, QueryTriggerInteraction.Ignore, this);
        else
            hits = EnemyQueryService.OverlapCapsule(start, end, lineRadius, ~0, QueryTriggerInteraction.Ignore, this);

        float dmgPct = cfg.baseDamagePercent + cfg.extraDamagePercentPerStack * Mathf.Max(0, stacks - 1);
        float slashDamage = Mathf.Max(1f, sourceDamage * Mathf.Max(0f, dmgPct));

        var alreadyHit = new HashSet<Combatant>();
        for (int i = 0, hitCount = EnemyQueryService.GetLastHitCount(this); i < hitCount; i++)
        {
            var col = hits[i];
            if (col == null)
                continue;

            var combatant = EnemyQueryService.GetCombatant(col);
            if (combatant == null || combatant.IsDead)
                continue;

            if (!alreadyHit.Add(combatant))
                continue;

            if (combatant.GetComponent<PlayerProgressionController>() != null)
                continue;

            RelicDamageText.Deal(combatant, slashDamage, transform, cfg);
        }
    }
}



