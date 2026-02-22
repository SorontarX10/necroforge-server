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

        [Header("Typography")]
        [SerializeField, Min(0f)] private float titleCharacterSpacing = 1.2f;
        [SerializeField, Min(0f)] private float descriptionCharacterSpacing = 0.9f;
        [SerializeField] private float textLiftPixels = 3f;
        [SerializeField] private float titleLiftExtraPixels = 4f;
        [SerializeField, Range(0.6f, 1f)] private float titleWidthScale = 0.88f;
        [SerializeField, Min(0f)] private float bottomInsetPixels = 4f;

        public static EnhancerCardUI Instance { get; private set; }

        private Coroutine hideRoutine;
        private bool typographyInitialized;
        private Vector2 titleBasePosition;
        private Vector2 descriptionBasePosition;
        private Vector2 titleBaseSize;
        private Vector2 descriptionBaseSize;

        private void Awake()
        {
            Instance = this;
            gameObject.SetActive(true);
            panel.SetActive(false); // panel off, controller ON
            EnsureTypography();
        }

        public void Show(EnhancerDefinition def)
        {
            if (hideRoutine != null)
                StopCoroutine(hideRoutine);

            EnsureTypography();
            title.text = def.enhancerId;
            description.text = def.description;
            icon.sprite = def.icon;
            background.color = def.emissionColor;

            panel.SetActive(true);
            hideRoutine = StartCoroutine(HideAfterDelay());
        }

        private void EnsureTypography()
        {
            if (!typographyInitialized)
            {
                if (title != null)
                {
                    titleBasePosition = title.rectTransform.anchoredPosition;
                    titleBaseSize = title.rectTransform.sizeDelta;
                }
                if (description != null)
                {
                    descriptionBasePosition = description.rectTransform.anchoredPosition;
                    descriptionBaseSize = description.rectTransform.sizeDelta;
                }

                typographyInitialized = true;
            }

            ApplyTypography(
                title,
                titleCharacterSpacing,
                titleBasePosition,
                titleBaseSize,
                textLiftPixels + titleLiftExtraPixels,
                titleWidthScale
            );
            ApplyTypography(
                description,
                descriptionCharacterSpacing,
                descriptionBasePosition,
                descriptionBaseSize,
                textLiftPixels,
                1f
            );
        }

        private void ApplyTypography(
            TMP_Text text,
            float spacing,
            Vector2 basePosition,
            Vector2 baseSize,
            float liftPixels,
            float widthScale
        )
        {
            if (text == null)
                return;

            text.characterSpacing = Mathf.Max(0f, spacing);
            RectTransform rect = text.rectTransform;
            rect.anchoredPosition = basePosition + Vector2.up * Mathf.Max(0f, liftPixels);

            if (baseSize.sqrMagnitude > 0.0001f)
                rect.sizeDelta = new Vector2(baseSize.x * Mathf.Clamp(widthScale, 0.6f, 1f), baseSize.y);

            Vector4 margin = text.margin;
            margin.w = Mathf.Max(margin.w, bottomInsetPixels);
            text.margin = margin;
        }

        private IEnumerator HideAfterDelay()
        {
            yield return new WaitForSecondsRealtime(showDuration);
            panel.SetActive(false);
        }
    }
}
