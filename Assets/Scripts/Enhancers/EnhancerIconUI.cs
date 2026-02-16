using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GrassSim.Enhancers
{
    public class EnhancerIconUI : MonoBehaviour
    {
        [Header("Refs")]
        [SerializeField] private Image icon;
        [SerializeField] private Image timerFill;
        [SerializeField] private TMP_Text stackText;

        private ActiveEnhancer enhancer;
        private RelicDefinition relic;
        private int staticStacks = 1;
        private bool useTimer;

        public void Bind(ActiveEnhancer active)
        {
            enhancer = active;
            relic = null;
            staticStacks = 1;
            useTimer = true;

            if (icon != null)
                icon.sprite = active != null ? active.Definition.icon : null;

            if (timerFill != null)
                timerFill.gameObject.SetActive(true);

            RefreshStatic();
        }

        public void BindRelic(RelicDefinition def, int stacks)
        {
            relic = def;
            enhancer = null;
            staticStacks = Mathf.Max(1, stacks);
            useTimer = false;

            if (icon != null)
                icon.sprite = def != null ? def.icon : null;

            if (timerFill != null)
                timerFill.gameObject.SetActive(false);

            RefreshStatic();
        }

        private void Update()
        {
            if (useTimer)
            {
                if (enhancer == null)
                {
                    Destroy(gameObject);
                    return;
                }

                RefreshTimer();
                return;
            }

            if (relic == null)
                Destroy(gameObject);
        }

        private void RefreshStatic()
        {
            if (stackText == null)
                return;

            int stacks = useTimer && enhancer != null ? enhancer.Stacks : staticStacks;
            stackText.text = stacks > 1 ? $"x{stacks}" : "";
        }

        private void RefreshTimer()
        {
            if (timerFill == null || enhancer == null)
                return;

            timerFill.fillAmount = 1f - enhancer.TimeToNextStackDrop01;

            if (stackText != null)
            {
                int stacks = enhancer.StacksFromTime;
                stackText.text = stacks > 1 ? $"x{stacks}" : "";
            }
        }
    }
}
