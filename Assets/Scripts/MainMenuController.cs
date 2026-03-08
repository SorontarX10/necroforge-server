using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;
using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.Rendering.Universal;
using UnityEngine.Networking;

public class MainMenuController : MonoBehaviour
{
    [Header("Roots")]
    public GameObject mainMenuRoot;
    public GameObject leaderboardRoot;
    public GameObject optionsRoot;

    [Header("Version")]
    [SerializeField] private TMP_Text versionText;
    [SerializeField] private string versionPrefix = "v";

    [Header("Main Buttons")]
    public Button newGameButton;

    [Header("Leaderboard")]
    public TMP_Text[] leaderboardEntries;
    public TMP_Text leaderboardStatusText;
    public TMP_Text leaderboardMyRankText;
    public Button leaderboardRetryButton;
    public Button leaderboardOverlayButton;
    private Coroutine leaderboardRefreshRoutine;

    [Header("Legal Links")]
    public Button privacyButton;
    public Button eulaButton;
    public Button thirdPartyLicensesButton;
    [SerializeField] private string legalLocalFolder = "legal";
    [SerializeField] private string privacyFallbackUrl = "https://github.com/SorontarX10/necroforge/blob/main/Docs/PRIVACY.md";
    [SerializeField] private string eulaFallbackUrl = "https://github.com/SorontarX10/necroforge/blob/main/Docs/EULA.md";
    [SerializeField] private string thirdPartyFallbackUrl = "https://github.com/SorontarX10/necroforge/blob/main/Docs/THIRD_PARTY_LICENSES.md";

    public float clickDelay = 0.3f;

    void Awake()
    {
        // Start: main menu visible, secondary panels hidden.
        if (mainMenuRoot) mainMenuRoot.SetActive(true);
        if (leaderboardRoot) leaderboardRoot.SetActive(false);
        if (optionsRoot) optionsRoot.SetActive(false);

        ResetMainMenuCameraStack();
        ApplyVersionLabel();
        SetupLeaderboardUiBindings();
        SetupLegalLinkBindings();
    }

    private void OnDestroy()
    {
        if (leaderboardRetryButton != null)
            leaderboardRetryButton.onClick.RemoveListener(OnLeaderboardRetryClicked);
        if (leaderboardOverlayButton != null)
            leaderboardOverlayButton.onClick.RemoveListener(OnLeaderboardOverlayClicked);
        if (privacyButton != null)
            privacyButton.onClick.RemoveListener(OnPrivacyClicked);
        if (eulaButton != null)
            eulaButton.onClick.RemoveListener(OnEulaClicked);
        if (thirdPartyLicensesButton != null)
            thirdPartyLicensesButton.onClick.RemoveListener(OnThirdPartyLicensesClicked);
    }

    // ======================
    // NEW GAME
    // ======================

    public void OnNewGameClicked()
    {
        newGameButton.interactable = false;
        StartCoroutine(StartNewGameRoutine());
    }

    IEnumerator StartNewGameRoutine()
    {
        yield return new WaitForSeconds(clickDelay);

        ResetMainMenuCameraStack();

        SceneManager.LoadScene("Loading");
    }

    private static void ResetMainMenuCameraStack()
    {
        Camera cam = Camera.main;
        if (cam == null)
            return;

        UniversalAdditionalCameraData additionalData = cam.GetUniversalAdditionalCameraData();
        if (additionalData?.cameraStack != null)
            additionalData.cameraStack.Clear();
    }

    // ======================
    // LEADERBOARD
    // ======================

    public void OnLeaderboardClicked()
    {
        if (mainMenuRoot) mainMenuRoot.SetActive(false);
        if (leaderboardRoot) leaderboardRoot.SetActive(true);

        RefreshLeaderboard();
    }

    public void OnLeaderboardBackClicked()
    {
        if (leaderboardRoot) leaderboardRoot.SetActive(false);
        if (mainMenuRoot) mainMenuRoot.SetActive(true);
    }

    public void OnLeaderboardRetryClicked()
    {
        RefreshLeaderboard();
    }

    public void OnLeaderboardOverlayClicked()
    {
        string baseUrl = OnlineLeaderboardSettings.GetBaseUrl().TrimEnd('/');
        string season = UnityWebRequest.EscapeURL(OnlineLeaderboardSettings.GetSeason());
        string url = $"{baseUrl}/leaderboard?season={season}&page=1&page_size=20";

        bool openedInPlatformOverlay = PlatformServices.OpenLeaderboardOverlay(url);
        if (!openedInPlatformOverlay)
            Application.OpenURL(url);
    }

    void RefreshLeaderboard()
    {
        if (leaderboardEntries == null || leaderboardEntries.Length == 0)
            return;

        if (leaderboardRefreshRoutine != null)
            StopCoroutine(leaderboardRefreshRoutine);

        leaderboardRefreshRoutine = StartCoroutine(RefreshLeaderboardRoutine());
    }

    private IEnumerator RefreshLeaderboardRoutine()
    {
        ApplyLocalEntries();
        SetRetryVisible(false);
        SetStatus("Loading online leaderboard...");
        SetMyRank(string.Empty);

        if (!OnlineLeaderboardSettings.IsOnlineEnabled)
        {
            SetStatus("Online leaderboard is disabled. Showing local scores.");
            SetMyRank("Your rank: offline");
            yield break;
        }

        bool usedOnline = false;
        OnlineLeaderboardApiClient.FetchTopResult fetchResult = null;
        yield return OnlineLeaderboardApiClient.FetchTopEntries(
            leaderboardEntries.Length,
            result => { fetchResult = result; }
        );

        if (fetchResult != null && fetchResult.success)
        {
            ApplyOnlineEntries(fetchResult.entries);
            usedOnline = true;

            if (fetchResult.isStale)
            {
                SetStatus($"Online unavailable ({SanitizeError(fetchResult.error)}). Showing last synced leaderboard.");
                SetRetryVisible(true);
            }
            else
            {
                SetStatus(
                    fetchResult.entries.Count > 0
                        ? "Online leaderboard synced."
                        : "Online leaderboard synced. No online scores yet."
                );
                SetRetryVisible(false);
            }
        }

        if (usedOnline)
        {
            OnlineLeaderboardApiClient.FetchMyRankResult myRank = null;
            yield return OnlineLeaderboardApiClient.FetchMyRank(result => { myRank = result; });
            if (myRank != null && myRank.success)
            {
                if (myRank.found && myRank.entry != null)
                    SetMyRank(
                        myRank.isStale
                            ? $"Your rank: #{myRank.entry.rank} (cached)"
                            : $"Your rank: #{myRank.entry.rank}"
                    );
                else
                    SetMyRank(myRank.isStale ? "Your rank: unavailable" : "Your rank: no online run yet");
            }
            else
            {
                SetMyRank("Your rank: unavailable");
            }
            yield break;
        }

        ApplyLocalEntries();
        string fetchError = fetchResult?.error ?? "unknown_error";
        SetStatus($"Online unavailable ({SanitizeError(fetchError)}). Showing local leaderboard.");
        SetMyRank("Your rank: offline");
        SetRetryVisible(true);
    }

    private void ApplyOnlineEntries(List<OnlineLeaderboardApiClient.LeaderboardEntry> entries)
    {
        for (int i = 0; i < leaderboardEntries.Length; i++)
        {
            if (i < entries.Count)
            {
                OnlineLeaderboardApiClient.LeaderboardEntry entry = entries[i];
                string name = string.IsNullOrWhiteSpace(entry.displayName) ? "Player" : entry.displayName;
                leaderboardEntries[i].text = $"{entry.rank}. {name} - {entry.score}";
            }
            else
            {
                leaderboardEntries[i].text = $"{i + 1}. ---";
            }
        }
    }

    private void ApplyLocalEntries()
    {
        LocalLeaderboardService.Entry[] entries = LocalLeaderboardService.GetTopEntries(leaderboardEntries.Length);
        for (int i = 0; i < leaderboardEntries.Length; i++)
        {
            if (i < entries.Length)
                leaderboardEntries[i].text = $"{i + 1}. {entries[i].score}  ({entries[i].date})";
            else
                leaderboardEntries[i].text = $"{i + 1}. ---";
        }
    }

    private void SetupLeaderboardUiBindings()
    {
        if (leaderboardRetryButton != null)
        {
            leaderboardRetryButton.onClick.RemoveListener(OnLeaderboardRetryClicked);
            leaderboardRetryButton.onClick.AddListener(OnLeaderboardRetryClicked);
            leaderboardRetryButton.gameObject.SetActive(false);
        }

        if (leaderboardOverlayButton != null)
        {
            leaderboardOverlayButton.onClick.RemoveListener(OnLeaderboardOverlayClicked);
            leaderboardOverlayButton.onClick.AddListener(OnLeaderboardOverlayClicked);
        }

        SetStatus(string.Empty);
        SetMyRank(string.Empty);
    }

    private void SetStatus(string message)
    {
        if (leaderboardStatusText != null)
            leaderboardStatusText.text = message ?? string.Empty;
    }

    private void SetMyRank(string message)
    {
        if (leaderboardMyRankText != null)
            leaderboardMyRankText.text = message ?? string.Empty;
    }

    private void SetRetryVisible(bool visible)
    {
        if (leaderboardRetryButton != null)
            leaderboardRetryButton.gameObject.SetActive(visible);
    }

    private static string SanitizeError(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "unknown";
        const int maxLength = 96;
        string trimmed = raw.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed.Substring(0, maxLength);
    }

    // ======================
    // LEGAL
    // ======================

    public void OnPrivacyClicked()
    {
        OpenLegalDocument("PRIVACY.md", privacyFallbackUrl);
    }

    public void OnEulaClicked()
    {
        OpenLegalDocument("EULA.md", eulaFallbackUrl);
    }

    public void OnThirdPartyLicensesClicked()
    {
        OpenLegalDocument("THIRD_PARTY_LICENSES.md", thirdPartyFallbackUrl);
    }

    private void SetupLegalLinkBindings()
    {
        EnsureRuntimeLegalButtons();
        BindLegalButton(privacyButton, OnPrivacyClicked);
        BindLegalButton(eulaButton, OnEulaClicked);
        BindLegalButton(thirdPartyLicensesButton, OnThirdPartyLicensesClicked);
    }

    private static void BindLegalButton(Button button, UnityEngine.Events.UnityAction handler)
    {
        if (button == null || handler == null)
            return;

        button.onClick.RemoveListener(handler);
        button.onClick.AddListener(handler);
    }

    private void OpenLegalDocument(string fileName, string fallbackUrl)
    {
        if (TryOpenLocalLegalDocument(fileName))
            return;

        if (!string.IsNullOrWhiteSpace(fallbackUrl))
        {
            Application.OpenURL(fallbackUrl.Trim());
            return;
        }

        Debug.LogWarning($"[Legal] Missing local document and fallback URL for {fileName}.");
    }

    private bool TryOpenLocalLegalDocument(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return false;

        string folder = string.IsNullOrWhiteSpace(legalLocalFolder) ? "legal" : legalLocalFolder.Trim();
        string localPath = Path.Combine(Application.streamingAssetsPath, folder, fileName);
        if (!File.Exists(localPath))
            return false;

        string uri = new Uri(localPath).AbsoluteUri;
        Application.OpenURL(uri);
        return true;
    }

    // ======================
    // EXIT
    // ======================

    public void OnQuitGameClicked()
    {
        Application.Quit();

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
    }

    public void OnOptionsClicked()
    {
        mainMenuRoot.SetActive(false);
        optionsRoot.SetActive(true);
    }

    public void OnOptionsBack()
    {
        optionsRoot.SetActive(false);
        mainMenuRoot.SetActive(true);
    }

    private void ApplyVersionLabel()
    {
        versionText = ResolveVersionText();
        if (versionText == null)
            return;

        string prefix = string.IsNullOrWhiteSpace(versionPrefix) ? "v" : versionPrefix;
        versionText.text = $"{prefix}{Application.version}";
    }

    private TMP_Text ResolveVersionText()
    {
        if (versionText != null)
            return versionText;

        if (mainMenuRoot != null)
        {
            TMP_Text[] texts = mainMenuRoot.GetComponentsInChildren<TMP_Text>(true);
            for (int i = 0; i < texts.Length; i++)
            {
                TMP_Text candidate = texts[i];
                if (candidate == null)
                    continue;

                if (string.Equals(candidate.gameObject.name, "VersionText", System.StringComparison.OrdinalIgnoreCase))
                {
                    versionText = candidate;
                    return versionText;
                }
            }
        }

        return CreateVersionLabel();
    }

    private TMP_Text CreateVersionLabel()
    {
        Canvas parentCanvas = null;
        if (mainMenuRoot != null)
            parentCanvas = mainMenuRoot.GetComponentInParent<Canvas>();

        if (parentCanvas == null)
            parentCanvas = FindFirstObjectByType<Canvas>();

        if (parentCanvas == null)
            return null;

        GameObject go = new GameObject(
            "VersionText",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(TextMeshProUGUI)
        );
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.SetParent(parentCanvas.transform, false);
        rt.anchorMin = new Vector2(1f, 0f);
        rt.anchorMax = new Vector2(1f, 0f);
        rt.pivot = new Vector2(1f, 0f);
        rt.anchoredPosition = new Vector2(-24f, 16f);
        rt.sizeDelta = new Vector2(260f, 40f);

        TMP_Text label = go.GetComponent<TextMeshProUGUI>();
        label.alignment = TextAlignmentOptions.BottomRight;
        label.fontSize = 18f;
        label.color = new Color(0.76f, 0.82f, 0.86f, 0.88f);
        label.raycastTarget = false;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Overflow;

        if (TMP_Settings.defaultFontAsset != null)
            label.font = TMP_Settings.defaultFontAsset;

        return label;
    }

    private void EnsureRuntimeLegalButtons()
    {
        if (privacyButton != null && eulaButton != null && thirdPartyLicensesButton != null)
            return;

        Canvas parentCanvas = null;
        if (mainMenuRoot != null)
            parentCanvas = mainMenuRoot.GetComponentInParent<Canvas>();
        if (parentCanvas == null)
            parentCanvas = FindFirstObjectByType<Canvas>();
        if (parentCanvas == null)
            return;

        GameObject panelGo = new GameObject(
            "LegalLinksPanel",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(HorizontalLayoutGroup)
        );
        RectTransform panelRt = panelGo.GetComponent<RectTransform>();
        panelRt.SetParent(parentCanvas.transform, false);
        panelRt.anchorMin = new Vector2(0f, 0f);
        panelRt.anchorMax = new Vector2(0f, 0f);
        panelRt.pivot = new Vector2(0f, 0f);
        panelRt.anchoredPosition = new Vector2(16f, 12f);
        panelRt.sizeDelta = new Vector2(560f, 40f);

        HorizontalLayoutGroup layout = panelGo.GetComponent<HorizontalLayoutGroup>();
        layout.spacing = 8f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        if (privacyButton == null)
            privacyButton = CreateLegalButton(panelGo.transform, "Privacy", "Privacy");
        if (eulaButton == null)
            eulaButton = CreateLegalButton(panelGo.transform, "EULA", "EULA");
        if (thirdPartyLicensesButton == null)
            thirdPartyLicensesButton = CreateLegalButton(panelGo.transform, "ThirdPartyLicenses", "Licenses");
    }

    private static Button CreateLegalButton(Transform parent, string objectName, string label)
    {
        GameObject buttonGo = new GameObject(
            objectName,
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image),
            typeof(Button),
            typeof(LayoutElement)
        );
        buttonGo.transform.SetParent(parent, false);

        LayoutElement layout = buttonGo.GetComponent<LayoutElement>();
        layout.preferredWidth = 150f;
        layout.preferredHeight = 34f;

        Image image = buttonGo.GetComponent<Image>();
        image.color = new Color(0.08f, 0.1f, 0.14f, 0.82f);

        Button button = buttonGo.GetComponent<Button>();
        ColorBlock colors = button.colors;
        colors.normalColor = image.color;
        colors.highlightedColor = new Color(0.13f, 0.17f, 0.24f, 0.95f);
        colors.pressedColor = new Color(0.06f, 0.08f, 0.12f, 1f);
        colors.selectedColor = colors.highlightedColor;
        button.colors = colors;

        GameObject textGo = new GameObject(
            "Label",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(TextMeshProUGUI)
        );
        textGo.transform.SetParent(buttonGo.transform, false);

        RectTransform textRt = textGo.GetComponent<RectTransform>();
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.offsetMin = Vector2.zero;
        textRt.offsetMax = Vector2.zero;

        TMP_Text text = textGo.GetComponent<TextMeshProUGUI>();
        text.text = label;
        text.fontSize = 16f;
        text.color = new Color(0.88f, 0.9f, 0.93f, 0.96f);
        text.alignment = TextAlignmentOptions.Center;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Truncate;

        if (TMP_Settings.defaultFontAsset != null)
            text.font = TMP_Settings.defaultFontAsset;

        return button;
    }
}
