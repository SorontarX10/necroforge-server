using UnityEngine;
using GrassSim.Combat;

[CreateAssetMenu(
    menuName = "GrassSim/Relics/Effects/Rare/Black Chapel Hourglass",
    fileName = "Relic_BlackChapelHourglass"
)]
public class BlackChapelHourglass : RelicEffect
{
    [Header("Charges")]
    public float chargeInterval = 18f;
    public float chargeIntervalReductionPerStack = 1f;
    [Min(1)] public int maxCharges = 2;

    [Header("Afterimage")]
    public float echoDamagePercent = 0.55f;
    public float echoDamagePerStack = 0.08f;

    public override void OnAcquire(PlayerRelicController player, int stacks)
    {
        Attach(player)?.Configure(this, stacks);
    }

    public override void OnStack(PlayerRelicController player, int stacks)
    {
        Attach(player)?.Configure(this, stacks);
    }

    private BlackChapelHourglassRuntime Attach(PlayerRelicController player)
    {
        if (player == null)
            return null;

        var rt = player.GetComponent<BlackChapelHourglassRuntime>();
        if (rt == null)
            rt = player.gameObject.AddComponent<BlackChapelHourglassRuntime>();

        return rt;
    }
}

public class BlackChapelHourglassRuntime : MonoBehaviour, IRelicBatchedUpdate, IRelicBatchedCadence
{
    private PlayerRelicController player;
    private BlackChapelHourglass cfg;
    private int stacks;
    private bool subscribed;

    private int charges;
    private float nextChargeAt;
    private float lastHitDamage;
    private float pendingEchoDamage;
    private bool applyingEcho;

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

    public void Configure(BlackChapelHourglass config, int stackCount)
    {
        cfg = config;
        stacks = Mathf.Max(1, stackCount);
        if (nextChargeAt <= 0f)
        {
            float interval = Mathf.Max(
                2f,
                cfg.chargeInterval - cfg.chargeIntervalReductionPerStack * Mathf.Max(0, stacks - 1)
            );
            nextChargeAt = Time.time + interval;
        }

        TrySubscribe();
    }

    public bool IsBatchedUpdateActive => isActiveAndEnabled && cfg != null;

    public float BatchedUpdateInterval => 0.1f;

    public RelicTickArchetype BatchedTickArchetype => RelicTickArchetype.PlayerState;

    public void TickFromRelicBatch(float now, float deltaTime)
    {
        int max = Mathf.Max(1, cfg.maxCharges);
        if (charges >= max)
            return;

        if (now < nextChargeAt)
            return;

        charges = Mathf.Min(max, charges + 1);
        float interval = Mathf.Max(
            2f,
            cfg.chargeInterval - cfg.chargeIntervalReductionPerStack * Mathf.Max(0, stacks - 1)
        );
        nextChargeAt = now + interval;
    }

    private void TrySubscribe()
    {
        if (subscribed || player == null)
            return;

        player.OnDodged += OnDodged;
        player.OnMeleeHitDealt += OnMeleeHit;
        subscribed = true;
    }

    private void TryUnsubscribe()
    {
        if (!subscribed || player == null)
            return;

        player.OnDodged -= OnDodged;
        player.OnMeleeHitDealt -= OnMeleeHit;
        subscribed = false;
    }

    private void OnDodged()
    {
        if (cfg == null || charges <= 0 || lastHitDamage <= 0f)
            return;

        charges--;
        float echoMultiplier = cfg.echoDamagePercent + cfg.echoDamagePerStack * Mathf.Max(0, stacks - 1);
        pendingEchoDamage = Mathf.Max(1f, lastHitDamage * Mathf.Max(0f, echoMultiplier));
    }

    private void OnMeleeHit(Combatant target, float damage, bool isCrit)
    {
        if (applyingEcho)
            return;

        if (damage > 0f)
            lastHitDamage = damage;

        if (pendingEchoDamage <= 0f || target == null || target.IsDead)
            return;

        applyingEcho = true;
        RelicDamageText.Deal(target, pendingEchoDamage, transform, cfg);
        applyingEcho = false;
        pendingEchoDamage = 0f;
    }
}
