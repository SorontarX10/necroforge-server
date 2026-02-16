using System.Collections;
using UnityEngine;
using TMPro;
using UnityEngine.UI;

namespace GrassSim.Enhancers
{
    public class EnhancerCardUI : MonoBehaviour
    {
        [Header("Refs")]
        [SerializeField] private GameObject panel;
        [SerializeField] private Image icon;
        [SerializeField] private TMP_Text title;
        [SerializeField] private TMP_Text description;
        [SerializeField] private Image background;

        [Header("Timing")]
        [SerializeField] private float showDuration = 5f;

        public static EnhancerCardUI Instance { get; private set; }

        private Coroutine hideRoutine;

        private void Awake()
        {
            Instance = this;
            gameObject.SetActive(true);
            panel.SetActive(false); // panel off, controller ON
        }

        public void Show(EnhancerDefinition def)
        {
            if (hideRoutine != null)
                StopCoroutine(hideRoutine);

            title.text = def.enhancerId;
            description.text = def.description;
            icon.sprite = def.icon;
            background.color = def.emissionColor;

            Debug.Log("Hahahaha");

            panel.SetActive(true);
            hideRoutine = StartCoroutine(HideAfterDelay());
        }

        private IEnumerator HideAfterDelay()
        {
            yield return new WaitForSecondsRealtime(showDuration);
            panel.SetActive(false);
        }
    }
}
