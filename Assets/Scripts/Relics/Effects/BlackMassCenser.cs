using UnityEngine;
using GrassSim.Combat;

[CreateAssetMenu(
    menuName = "GrassSim/Relics/Effects/Mythic/Black Mass Censer",
    fileName = "Relic_BlackMassCenser"
)]
public class BlackMassCenser : RelicEffect, IDamageModifier, ISwingSpeedModifier, IDamageReductionModifier
{
    public enum RiteType
    {
        Wrath = 0,
        Mercy = 1
    }

    [Header("Rite Cycle")]
    public float riteDuration = 8f;
    public float extendOnKill = 0.5f;
    public float maxExtraDuration = 4f;

    [Header("Wrath Rite")]
    public float baseWrathDamageBonus = 0.25f;
    public float wrathDamagePerStack = 0.03f;
    public float baseWrathSwingSpeedBonus = 0.2f;
    public float wrathSwingSpeedPerStack = 0.02f;

    [Header("Mercy Rite")]
    public float baseMercyDamageReduction = 0.16f;
    public float mercyDamageReductionPerStack = 0.02f;
    public float baseMercyHealPerSecond = 12f;
    public float mercyHealPerSecondPerStack = 1.5f;

    public override void OnAcquire(PlayerRelicController player, int stacks)
    {
        Attach(player)?.Configure(this, stacks);
    }

    public override void OnStack(PlayerRelicController player, int stacks)
    {
        Attach(player)?.Configure(this, stacks);
    }

    public float GetDamageMultiplier(PlayerRelicController player, int stacks)
    {
        var rt = player != null ? player.GetComponent<BlackMassCenserRuntime>() : null;
        if (rt == null || rt.CurrentRite != RiteType.Wrath)
            return 1f;

        return 1f + baseWrathDamageBonus + wrathDamagePerStack * Mathf.Max(0, stacks - 1);
    }

    public float GetSwingSpeedBonus(PlayerRelicController player, int stacks)
    {
        var rt = player != null ? player.GetComponent<BlackMassCenserRuntime>() : null;
        if (rt == null || rt.CurrentRite != RiteType.Wrath)
            return 0f;

        return baseWrathSwingSpeedBonus + wrathSwingSpeedPerStack * Mathf.Max(0, stacks - 1);
    }

    public float GetDamageReductionBonus(PlayerRelicController player, int stacks)
    {
        var rt = player != null ? player.GetComponent<BlackMassCenserRuntime>() : null;
        if (rt == null || rt.CurrentRite != RiteType.Mercy)
            return 0f;

        return baseMercyDamageReduction + mercyDamageReductionPerStack * Mathf.Max(0, stacks - 1);
    }

    private BlackMassCenserRuntime Attach(PlayerRelicController player)
    {
        if (player == null)
            return null;

        var rt = player.GetComponent<BlackMassCenserRuntime>();
        if (rt == null)
            rt = player.gameObject.AddComponent<BlackMassCenserRuntime>();

        return rt;
    }
}

public class BlackMassCenserRuntime : MonoBehaviour, IRelicBatchedUpdate, IRelicBatchedCadence
{
    private static readonly Color WrathTextColor = new(1f, 0.42f, 0.32f, 1f);
    private static readonly Color MercyTextColor = new(0.46f, 0.9f, 0.58f, 1f);
    private const float FloatingTextHeight = 2.05f;
    private const float FloatingTextSize = 32f;

    private PlayerRelicController player;
    private BlackMassCenser cfg;
    private int stacks;
    private bool subscribed;
    private BlackMassCenser.RiteType previousRite = (BlackMassCenser.RiteType)(-1);

    private BlackMassCenser.RiteType currentRite = BlackMassCenser.RiteType.Wrath;
    private float riteEndsAt;
    private float riteHardCapAt;

    public BlackMassCenser.RiteType CurrentRite => currentRite;
    public float RiteTimeRemaining => Mathf.Max(0f, riteEndsAt - Time.time);

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

    public void Configure(BlackMassCenser config, int stackCount)
    {
        cfg = config;
        stacks = Mathf.Max(1, stackCount);
        if (riteEndsAt <= 0f)
            BeginRite(currentRite);

        TrySubscribe();
    }

    public bool IsBatchedUpdateActive => isActiveAndEnabled && cfg != null;

    public float BatchedUpdateInterval => 0.05f;

    public RelicTickArchetype BatchedTickArchetype => RelicTickArchetype.PlayerState;

    public void TickFromRelicBatch(float now, float deltaTime)
    {
        if (cfg == null || player == null || player.Progression == null)
            return;

        if (now >= riteEndsAt)
            BeginRite(currentRite == BlackMassCenser.RiteType.Wrath
                ? BlackMassCenser.RiteType.Mercy
                : BlackMassCenser.RiteType.Wrath);

        if (currentRite == BlackMassCenser.RiteType.Mercy)
        {
            float hps = cfg.baseMercyHealPerSecond + cfg.mercyHealPerSecondPerStack * Mathf.Max(0, stacks - 1);
            if (hps > 0f)
                player.Progression.Heal(hps * deltaTime);
        }

        if (currentRite != previousRite)
        {
            previousRite = currentRite;
            player.Progression.NotifyStatsChanged();
        }
    }

    private void TrySubscribe()
    {
        if (subscribed || player == null)
            return;

        player.OnMeleeKill += OnMeleeKill;
        subscribed = true;
    }

    private void TryUnsubscribe()
    {
        if (!subscribed || player == null)
            return;

        player.OnMeleeKill -= OnMeleeKill;
        subscribed = false;
    }

    private void BeginRite(BlackMassCenser.RiteType rite)
    {
        currentRite = rite;
        riteEndsAt = Time.time + Mathf.Max(0.2f, cfg.riteDuration);
        riteHardCapAt = riteEndsAt + Mathf.Max(0f, cfg.maxExtraDuration);
        SpawnRiteSwitchText(rite);
    }

    private void OnMeleeKill(Combatant target, float damage, bool isCrit)
    {
        if (cfg == null)
            return;

        riteEndsAt = Mathf.Min(riteHardCapAt, riteEndsAt + Mathf.Max(0f, cfg.extendOnKill));
    }

    private void SpawnRiteSwitchText(BlackMassCenser.RiteType rite)
    {
        if (FloatingTextSystem.Instance == null)
            return;

        string label = rite == BlackMassCenser.RiteType.Wrath ? "Rite: WRATH" : "Rite: MERCY";
        Color color = rite == BlackMassCenser.RiteType.Wrath ? WrathTextColor : MercyTextColor;
        Vector3 pos = transform.position + Vector3.up * FloatingTextHeight;
        FloatingTextSystem.Instance.SpawnText(pos, label, color, FloatingTextSize);
    }
}
