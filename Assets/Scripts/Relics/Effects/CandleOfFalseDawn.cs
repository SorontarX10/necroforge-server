using UnityEngine;

[CreateAssetMenu(
    menuName = "GrassSim/Relics/Effects/Rare/Candle Of False Dawn",
    fileName = "Relic_CandleOfFalseDawn"
)]
public class CandleOfFalseDawn : RelicEffect, ISpeedModifier, IDamageModifier
{
    [Header("Threshold")]
    [Range(0.05f, 0.95f)] public float healthThresholdPct = 0.4f;

    [Header("Low Health Bonuses")]
    public float baseSpeedBonus = 0.35f;
    public float speedBonusPerStack = 0.05f;
    public float baseHealthRegenPerSecond = 20f;
    public float healthRegenPerStack = 3f;

    [Header("Low Health Penalty")]
    public float baseDamageMultiplier = 0.8f;
    public float damageMultiplierPerStack = 0.03f;

    public override void OnAcquire(PlayerRelicController player, int stacks)
    {
        Attach(player)?.Configure(this, stacks);
    }

    public override void OnStack(PlayerRelicController player, int stacks)
    {
        Attach(player)?.Configure(this, stacks);
    }

    public float GetSpeedBonus(PlayerRelicController player, int stacks)
    {
        var rt = player != null ? player.GetComponent<CandleOfFalseDawnRuntime>() : null;
        if (rt == null || !rt.Active)
            return 0f;

        return baseSpeedBonus + speedBonusPerStack * Mathf.Max(0, stacks - 1);
    }

    public float GetDamageMultiplier(PlayerRelicController player, int stacks)
    {
        var rt = player != null ? player.GetComponent<CandleOfFalseDawnRuntime>() : null;
        if (rt == null || !rt.Active)
            return 1f;

        return Mathf.Max(0f, baseDamageMultiplier + damageMultiplierPerStack * Mathf.Max(0, stacks - 1));
    }

    private CandleOfFalseDawnRuntime Attach(PlayerRelicController player)
    {
        if (player == null)
            return null;

        var rt = player.GetComponent<CandleOfFalseDawnRuntime>();
        if (rt == null)
            rt = player.gameObject.AddComponent<CandleOfFalseDawnRuntime>();

        return rt;
    }
}

public class CandleOfFalseDawnRuntime : MonoBehaviour, IRelicBatchedUpdate, IRelicBatchedCadence
{
    private PlayerRelicController player;
    private CandleOfFalseDawn cfg;
    private int stacks;
    private bool active;

    public bool Active => active;

    private void Awake()
    {
        player = GetComponent<PlayerRelicController>();
    }

    public void Configure(CandleOfFalseDawn config, int stackCount)
    {
        cfg = config;
        stacks = Mathf.Max(1, stackCount);
    }

    private void OnEnable()
    {
        RelicBatchedTickSystem.Register(this);
    }

    private void OnDisable()
    {
        RelicBatchedTickSystem.Unregister(this);
    }

    public bool IsBatchedUpdateActive => isActiveAndEnabled && cfg != null && player != null && player.Progression != null;

    public float BatchedUpdateInterval => 0.04f;

    public RelicTickArchetype BatchedTickArchetype => RelicTickArchetype.PlayerState;

    public void TickFromRelicBatch(float now, float deltaTime)
    {
        float maxHp = Mathf.Max(1f, player.Progression.MaxHealth);
        float hpPct = player.Progression.CurrentHealth / maxHp;
        bool nowActive = hpPct <= cfg.healthThresholdPct;

        if (nowActive)
        {
            float regen = cfg.baseHealthRegenPerSecond + cfg.healthRegenPerStack * Mathf.Max(0, stacks - 1);
            if (regen > 0f)
                player.Progression.Heal(regen * Mathf.Max(0f, deltaTime));
        }

        if (nowActive != active)
        {
            active = nowActive;
            player.Progression.NotifyStatsChanged();
        }
    }
}
