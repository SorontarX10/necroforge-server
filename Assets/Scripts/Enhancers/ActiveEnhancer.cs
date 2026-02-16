using UnityEngine;

namespace GrassSim.Enhancers
{
    public class ActiveEnhancer
    {
        public EnhancerDefinition Definition { get; }

        /// <summary>Łączny pozostały czas działania (sekundy).</summary>
        public float RemainingDuration { get; private set; }

        /// <summary>Całkowity czas jaki enhancer dostał od początku.</summary>
        public float TotalDuration { get; private set; }

        /// <summary>
        /// Aktualna liczba stacków wynikająca z czasu.
        /// 1 stack = Definition.duration sekund.
        /// </summary>
        public int Stacks
        {
            get
            {
                float d = Mathf.Max(0.0001f, Definition.duration);
                int stacks = Mathf.CeilToInt(RemainingDuration / d);
                return Mathf.Clamp(stacks, 1, Definition.maxStacks);
            }
        }

        /// <summary>
        /// ALIAS dla UI (kompatybilność wsteczna).
        /// </summary>
        public int StacksFromTime => Stacks;

        public ActiveEnhancer(EnhancerDefinition def)
        {
            Definition = def;
            RemainingDuration = def.duration;
            TotalDuration = def.duration;
        }

        /// <summary>
        /// Refresh = DODAJ duration (nie resetuj).
        /// </summary>
        public void RefreshDuration()
        {
            RemainingDuration += Definition.duration;
            TotalDuration += Definition.duration;
        }

        public void Tick(float dt)
        {
            RemainingDuration -= dt;
        }

        public bool IsExpired => RemainingDuration <= 0f;

        // =========================
        // 🔢 DIMINISHING RETURNS
        // =========================

        public float GetStrength01()
        {
            return 1f - Mathf.Exp(-Definition.diminishingK * Stacks);
        }

        // =========================
        // 🕒 UI TIMER
        // =========================

        /// <summary>
        /// 0..1 – ile zostało do spadku NAJBLIŻSZEGO stacka.
        /// Idealne do Image.fillAmount.
        /// </summary>
        public float TimeToNextStackDrop01
        {
            get
            {
                float d = Mathf.Max(0.0001f, Definition.duration);

                if (RemainingDuration <= 0f)
                    return 0f;

                float remainder = RemainingDuration % d;

                if (remainder <= 0.0001f)
                    remainder = d;

                return Mathf.Clamp01(remainder / d);
            }
        }
    }
}
