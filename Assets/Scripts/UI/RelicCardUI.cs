using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using System.Text;
using System.Collections.Generic;

public class RelicCardUI : MonoBehaviour
{
    [Header("UI")]
    public Image icon;
    public Image rarityBorder;
    public TMP_Text title;
    public TMP_Text description;
    [SerializeField] private TMP_Text valueTextToHide;

    [Header("Card Description Fit")]
    [SerializeField, Min(80)] private int maxDescriptionChars = 175;
    [SerializeField, Min(40)] private int smartCutMinChars = 120;
    [SerializeField] private Vector2 titleSize = new Vector2(186f, 36f);
    [SerializeField] private Vector2 titlePosition = new Vector2(0f, -120f);
    [SerializeField] private Vector2 descriptionSize = new Vector2(182f, 82f);
    [SerializeField] private Vector2 descriptionPosition = new Vector2(0f, -62f);
    [SerializeField, Min(8f)] private float descriptionMinFont = 10f;
    [SerializeField, Min(8f)] private float descriptionMaxFont = 15f;
    [SerializeField, Min(8f)] private float titleMinFont = 14f;
    [SerializeField, Min(8f)] private float titleMaxFont = 24f;
    [SerializeField] private string relicIconsResourcesPath = "Textures/UI/UpgradeMenu/Relics";

    private RelicDefinition relic;
    private Action<RelicDefinition> onPick;
    public RelicDefinition BoundRelic => relic;
    private static readonly Dictionary<string, Sprite> FallbackIconCache = new(StringComparer.OrdinalIgnoreCase);

    private void Awake()
    {
        EnsureLayoutDefaults();

        if (valueTextToHide == null)
            valueTextToHide = transform.Find("ValueText")?.GetComponent<TMP_Text>();

        ConfigureTextStyles();
    }

    public void Bind(RelicDefinition def, Action<RelicDefinition> callback)
    {
        relic = def;
        onPick = callback;

        if (def == null)
        {
            if (icon != null)
            {
                icon.sprite = null;
                icon.enabled = false;
            }

            if (title != null)
                title.text = string.Empty;

            if (description != null)
                description.text = string.Empty;

            if (valueTextToHide != null)
                valueTextToHide.gameObject.SetActive(false);

            if (rarityBorder != null)
                rarityBorder.color = Color.gray;

            Button button = GetComponent<Button>();
            if (button != null)
                button.interactable = false;

            return;
        }

        gameObject.SetActive(true);

        if (icon != null)
        {
            icon.sprite = ResolveIcon(def);
            icon.enabled = icon.sprite != null;
        }

        if (title != null)
        {
            string effectName = def.effect != null ? def.effect.displayName : string.Empty;
            string fallbackName = !string.IsNullOrWhiteSpace(effectName) ? effectName : def.name;
            title.text = string.IsNullOrWhiteSpace(def.displayName) ? fallbackName : def.displayName;
            ApplyTitleLayout();
        }

        if (description != null)
        {
            description.text = BuildCardDescription(def);
            ApplyDescriptionLayout();
        }

        if (valueTextToHide != null)
            valueTextToHide.gameObject.SetActive(false);

        if (rarityBorder != null)
            rarityBorder.color = RelicRarityColors.Get(def.rarity);

        Button activeButton = GetComponent<Button>();
        if (activeButton != null)
            activeButton.interactable = true;
    }

    public void OnClick()
    {
        onPick?.Invoke(relic);
    }

    private void ConfigureTextStyles()
    {
        if (title != null)
        {
            title.enableAutoSizing = true;
            title.fontSizeMin = titleMinFont;
            title.fontSizeMax = titleMaxFont;
            title.textWrappingMode = TextWrappingModes.Normal;
            title.overflowMode = TextOverflowModes.Ellipsis;
            title.alignment = TextAlignmentOptions.Center;
        }

        if (description != null)
        {
            description.enableAutoSizing = true;
            description.fontSizeMin = descriptionMinFont;
            description.fontSizeMax = descriptionMaxFont;
            description.textWrappingMode = TextWrappingModes.Normal;
            description.overflowMode = TextOverflowModes.Ellipsis;
            description.alignment = TextAlignmentOptions.Top;
        }
    }

    private void EnsureLayoutDefaults()
    {
        if (maxDescriptionChars < 80)
            maxDescriptionChars = 175;

        if (smartCutMinChars < 40 || smartCutMinChars >= maxDescriptionChars)
            smartCutMinChars = Mathf.Min(120, maxDescriptionChars - 10);

        if (titleSize.sqrMagnitude < 1f)
            titleSize = new Vector2(186f, 36f);
        if (descriptionSize.sqrMagnitude < 1f)
            descriptionSize = new Vector2(182f, 82f);

        if (Mathf.Abs(titlePosition.x) < 0.01f && Mathf.Abs(titlePosition.y) < 0.01f)
            titlePosition = new Vector2(0f, -120f);
        if (Mathf.Abs(descriptionPosition.x) < 0.01f && Mathf.Abs(descriptionPosition.y) < 0.01f)
            descriptionPosition = new Vector2(0f, -62f);

        if (descriptionMinFont < 8f)
            descriptionMinFont = 10f;
        if (descriptionMaxFont < descriptionMinFont)
            descriptionMaxFont = descriptionMinFont;

        if (titleMinFont < 8f)
            titleMinFont = 14f;
        if (titleMaxFont < titleMinFont)
            titleMaxFont = titleMinFont;
    }

    private void ApplyDescriptionLayout()
    {
        RectTransform rect = description.rectTransform;
        rect.anchoredPosition = descriptionPosition;
        rect.sizeDelta = descriptionSize;
    }

    private void ApplyTitleLayout()
    {
        RectTransform rect = title.rectTransform;
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = titlePosition;
        rect.sizeDelta = titleSize;
    }

    private string BuildCardDescription(RelicDefinition def)
    {
        string raw = def != null ? def.description : string.Empty;
        if (string.IsNullOrWhiteSpace(raw) && def != null && def.effect != null)
            raw = def.effect.description;

        string normalized = NormalizeWhitespace(raw);
        if (string.IsNullOrWhiteSpace(normalized))
            return "No description available.";

        return ShortenForCard(normalized);
    }

    private string ShortenForCard(string value)
    {
        if (value.Length <= maxDescriptionChars)
            return value;

        int sentenceCut = value.LastIndexOf('.', maxDescriptionChars);
        if (sentenceCut >= smartCutMinChars)
            return value.Substring(0, sentenceCut + 1).Trim();

        int wordCut = value.LastIndexOf(' ', maxDescriptionChars);
        if (wordCut < smartCutMinChars)
            wordCut = maxDescriptionChars;

        string cut = value.Substring(0, wordCut).TrimEnd(' ', ',', ';', '.');
        return cut + "...";
    }

    private static string NormalizeWhitespace(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var sb = new StringBuilder(value.Length);
        bool previousWasSpace = false;
        foreach (char c in value)
        {
            char current = char.IsWhiteSpace(c) ? ' ' : c;
            if (current == ' ')
            {
                if (previousWasSpace)
                    continue;
                previousWasSpace = true;
            }
            else
            {
                previousWasSpace = false;
            }

            sb.Append(current);
        }

        return sb.ToString().Trim();
    }

    private Sprite ResolveIcon(RelicDefinition def)
    {
        if (def == null)
            return null;

        if (def.icon != null)
            return def.icon;

        if (string.IsNullOrWhiteSpace(relicIconsResourcesPath))
            return null;

        string key = string.IsNullOrWhiteSpace(def.displayName) ? def.name : def.displayName;
        if (string.IsNullOrWhiteSpace(key))
            return null;

        if (FallbackIconCache.TryGetValue(key, out Sprite cached))
            return cached;

        string path = $"{relicIconsResourcesPath}/{key}";
        Sprite loaded = Resources.Load<Sprite>(path);
        FallbackIconCache[key] = loaded;
        return loaded;
    }
}
