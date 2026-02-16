using UnityEngine;

[CreateAssetMenu(
    menuName = "GrassSim/Relics/Effects/Common/Guardian's Vow",
    fileName = "Relic_GuardiansVow"
)]
public class GuardiansVow : RelicEffect
{
    [Header("Ward")]
    public float chargeDelay = 7f;
    public float cooldown = 12f;
    public float chargeDelayReductionPerStack = 0.3f;
    public float cooldownReductionPerStack = 0.6f;

    public override void OnAcquire(PlayerRelicController player, int stacks)
    {
        Attach(player)?.Configure(this, stacks);
    }

    public override void OnStack(PlayerRelicController player, int stacks)
    {
        Attach(player)?.Configure(this, stacks);
    }

    private GuardiansVowRuntime Attach(PlayerRelicController player)
    {
        if (player == null) return null;
        var rt = player.GetComponent<GuardiansVowRuntime>();
        if (rt == null) rt = player.gameObject.AddComponent<GuardiansVowRuntime>();
        return rt;
    }
}

public class GuardiansVowRuntime : MonoBehaviour, IRelicBatchedUpdate, IRelicBatchedCadence
{
    private PlayerRelicController player;
    private GuardiansVow cfg;
    private int stacks = 1;
    private bool subscribed;
    private bool wardReady;
    private float lastDamageAt;
    private float nextWardAt;

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
    }

    public void Configure(GuardiansVow config, int stackCount)
    {
        cfg = config;
        stacks = Mathf.Max(1, stackCount);
        TrySubscribe();
    }

    public bool IsBatchedUpdateActive => isActiveAndEnabled && cfg != null;

    public float BatchedUpdateInterval => 0.05f;

    public RelicTickArchetype BatchedTickArchetype => RelicTickArchetype.PlayerState;

    public void TickFromRelicBatch(float now, float deltaTime)
    {
        if (wardReady)
            return;

        if (now < nextWardAt)
            return;

        float chargeDelay = Mathf.Max(
            0.35f,
            cfg.chargeDelay - cfg.chargeDelayReductionPerStack * Mathf.Max(0, stacks - 1)
        );
        if (now - lastDamageAt >= chargeDelay)
            wardReady = true;
    }

    private void TrySubscribe()
    {
        if (subscribed || player == null)
            return;

        player.OnDamageTaken += OnDamageTaken;
        player.OnTryBlockIncomingHit += TryBlockHit;
        subscribed = true;
    }

    private void TryUnsubscribe()
    {
        if (!subscribed || player == null)
            return;

        player.OnDamageTaken -= OnDamageTaken;
        player.OnTryBlockIncomingHit -= TryBlockHit;
        subscribed = false;
    }

    private void OnDamageTaken(float amount)
    {
        lastDamageAt = Time.time;
    }

    private bool TryBlockHit()
    {
        if (!wardReady || cfg == null)
            return false;

        wardReady = false;
        lastDamageAt = Time.time;
        float cooldown = Mathf.Max(
            2f,
            cfg.cooldown - cfg.cooldownReductionPerStack * Mathf.Max(0, stacks - 1)
        );
        nextWardAt = Time.time + cooldown;
        return true;
    }
}
