using UnityEngine;

[CreateAssetMenu(
    menuName = "GrassSim/Relics/Effects/Hunter's Sigil",
    fileName = "Relic_HuntersSigil"
)]
public class HuntersSigil : RelicEffect, ISpeedModifier, ICritChanceModifier
{
    [Header("Detection")]
    public float detectRadius = 12f;
    public float checkInterval = 0.25f;
    public LayerMask enemyMask; // if 0 -> Enemy

    [Header("Bonuses when NO enemies nearby")]
    [Tooltip("+ speed per stack while focused")]
    public float speedBonusPerStack = 0.14f;

    [Tooltip("+ crit chance (0..1) per stack while focused")]
    public float critBonusPerStack = 0.02f;

    public override void OnAcquire(PlayerRelicController player, int stacks)
    {
        Attach(player)?.Configure(this);
    }

    public override void OnStack(PlayerRelicController player, int stacks)
    {
        Attach(player)?.Configure(this);
    }

    private HuntersSigilRuntime Attach(PlayerRelicController player)
    {
        if (player == null) return null;
        var rt = player.GetComponent<HuntersSigilRuntime>();
        if (rt == null) rt = player.gameObject.AddComponent<HuntersSigilRuntime>();
        return rt;
    }

    public float GetSpeedBonus(PlayerRelicController player, int stacks)
    {
        if (player == null || stacks <= 0)
            return 0f;

        var rt = player.GetComponent<HuntersSigilRuntime>();
        return rt != null && rt.IsFocused ? speedBonusPerStack * stacks : 0f;
    }

    public float GetCritChanceBonus(PlayerRelicController player, int stacks)
    {
        if (player == null || stacks <= 0)
            return 0f;

        var rt = player.GetComponent<HuntersSigilRuntime>();
        return rt != null && rt.IsFocused ? critBonusPerStack * stacks : 0f;
    }
}

public class HuntersSigilRuntime : MonoBehaviour, IRelicBatchedUpdate, IRelicBatchedCadence
{
    private HuntersSigil cfg;
    private bool focused;
    private PlayerRelicController player;

    public bool IsFocused => focused;

    private void Awake()
    {
        player = GetComponent<PlayerRelicController>();
    }

    public void Configure(HuntersSigil config)
    {
        cfg = config;
        EnemyQueryService.ConfigureOwnerBudget(this, RelicQueryBudgetProfiles.For(RelicTickArchetype.PlayerState));
    }

    private void OnEnable()
    {
        RelicBatchedTickSystem.Register(this);
    }

    private void OnDisable()
    {
        RelicBatchedTickSystem.Unregister(this);
    }

    public bool IsBatchedUpdateActive => isActiveAndEnabled && cfg != null;

    public float BatchedUpdateInterval => Mathf.Max(0.05f, cfg != null ? cfg.checkInterval : 0.25f);

    public RelicTickArchetype BatchedTickArchetype => RelicTickArchetype.PlayerState;

    public void TickFromRelicBatch(float now, float deltaTime)
    {
        LayerMask mask = cfg.enemyMask.value != 0 ? cfg.enemyMask : LayerMask.GetMask("Enemy");
        EnemyQueryService.OverlapSphere(transform.position, cfg.detectRadius, mask, QueryTriggerInteraction.Ignore, this);
        bool nowFocused = EnemyQueryService.GetLastHitCount(this) == 0;
        if (nowFocused == focused)
            return;

        focused = nowFocused;
        player?.Progression?.NotifyStatsChanged();
    }
}

