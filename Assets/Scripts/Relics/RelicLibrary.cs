using UnityEngine;
using System.Collections.Generic;
using GrassSim.Stats;

[CreateAssetMenu(menuName = "GrassSim/Relics/RelicLibrary")]
public class RelicLibrary : ScriptableObject
{
    [Header("Pool")]
    public List<RelicDefinition> relics;

    [Header("Roll")]
    [Tooltip("When enabled, rarity chances evolve with run time instead of pure uniform random.")]
    public bool useRarityWeightedRoll = true;

    public List<RelicDefinition> Roll(int count, PlayerRelicController player = null)
    {
        List<RelicDefinition> pool = BuildEligiblePool(player);
        List<RelicDefinition> result = new();

        for (int i = 0; i < count && pool.Count > 0; i++)
        {
            int idx = useRarityWeightedRoll
                ? WeightedPickIndex(pool)
                : Random.Range(0, pool.Count);

            result.Add(pool[idx]);
            pool.RemoveAt(idx);
        }

        return result;
    }

    private List<RelicDefinition> BuildEligiblePool(PlayerRelicController player)
    {
        List<RelicDefinition> pool = new();
        if (relics == null || relics.Count == 0)
            return pool;

        for (int i = 0; i < relics.Count; i++)
        {
            RelicDefinition relic = relics[i];
            if (relic == null)
                continue;

            if (player != null && !player.CanAcceptRelic(relic))
                continue;

            pool.Add(relic);
        }

        return pool;
    }

    private int WeightedPickIndex(List<RelicDefinition> pool)
    {
        float total = 0f;
        for (int i = 0; i < pool.Count; i++)
            total += GetRelicWeight(pool[i]);

        if (total <= 0f)
            return Random.Range(0, pool.Count);

        float roll = Random.value * total;
        float acc = 0f;
        for (int i = 0; i < pool.Count; i++)
        {
            acc += GetRelicWeight(pool[i]);
            if (roll <= acc)
                return i;
        }

        return pool.Count - 1;
    }

    private float GetRelicWeight(RelicDefinition relic)
    {
        if (relic == null)
            return 0.0001f;

        float progress = GetRunProgress();
        float common = EvaluatePiecewise(progress, 0.70f, 0.50f, 0.30f, 0.16f);
        float uncommon = EvaluatePiecewise(progress, 0.24f, 0.30f, 0.32f, 0.27f);
        float rare = EvaluatePiecewise(progress, 0.06f, 0.16f, 0.24f, 0.31f);
        float legendary = EvaluatePiecewise(progress, 0.00f, 0.04f, 0.11f, 0.18f);
        float mythic = EvaluatePiecewise(progress, 0.00f, 0.00f, 0.03f, 0.08f);

        // Small late-game push towards high rarity based on current difficulty.
        int difficulty = (WorldStats.Instance != null) ? Mathf.Max(1, WorldStats.Instance.difficulty) : 1;
        float diffBias = Mathf.Clamp01((difficulty - 1) / 12f) * 0.05f;
        common = Mathf.Max(0f, common - diffBias);
        rare += diffBias * 0.40f;
        legendary += diffBias * 0.35f;
        mythic += diffBias * 0.25f;

        float rarityWeight = relic.rarity switch
        {
            RelicRarity.Common => common,
            RelicRarity.Uncommon => uncommon,
            RelicRarity.Rare => rare,
            RelicRarity.Legendary => legendary,
            RelicRarity.Mythic => mythic,
            _ => 0.01f
        };

        return Mathf.Max(0.0001f, rarityWeight);
    }

    private static float GetRunProgress()
    {
        if (GameTimerController.Instance == null)
            return 0f;

        float t = GameTimerController.Instance.elapsedTime;
        float end = GameTimerController.Instance.endGameTime;
        if (end <= 0f)
            return 0f;

        return Mathf.Clamp01(t / end);
    }

    private static float EvaluatePiecewise(float p, float a, float b, float c, float d)
    {
        p = Mathf.Clamp01(p);

        if (p <= 0.3333333f)
            return Mathf.Lerp(a, b, p / 0.3333333f);

        if (p <= 0.6666667f)
            return Mathf.Lerp(b, c, (p - 0.3333333f) / 0.3333334f);

        return Mathf.Lerp(c, d, (p - 0.6666667f) / 0.3333333f);
    }
}
