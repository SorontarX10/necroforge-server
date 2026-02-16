using UnityEngine;
using GrassSim.Combat;
using GrassSim.Core;

[CreateAssetMenu(
    menuName = "GrassSim/Relics/Effects/Common/Duelist's Ash",
    fileName = "Relic_DuelistsAsh"
)]
public class DuelistsAsh : RelicEffect, IDamageModifier, IDodgeChanceModifier
{
    [Header("Condition")]
    public float radius = 6f;
    public float checkInterval = 0.25f;
    public LayerMask enemyMask;

    [Header("Bonuses")]
    public float baseDamageBonus = 0.12f;
    public float extraDamageBonusPerStack = 0.03f;
    public float baseDodgeBonus = 0.08f;
    public float extraDodgeBonusPerStack = 0.015f;

    public override void OnAcquire(PlayerRelicController player, int stacks)
    {
        Attach(player)?.Configure(this);
    }

    public override void OnStack(PlayerRelicController player, int stacks)
    {
        Attach(player)?.Configure(this);
    }

    public float GetDamageMultiplier(PlayerRelicController player, int stacks)
    {
        var rt = player != null ? player.GetComponent<DuelistsAshRuntime>() : null;
        if (rt == null || !rt.Active)
            return 1f;

        return 1f + baseDamageBonus + extraDamageBonusPerStack * Mathf.Max(0, stacks - 1);
    }

    public float GetDodgeChanceBonus(PlayerRelicController player, int stacks)
    {
        var rt = player != null ? player.GetComponent<DuelistsAshRuntime>() : null;
        if (rt == null || !rt.Active)
            return 0f;

        return baseDodgeBonus + extraDodgeBonusPerStack * Mathf.Max(0, stacks - 1);
    }

    private DuelistsAshRuntime Attach(PlayerRelicController player)
    {
        if (player == null) return null;
        var rt = player.GetComponent<DuelistsAshRuntime>();
        if (rt == null) rt = player.gameObject.AddComponent<DuelistsAshRuntime>();
        return rt;
    }
}

public class DuelistsAshRuntime : MonoBehaviour, IRelicBatchedUpdate, IRelicBatchedCadence
{
    private DuelistsAsh cfg;
    private bool active;
    private PlayerRelicController player;

    public bool Active => active;

    private void Awake()
    {
        player = GetComponent<PlayerRelicController>();
    }

    public void Configure(DuelistsAsh config)
    {
        cfg = config;
        EnemyQueryService.ConfigureOwnerBudget(this, 6);
    }

    private void OnEnable()
    {
        RelicBatchedTickSystem.Register(this);
    }

    private void OnDisable()
    {
        RelicBatchedTickSystem.Unregister(this);
        if (!active)
            return;

        active = false;
        player?.Progression?.NotifyStatsChanged();
    }

    public bool IsBatchedUpdateActive => isActiveAndEnabled && cfg != null;

    public float BatchedUpdateInterval => Mathf.Max(0.05f, cfg != null ? cfg.checkInterval : 0.25f);

    public RelicTickArchetype BatchedTickArchetype => RelicTickArchetype.PlayerState;

    public void TickFromRelicBatch(float now, float deltaTime)
    {
        bool nowActive = CountNearbyEnemies() == 1;
        if (nowActive == active)
            return;

        active = nowActive;
        player?.Progression?.NotifyStatsChanged();
    }

    private int CountNearbyEnemies()
    {
        LayerMask mask = cfg.enemyMask.value != 0 ? cfg.enemyMask : LayerMask.GetMask("Enemy", "Zombie");

        Collider[] hits;
        if (mask.value != 0)
            hits = EnemyQueryService.OverlapSphere(transform.position, cfg.radius, mask, QueryTriggerInteraction.Ignore, this);
        else
            hits = EnemyQueryService.OverlapSphere(transform.position, cfg.radius, ~0, QueryTriggerInteraction.Ignore, this);

        int count = 0;
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

            count++;
            if (count > 1)
                break;
        }

        return count;
    }
}

