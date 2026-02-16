using UnityEngine;

[CreateAssetMenu(
    menuName = "GrassSim/Relics/Effects/Mythic/Crown Of The Last Crusade",
    fileName = "Relic_CrownOfTheLastCrusade"
)]
public class CrownOfTheLastCrusade : RelicEffect,
    ISpeedModifier,
    IStaminaRegenModifier,
    ICritChanceModifier,
    IDamageReductionModifier,
    IDamageModifier
{
    [Header("Tier Passives")]
    public float commonSpeedBonus = 0.08f;
    public float uncommonStaminaRegenBonus = 10f;
    public float rareCritChanceBonus = 0.12f;
    public float legendaryDamageReductionBonus = 0.12f;

    [Header("Crusade")]
    public float crusadeCooldown = 30f;
    public float crusadeDuration = 6f;
    public float crusadeDamageBonus = 0.2f;

    public override void OnAcquire(PlayerRelicController player, int stacks)
    {
        Attach(player)?.Configure(this, stacks);
    }

    public override void OnStack(PlayerRelicController player, int stacks)
    {
        Attach(player)?.Configure(this, stacks);
    }

    public float GetSpeedBonus(PlayerRelicController player, int stacks)
    {
        var rt = player != null ? player.GetComponent<CrownOfTheLastCrusadeRuntime>() : null;
        if (rt == null || !rt.HasCommon)
            return 0f;

        return commonSpeedBonus;
    }

    public float GetStaminaRegenBonus(PlayerRelicController player, int stacks)
    {
        var rt = player != null ? player.GetComponent<CrownOfTheLastCrusadeRuntime>() : null;
        if (rt == null || !rt.HasUncommon)
            return 0f;

        return uncommonStaminaRegenBonus;
    }

    public float GetCritChanceBonus(PlayerRelicController player, int stacks)
    {
        var rt = player != null ? player.GetComponent<CrownOfTheLastCrusadeRuntime>() : null;
        if (rt == null || !rt.HasRare)
            return 0f;

        return rareCritChanceBonus;
    }

    public float GetDamageReductionBonus(PlayerRelicController player, int stacks)
    {
        var rt = player != null ? player.GetComponent<CrownOfTheLastCrusadeRuntime>() : null;
        if (rt == null || !rt.HasLegendary)
            return 0f;

        return legendaryDamageReductionBonus;
    }

    public float GetDamageMultiplier(PlayerRelicController player, int stacks)
    {
        var rt = player != null ? player.GetComponent<CrownOfTheLastCrusadeRuntime>() : null;
        if (rt == null || !rt.IsCrusadeActive)
            return 1f;

        return 1f + crusadeDamageBonus;
    }

    private CrownOfTheLastCrusadeRuntime Attach(PlayerRelicController player)
    {
        if (player == null)
            return null;

        var rt = player.GetComponent<CrownOfTheLastCrusadeRuntime>();
        if (rt == null)
            rt = player.gameObject.AddComponent<CrownOfTheLastCrusadeRuntime>();

        return rt;
    }
}

public class CrownOfTheLastCrusadeRuntime : MonoBehaviour, IRelicBatchedUpdate, IRelicBatchedCadence
{
    private PlayerRelicController player;
    private CrownOfTheLastCrusade cfg;
    private int stacks;
    private bool subscribed;
    private bool wasCrusadeActive;

    private bool hasCommon;
    private bool hasUncommon;
    private bool hasRare;
    private bool hasLegendary;

    private float crusadeEndsAt;
    private float nextCrusadeAt;

    public bool HasCommon => hasCommon;
    public bool HasUncommon => hasUncommon;
    public bool HasRare => hasRare;
    public bool HasLegendary => hasLegendary;
    public bool IsCrusadeActive => Time.time < crusadeEndsAt;

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

    public void Configure(CrownOfTheLastCrusade config, int stackCount)
    {
        cfg = config;
        stacks = Mathf.Max(1, stackCount);
        RecomputeTiers();
        if (nextCrusadeAt <= 0f)
            nextCrusadeAt = Time.time + 2f;

        TrySubscribe();
    }

    public bool IsBatchedUpdateActive => isActiveAndEnabled && cfg != null;

    public float BatchedUpdateInterval => 0.04f;

    public RelicTickArchetype BatchedTickArchetype => RelicTickArchetype.PlayerState;

    public void TickFromRelicBatch(float now, float deltaTime)
    {
        if (!IsCrusadeActive && HasAllTiers() && now >= nextCrusadeAt)
            ActivateCrusade();

        if (IsCrusadeActive)
        {
            // Removes active root-like control effects while Crusade is running.
            var bossRoot = GetComponent<BossRootDebuff>();
            if (bossRoot != null && bossRoot.IsRooted)
                Destroy(bossRoot);

            var relicRoot = GetComponent<RelicRootDebuff>();
            if (relicRoot != null)
                Destroy(relicRoot);
        }

        bool nowActive = IsCrusadeActive;
        if (nowActive != wasCrusadeActive)
        {
            wasCrusadeActive = nowActive;
            player?.Progression?.NotifyStatsChanged();
        }
    }

    private void TrySubscribe()
    {
        if (subscribed || player == null)
            return;

        player.OnChanged += OnRelicsChanged;
        subscribed = true;
    }

    private void TryUnsubscribe()
    {
        if (!subscribed || player == null)
            return;

        player.OnChanged -= OnRelicsChanged;
        subscribed = false;
    }

    private void OnRelicsChanged()
    {
        bool prevCommon = hasCommon;
        bool prevUncommon = hasUncommon;
        bool prevRare = hasRare;
        bool prevLegendary = hasLegendary;

        RecomputeTiers();

        if (prevCommon != hasCommon || prevUncommon != hasUncommon || prevRare != hasRare || prevLegendary != hasLegendary)
            player?.Progression?.NotifyStatsChanged();
    }

    private void RecomputeTiers()
    {
        hasCommon = false;
        hasUncommon = false;
        hasRare = false;
        hasLegendary = false;

        if (player == null || player.Relics == null)
            return;

        foreach (var kv in player.Relics)
        {
            var relic = kv.Value;
            if (relic == null)
                continue;

            switch (relic.rarity)
            {
                case RelicRarity.Common:
                    hasCommon = true;
                    break;
                case RelicRarity.Uncommon:
                    hasUncommon = true;
                    break;
                case RelicRarity.Rare:
                    hasRare = true;
                    break;
                case RelicRarity.Legendary:
                    hasLegendary = true;
                    break;
            }
        }
    }

    private bool HasAllTiers()
    {
        return hasCommon && hasUncommon && hasRare && hasLegendary;
    }

    private void ActivateCrusade()
    {
        crusadeEndsAt = Time.time + Mathf.Max(0.2f, cfg.crusadeDuration);
        nextCrusadeAt = Time.time + Mathf.Max(1f, cfg.crusadeCooldown);
    }
}
