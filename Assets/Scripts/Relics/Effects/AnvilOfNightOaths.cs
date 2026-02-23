using UnityEngine;
using GrassSim.Combat;

[CreateAssetMenu(
    menuName = "GrassSim/Relics/Effects/Legendary/Anvil Of Night Oaths",
    fileName = "Relic_AnvilOfNightOaths"
)]
public class AnvilOfNightOaths : RelicEffect, ISwordLengthModifier, ICritMultiplierModifier
{
    [Header("Forging")]
    [Min(1)] public int hitsToForge = 8;
    public float baseForgedDuration = 6f;
    public float forgedDurationPerStack = 0.4f;

    [Header("Bonuses")]
    public float baseSwordLengthBonus = 0.2f;
    public float swordLengthBonusPerStack = 0.02f;
    public float baseCritDamageBonus = 0.35f;
    public float critDamageBonusPerStack = 0.05f;

    public override void OnAcquire(PlayerRelicController player, int stacks)
    {
        Attach(player)?.Configure(this, stacks);
    }

    public override void OnStack(PlayerRelicController player, int stacks)
    {
        Attach(player)?.Configure(this, stacks);
    }

    public float GetSwordLengthBonus(PlayerRelicController player, int stacks)
    {
        var rt = player != null ? player.GetComponent<AnvilOfNightOathsRuntime>() : null;
        if (rt == null || !rt.IsForgedActive)
            return 0f;

        return baseSwordLengthBonus + swordLengthBonusPerStack * Mathf.Max(0, stacks - 1);
    }

    public float GetCritMultiplierBonus(PlayerRelicController player, int stacks)
    {
        var rt = player != null ? player.GetComponent<AnvilOfNightOathsRuntime>() : null;
        if (rt == null || !rt.IsForgedActive)
            return 0f;

        return baseCritDamageBonus + critDamageBonusPerStack * Mathf.Max(0, stacks - 1);
    }

    private AnvilOfNightOathsRuntime Attach(PlayerRelicController player)
    {
        if (player == null)
            return null;

        var rt = player.GetComponent<AnvilOfNightOathsRuntime>();
        if (rt == null)
            rt = player.gameObject.AddComponent<AnvilOfNightOathsRuntime>();

        return rt;
    }
}

public class AnvilOfNightOathsRuntime : MonoBehaviour, IRelicBatchedUpdate, IRelicBatchedCadence
{
    private PlayerRelicController player;
    private AnvilOfNightOaths cfg;
    private int stacks;
    private bool subscribed;
    private bool wasActive;

    private int forgedHits;
    private float forgedEndsAt;

    public bool IsForgedActive => Time.time < forgedEndsAt;
    public int ForgedHitProgress => forgedHits;
    public int HitsToForge => cfg != null ? Mathf.Max(1, cfg.hitsToForge) : 0;
    public float ForgedTimeRemaining => Mathf.Max(0f, forgedEndsAt - Time.time);

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

    public void Configure(AnvilOfNightOaths config, int stackCount)
    {
        cfg = config;
        stacks = Mathf.Max(1, stackCount);
        TrySubscribe();
    }

    public bool IsBatchedUpdateActive => isActiveAndEnabled && cfg != null;

    public float BatchedUpdateInterval => 0.08f;

    public RelicTickArchetype BatchedTickArchetype => RelicTickArchetype.PlayerState;

    public void TickFromRelicBatch(float now, float deltaTime)
    {
        bool nowActive = now < forgedEndsAt;
        if (nowActive != wasActive)
        {
            wasActive = nowActive;
            player?.Progression?.NotifyStatsChanged();
        }
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
        // OnMeleeHitDealt is raised after damage is applied, so killing blows can already have target.IsDead == true.
        // We still want those hits to count toward forging progress.
        if (cfg == null || target == null)
            return;

        if (IsForgedActive)
            return;

        forgedHits++;
        if (forgedHits < Mathf.Max(1, cfg.hitsToForge))
            return;

        forgedHits = 0;
        float duration = cfg.baseForgedDuration + cfg.forgedDurationPerStack * Mathf.Max(0, stacks - 1);
        forgedEndsAt = Time.time + Mathf.Max(0.2f, duration);
    }
}
