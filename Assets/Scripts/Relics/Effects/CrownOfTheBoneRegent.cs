using UnityEngine;
using GrassSim.Combat;

[CreateAssetMenu(
    menuName = "GrassSim/Relics/Effects/Legendary/Crown Of The Bone Regent",
    fileName = "Relic_CrownOfTheBoneRegent"
)]
public class CrownOfTheBoneRegent : RelicEffect, ISwingSpeedModifier, ICritChanceModifier
{
    [Header("Decree")]
    [Min(1)] public int maxDecree = 20;

    [Header("Regency")]
    public float baseRegencyDuration = 8f;
    public float regencyDurationPerStack = 0.5f;
    public float extendOnKill = 0.7f;
    public float maxExtraDuration = 4f;

    [Header("Bonuses")]
    public float baseSwingSpeedBonus = 0.2f;
    public float swingSpeedBonusPerStack = 0.03f;
    public float baseCritChanceBonus = 0.2f;
    public float critChanceBonusPerStack = 0.03f;

    public override void OnAcquire(PlayerRelicController player, int stacks)
    {
        Attach(player)?.Configure(this, stacks);
    }

    public override void OnStack(PlayerRelicController player, int stacks)
    {
        Attach(player)?.Configure(this, stacks);
    }

    public float GetSwingSpeedBonus(PlayerRelicController player, int stacks)
    {
        var rt = player != null ? player.GetComponent<CrownOfTheBoneRegentRuntime>() : null;
        if (rt == null || !rt.IsRegencyActive)
            return 0f;

        return baseSwingSpeedBonus + swingSpeedBonusPerStack * Mathf.Max(0, stacks - 1);
    }

    public float GetCritChanceBonus(PlayerRelicController player, int stacks)
    {
        var rt = player != null ? player.GetComponent<CrownOfTheBoneRegentRuntime>() : null;
        if (rt == null || !rt.IsRegencyActive)
            return 0f;

        return baseCritChanceBonus + critChanceBonusPerStack * Mathf.Max(0, stacks - 1);
    }

    private CrownOfTheBoneRegentRuntime Attach(PlayerRelicController player)
    {
        if (player == null)
            return null;

        var rt = player.GetComponent<CrownOfTheBoneRegentRuntime>();
        if (rt == null)
            rt = player.gameObject.AddComponent<CrownOfTheBoneRegentRuntime>();

        return rt;
    }
}

public class CrownOfTheBoneRegentRuntime : MonoBehaviour, IRelicBatchedUpdate, IRelicBatchedCadence
{
    private PlayerRelicController player;
    private CrownOfTheBoneRegent cfg;
    private int stacks;
    private bool subscribed;
    private bool wasActive;

    private int decree;
    private float regencyEndsAt;
    private float regencyHardCapAt;

    public bool IsRegencyActive => Time.time < regencyEndsAt;

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

    public void Configure(CrownOfTheBoneRegent config, int stackCount)
    {
        cfg = config;
        stacks = Mathf.Max(1, stackCount);
        TrySubscribe();
    }

    public bool IsBatchedUpdateActive => isActiveAndEnabled && player != null;

    public float BatchedUpdateInterval => 0.05f;

    public RelicTickArchetype BatchedTickArchetype => RelicTickArchetype.PlayerState;

    public void TickFromRelicBatch(float now, float deltaTime)
    {
        bool nowActive = IsRegencyActive;
        if (nowActive != wasActive)
        {
            wasActive = nowActive;
            player.Progression?.NotifyStatsChanged();
        }
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
        if (cfg == null || target == null || target.IsDead)
            return;

        if (IsRegencyActive)
            return;

        decree = Mathf.Min(Mathf.Max(1, cfg.maxDecree), decree + 1);
        if (decree >= Mathf.Max(1, cfg.maxDecree))
            ActivateRegency();
    }

    private void OnMeleeKill(Combatant target, float damage, bool isCrit)
    {
        if (cfg == null || !IsRegencyActive)
            return;

        regencyEndsAt = Mathf.Min(regencyHardCapAt, regencyEndsAt + Mathf.Max(0f, cfg.extendOnKill));
    }

    private void ActivateRegency()
    {
        decree = 0;

        float duration = cfg.baseRegencyDuration + cfg.regencyDurationPerStack * Mathf.Max(0, stacks - 1);
        duration = Mathf.Max(0.2f, duration);

        regencyEndsAt = Time.time + duration;
        regencyHardCapAt = regencyEndsAt + Mathf.Max(0f, cfg.maxExtraDuration);
    }
}
