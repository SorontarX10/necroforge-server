using UnityEngine;
using GrassSim.Combat;
using GrassSim.Core;

[CreateAssetMenu(
    menuName = "GrassSim/Relics/Effects/Rare/Reliquary Of Debts",
    fileName = "Relic_ReliquaryOfDebts"
)]
public class ReliquaryOfDebts : RelicEffect
{
    [Header("Debt")]
    [Range(0f, 1f)] public float baseDamageToDebt = 0.3f;
    [Range(0f, 1f)] public float extraDamageToDebtPerStack = 0.03f;
    [Range(0f, 1f)] public float baseDebtCapPct = 0.35f;
    [Range(0f, 1f)] public float extraDebtCapPctPerStack = 0.03f;

    [Header("Detonation")]
    public float explosionRadius = 4.5f;
    public float baseExplosionDamageMultiplier = 1f;
    public float explosionDamageMultiplierPerStack = 0.1f;
    public LayerMask enemyMask;

    public override void OnAcquire(PlayerRelicController player, int stacks)
    {
        Attach(player)?.Configure(this, stacks);
    }

    public override void OnStack(PlayerRelicController player, int stacks)
    {
        Attach(player)?.Configure(this, stacks);
    }

    private ReliquaryOfDebtsRuntime Attach(PlayerRelicController player)
    {
        if (player == null)
            return null;

        var rt = player.GetComponent<ReliquaryOfDebtsRuntime>();
        if (rt == null)
            rt = player.gameObject.AddComponent<ReliquaryOfDebtsRuntime>();

        return rt;
    }
}

public class ReliquaryOfDebtsRuntime : MonoBehaviour
{
    private static readonly Color DebtExplosionColor = new(0.52f, 0.98f, 0.74f, 0.95f);

    private PlayerRelicController player;
    private ReliquaryOfDebts cfg;
    private int stacks;
    private bool subscribed;
    private float debt;

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

    public void Configure(ReliquaryOfDebts config, int stackCount)
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
        player.OnMeleeKill += OnMeleeKill;
        subscribed = true;
    }

    private void TryUnsubscribe()
    {
        if (!subscribed || player == null)
            return;

        player.OnDamageTaken -= OnDamageTaken;
        player.OnMeleeKill -= OnMeleeKill;
        subscribed = false;
    }

    private void OnDamageTaken(float amount)
    {
        if (cfg == null || amount <= 0f || player == null || player.Progression == null)
            return;

        float ratio = cfg.baseDamageToDebt + cfg.extraDamageToDebtPerStack * Mathf.Max(0, stacks - 1);
        ratio = Mathf.Clamp01(ratio);

        float capPct = cfg.baseDebtCapPct + cfg.extraDebtCapPctPerStack * Mathf.Max(0, stacks - 1);
        capPct = Mathf.Clamp(capPct, 0f, 0.95f);

        float cap = player.Progression.MaxHealth * capPct;
        debt = Mathf.Clamp(debt + amount * ratio, 0f, cap);
    }

    private void OnMeleeKill(Combatant target, float damage, bool isCrit)
    {
        if (cfg == null || target == null || debt <= 0f)
            return;

        float explosionMul = cfg.baseExplosionDamageMultiplier +
            cfg.explosionDamageMultiplierPerStack * Mathf.Max(0, stacks - 1);
        float explosionDamage = Mathf.Max(1f, debt * Mathf.Max(0f, explosionMul));

        RelicGeneratedVfx.SpawnGroundCircle(
            target.transform.position + Vector3.up * 0.04f,
            Mathf.Max(0.8f, cfg.explosionRadius),
            DebtExplosionColor,
            0.46f,
            null,
            default,
            "ReliquaryOfDebts_Detonation"
        );

        LayerMask mask = cfg.enemyMask.value != 0 ? cfg.enemyMask : LayerMask.GetMask("Enemy", "Zombie");
        Collider[] hits;
        if (mask.value != 0)
            hits = EnemyQueryService.OverlapSphere(target.transform.position, cfg.explosionRadius, mask, QueryTriggerInteraction.Ignore, this);
        else
            hits = EnemyQueryService.OverlapSphere(target.transform.position, cfg.explosionRadius, ~0, QueryTriggerInteraction.Ignore, this);

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

            RelicDamageText.Deal(combatant, explosionDamage, transform, cfg);
        }

        player?.Progression?.Heal(debt);
        debt = 0f;
    }
}

