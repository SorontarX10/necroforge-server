using UnityEngine;
using GrassSim.Combat;
using GrassSim.Core;

[CreateAssetMenu(
    menuName = "GrassSim/Relics/Effects/Common/Gravedigger Hook",
    fileName = "Relic_GravediggerHook"
)]
public class GravediggerHook : RelicEffect
{
    [Header("Trigger")]
    [Min(1)] public int hitsPerRoot = 6;

    [Header("Root")]
    public float baseRootDuration = 0.7f;
    public float extraRootDurationPerStack = 0.1f;

    public override void OnAcquire(PlayerRelicController player, int stacks)
    {
        Attach(player)?.Configure(this, stacks);
    }

    public override void OnStack(PlayerRelicController player, int stacks)
    {
        Attach(player)?.Configure(this, stacks);
    }

    private GravediggerHookRuntime Attach(PlayerRelicController player)
    {
        if (player == null) return null;
        var rt = player.GetComponent<GravediggerHookRuntime>();
        if (rt == null) rt = player.gameObject.AddComponent<GravediggerHookRuntime>();
        return rt;
    }
}

public class GravediggerHookRuntime : MonoBehaviour
{
    private static readonly Color RootColor = new(0.78f, 0.9f, 0.5f, 0.95f);

    private PlayerRelicController player;
    private GravediggerHook cfg;
    private int stacks;
    private int hitCounter;
    private bool subscribed;

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

    public void Configure(GravediggerHook config, int stackCount)
    {
        cfg = config;
        stacks = Mathf.Max(1, stackCount);
        TrySubscribe();
    }

    private void TrySubscribe()
    {
        if (subscribed || player == null)
            return;

        player.OnMeleeHitDealt += OnMeleeHit;
        subscribed = true;
    }

    private void TryUnsubscribe()
    {
        if (!subscribed || player == null)
            return;

        player.OnMeleeHitDealt -= OnMeleeHit;
        subscribed = false;
    }

    private void OnMeleeHit(Combatant target, float damage, bool isCrit)
    {
        if (cfg == null || target == null || target.IsDead)
            return;

        if (target.GetComponent<PlayerProgressionController>() != null)
            return;

        hitCounter++;
        if (hitCounter < Mathf.Max(1, cfg.hitsPerRoot))
            return;

        hitCounter = 0;
        float duration = cfg.baseRootDuration + cfg.extraRootDurationPerStack * Mathf.Max(0, stacks - 1);

        var debuff = target.GetComponent<RelicRootDebuff>();
        if (debuff == null)
            debuff = target.gameObject.AddComponent<RelicRootDebuff>();

        debuff.Apply(duration);
        RelicGeneratedVfx.SpawnAttachedMarker(
            target.transform,
            0.72f,
            RootColor,
            Mathf.Max(0.15f, duration),
            new Vector3(0f, 0.04f, 0f),
            "GravediggerHook_Root"
        );
    }
}
