using UnityEngine;

[CreateAssetMenu(
    menuName = "GrassSim/Relics/Effects/Rare/Oath Of Still Iron",
    fileName = "Relic_OathOfStillIron"
)]
public class OathOfStillIron : RelicEffect, IDamageReductionModifier, IStaminaRegenModifier, ICritChanceModifier
{
    [Header("Stance")]
    public float stillTimeToActivate = 1.25f;
    public float movementThreshold = 0.08f;

    [Header("Bonuses")]
    public float baseDamageReductionBonus = 0.2f;
    public float damageReductionPerStack = 0.03f;
    public float baseStaminaRegenBonus = 10f;
    public float staminaRegenPerStack = 1.5f;
    public float baseCritChanceBonus = 0.1f;
    public float critChancePerStack = 0.02f;

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
        var rt = player != null ? player.GetComponent<OathOfStillIronRuntime>() : null;
        if (rt == null || !rt.Active)
            return 0f;

        return baseDamageReductionBonus + damageReductionPerStack * Mathf.Max(0, stacks - 1);
    }

    public float GetStaminaRegenBonus(PlayerRelicController player, int stacks)
    {
        var rt = player != null ? player.GetComponent<OathOfStillIronRuntime>() : null;
        if (rt == null || !rt.Active)
            return 0f;

        return baseStaminaRegenBonus + staminaRegenPerStack * Mathf.Max(0, stacks - 1);
    }

    public float GetCritChanceBonus(PlayerRelicController player, int stacks)
    {
        var rt = player != null ? player.GetComponent<OathOfStillIronRuntime>() : null;
        if (rt == null || !rt.Active)
            return 0f;

        return baseCritChanceBonus + critChancePerStack * Mathf.Max(0, stacks - 1);
    }

    private OathOfStillIronRuntime Attach(PlayerRelicController player)
    {
        if (player == null)
            return null;

        var rt = player.GetComponent<OathOfStillIronRuntime>();
        if (rt == null)
            rt = player.gameObject.AddComponent<OathOfStillIronRuntime>();

        return rt;
    }
}

public class OathOfStillIronRuntime : MonoBehaviour, IRelicBatchedUpdate, IRelicBatchedCadence
{
    private PlayerRelicController player;
    private OathOfStillIron cfg;
    private int stacks;
    private bool active;
    private Vector3 lastPos;
    private float stillSince;

    public bool Active => active;

    private void Awake()
    {
        player = GetComponent<PlayerRelicController>();
        lastPos = transform.position;
        stillSince = Time.time;
    }

    private void OnEnable()
    {
        RelicBatchedTickSystem.Register(this);
        lastPos = transform.position;
        stillSince = Time.time;
    }

    private void OnDisable()
    {
        RelicBatchedTickSystem.Unregister(this);
        active = false;
    }

    public void Configure(OathOfStillIron config, int stackCount)
    {
        cfg = config;
        stacks = Mathf.Max(1, stackCount);
    }

    public bool IsBatchedUpdateActive => isActiveAndEnabled && cfg != null;

    public float BatchedUpdateInterval => 0.033f;

    public RelicTickArchetype BatchedTickArchetype => RelicTickArchetype.PlayerState;

    public void TickFromRelicBatch(float now, float deltaTime)
    {
        Vector3 nowPos = transform.position;
        Vector3 delta = nowPos - lastPos;
        delta.y = 0f;
        lastPos = nowPos;

        bool moving = delta.magnitude > Mathf.Max(0.001f, cfg.movementThreshold);
        bool wasActive = active;

        if (moving)
        {
            stillSince = now;
            active = false;
        }
        else if (now - stillSince >= Mathf.Max(0.05f, cfg.stillTimeToActivate))
        {
            active = true;
        }

        if (active != wasActive)
            player?.Progression?.NotifyStatsChanged();
    }
}
