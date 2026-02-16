using UnityEngine;
using GrassSim.Combat;
using GrassSim.Core;

[CreateAssetMenu(
    menuName = "GrassSim/Relics/Effects/Rare/Doomsman Compass",
    fileName = "Relic_DoomsmanCompass"
)]
public class DoomsmanCompass : RelicEffect, IDamageModifier
{
    [Header("Mark")]
    public float retargetInterval = 4f;
    public float markSearchRadius = 36f;
    public LayerMask enemyMask;

    [Header("Damage")]
    public float condemnedDamageMultiplier = 1.4f;
    public float unmarkedDamageMultiplier = 0.9f;

    [Header("Resource")]
    public float baseStaminaOnCondemnedHit = 12f;
    public float extraStaminaPerStack = 2f;

    public override void OnAcquire(PlayerRelicController player, int stacks)
    {
        Attach(player)?.Configure(this, stacks);
    }

    public override void OnStack(PlayerRelicController player, int stacks)
    {
        Attach(player)?.Configure(this, stacks);
    }

    public float GetDamageMultiplier(PlayerRelicController player, int stacks)
    {
        var rt = player != null ? player.GetComponent<DoomsmanCompassRuntime>() : null;
        return rt != null ? rt.CurrentSwingMultiplier : 1f;
    }

    private DoomsmanCompassRuntime Attach(PlayerRelicController player)
    {
        if (player == null)
            return null;

        var rt = player.GetComponent<DoomsmanCompassRuntime>();
        if (rt == null)
            rt = player.gameObject.AddComponent<DoomsmanCompassRuntime>();

        return rt;
    }
}

public class DoomsmanCompassRuntime : MonoBehaviour, IRelicBatchedUpdate, IRelicBatchedCadence
{
    private PlayerRelicController player;
    private DoomsmanCompass cfg;
    private int stacks;
    private bool subscribed;
    private float nextRetargetAt;
    private Combatant condemnedTarget;
    private float currentSwingMultiplier = 1f;

    public float CurrentSwingMultiplier => Mathf.Max(0f, currentSwingMultiplier);

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

    public void Configure(DoomsmanCompass config, int stackCount)
    {
        cfg = config;
        stacks = Mathf.Max(1, stackCount);
        EnemyQueryService.ConfigureOwnerBudget(this, RelicQueryBudgetProfiles.For(BatchedTickArchetype));
        if (nextRetargetAt <= 0f)
            nextRetargetAt = Time.time + 0.5f;

        TrySubscribe();
    }

    public bool IsBatchedUpdateActive => isActiveAndEnabled && cfg != null;

    public float BatchedUpdateInterval => 0.1f;

    public RelicTickArchetype BatchedTickArchetype => RelicTickArchetype.PlayerState;

    public void TickFromRelicBatch(float now, float deltaTime)
    {
        if (cfg == null)
            return;

        if (now >= nextRetargetAt)
        {
            nextRetargetAt = now + Mathf.Max(0.1f, cfg.retargetInterval);
            condemnedTarget = FindFarthestEnemy();
        }

        if (condemnedTarget != null && condemnedTarget.IsDead)
            condemnedTarget = null;
    }

    private void TrySubscribe()
    {
        if (subscribed || player == null)
            return;

        player.OnBeforeMeleeHit += OnBeforeMeleeHit;
        player.OnMeleeHitDealt += OnMeleeHit;
        subscribed = true;
    }

    private void TryUnsubscribe()
    {
        if (!subscribed || player == null)
            return;

        player.OnBeforeMeleeHit -= OnBeforeMeleeHit;
        player.OnMeleeHitDealt -= OnMeleeHit;
        subscribed = false;
    }

    private void OnBeforeMeleeHit(Combatant target)
    {
        if (cfg == null || target == null)
        {
            currentSwingMultiplier = 1f;
            return;
        }

        bool isCondemned = target == condemnedTarget;
        currentSwingMultiplier = isCondemned
            ? Mathf.Max(0f, cfg.condemnedDamageMultiplier)
            : Mathf.Max(0f, cfg.unmarkedDamageMultiplier);
    }

    private void OnMeleeHit(Combatant target, float damage, bool isCrit)
    {
        if (cfg == null)
        {
            currentSwingMultiplier = 1f;
            return;
        }

        if (target != null && target == condemnedTarget && !target.IsDead)
        {
            float gain = cfg.baseStaminaOnCondemnedHit + cfg.extraStaminaPerStack * Mathf.Max(0, stacks - 1);
            player?.Progression?.AddStamina(gain);
        }

        currentSwingMultiplier = 1f;
    }

    private Combatant FindFarthestEnemy()
    {
        LayerMask mask = cfg.enemyMask.value != 0 ? cfg.enemyMask : LayerMask.GetMask("Enemy", "Zombie");

        Collider[] hits;
        if (mask.value != 0)
            hits = EnemyQueryService.OverlapSphere(transform.position, cfg.markSearchRadius, mask, QueryTriggerInteraction.Ignore, this);
        else
            hits = EnemyQueryService.OverlapSphere(transform.position, cfg.markSearchRadius, ~0, QueryTriggerInteraction.Ignore, this);

        Combatant best = null;
        float bestSqr = float.NegativeInfinity;
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

            float sqr = (combatant.transform.position - transform.position).sqrMagnitude;
            if (sqr > bestSqr)
            {
                bestSqr = sqr;
                best = combatant;
            }
        }

        return best;
    }
}

