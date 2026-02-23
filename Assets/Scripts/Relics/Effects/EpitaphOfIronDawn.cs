using UnityEngine;
using GrassSim.Combat;

[CreateAssetMenu(
    menuName = "GrassSim/Relics/Effects/Legendary/Epitaph Of Iron Dawn",
    fileName = "Relic_EpitaphOfIronDawn"
)]
public class EpitaphOfIronDawn : RelicEffect, IDamageReductionModifier
{
    [Header("Iron Dawn")]
    [Range(0.05f, 0.95f)] public float healthThresholdPercent = 0.4f;
    public float cooldown = 30f;
    public float baseDuration = 6f;
    public float durationPerStack = 0.4f;

    [Header("Defense")]
    public float baseDamageReductionBonus = 0.35f;
    public float damageReductionPerStack = 0.04f;

    [Header("Judged")]
    public float baseDetonationMultiplier = 1.25f;
    public float detonationMultiplierPerStack = 0.12f;
    public float minDetonationDamage = 45f;

    public override void OnAcquire(PlayerRelicController player, int stacks)
    {
        Attach(player)?.Configure(this, stacks);
    }

    public override void OnStack(PlayerRelicController player, int stacks)
    {
        Attach(player)?.Configure(this, stacks);
    }

    public float GetDamageReductionBonus(PlayerRelicController player, int stacks)
    {
        var rt = player != null ? player.GetComponent<EpitaphOfIronDawnRuntime>() : null;
        if (rt == null || !rt.IsIronDawnActive)
            return 0f;

        return baseDamageReductionBonus + damageReductionPerStack * Mathf.Max(0, stacks - 1);
    }

    private EpitaphOfIronDawnRuntime Attach(PlayerRelicController player)
    {
        if (player == null)
            return null;

        var rt = player.GetComponent<EpitaphOfIronDawnRuntime>();
        if (rt == null)
            rt = player.gameObject.AddComponent<EpitaphOfIronDawnRuntime>();

        return rt;
    }
}

public class EpitaphOfIronDawnRuntime : MonoBehaviour, IRelicBatchedUpdate, IRelicBatchedCadence
{
    private static readonly Color JudgedColor = new(1f, 0.75f, 0.2f, 0.95f);

    private PlayerRelicController player;
    private EpitaphOfIronDawn cfg;
    private int stacks;
    private bool subscribed;

    private bool active;
    private bool wasActive;
    private float activeEndsAt;
    private float nextReadyAt;

    private Combatant judgedTarget;
    private bool judgedArmed;

    public bool IsIronDawnActive => active;

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

    public void Configure(EpitaphOfIronDawn config, int stackCount)
    {
        cfg = config;
        stacks = Mathf.Max(1, stackCount);
        TrySubscribe();
    }

    public bool IsBatchedUpdateActive => isActiveAndEnabled && cfg != null && player != null && player.Progression != null;

    public float BatchedUpdateInterval => 0.04f;

    public RelicTickArchetype BatchedTickArchetype => RelicTickArchetype.PlayerState;

    public void TickFromRelicBatch(float now, float deltaTime)
    {
        if (active && now >= activeEndsAt)
            EndIronDawn();

        if (!active && now >= nextReadyAt)
        {
            float hpPct = player.Progression.CurrentHealth / Mathf.Max(1f, player.Progression.MaxHealth);
            if (hpPct <= Mathf.Clamp01(cfg.healthThresholdPercent))
                StartIronDawn();
        }

        if (active)
        {
            // Treat existing root as stagger-like control and cleanse it during Iron Dawn.
            var root = GetComponent<BossRootDebuff>();
            if (root != null && root.IsRooted)
                Destroy(root);
        }

        if (active != wasActive)
        {
            wasActive = active;
            player.Progression.NotifyStatsChanged();
        }
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

    private void StartIronDawn()
    {
        active = true;
        judgedTarget = null;
        judgedArmed = false;

        float duration = cfg.baseDuration + cfg.durationPerStack * Mathf.Max(0, stacks - 1);
        activeEndsAt = Time.time + Mathf.Max(0.2f, duration);
        nextReadyAt = Time.time + Mathf.Max(0.5f, cfg.cooldown);
    }

    private void EndIronDawn()
    {
        active = false;
        judgedTarget = null;
        judgedArmed = false;
        activeEndsAt = 0f;
    }

    private void OnMeleeHit(Combatant target, float damage, bool isCrit)
    {
        if (!active || target == null || target.IsDead)
            return;

        if (!judgedArmed || judgedTarget == null || judgedTarget != target || judgedTarget.IsDead)
        {
            judgedTarget = target;
            judgedArmed = true;
            RelicGeneratedVfx.SpawnAttachedMarker(
                target.transform,
                0.85f,
                JudgedColor,
                1.1f,
                new Vector3(0f, 0.05f, 0f),
                "Epitaph_JudgedMarker"
            );
            return;
        }

        float mul = cfg.baseDetonationMultiplier + cfg.detonationMultiplierPerStack * Mathf.Max(0, stacks - 1);
        float detonation = Mathf.Max(cfg.minDetonationDamage, damage * Mathf.Max(0f, mul));
        RelicGeneratedVfx.SpawnGroundCircle(
            target.transform.position + Vector3.up * 0.04f,
            1.2f,
            JudgedColor,
            0.42f,
            null,
            default,
            "Epitaph_DetonationPulse"
        );
        RelicDamageText.Deal(target, detonation, transform, cfg);

        judgedTarget = null;
        judgedArmed = false;
    }
}
