using UnityEngine;

[CreateAssetMenu(
    menuName = "GrassSim/Relics/Effects/Common/Cup Of Penance",
    fileName = "Relic_CupOfPenance"
)]
public class CupOfPenance : RelicEffect
{
    [Header("Barrier Conversion")]
    [Range(0f, 1f)] public float baseOverhealToBarrier = 0.2f;
    [Range(0f, 1f)] public float extraOverhealToBarrierPerStack = 0.03f;
    [Range(0f, 1f)] public float baseBarrierCapPct = 0.15f;
    [Range(0f, 1f)] public float extraBarrierCapPctPerStack = 0.02f;

    public override void OnAcquire(PlayerRelicController player, int stacks)
    {
        Attach(player)?.Configure(this, stacks);
    }

    public override void OnStack(PlayerRelicController player, int stacks)
    {
        Attach(player)?.Configure(this, stacks);
    }

    private CupOfPenanceRuntime Attach(PlayerRelicController player)
    {
        if (player == null) return null;
        var rt = player.GetComponent<CupOfPenanceRuntime>();
        if (rt == null) rt = player.gameObject.AddComponent<CupOfPenanceRuntime>();
        return rt;
    }
}

public class CupOfPenanceRuntime : MonoBehaviour
{
    private PlayerRelicController player;
    private CupOfPenance cfg;
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

    public void Configure(CupOfPenance config, int stackCount)
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

        float conversion = cfg.baseOverhealToBarrier +
            cfg.extraOverhealToBarrierPerStack * Mathf.Max(0, stacks - 1);
        conversion = Mathf.Clamp01(conversion);

        float capPct = cfg.baseBarrierCapPct +
            cfg.extraBarrierCapPctPerStack * Mathf.Max(0, stacks - 1);
        capPct = Mathf.Clamp(capPct, 0f, 0.95f);

        float cap = progression.MaxHealth * capPct;
        float gained = overheal * conversion;
        progression.AddBarrier(gained, cap);
    }
}
