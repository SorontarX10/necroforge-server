using UnityEngine;

[CreateAssetMenu(
    menuName = "GrassSim/Relics/Effects/Uncommon/Iron Vigil",
    fileName = "Relic_IronVigil"
)]
public class IronVigil : RelicEffect, IDodgeChanceModifier
{
    [Header("Vigil")]
    public float chargeDelay = 7f;
    public float baseDodgeChanceBonus = 0.15f;
    public float extraDodgeChancePerStack = 0.025f;

    public override void OnAcquire(PlayerRelicController player, int stacks)
    {
        Attach(player)?.Configure(this);
    }

    public override void OnStack(PlayerRelicController player, int stacks)
    {
        Attach(player)?.Configure(this);
    }

    public float GetDodgeChanceBonus(PlayerRelicController player, int stacks)
    {
        var rt = player != null ? player.GetComponent<IronVigilRuntime>() : null;
        if (rt == null || !rt.IsVigilActive)
            return 0f;

        return baseDodgeChanceBonus + extraDodgeChancePerStack * Mathf.Max(0, stacks - 1);
    }

    private IronVigilRuntime Attach(PlayerRelicController player)
    {
        if (player == null)
            return null;

        var rt = player.GetComponent<IronVigilRuntime>();
        if (rt == null)
            rt = player.gameObject.AddComponent<IronVigilRuntime>();

        return rt;
    }
}

public class IronVigilRuntime : MonoBehaviour, IRelicBatchedUpdate, IRelicBatchedCadence
{
    private PlayerRelicController player;
    private IronVigil cfg;
    private bool subscribed;
    private bool active;
    private float lastDamageAt;

    public bool IsVigilActive => active;

    private void Awake()
    {
        player = GetComponent<PlayerRelicController>();
        lastDamageAt = Time.time;
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
        active = false;
    }

    public void Configure(IronVigil config)
    {
        cfg = config;
        TrySubscribe();
    }

    public bool IsBatchedUpdateActive => isActiveAndEnabled && cfg != null;

    public float BatchedUpdateInterval => 0.05f;

    public RelicTickArchetype BatchedTickArchetype => RelicTickArchetype.PlayerState;

    public void TickFromRelicBatch(float now, float deltaTime)
    {
        if (active)
            return;

        if (now - lastDamageAt >= Mathf.Max(0.1f, cfg.chargeDelay))
        {
            active = true;
            player?.Progression?.NotifyStatsChanged();
        }
    }

    private void TrySubscribe()
    {
        if (subscribed || player == null)
            return;

        player.OnDamageTaken += OnDamageTaken;
        subscribed = true;
    }

    private void TryUnsubscribe()
    {
        if (!subscribed || player == null)
            return;

        player.OnDamageTaken -= OnDamageTaken;
        subscribed = false;
    }

    private void OnDamageTaken(float amount)
    {
        lastDamageAt = Time.time;
        if (!active)
            return;

        active = false;
        player?.Progression?.NotifyStatsChanged();
    }
}
