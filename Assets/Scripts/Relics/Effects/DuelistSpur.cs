using UnityEngine;
using GrassSim.Combat;
using GrassSim.Core;

[CreateAssetMenu(
    menuName = "GrassSim/Relics/Effects/Uncommon/Duelist Spur",
    fileName = "Relic_DuelistSpur"
)]
public class DuelistSpur : RelicEffect, ISwingSpeedModifier, IDodgeChanceModifier
{
    [Header("Condition")]
    public float radius = 6f;
    public float checkInterval = 0.25f;
    public LayerMask enemyMask;

    [Header("Bonuses")]
    public float baseSwingSpeedBonus = 0.15f;
    public float swingSpeedBonusPerStack = 0.03f;
    public float baseDodgeChanceBonus = 0.1f;
    public float dodgeChanceBonusPerStack = 0.02f;

    public override void OnAcquire(PlayerRelicController player, int stacks)
    {
        Attach(player)?.Configure(this);
    }

    public override void OnStack(PlayerRelicController player, int stacks)
    {
        Attach(player)?.Configure(this);
    }

    public float GetSwingSpeedBonus(PlayerRelicController player, int stacks)
    {
        var rt = player != null ? player.GetComponent<DuelistSpurRuntime>() : null;
        if (rt == null || !rt.Active)
            return 0f;

        return baseSwingSpeedBonus + swingSpeedBonusPerStack * Mathf.Max(0, stacks - 1);
    }

    public float GetDodgeChanceBonus(PlayerRelicController player, int stacks)
    {
        var rt = player != null ? player.GetComponent<DuelistSpurRuntime>() : null;
        if (rt == null || !rt.Active)
            return 0f;

        return baseDodgeChanceBonus + dodgeChanceBonusPerStack * Mathf.Max(0, stacks - 1);
    }

    private DuelistSpurRuntime Attach(PlayerRelicController player)
    {
        if (player == null)
            return null;

        var rt = player.GetComponent<DuelistSpurRuntime>();
        if (rt == null)
            rt = player.gameObject.AddComponent<DuelistSpurRuntime>();

        return rt;
    }
}

public class DuelistSpurRuntime : MonoBehaviour, IRelicBatchedUpdate, IRelicBatchedCadence
{
    private DuelistSpur cfg;
    private PlayerRelicController player;
    private bool active;

    public bool Active => active;

    private void Awake()
    {
        player = GetComponent<PlayerRelicController>();
    }

    public void Configure(DuelistSpur config)
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

