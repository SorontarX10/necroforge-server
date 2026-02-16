using System.Collections.Generic;
using UnityEngine;
using GrassSim.Combat;
using GrassSim.Core;

[CreateAssetMenu(
    menuName = "GrassSim/Relics/Effects/Common/Crypt Spines",
    fileName = "Relic_CryptSpines"
)]
public class CryptSpines : RelicEffect
{
    [Header("Charge")]
    public float chargeWindow = 4f;

    [Header("Wave")]
    public float baseWaveDamagePercent = 0.35f;
    public float extraWaveDamagePerStack = 0.05f;
    public float baseRange = 9f;
    public float extraRangePerStack = 1f;
    public float waveRadius = 1.1f;
    public LayerMask enemyMask;

    public override void OnAcquire(PlayerRelicController player, int stacks)
    {
        Attach(player)?.Configure(this, stacks);
    }

    public override void OnStack(PlayerRelicController player, int stacks)
    {
        Attach(player)?.Configure(this, stacks);
    }

    private CryptSpinesRuntime Attach(PlayerRelicController player)
    {
        if (player == null) return null;
        var rt = player.GetComponent<CryptSpinesRuntime>();
        if (rt == null) rt = player.gameObject.AddComponent<CryptSpinesRuntime>();
        return rt;
    }
}

public class CryptSpinesRuntime : MonoBehaviour
{
    private PlayerRelicController player;
    private CryptSpines cfg;
    private int stacks;
    private bool subscribed;
    private float chargedUntil;

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

    public void Configure(CryptSpines config, int stackCount)
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

        player.OnDodged += OnDodged;
        player.OnMeleeHitDealt += OnMeleeHit;
        subscribed = true;
    }

    private void TryUnsubscribe()
    {
        if (!subscribed || player == null)
            return;

        player.OnDodged -= OnDodged;
        player.OnMeleeHitDealt -= OnMeleeHit;
        subscribed = false;
    }

    private void OnDodged()
    {
        if (cfg == null)
            return;

        chargedUntil = Time.time + Mathf.Max(0.2f, cfg.chargeWindow);
    }

    private void OnMeleeHit(Combatant target, float hitDamage, bool isCrit)
    {
        if (cfg == null || target == null || hitDamage <= 0f)
            return;

        if (Time.time > chargedUntil)
            return;

        chargedUntil = 0f;
        FireWave(target, hitDamage);
    }

    private void FireWave(Combatant primaryTarget, float hitDamage)
    {
        LayerMask mask = cfg.enemyMask.value != 0 ? cfg.enemyMask : LayerMask.GetMask("Enemy", "Zombie");

        Vector3 start = primaryTarget.transform.position + Vector3.up * 1.1f;
        Vector3 dir = Vector3.ProjectOnPlane(
            primaryTarget.transform.position - transform.position,
            Vector3.up
        );
        if (dir.sqrMagnitude < 0.0001f)
            dir = transform.forward;
        dir.Normalize();

        float range = Mathf.Max(1f, cfg.baseRange + cfg.extraRangePerStack * Mathf.Max(0, stacks - 1));
        float radius = Mathf.Max(0.2f, cfg.waveRadius);
        Vector3 end = start + dir * range;

        Collider[] hits;
        if (mask.value != 0)
            hits = EnemyQueryService.OverlapCapsule(start, end, radius, mask, QueryTriggerInteraction.Ignore, this);
        else
            hits = EnemyQueryService.OverlapCapsule(start, end, radius, ~0, QueryTriggerInteraction.Ignore, this);

        if (EnemyQueryService.GetLastHitCount(this) <= 0)
            return;

        float damagePct = cfg.baseWaveDamagePercent + cfg.extraWaveDamagePerStack * Mathf.Max(0, stacks - 1);
        float damage = Mathf.Max(1f, hitDamage * Mathf.Max(0f, damagePct));

        var hitCombatants = new HashSet<Combatant>();
        for (int i = 0, hitCount = EnemyQueryService.GetLastHitCount(this); i < hitCount; i++)
        {
            var col = hits[i];
            if (col == null)
                continue;

            var combatant = EnemyQueryService.GetCombatant(col);
            if (combatant == null || combatant == primaryTarget || combatant.IsDead)
                continue;

            if (!hitCombatants.Add(combatant))
                continue;

            if (combatant.GetComponent<PlayerProgressionController>() != null)
                continue;

            RelicDamageText.Deal(combatant, damage, transform, cfg);
        }
    }
}



