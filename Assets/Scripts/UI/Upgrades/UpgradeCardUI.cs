using UnityEngine;
using UnityEngine.UI;
using TMPro;
using GrassSim.Upgrades;
using GrassSim.Stats;
using System;

public class UpgradeCardUI : MonoBehaviour
{
    [Header("UI")]
    public Image icon;
    public Image rarityBorder;
    public TMP_Text nameText;
    public TMP_Text descText;
    public TMP_Text valueText;
    public Button button;

    [Header("Typography")]
    [SerializeField, Min(0f)] private float nameCharacterSpacing = 1.2f;
    [SerializeField, Min(0f)] private float descCharacterSpacing = 0.9f;
    [SerializeField, Min(0f)] private float valueCharacterSpacing = 1.4f;
    [SerializeField] private float textLiftPixels = 4f;
    [SerializeField] private float nameLiftExtraPixels = 4f;
    [SerializeField, Range(0.6f, 1f)] private float nameWidthScale = 0.88f;
    [SerializeField, Min(0f)] private float bottomInsetPixels = 4f;

    private UpgradeOption boundOption;
    private Action<UpgradeOption> onPickCallback;
    private bool typographyInitialized;
    private Vector2 nameBasePosition;
    private Vector2 descBasePosition;
    private Vector2 valueBasePosition;
    private Vector2 nameBaseSize;
    private Vector2 descBaseSize;
    private Vector2 valueBaseSize;
    public UpgradeOption BoundOption => boundOption;

    private void Awake()
    {
        EnsureTypography();
    }

    public void Bind(
        UpgradeOption option,
        UpgradeStatIconLibrary iconLibrary,
        Color rarityColor,
        Action<UpgradeOption> onPick
    )
    {
        EnsureTypography();
        boundOption = option;
        onPickCallback = onPick;

        if (option == null)
        {
            if (icon != null)
            {
                icon.sprite = null;
                icon.enabled = false;
            }

            if (rarityBorder != null)
                rarityBorder.color = Color.gray;

            if (nameText != null)
                nameText.text = string.Empty;

            if (descText != null)
                descText.text = string.Empty;

            if (valueText != null)
                valueText.text = string.Empty;

            if (button != null)
            {
                button.onClick.RemoveAllListeners();
                button.interactable = false;
            }

            return;
        }

        // === ICON ===
        if (icon != null)
        {
            icon.sprite = iconLibrary != null ? iconLibrary.GetIcon(option.stat) : null;
            icon.enabled = icon.sprite != null;
        }

        // === RARITY ===
        if (rarityBorder != null)
            rarityBorder.color = rarityColor;

        // === TEXTS ===
        if (nameText != null)
            nameText.text = string.IsNullOrWhiteSpace(option.displayName) ? option.stat.ToString() : option.displayName;

        if (descText != null)
        {
            string readable = GetDescription(option.stat);
            descText.text = !string.IsNullOrWhiteSpace(readable) ? readable : option.description;
        }

        if (valueText != null)
            valueText.text = GetValueText(option);

        // === BUTTON ===
        if (button != null)
        {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(OnClick);
            button.interactable = true;
        }
    }

    private void OnClick()
    {
        onPickCallback?.Invoke(boundOption);
    }

    private void EnsureTypography()
    {
        if (!typographyInitialized)
        {
            if (nameText != null)
            {
                nameBasePosition = nameText.rectTransform.anchoredPosition;
                nameBaseSize = nameText.rectTransform.sizeDelta;
            }
            if (descText != null)
            {
                descBasePosition = descText.rectTransform.anchoredPosition;
                descBaseSize = descText.rectTransform.sizeDelta;
            }
            if (valueText != null)
            {
                valueBasePosition = valueText.rectTransform.anchoredPosition;
                valueBaseSize = valueText.rectTransform.sizeDelta;
            }

            typographyInitialized = true;
        }

        ApplyTypography(
            nameText,
            nameCharacterSpacing,
            nameBasePosition,
            nameBaseSize,
            textLiftPixels + nameLiftExtraPixels,
            nameWidthScale
        );
        ApplyTypography(
            descText,
            descCharacterSpacing,
            descBasePosition,
            descBaseSize,
            textLiftPixels,
            1f
        );
        ApplyTypography(
            valueText,
            valueCharacterSpacing,
            valueBasePosition,
            valueBaseSize,
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

    // ================= HELPERS =================

    private string GetValueText(UpgradeOption option)
    {
        // procentowe staty
        if (IsPercentStat(option.stat))
            return $"+{option.value * 100f:0.#}%";

        // regeneracje – 2 miejsca po przecinku
        if (option.stat == StatType.HealthRegen || option.stat == StatType.StaminaRegen)
            return $"+{option.value:0.00}/s";

        // reszta – flat
        return $"+{option.value:0.00}";
    }


    private bool IsPercentStat(StatType stat)
    {
        return stat == StatType.CritChance
            || stat == StatType.CritMultiplier
            || stat == StatType.DodgeChance
            || stat == StatType.DamageReduction
            || stat == StatType.LifeSteal;
    }

    private string GetDescription(StatType stat)
    {
        return stat switch
        {
            StatType.MaxHealth => "Increases maximum health",
            StatType.HealthRegen => "Regenerates health over time",
            StatType.MaxStamina => "Increases maximum stamina",
            StatType.StaminaRegen => "Regenerates stamina over time",
            StatType.Damage => "Increases damage dealt",
            StatType.CritChance => "Increases critical hit chance",
            StatType.CritMultiplier => "Increases critical damage",
            StatType.LifeSteal => "Heals on dealing damage",
            StatType.DamageReduction => "Reduces incoming damage",
            StatType.DodgeChance => "Chance to avoid damage",
            StatType.SwingSpeed => "Increases sword attack velocity",
            StatType.Speed => "Increases velocity of the player",
            _ => ""
        };
    }
}
