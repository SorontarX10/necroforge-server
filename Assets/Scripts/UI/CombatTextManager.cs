using UnityEngine;

namespace GrassSim.UI
{
    public class CombatTextManager : MonoBehaviour
    {
        public static CombatTextManager Instance;

        public FloatingCombatText prefab;

        [Header("Colors")]
        public Color damageColor = new Color(1f, 0.2f, 0.2f);   // czerwony
        public Color critColor   = new Color(1f, 0.85f, 0.2f);  // żółty
        public Color healColor   = new Color(0.2f, 1f, 0.2f);   // zielony

        private void Awake()
        {
            Instance = this;
        }

        public void Spawn(
            Vector3 worldPos,
            float value,
            CombatTextType type
        )
        {
            if (prefab == null) return;

            Vector3 offset = Vector3.up * Random.Range(1.6f, 2.2f);
            var text = Instantiate(prefab, worldPos + offset, Quaternion.identity);

            if (value == 0) return;

            switch (type)
            {
                case CombatTextType.Damage:
                    text.Init((Mathf.Round(value) * 0.01f).ToString("0.00"), damageColor, 1f);
                    break;

                case CombatTextType.Crit:
                    text.Init(
                        $"<b>{(Mathf.Round(value) * 0.01f).ToString(("0.00"))}</b>",
                        critColor,
                        1.3f
                    );
                    break;

                case CombatTextType.Heal:
                    text.Init(
                        $"+{(Mathf.Round(value) * 0.1f).ToString(("0.00"))}",
                        healColor,
                        1.1f
                    );
                    break;
            }
        }
    }
}
