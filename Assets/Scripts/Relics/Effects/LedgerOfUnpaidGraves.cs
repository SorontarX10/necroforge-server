using System.Collections.Generic;
using UnityEngine;
using GrassSim.Combat;
using GrassSim.Core;

[CreateAssetMenu(
    menuName = "GrassSim/Relics/Effects/Mythic/Ledger Of Unpaid Graves",
    fileName = "Relic_LedgerOfUnpaidGraves"
)]
public class LedgerOfUnpaidGraves : RelicEffect
{
    [Header("Storage")]
    [Range(0f, 1f)] public float storePercent = 0.2f;
    public float capDamageMultiplier = 3f;

    [Header("Debt Wave")]
    public float waveRange = 9f;
    public float waveRadius = 1.2f;
    public LayerMask enemyMask;

    [Header("Idle Conversion")]
    public float unhitDelay = 10f;
    [Range(0f, 1f)] public float idleHealPercent = 0.5f;

    public override void OnAcquire(PlayerRelicController player, int stacks)
    {
        Attach(player)?.Configure(this, stacks);
    }

    public override void OnStack(PlayerRelicController player, int stacks)
    {
        Attach(player)?.Configure(this, stacks);
    }

    private LedgerOfUnpaidGravesRuntime Attach(PlayerRelicController player)
    {
        if (player == null)
            return null;

        var rt = player.GetComponent<LedgerOfUnpaidGravesRuntime>();
        if (rt == null)
            rt = player.gameObject.AddComponent<LedgerOfUnpaidGravesRuntime>();

        return rt;
    }
}

public class LedgerOfUnpaidGravesRuntime : MonoBehaviour, IRelicBatchedUpdate, IRelicBatchedCadence
{
    private static readonly Color DebtWaveColor = new(0.28f, 0.9f, 1f, 0.95f);

    private readonly HashSet<Combatant> affectedByWave = new();

    private PlayerRelicController player;
    private LedgerOfUnpaidGraves cfg;
    private int stacks;
    private bool subscribed;

    private float storedDebt;
    private float lastDamageTakenAt;
    private bool idleConvertedForCurrentDebt;

    private WeaponController weapon;

    private void Awake()
    {
        player = GetComponent<PlayerRelicController>();
        weapon = GetComponentInChildren<WeaponController>(true);
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

    public void Configure(LedgerOfUnpaidGraves config, int stackCount)
    {
        cfg = config;
        stacks = Mathf.Max(1, stackCount);
        EnemyQueryService.ConfigureOwnerBudget(this, RelicQueryBudgetProfiles.For(RelicTickArchetype.EnemyDebuff));
        TrySubscribe();
    }

    public bool IsBatchedUpdateActive => isActiveAndEnabled && cfg != null && player != null && player.Progression != null;

    public float BatchedUpdateInterval => 0.1f;

    public RelicTickArchetype BatchedTickArchetype => RelicTickArchetype.PlayerState;

    public void TickFromRelicBatch(float now, float deltaTime)
    {
        if (storedDebt <= 0f || idleConvertedForCurrentDebt)
            return;

        if (now - lastDamageTakenAt < Mathf.Max(0.1f, cfg.unhitDelay))
            return;

        float convert = storedDebt * Mathf.Clamp01(cfg.idleHealPercent);
        if (convert > 0f)
            player.Progression.Heal(convert);

        storedDebt = Mathf.Max(0f, storedDebt - convert);
        idleConvertedForCurrentDebt = true;
    }

    private void TrySubscribe()
    {
        if (subscribed || player == null)
            return;

        player.OnMeleeHitDealt += OnMeleeHit;
        player.OnDamageTaken += OnDamageTaken;
        subscribed = true;
    }

    private void TryUnsubscribe()
    {
        if (!subscribed || player == null)
            return;

        player.OnMeleeHitDealt -= OnMeleeHit;
        player.OnDamageTaken -= OnDamageTaken;
        subscribed = false;
    }

    private void OnMeleeHit(Combatant target, float damage, bool isCrit)
    {
        if (cfg == null || damage <= 0f)
            return;

        float cap = GetDebtCapFromWeaponDamage();
        float gain = damage * Mathf.Clamp01(cfg.storePercent);
        storedDebt = Mathf.Clamp(storedDebt + gain, 0f, Mathf.Max(1f, cap));
        idleConvertedForCurrentDebt = false;
    }

    private void OnDamageTaken(float amount)
    {
        if (cfg == null)
            return;

        lastDamageTakenAt = Time.time;
        idleConvertedForCurrentDebt = false;

        if (storedDebt <= 0f)
            return;

        FireDebtWave(storedDebt);
        storedDebt = 0f;
    }

    private float GetDebtCapFromWeaponDamage()
    {
        float weaponDamage = 1f;

        if (weapon == null)
            weapon = GetComponentInChildren<WeaponController>(true);

        if (weapon != null)
            weaponDamage = Mathf.Max(1f, weapon.GetDamageMultiplier());
        else if (player != null && player.Progression != null && player.Progression.stats != null)
            weaponDamage = Mathf.Max(1f, player.Progression.stats.damage);

        return weaponDamage * Mathf.Max(0.5f, cfg.capDamageMultiplier);
    }

    private void FireDebtWave(float damageValue)
    {
        if (cfg == null || damageValue <= 0f)
            return;

        Vector3 start = transform.position + Vector3.up * 1f;
        Vector3 dir = transform.forward;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.001f)
            dir = Vector3.forward;
        dir.Normalize();

        Vector3 end = start + dir * Mathf.Max(0.5f, cfg.waveRange);
        float radius = Mathf.Max(0.15f, cfg.waveRadius);
        float damage = Mathf.Max(1f, damageValue);

        RelicGeneratedVfx.SpawnLineWave(
            start + Vector3.up * 0.03f,
            dir,
            Mathf.Max(0.5f, cfg.waveRange),
            radius,
            DebtWaveColor,
            0.36f,
            "LedgerOfUnpaidGraves_DebtWave"
        );

        LayerMask mask = cfg.enemyMask.value != 0 ? cfg.enemyMask : LayerMask.GetMask("Enemy", "Zombie");
        Collider[] hits;
        if (mask.value != 0)
            hits = EnemyQueryService.OverlapCapsule(start, end, radius, mask, QueryTriggerInteraction.Ignore, this);
        else
            hits = EnemyQueryService.OverlapCapsule(start, end, radius, ~0, QueryTriggerInteraction.Ignore, this);

        affectedByWave.Clear();
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

            if (!affectedByWave.Add(combatant))
                continue;

            RelicDamageText.Deal(combatant, damage, transform, cfg);
        }
    }
}



