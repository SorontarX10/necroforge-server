using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using GrassSim.Core;
using GrassSim.UI;
using GrassSim.Upgrades;

public class UpgradeSelectionUI : MonoBehaviour
{
    [Header("Refs")]
    public Canvas upgradeCanvas;
    public GameObject root;
    public UpgradeCardUI[] cards;
    public UpgradeStatIconLibrary iconLibrary;

    [Header("Action Buttons")]
    [SerializeField] private Button actionButtonTemplate;
    [SerializeField] private Button banishButton;
    [SerializeField] private TMP_Text banishButtonText;
    [SerializeField] private Button rerollButton;
    [SerializeField] private TMP_Text rerollButtonText;
    [SerializeField] private Vector2 actionButtonsAnchoredPosition = new(0f, -230f);
    [SerializeField] private Vector2 actionButtonsContainerSize = new(520f, 56f);
    [SerializeField] private Vector2 actionButtonSize = new(240f, 56f);
    [SerializeField, Min(0f)] private float actionButtonsSpacing = 20f;
    [SerializeField] private string banishButtonLabel = "Banish";
    [SerializeField] private string rerollButtonLabel = "Reroll";

    private PlayerProgressionController player;
    private CursorScript cursor;
    private readonly List<UpgradeOption> currentOptions = new(3);
    private RectTransform actionButtonsRoot;
    private bool awaitingBanishPick;

    private void Awake()
    {
        ResolvePlayer();
        cursor = FindFirstObjectByType<CursorScript>();
        EnsureActionButtons();
        Hide();
    }

    private void Update()
    {
        ResolvePlayer();

        if (IsSelectionVisible())
            RefreshActionButtons();
    }

    private void OnDestroy()
    {
        if (player != null)
        {
            player.OnLevelUpOptionsRolled -= QueueShow;
            player.OnChoiceActionStateChanged -= RefreshActionButtons;
        }
    }

    private void QueueShow(List<UpgradeOption> options)
    {
        List<UpgradeOption> snapshot = options != null
            ? new List<UpgradeOption>(options)
            : new List<UpgradeOption>();

        ChoiceUiQueue.Enqueue(() => ShowNow(snapshot), "upgrade_selection");
    }

    private void ShowNow(List<UpgradeOption> options)
    {
        PlayerProgressionController resolvedPlayer = ResolvePlayer();
        if (resolvedPlayer == null)
        {
            AbortSelection("upgrade_selection_no_player");
            return;
        }

        if (cards == null || cards.Length == 0)
        {
            AbortSelection("upgrade_selection_no_cards", resolvedPlayer);
            return;
        }

        if (options == null || options.Count == 0)
        {
            AbortSelection("upgrade_selection_empty", resolvedPlayer);
            return;
        }

        if (upgradeCanvas != null)
            upgradeCanvas.enabled = true;

        if (root != null)
            root.SetActive(true);

        cursor?.ShowCursor();

        awaitingBanishPick = false;
        SetCurrentOptions(options, resolvedPlayer);
        if (!HasVisibleOptions())
        {
            AbortSelection("upgrade_selection_empty_after_filter", resolvedPlayer);
            return;
        }

        RebindCards();
        RefreshActionButtons();
    }

    private void Hide()
    {
        awaitingBanishPick = false;
        currentOptions.Clear();

        if (root != null)
            root.SetActive(false);

        if (upgradeCanvas != null)
            upgradeCanvas.enabled = false;
    }

    private void OnPick(UpgradeOption option)
    {
        PlayerProgressionController resolvedPlayer = ResolvePlayer();

        if (awaitingBanishPick)
        {
            TryBanishOption(option, resolvedPlayer);
            return;
        }

        if (option == null)
            return;

        if (resolvedPlayer == null)
        {
            AbortSelection("upgrade_selection_invalid_pick", resolvedPlayer);
            return;
        }

        if (UpgradeWeightRuntime.Instance != null)
            UpgradeWeightRuntime.Instance.OnUpgradePicked(option.stat);

        resolvedPlayer.ApplyUpgrade(option);

        Hide();

        if (ChoiceUiQueue.PendingCount > 0)
        {
            Time.timeScale = 0f;
        }
        else
        {
            cursor?.HideCursor();
        }

        ChoiceUiQueue.CompleteCurrent("upgrade_selection_pick");
    }

    private void TryBanishOption(UpgradeOption option, PlayerProgressionController resolvedPlayer)
    {
        if (option == null || resolvedPlayer == null)
        {
            awaitingBanishPick = false;
            RefreshActionButtons();
            return;
        }

        if (!resolvedPlayer.TryBanishUpgrade(option))
        {
            awaitingBanishPick = false;
            RefreshActionButtons();
            return;
        }

        int optionIndex = currentOptions.IndexOf(option);
        if (optionIndex >= 0)
        {
            currentOptions[optionIndex] = null;
        }
        else
        {
            for (int i = 0; i < currentOptions.Count; i++)
            {
                UpgradeOption entry = currentOptions[i];
                if (entry != null && entry.stat == option.stat)
                {
                    currentOptions[i] = null;
                    break;
                }
            }
        }

        awaitingBanishPick = false;
        RebindCards();
        RefreshActionButtons();

        if (HasVisibleOptions())
            return;

        if (CanUseReroll(resolvedPlayer))
            return;

        AbortSelection("upgrade_selection_all_banished", resolvedPlayer);
    }

    private void OnBanishButtonPressed()
    {
        PlayerProgressionController resolvedPlayer = ResolvePlayer();
        if (resolvedPlayer == null)
            return;

        if (resolvedPlayer.BanishesRemaining <= 0 || !HasVisibleOptions())
            return;

        awaitingBanishPick = !awaitingBanishPick;
        RefreshActionButtons();
    }

    private void OnRerollButtonPressed()
    {
        PlayerProgressionController resolvedPlayer = ResolvePlayer();
        if (resolvedPlayer == null)
            return;

        if (!resolvedPlayer.TrySpendReroll())
        {
            RefreshActionButtons();
            return;
        }

        awaitingBanishPick = false;

        int choiceCount = cards != null && cards.Length > 0 ? cards.Length : 3;
        List<UpgradeOption> rerolled = resolvedPlayer.RollUpgradeOptions(choiceCount);
        SetCurrentOptions(rerolled, resolvedPlayer);
        RebindCards();
        RefreshActionButtons();

        if (HasVisibleOptions())
            return;

        if (CanUseReroll(resolvedPlayer))
            return;

        AbortSelection("upgrade_selection_reroll_empty", resolvedPlayer);
    }

    private void AbortSelection(
        string reason,
        PlayerProgressionController resolvedPlayer = null
    )
    {
        Hide();

        PlayerProgressionController activePlayer =
            resolvedPlayer != null ? resolvedPlayer : ResolvePlayer();
        if (activePlayer != null && activePlayer.IsChoosingUpgrade)
        {
            activePlayer.CancelCurrentUpgradeChoice();
        }
        else if (ChoiceUiQueue.PendingCount == 0)
        {
            Time.timeScale = 1f;
            cursor?.HideCursor();
        }

        ChoiceUiQueue.CompleteCurrent(reason);
    }

    private void SetCurrentOptions(List<UpgradeOption> options, PlayerProgressionController resolvedPlayer)
    {
        currentOptions.Clear();
        if (options == null)
            return;

        int maxCount = cards != null && cards.Length > 0 ? cards.Length : options.Count;
        for (int i = 0; i < options.Count && currentOptions.Count < maxCount; i++)
        {
            UpgradeOption option = options[i];
            if (option == null)
                continue;

            if (resolvedPlayer != null && resolvedPlayer.IsUpgradeBanished(option.stat))
                continue;

            currentOptions.Add(option);
        }
    }

    private void RebindCards()
    {
        if (cards == null)
            return;

        for (int i = 0; i < cards.Length; i++)
        {
            UpgradeCardUI card = cards[i];
            if (card == null)
                continue;

            UpgradeOption option = i < currentOptions.Count ? currentOptions[i] : null;
            card.gameObject.SetActive(true);
            card.Bind(
                option,
                iconLibrary,
                option != null ? RarityColor(option.rarity) : Color.gray,
                OnPick
            );
            SetCardSlotVisible(card, option != null);
        }
    }

    private static void SetCardSlotVisible(UpgradeCardUI card, bool isVisible)
    {
        if (card == null)
            return;

        CanvasGroup group = card.GetComponent<CanvasGroup>();
        if (group == null)
            group = card.gameObject.AddComponent<CanvasGroup>();

        group.alpha = isVisible ? 1f : 0f;
        group.interactable = isVisible;
        group.blocksRaycasts = isVisible;
    }

    private bool HasVisibleOptions()
    {
        for (int i = 0; i < currentOptions.Count; i++)
        {
            if (currentOptions[i] != null)
                return true;
        }

        return false;
    }

    private bool CanUseReroll(PlayerProgressionController resolvedPlayer)
    {
        return IsSelectionVisible()
            && resolvedPlayer != null
            && resolvedPlayer.RerollsRemaining > 0
            && resolvedPlayer.upgradeLibrary != null;
    }

    private bool IsSelectionVisible()
    {
        bool rootVisible = root != null && root.activeInHierarchy;
        bool canvasVisible = upgradeCanvas != null && upgradeCanvas.enabled;
        return rootVisible || canvasVisible;
    }

    private void EnsureActionButtons()
    {
        if (root == null)
            return;

        if (actionButtonsRoot == null)
        {
            Transform existing = root.transform.Find("ChoiceActionsRow");
            if (existing != null)
            {
                actionButtonsRoot = existing as RectTransform;
            }
            else
            {
                var rowGo = new GameObject(
                    "ChoiceActionsRow",
                    typeof(RectTransform),
                    typeof(HorizontalLayoutGroup)
                );
                actionButtonsRoot = rowGo.GetComponent<RectTransform>();
                actionButtonsRoot.SetParent(root.transform, false);
                actionButtonsRoot.anchorMin = new Vector2(0.5f, 0.5f);
                actionButtonsRoot.anchorMax = new Vector2(0.5f, 0.5f);
                actionButtonsRoot.pivot = new Vector2(0.5f, 0.5f);
                actionButtonsRoot.anchoredPosition = actionButtonsAnchoredPosition;
                actionButtonsRoot.sizeDelta = actionButtonsContainerSize;

                HorizontalLayoutGroup layout = rowGo.GetComponent<HorizontalLayoutGroup>();
                layout.childAlignment = TextAnchor.MiddleCenter;
                layout.spacing = actionButtonsSpacing;
                layout.childControlWidth = false;
                layout.childControlHeight = false;
                layout.childForceExpandWidth = false;
                layout.childForceExpandHeight = false;
            }
        }

        if (banishButton == null)
            banishButton = CreateActionButton("BanishButton");

        if (rerollButton == null)
            rerollButton = CreateActionButton("RerollButton");

        if (banishButton != null)
        {
            banishButton.onClick = new Button.ButtonClickedEvent();
            banishButton.onClick.AddListener(OnBanishButtonPressed);
            banishButtonText = ResolveButtonText(banishButton, banishButtonText);
        }

        if (rerollButton != null)
        {
            rerollButton.onClick = new Button.ButtonClickedEvent();
            rerollButton.onClick.AddListener(OnRerollButtonPressed);
            rerollButtonText = ResolveButtonText(rerollButton, rerollButtonText);
        }

        RefreshActionButtons();
    }

    private Button CreateActionButton(string objectName)
    {
        if (actionButtonsRoot == null)
            return null;

        Button template = ResolveActionButtonTemplate();
        Button created;

        if (template != null)
        {
            created = Instantiate(template, actionButtonsRoot, false);
            created.onClick = new Button.ButtonClickedEvent();
        }
        else
        {
            created = CreateFallbackButton(actionButtonsRoot, objectName);
        }

        if (created == null)
            return null;

        created.gameObject.name = objectName;
        created.gameObject.SetActive(true);

        RectTransform rect = created.transform as RectTransform;
        if (rect != null)
            rect.sizeDelta = actionButtonSize;

        return created;
    }

    private Button ResolveActionButtonTemplate()
    {
        if (actionButtonTemplate != null)
            return actionButtonTemplate;

        Button[] allButtons = Resources.FindObjectsOfTypeAll<Button>();
        if (allButtons == null || allButtons.Length == 0)
            return null;

        string[] preferredNames =
        {
            "ResumeButton",
            "RestartButton",
            "BackButton",
            "OptionsButton",
            "QuitButton"
        };

        for (int n = 0; n < preferredNames.Length; n++)
        {
            string expectedName = preferredNames[n];
            for (int i = 0; i < allButtons.Length; i++)
            {
                Button candidate = allButtons[i];
                if (candidate == null)
                    continue;

                GameObject go = candidate.gameObject;
                if (go == null || !go.scene.IsValid())
                    continue;

                if (!string.Equals(go.name, expectedName, System.StringComparison.Ordinal))
                    continue;

                actionButtonTemplate = candidate;
                return actionButtonTemplate;
            }
        }

        for (int i = 0; i < allButtons.Length; i++)
        {
            Button candidate = allButtons[i];
            if (candidate == null)
                continue;

            GameObject go = candidate.gameObject;
            if (go == null || !go.scene.IsValid())
                continue;

            actionButtonTemplate = candidate;
            return actionButtonTemplate;
        }

        return null;
    }

    private static Button CreateFallbackButton(Transform parent, string objectName)
    {
        GameObject buttonGo = new GameObject(
            objectName,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image),
            typeof(Button)
        );
        var rect = buttonGo.GetComponent<RectTransform>();
        rect.SetParent(parent, false);

        Image image = buttonGo.GetComponent<Image>();
        image.color = new Color(0.17f, 0.17f, 0.17f, 0.94f);

        Button button = buttonGo.GetComponent<Button>();
        ColorBlock colors = button.colors;
        colors.normalColor = new Color(0.17f, 0.17f, 0.17f, 0.94f);
        colors.highlightedColor = new Color(0.24f, 0.24f, 0.24f, 0.96f);
        colors.pressedColor = new Color(0.12f, 0.12f, 0.12f, 0.98f);
        colors.disabledColor = new Color(0.10f, 0.10f, 0.10f, 0.55f);
        button.colors = colors;

        GameObject labelGo = new GameObject(
            "Label",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(TextMeshProUGUI)
        );
        var labelRect = labelGo.GetComponent<RectTransform>();
        labelRect.SetParent(rect, false);
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;

        TMP_Text label = labelGo.GetComponent<TMP_Text>();
        label.text = objectName;
        label.alignment = TextAlignmentOptions.Center;
        label.fontSize = 26f;
        label.color = new Color(0.95f, 0.95f, 0.95f, 1f);

        if (TMP_Settings.defaultFontAsset != null)
            label.font = TMP_Settings.defaultFontAsset;

        return button;
    }

    private static TMP_Text ResolveButtonText(Button button, TMP_Text current)
    {
        if (current != null)
            return current;

        if (button == null)
            return null;

        return button.GetComponentInChildren<TMP_Text>(true);
    }

    private void RefreshActionButtons()
    {
        PlayerProgressionController resolvedPlayer = ResolvePlayer();
        int banishes = resolvedPlayer != null ? resolvedPlayer.BanishesRemaining : 0;
        int rerolls = resolvedPlayer != null ? resolvedPlayer.RerollsRemaining : 0;

        if (banishButtonText != null)
        {
            string prefix = awaitingBanishPick ? "Pick Card" : banishButtonLabel;
            banishButtonText.text = $"{prefix} ({banishes})";
        }

        if (rerollButtonText != null)
            rerollButtonText.text = $"{rerollButtonLabel} ({rerolls})";

        bool canBanish = IsSelectionVisible() && resolvedPlayer != null && banishes > 0 && HasVisibleOptions();
        bool canReroll = CanUseReroll(resolvedPlayer);

        if (banishButton != null)
            banishButton.interactable = canBanish;

        if (rerollButton != null)
            rerollButton.interactable = canReroll;

        if (!canBanish)
            awaitingBanishPick = false;
    }

    private PlayerProgressionController ResolvePlayer()
    {
        PlayerProgressionController resolved = PlayerLocator.GetProgression();
        if (resolved == player)
            return player;

        if (player != null)
        {
            player.OnLevelUpOptionsRolled -= QueueShow;
            player.OnChoiceActionStateChanged -= RefreshActionButtons;
        }

        player = resolved;
        if (player == null)
            return null;

        player.OnLevelUpOptionsRolled -= QueueShow;
        player.OnLevelUpOptionsRolled += QueueShow;
        player.OnChoiceActionStateChanged -= RefreshActionButtons;
        player.OnChoiceActionStateChanged += RefreshActionButtons;

        return player;
    }

    private Color RarityColor(UpgradeRarity rarity)
    {
        return rarity switch
        {
            UpgradeRarity.Common => Hex("#808080"),
            UpgradeRarity.Uncommon => Hex("#3CB371"),
            UpgradeRarity.Rare => Hex("#3A7BD5"),
            UpgradeRarity.Legendary => Hex("#FFD700"),
            UpgradeRarity.Mythic => Hex("#8A2BE2"),
            _ => Color.white
        };
    }

    private Color Hex(string hex)
    {
        ColorUtility.TryParseHtmlString(hex, out Color color);
        return color;
    }
}

