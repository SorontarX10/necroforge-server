using UnityEngine;

[CreateAssetMenu(
    menuName = "GrassSim/Relics/Effects/Uncommon/Marrow Reservoir",
    fileName = "Relic_MarrowReservoir"
)]
public class MarrowReservoir : RelicEffect
{
    [Header("Barrier Conversion")]
    [Range(0f, 1f)] public float baseOverhealToBarrier = 0.3f;
    [Range(0f, 1f)] public float overhealToBarrierPerStack = 0.04f;
    [Range(0f, 1f)] public float baseBarrierCapPct = 0.25f;
    [Range(0f, 1f)] public float barrierCapPctPerStack = 0.03f;

    public override void OnAcquire(PlayerRelicController player, int stacks)
    {
        Attach(player)?.Configure(this, stacks);
    }

    public override void OnStack(PlayerRelicController player, int stacks)
    {
        Attach(player)?.Configure(this, stacks);
    }

    private MarrowReservoirRuntime Attach(PlayerRelicController player)
    {
        if (player == null)
            return null;

        var rt = player.GetComponent<MarrowReservoirRuntime>();
        if (rt == null)
            rt = player.gameObject.AddComponent<MarrowReservoirRuntime>();

        return rt;
    }
}

public class MarrowReservoirRuntime : MonoBehaviour
{
    private PlayerRelicController player;
    private MarrowReservoir cfg;
    private int stacks;
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

    public void Configure(MarrowReservoir config, int stackCount)
    {
        cfg = config;
        stacks = Mathf.Max(1, stackCount);
        TrySubscribe();
    }

    private void TrySubscribe()
    {
        if (subscribed || player == null)
            return;

        player.OnHealed += OnHealed;
        subscribed = true;
    }

    private void TryUnsubscribe()
    {
        if (!subscribed || player == null)
            return;

        player.OnHealed -= OnHealed;
        subscribed = false;
    }

    private void OnHealed(float amount, float overheal)
    {
        if (cfg == null || overheal <= 0f)
            return;

        var progression = player != null ? player.Progression : null;
        if (progression == null)
            return;

        float conversion = cfg.baseOverhealToBarrier + cfg.overhealToBarrierPerStack * Mathf.Max(0, stacks - 1);
        conversion = Mathf.Clamp01(conversion);

        float capPct = cfg.baseBarrierCapPct + cfg.barrierCapPctPerStack * Mathf.Max(0, stacks - 1);
        capPct = Mathf.Clamp(capPct, 0f, 0.95f);

        float cap = progression.MaxHealth * capPct;
        progression.AddBarrier(overheal * conversion, cap);
    }
}
