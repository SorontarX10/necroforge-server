using UnityEngine;
using GrassSim.Combat;
using GrassSim.Core;

[CreateAssetMenu(
    menuName = "GrassSim/Relics/Effects/Uncommon/Warden's Chain",
    fileName = "Relic_WardenChain"
)]
public class WardenChain : RelicEffect
{
    [Header("Charge")]
    public float chargeWindow = 4f;

    [Header("Pull")]
    public float basePullDistance = 2f;
    public float extraPullDistancePerStack = 0.2f;
    public float minDistanceToPlayer = 1.4f;

    [Header("Root")]
    public float baseRootDuration = 0.75f;
    public float extraRootDurationPerStack = 0.1f;

    public override void OnAcquire(PlayerRelicController player, int stacks)
    {
        Attach(player)?.Configure(this, stacks);
    }

    public override void OnStack(PlayerRelicController player, int stacks)
    {
        Attach(player)?.Configure(this, stacks);
    }

    private WardenChainRuntime Attach(PlayerRelicController player)
    {
        if (player == null)
            return null;

        var rt = player.GetComponent<WardenChainRuntime>();
        if (rt == null)
            rt = player.gameObject.AddComponent<WardenChainRuntime>();

        return rt;
    }
}

public class WardenChainRuntime : MonoBehaviour
{
    private static readonly Color ChainColor = new(0.68f, 0.9f, 1f, 0.95f);

    private PlayerRelicController player;
    private WardenChain cfg;
    private int stacks;
    private bool subscribed;
    private float chargedUntil;

    private void Awake()
    {
        player = GetComponent<PlayerRelicController>();
    }

    private void OnEnable()
    {
        TrySubscribe();
    }

    private void OnDisable()
    {
        TryUnsubscribe();
    }

    public void Configure(WardenChain config, int stackCount)
    {
        cfg = config;
        stacks = Mathf.Max(1, stackCount);
        TrySubscribe();
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
        if (cfg == null)
            return;

        chargedUntil = Time.time + Mathf.Max(0.2f, cfg.chargeWindow);
    }

    private void OnMeleeHit(Combatant target, float damage, bool isCrit)
    {
        if (cfg == null || target == null || target.IsDead)
            return;

        if (Time.time > chargedUntil)
            return;

        if (target.GetComponent<PlayerProgressionController>() != null)
            return;

        chargedUntil = 0f;
        RelicGeneratedVfx.SpawnBeam(
            target.transform.position + Vector3.up * 1.0f,
            transform.position + Vector3.up * 1.0f,
            0.05f,
            ChainColor,
            0.16f,
            "WardenChain_Pull"
        );
        PullTarget(target);

        float duration = cfg.baseRootDuration + cfg.extraRootDurationPerStack * Mathf.Max(0, stacks - 1);
        var rootDebuff = target.GetComponent<RelicRootDebuff>();
        if (rootDebuff == null)
            rootDebuff = target.gameObject.AddComponent<RelicRootDebuff>();
        rootDebuff.Apply(duration);
        RelicGeneratedVfx.SpawnAttachedMarker(
            target.transform,
            0.78f,
            ChainColor,
            Mathf.Max(0.15f, duration),
            new Vector3(0f, 0.045f, 0f),
            "WardenChain_Root"
        );
    }

    private void PullTarget(Combatant target)
    {
        Vector3 playerPos = transform.position;
        Vector3 targetPos = target.transform.position;

        Vector3 toPlayer = playerPos - targetPos;
        toPlayer.y = 0f;
        float distance = toPlayer.magnitude;
        if (distance <= 0.001f)
            return;

        float pullDistance = cfg.basePullDistance + cfg.extraPullDistancePerStack * Mathf.Max(0, stacks - 1);
        float maxStep = Mathf.Min(pullDistance, Mathf.Max(0f, distance - Mathf.Max(0.5f, cfg.minDistanceToPlayer)));
        if (maxStep <= 0f)
            return;

        Vector3 delta = toPlayer.normalized * maxStep;
        var rb = target.GetComponent<Rigidbody>();
        if (rb != null && !rb.isKinematic)
            rb.MovePosition(rb.position + delta);
        else
            target.transform.position += delta;
    }
}
