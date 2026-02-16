using System;
using System.Collections.Generic;
using UnityEngine;
using GrassSim.Stats;

namespace GrassSim.Enhancers
{
    public class WeaponEnhancerSystem : MonoBehaviour
    {
        [Header("Time Scaling")]
        [Tooltip("Enhancer strength at run start.")]
        [Range(0.1f, 2f)] public float earlyPowerMultiplier = 0.85f;
        [Tooltip("Enhancer strength at 10:00+.")]
        [Range(0.1f, 3f)] public float latePowerMultiplier = 1.25f;
        [Min(0f)] public float latePowerStartSeconds = 300f;
        [Min(1f)] public float latePowerFullSeconds = 600f;

        private readonly List<ActiveEnhancer> active = new();

        public event Action OnChanged;
        public IReadOnlyList<ActiveEnhancer> Active => active;

        private void Update()
        {
            bool changed = false;
            float dt = Time.deltaTime;

            for (int i = active.Count - 1; i >= 0; i--)
            {
                var enhancer = active[i];
                if (enhancer == null)
                {
                    active.RemoveAt(i);
                    changed = true;
                    continue;
                }

                enhancer.Tick(dt);
                if (enhancer.IsExpired)
                {
                    active.RemoveAt(i);
                    changed = true;
                }
            }

            if (changed)
                RaiseChanged();
        }

        public void AddEnhancer(EnhancerDefinition def)
        {
            if (def == null)
                return;

            var existing = active.Find(e => e.Definition == def);
            if (existing != null)
            {
                // Refresh extends duration instead of creating a duplicate entry.
                existing.RefreshDuration();
            }
            else
            {
                active.Add(new ActiveEnhancer(def));
            }

            RaiseChanged();
        }

        // (base + additive) * multiplicative
        public float GetEffectiveValue(StatType stat, float baseValue)
        {
            float additive = 0f;
            float multiplicative = 1f;
            float timePower = GetTimePowerMultiplier();

            foreach (var enhancer in active)
            {
                float strength = enhancer.GetStrength01();

                foreach (var effect in enhancer.Definition.statEffects)
                {
                    if (effect.stat != stat)
                        continue;

                    float value = effect.maxBonus * strength * timePower;

                    switch (effect.mathMode)
                    {
                        case EnhancerMathMode.Additive:
                            additive += value;
                            break;

                        case EnhancerMathMode.Multiplicative:
                            // If base is 0 for probability stats, multiplicative mode would do nothing.
                            if (Mathf.Approximately(baseValue, 0f) && UsesZeroBaseAdditiveFallback(stat))
                                additive += value;
                            else
                                multiplicative *= 1f + value;
                            break;

                        case EnhancerMathMode.AdditiveThenMultiplicative:
                            additive += value;
                            multiplicative *= 1f + value;
                            break;
                    }
                }
            }

            return (baseValue + additive) * multiplicative;
        }

        private float GetTimePowerMultiplier()
        {
            if (GameTimerController.Instance == null)
                return earlyPowerMultiplier;

            float now = Mathf.Max(0f, GameTimerController.Instance.elapsedTime);
            float start = Mathf.Max(0f, latePowerStartSeconds);
            float full = Mathf.Max(start + 0.01f, latePowerFullSeconds);
            float t = Mathf.Clamp01((now - start) / (full - start));
            return Mathf.Lerp(earlyPowerMultiplier, latePowerMultiplier, t);
        }

        private static bool UsesZeroBaseAdditiveFallback(StatType stat)
        {
            return stat == StatType.CritChance ||
                   stat == StatType.LifeSteal ||
                   stat == StatType.DodgeChance ||
                   stat == StatType.DamageReduction;
        }

        private void RaiseChanged()
        {
            OnChanged?.Invoke();
        }
    }
}
