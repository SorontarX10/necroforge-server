using System.Collections.Generic;
using TMPro;
using GrassSim.Core;
using GrassSim.UI;
using UnityEngine;
using UnityEngine.UI;

public class RelicSelectionUI : MonoBehaviour
{
    public GameObject root;
    public RelicCardUI[] cards;

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

    private PlayerRelicController player;
    private PlayerProgressionController progression;
    private CursorScript cursor;
    private readonly List<RelicDefinition> eligibleRelics = new(8);
    private RectTransform actionButtonsRoot;
    private bool awaitingBanishPick;
    private System.Func<List<RelicDefinition>> currentRerollProvider;

    private void Awake()
    {
        if (root != null)
            root.SetActive(false);

        cursor = FindFirstObjectByType<CursorScript>();
        ResolvePlayer();
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
        if (progression != null)
            progression.OnChoiceActionStateChanged -= RefreshActionButtons;
    }

    public void Show(List<RelicDefinition> relics)
    {
        Show(relics, null);
    }

    public void Show(List<RelicDefinition> relics, System.Func<List<RelicDefinition>> rerollProvider)
    {
        List<RelicDefinition> snapshot = relics != null ? new List<RelicDefinition>(relics) : new List<RelicDefinition>();
        ChoiceUiQueue.Enqueue(() => ShowNow(snapshot, rerollProvider), "relic_selection");
    }

    private void ShowNow(List<RelicDefinition> relics, System.Func<List<RelicDefinition>> rerollProvider)
    {
        currentRerollProvider = rerollProvider;
        awaitingBanishPick = false;

        BuildEligibleList(relics);
        if (eligibleRelics.Count == 0)
        {
            Hide();
            ChoiceUiQueue.CompleteCurrent("relic_selection_empty");
            return;
        }

        Time.timeScale = 0f;
        if (root != null)
            root.SetActive(true);

        cursor?.ShowCursor();
        UnlockCursor();

        ResolvePlayer();

        if (cards == null || cards.Length == 0)
        {
            Hide();
            ChoiceUiQueue.CompleteCurrent("relic_selection_no_cards");
            return;
        }

        RebindCards();
        RefreshActionButtons();
    }

    private void RebindCards()
    {
        if (cards == null)
            return;

        for (int i = 0; i < cards.Length; i++)
        {
            if (cards[i] == null)
                continue;

            RelicDefinition relic = i < eligibleRelics.Count ? eligibleRelics[i] : null;
            cards[i].gameObject.SetActive(true);
            cards[i].Bind(relic, OnPick);
            SetCardSlotVisible(cards[i], relic != null);
        }
    }

    private void OnPick(RelicDefinition relic)
    {
        PlayerRelicController resolvedPlayer = ResolvePlayer();

        if (awaitingBanishPick)
        {
            TryBanishRelic(relic);
            return;
        }

        if (relic == null)
            return;

        if (resolvedPlayer == null)
        {
            Hide();
            ChoiceUiQueue.CompleteCurrent("relic_selection_invalid_pick");
            return;
        }

        bool applied = resolvedPlayer.AddRelic(relic);
        if (!applied)
        {
            Hide();
            ChoiceUiQueue.CompleteCurrent("relic_selection_rejected_pick");
            return;
        }

        Hide();
        ChoiceUiQueue.CompleteCurrent("relic_selection_pick");
    }

    private void TryBanishRelic(RelicDefinition relic)
    {
        if (relic == null || progression == null)
        {
            awaitingBanishPick = false;
            RefreshActionButtons();
            return;
        }

        if (!progression.TryBanishRelic(relic))
        {
            awaitingBanishPick = false;
            RefreshActionButtons();
            return;
        }

        int relicIndex = eligibleRelics.IndexOf(relic);
        if (relicIndex >= 0)
        {
            eligibleRelics[relicIndex] = null;
        }
        else
        {
            for (int i = 0; i < eligibleRelics.Count; i++)
            {
                RelicDefinition entry = eligibleRelics[i];
                if (entry != null && string.Equals(entry.id, relic.id, System.StringComparison.Ordinal))
                {
                    eligibleRelics[i] = null;
                    break;
                }
            }
        }

        awaitingBanishPick = false;
        RebindCards();
        RefreshActionButtons();

        if (HasVisibleRelics())
            return;

        if (CanUseReroll())
            return;

        Hide();
        ChoiceUiQueue.CompleteCurrent("relic_selection_all_banished");
    }

    private void OnBanishButtonPressed()
    {
        if (progression == null)
            return;

        if (progression.BanishesRemaining <= 0 || !HasVisibleRelics())
            return;

        awaitingBanishPick = !awaitingBanishPick;
        RefreshActionButtons();
    }

    private void OnRerollButtonPressed()
    {
        if (progression == null)
            return;

        if (!progression.TrySpendReroll())
        {
            RefreshActionButtons();
            return;
        }

        awaitingBanishPick = false;

        List<RelicDefinition> rerolled = null;
        if (currentRerollProvider != null)
        {
            try
            {
                rerolled = currentRerollProvider.Invoke();
            }
            catch (System.Exception ex)
            {
                Debug.LogException(ex, this);
            }
        }

        BuildEligibleList(rerolled);
        RebindCards();
        RefreshActionButtons();

        if (HasVisibleRelics())
            return;

        if (CanUseReroll())
            return;

        Hide();
        ChoiceUiQueue.CompleteCurrent("relic_selection_reroll_empty");
    }

    private void Hide()
    {
        awaitingBanishPick = false;
        currentRerollProvider = null;
        eligibleRelics.Clear();

        if (root != null)
            root.SetActive(false);

        bool hasPendingModal = ChoiceUiQueue.PendingCount > 0;
        PlayerRelicController resolvedPlayer = player != null ? player : ResolvePlayer();
        bool upgradeModalActive =
            resolvedPlayer != null
            && resolvedPlayer.Progression != null
            && resolvedPlayer.Progression.IsChoosingUpgrade;

        if (!hasPendingModal && !upgradeModalActive)
        {
            cursor?.HideCursor();
            LockCursor();
            Time.timeScale = 1f;
        }
    }

    private void BuildEligibleList(List<RelicDefinition> relics)
    {
        eligibleRelics.Clear();
        if (relics == null || relics.Count == 0)
            return;

        ResolvePlayer();

        int maxCount = cards != null && cards.Length > 0 ? cards.Length : relics.Count;
        for (int i = 0; i < relics.Count && eligibleRelics.Count < maxCount; i++)
        {
            RelicDefinition relic = relics[i];
            if (relic == null)
                continue;

            if (progression != null && progression.IsRelicBanished(relic.id))
                continue;

            if (player != null && !player.CanAcceptRelic(relic))
                continue;

            eligibleRelics.Add(relic);
        }
    }

    private bool HasVisibleRelics()
    {
        for (int i = 0; i < eligibleRelics.Count; i++)
        {
            if (eligibleRelics[i] != null)
                return true;
        }

        return false;
    }

    private bool CanUseReroll()
    {
        return IsSelectionVisible()
            && progression != null
            && progression.RerollsRemaining > 0
            && currentRerollProvider != null;
    }

    private bool IsSelectionVisible()
    {
        return root != null && root.activeInHierarchy;
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

    private static void SetCardSlotVisible(RelicCardUI card, bool isVisible)
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

    private void RefreshActionButtons()
    {
        int banishes = progression != null ? progression.BanishesRemaining : 0;
        int rerolls = progression != null ? progression.RerollsRemaining : 0;

        if (banishButtonText != null)
        {
            string prefix = awaitingBanishPick ? "Pick Card" : banishButtonLabel;
            banishButtonText.text = $"{prefix} ({banishes})";
        }

        if (rerollButtonText != null)
            rerollButtonText.text = $"{rerollButtonLabel} ({rerolls})";

        bool canBanish = IsSelectionVisible() && progression != null && banishes > 0 && HasVisibleRelics();
        bool canReroll = CanUseReroll();

        if (banishButton != null)
            banishButton.interactable = canBanish;

        if (rerollButton != null)
            rerollButton.interactable = canReroll;

        if (!canBanish)
            awaitingBanishPick = false;
    }

    private PlayerRelicController ResolvePlayer()
    {
        PlayerRelicController resolved = player;

        if (resolved == null)
            resolved = FindFirstObjectByType<PlayerRelicController>();

        if (resolved == null)
        {
            PlayerProgressionController resolvedProgression = PlayerLocator.GetProgression();
            if (resolvedProgression != null)
                resolved = resolvedProgression.GetComponent<PlayerRelicController>();
        }

        if (player != resolved)
            player = resolved;

        PlayerProgressionController nextProgression = player != null ? player.Progression : null;
        if (nextProgression != progression)
        {
            if (progression != null)
                progression.OnChoiceActionStateChanged -= RefreshActionButtons;

            progression = nextProgression;

            if (progression != null)
                progression.OnChoiceActionStateChanged += RefreshActionButtons;
        }

        return player;
    }

    private static void UnlockCursor()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private static void LockCursor()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }
}

