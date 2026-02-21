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

    private UpgradeOption boundOption;
    private Action<UpgradeOption> onPickCallback;
    public UpgradeOption BoundOption => boundOption;

    public void Bind(
        UpgradeOption option,
        UpgradeStatIconLibrary iconLibrary,
        Color rarityColor,
        Action<UpgradeOption> onPick
    )
    {
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
