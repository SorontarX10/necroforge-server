using UnityEngine;
using GrassSim.Combat;

[CreateAssetMenu(
    menuName = "GrassSim/Relics/Effects/Mythic/Scripture Of The Ninth Hour",
    fileName = "Relic_ScriptureOfTheNinthHour"
)]
public class ScriptureOfTheNinthHour : RelicEffect, IIncomingHitModifier, IMaxHealthModifier
{
    [Header("Lethal Save")]
    public float cooldown = 90f;
    public float condemnedDuration = 6f;
    public float condemnedDamageMultiplier = 3f;
    public float cooldownReductionOnSuccess = 30f;

    [Header("Failure Penalty")]
    [Range(0f, 1f)] public float failedPenaltyMaxHealthPercent = 0.2f;
    public float failedPenaltyDuration = 20f;

    public override void OnAcquire(PlayerRelicController player, int stacks)
    {
        Attach(player)?.Configure(this, stacks);
    }

    public override void OnStack(PlayerRelicController player, int stacks)
    {
        Attach(player)?.Configure(this, stacks);
    }

    public float ModifyIncomingDamage(PlayerRelicController player, Combatant attacker, float damage, int stacks)
    {
        var rt = player != null ? player.GetComponent<ScriptureOfTheNinthHourRuntime>() : null;
        if (rt == null)
            return damage;

        return rt.ModifyIncomingDamage(attacker, damage);
    }

    public float GetMaxHealthBonus(PlayerRelicController player, int stacks)
    {
        var rt = player != null ? player.GetComponent<ScriptureOfTheNinthHourRuntime>() : null;
        if (rt == null || !rt.IsPenaltyActive)
            return 0f;

        return -Mathf.Max(0f, rt.PenaltyFlatHealthLoss);
    }

    private ScriptureOfTheNinthHourRuntime Attach(PlayerRelicController player)
    {
        if (player == null)
            return null;

        var rt = player.GetComponent<ScriptureOfTheNinthHourRuntime>();
        if (rt == null)
            rt = player.gameObject.AddComponent<ScriptureOfTheNinthHourRuntime>();

        return rt;
    }
}

public class ScriptureOfTheNinthHourRuntime : MonoBehaviour, IRelicBatchedUpdate, IRelicBatchedCadence
{
    private static readonly Color CondemnedColor = new(1f, 0.42f, 0.62f, 0.95f);
    private static readonly Color FailedColor = new(0.4f, 0.25f, 0.25f, 0.95f);

    private PlayerRelicController player;
    private ScriptureOfTheNinthHour cfg;
    private int stacks;
    private bool subscribed;
    private bool wasPenaltyActive;

    private float nextReadyAt;
    private Combatant condemnedTarget;
    private float condemnedEndsAt;
    private bool condemnationResolved = true;

    private float penaltyEndsAt;
    private float penaltyFlatHealthLoss;

    public bool IsPenaltyActive => Time.time < penaltyEndsAt;
    public float PenaltyFlatHealthLoss => penaltyFlatHealthLoss;

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

    public void Configure(ScriptureOfTheNinthHour config, int stackCount)
    {
        cfg = config;
        stacks = Mathf.Max(1, stackCount);
        TrySubscribe();
    }

    public bool IsBatchedUpdateActive => isActiveAndEnabled && cfg != null && player != null && player.Progression != null;

    public float BatchedUpdateInterval => 0.05f;

    public RelicTickArchetype BatchedTickArchetype => RelicTickArchetype.PlayerState;

    public void TickFromRelicBatch(float now, float deltaTime)
    {
        if (!condemnationResolved && now >= condemnedEndsAt)
            ApplyFailurePenalty();

        bool penalty = IsPenaltyActive;
        if (penalty != wasPenaltyActive)
        {
            wasPenaltyActive = penalty;
            if (!penalty)
                penaltyFlatHealthLoss = 0f;

            player.Progression.NotifyStatsChanged();
        }
    }

    public float ModifyIncomingDamage(Combatant attacker, float damage)
    {
        if (cfg == null || player == null || player.Progression == null || damage <= 0f)
            return damage;

        if (Time.time < nextReadyAt)
            return damage;

        if (PredictFinalDamage(damage) < player.Progression.CurrentHealth)
            return damage;

        nextReadyAt = Time.time + Mathf.Max(0.5f, cfg.cooldown);
        condemnationResolved = attacker == null;
        condemnedTarget = attacker;
        condemnedEndsAt = Time.time + Mathf.Max(0.1f, cfg.condemnedDuration);

        if (attacker != null)
        {
            RelicGeneratedVfx.SpawnAttachedMarker(
                attacker.transform,
                0.9f,
                CondemnedColor,
                Mathf.Max(0.2f, cfg.condemnedDuration),
                new Vector3(0f, 0.05f, 0f),
                "ScriptureNinthHour_Condemned"
            );
            RelicGeneratedVfx.SpawnBeam(
                transform.position + Vector3.up * 1.05f,
                attacker.transform.position + Vector3.up * 1.05f,
                0.045f,
                CondemnedColor,
                0.16f,
                "ScriptureNinthHour_Link"
            );
        }

        return 0f;
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
        if (cfg == null || target == null || target.IsDead || damage <= 0f)
            return;

        if (condemnationResolved || target != condemnedTarget || Time.time >= condemnedEndsAt)
            return;

        float mul = Mathf.Max(1f, cfg.condemnedDamageMultiplier);
        float extra = damage * (mul - 1f);
        if (extra > 0f)
            RelicDamageText.Deal(target, extra, transform, cfg);
    }

    private void OnMeleeKill(Combatant target, float damage, bool isCrit)
    {
        if (cfg == null || target == null)
            return;

        if (condemnationResolved || target != condemnedTarget || Time.time >= condemnedEndsAt)
            return;

        condemnationResolved = true;
        condemnedTarget = null;
        condemnedEndsAt = 0f;
        nextReadyAt = Mathf.Max(Time.time, nextReadyAt - Mathf.Max(0f, cfg.cooldownReductionOnSuccess));
    }

    private void ApplyFailurePenalty()
    {
        if (cfg == null || player == null || player.Progression == null || condemnationResolved)
            return;

        condemnationResolved = true;
        condemnedTarget = null;
        condemnedEndsAt = 0f;

        float pct = Mathf.Clamp01(cfg.failedPenaltyMaxHealthPercent);
        penaltyFlatHealthLoss = player.Progression.MaxHealth * pct;
        penaltyEndsAt = Time.time + Mathf.Max(0.2f, cfg.failedPenaltyDuration);

        RelicGeneratedVfx.SpawnGroundCircle(
            transform.position + Vector3.up * 0.04f,
            1.3f,
            FailedColor,
            0.5f,
            transform,
            new Vector3(0f, 0.04f, 0f),
            "ScriptureNinthHour_Failure"
        );

        player.Progression.NotifyStatsChanged();
        player.Progression.currentHealth = Mathf.Min(player.Progression.currentHealth, player.Progression.MaxHealth);
    }

    private float PredictFinalDamage(float rawDamage)
    {
        float reduction = 0f;
        if (player.Progression.stats != null)
            reduction = player.Progression.stats.damageReduction;

        reduction += player.GetDamageReductionBonus();
        reduction = CombatBalanceCaps.ClampDamageReduction(reduction);

        float afterReduction = rawDamage * (1f - reduction);
        float afterBarrier = Mathf.Max(0f, afterReduction - player.Progression.CurrentBarrier);
        return afterBarrier;
    }
}
