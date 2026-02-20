using System.Collections.Generic;
using UnityEngine;
using GrassSim.Stats;

namespace GrassSim.Upgrades
{
    public class UpgradeWeightRuntime : MonoBehaviour
    {
        public static UpgradeWeightRuntime Instance { get; private set; }

        [Header("Legacy")]
        [Tooltip("Used when adaptive pick increase is disabled.")]
        [Range(0.05f, 1f)]
        public float pickIncreasePercent = 0.25f;

        [Header("Adaptive Pick Increase")]
        public bool useAdaptivePickIncrease = true;
        [Range(0.05f, 1f)] public float earlyPickIncreasePercent = 0.12f;
        [Range(0.05f, 1f)] public float latePickIncreasePercent = 0.35f;
        [Min(0f)] public float lateStartSeconds = 300f;
        [Min(1f)] public float lateFullSeconds = 600f;

        [Header("Safety")]
        [Min(1f)] public float maxWeightRelativeToBase = 8f;

        private readonly Dictionary<StatType, float> runtimeWeights = new();
        private readonly Dictionary<StatType, float> baseWeights = new();

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        public void InitializeFromLibrary(UpgradeLibrary library)
        {
            runtimeWeights.Clear();
            baseWeights.Clear();

            if (library == null || library.entries == null)
                return;

            foreach (var e in library.entries)
            {
                runtimeWeights[e.stat] = e.weight;
                baseWeights[e.stat] = Mathf.Max(0.0001f, e.weight);
            }
        }

        public float GetWeight(StatType stat, float fallback)
        {
            return runtimeWeights.TryGetValue(stat, out var w)
                ? w
                : fallback;
        }

        public void OnUpgradePicked(StatType stat)
        {
            if (!runtimeWeights.ContainsKey(stat))
                return;

            float increase = GetCurrentIncreasePercent();
            float baseWeight = baseWeights.TryGetValue(stat, out var bw)
                ? Mathf.Max(0.0001f, bw)
                : Mathf.Max(0.0001f, runtimeWeights[stat]);

            float cap = baseWeight * Mathf.Max(1f, maxWeightRelativeToBase);
            float next = runtimeWeights[stat] * (1f + increase);
            runtimeWeights[stat] = Mathf.Min(next, cap);

            Debug.Log($"[UpgradeWeightRuntime] {stat} weight -> {runtimeWeights[stat]:0.###}");
        }

        public void ResetWeights()
        {
            runtimeWeights.Clear();
            baseWeights.Clear();
        }

        private float GetCurrentIncreasePercent()
        {
            if (!useAdaptivePickIncrease)
                return pickIncreasePercent;

            float now = GameTimerController.Instance != null
                ? Mathf.Max(0f, GameTimerController.Instance.elapsedTime)
                : 0f;

            float start = Mathf.Max(0f, lateStartSeconds);
            float full = Mathf.Max(start + 0.01f, lateFullSeconds);
            float t = Mathf.Clamp01((now - start) / (full - start));
            return Mathf.Lerp(earlyPickIncreasePercent, latePickIncreasePercent, t);
        }
    }
}
