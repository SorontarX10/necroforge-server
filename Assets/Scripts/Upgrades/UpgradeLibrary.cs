using System;
using System.Collections.Generic;
using UnityEngine;
using GrassSim.Stats;

namespace GrassSim.Upgrades
{
    [CreateAssetMenu(menuName = "GrassSim/Upgrades/UpgradeLibrary", fileName = "UpgradeLibrary")]
    public class UpgradeLibrary : ScriptableObject
    {
        [Serializable]
        public class Entry
        {
            public StatType stat;

            [Header("Base Scaling")]
            [Tooltip("Base value for this stat (e.g. Damage=30, Speed=5)")]
            public float baseValue = 1f;

            [Tooltip("Random variance (+/- %) applied to baseValue")]
            [Range(0f, 0.5f)]
            public float variancePercent = 0.10f;

            [Header("Roll Weight")]
            [Min(0.0001f)]
            public float weight = 1f;
        }

        [Header("Upgrade Pool")]
        public List<Entry> entries = new();

        public List<UpgradeOption> RollOptions(int count, int seed)
        {
            if (entries == null || entries.Count == 0)
                return new List<UpgradeOption>();

            System.Random rng = new(seed);
            List<Entry> temp = new(entries);
            List<UpgradeOption> results = new(count);

            int difficulty = GetDifficulty();

            for (int i = 0; i < count && temp.Count > 0; i++)
            {
                int idx = WeightedPickIndex(temp, rng);
                Entry picked = temp[idx];

                float baseValue = RollBaseValue(picked, rng);
                UpgradeRarity rarity = RollRarity(rng, difficulty);
                float finalValue = baseValue * RarityMultiplier(rarity);

                results.Add(new UpgradeOption(
                    picked.stat,
                    finalValue,
                    rarity,
                    picked.stat.ToString(),
                    BuildDescription(picked.stat, finalValue, rarity)
                ));

                temp.RemoveAt(idx);
            }

            return results;
        }

        private float RollBaseValue(Entry e, System.Random rng)
        {
            if (e.variancePercent <= 0f)
                return e.baseValue;

            float variance = e.baseValue * e.variancePercent;
            float min = e.baseValue - variance;
            float max = e.baseValue + variance;
            return Mathf.Lerp(min, max, (float)rng.NextDouble());
        }

        private UpgradeRarity RollRarity(System.Random rng, int difficulty)
        {
            float p = GetRunProgress();

            float common = EvaluatePiecewise(p, 0.75f, 0.52f, 0.32f, 0.18f);
            float uncommon = EvaluatePiecewise(p, 0.21f, 0.28f, 0.30f, 0.27f);
            float rare = EvaluatePiecewise(p, 0.04f, 0.15f, 0.24f, 0.29f);
            float legendary = EvaluatePiecewise(p, 0.00f, 0.045f, 0.11f, 0.18f);
            float mythic = EvaluatePiecewise(p, 0.00f, 0.005f, 0.03f, 0.08f);

            // Small push towards higher rarity when global difficulty grows.
            float diffBias = Mathf.Clamp01((difficulty - 1) / 12f) * 0.06f;
            common = Mathf.Max(0f, common - diffBias);
            rare += diffBias * 0.45f;
            legendary += diffBias * 0.35f;
            mythic += diffBias * 0.20f;

            float sum = common + uncommon + rare + legendary + mythic;
            if (sum <= 0f)
                return UpgradeRarity.Common;

            float roll = (float)rng.NextDouble() * sum;
            float acc = common;
            if (roll < acc) return UpgradeRarity.Common;
            acc += uncommon;
            if (roll < acc) return UpgradeRarity.Uncommon;
            acc += rare;
            if (roll < acc) return UpgradeRarity.Rare;
            acc += legendary;
            if (roll < acc) return UpgradeRarity.Legendary;
            return UpgradeRarity.Mythic;
        }

        private float RarityMultiplier(UpgradeRarity r)
        {
            return r switch
            {
                UpgradeRarity.Common => 1f,
                UpgradeRarity.Uncommon => 1.30f,
                UpgradeRarity.Rare => 1.75f,
                UpgradeRarity.Legendary => 2.45f,
                UpgradeRarity.Mythic => 3.50f,
                _ => 1f
            };
        }

        private int WeightedPickIndex(List<Entry> list, System.Random rng)
        {
            double total = 0d;
            foreach (var e in list)
                total += Math.Max(0.0001f, GetRuntimeWeight(e));

            double roll = rng.NextDouble() * total;
            double acc = 0d;
            for (int i = 0; i < list.Count; i++)
            {
                acc += Math.Max(0.0001f, GetRuntimeWeight(list[i]));
                if (roll <= acc)
                    return i;
            }

            return list.Count - 1;
        }

        private float GetRuntimeWeight(Entry e)
        {
            float runtimeWeight;
            if (UpgradeWeightRuntime.Instance != null)
                runtimeWeight = UpgradeWeightRuntime.Instance.GetWeight(e.stat, e.weight);
            else
                runtimeWeight = e.weight;

            return ApplyBalanceWeightBias(e.stat, runtimeWeight);
        }

        // Reduces overrepresented offensive snowball picks and boosts defensive/utility options.
        private static float ApplyBalanceWeightBias(StatType stat, float weight)
        {
            float multiplier = stat switch
            {
                StatType.Damage => 0.72f,
                StatType.CritChance => 0.66f,
                StatType.LifeSteal => 0.70f,

                StatType.MaxHealth => 1.26f,
                StatType.HealthRegen => 1.22f,
                StatType.DamageReduction => 1.24f,
                StatType.DodgeChance => 1.2f,
                StatType.MaxStamina => 1.12f,
                StatType.StaminaRegen => 1.15f,
                StatType.Speed => 1.12f,
                _ => 1f
            };

            return Mathf.Max(0.0001f, weight * multiplier);
        }

        private static int GetDifficulty()
        {
            if (WorldStats.Instance == null)
                return 1;

            return Mathf.Max(1, WorldStats.Instance.difficulty);
        }

        private static string BuildDescription(StatType stat, float value, UpgradeRarity rarity)
        {
            return $"+{value:0.##} {stat} ({rarity})";
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

        // 4-point piecewise linear interpolation over [0, 0.333, 0.667, 1].
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
}
