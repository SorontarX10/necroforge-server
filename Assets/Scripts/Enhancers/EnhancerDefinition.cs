using System.Collections.Generic;
using UnityEngine;
using GrassSim.Stats;

namespace GrassSim.Enhancers
{
    [CreateAssetMenu(
        menuName = "GrassSim/Enhancers/Enhancer Definition",
        fileName = "Enhancer_"
    )]
    public class EnhancerDefinition : ScriptableObject
    {
        [Header("Identity")]
        public string enhancerId;
        [TextArea(2, 4)]
        public string description;
        public Sprite icon;

        [Header("Stacking")]
        public int maxStacks = 5;
        public float duration = 120f;

        [Header("Diminishing Returns")]
        [Tooltip("k parameter in 1 - exp(-k * stacks)")]
        public float diminishingK = 0.6f;

        [Header("Stat Effects")]
        public List<EnhancerStatEffect> statEffects = new();

        [Header("Visuals")]
        public Color emissionColor = Color.green;
    }

    public enum EnhancerMathMode
    {
        Additive,                  // +X (crit, lifesteal)
        Multiplicative,             // *X (damage, speed)
        AdditiveThenMultiplicative  // (base + add) * mul
    }

    [System.Serializable]
    public struct EnhancerStatEffect
    {
        public StatType stat;

        [Tooltip("Max additive bonus, np 0.2 = +20%")]
        public float maxBonus;

        public EnhancerMathMode mathMode;
    }
}
