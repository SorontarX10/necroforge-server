using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using GrassSim.Auth;
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
    [SerializeField] private string legalBaseUrlOverride = string.Empty;
    private const string DefaultLegalBaseUrl = "https://necroforge-lb.duckdns.org";
    private const string PrivacyPolicyPath = "/legal/privacy-policy.html";
    private const string EulaPath = "/legal/eula.html";
    private const string ThirdPartyLicensesPath = "/legal/third-party-licenses.html";

    [Header("External Auth")]
    public TMP_Text authStatusText;
    public Button authLoginGoogleButton;
    public Button authLoginMicrosoftButton;
    public Button authLoginFacebookButton;
    public Button authLogoutButton;
    [SerializeField] private bool createAuthPanelAtRuntime = false;
    [SerializeField] private bool requireAuthBeforeMenu = true;

    private GameObject runtimeAuthPanel;
    private GameObject runtimeAuthLoginGatePanel;
    private TMP_Text runtimeAuthLoginGateStatusText;
    private Button runtimeAuthLoginGateGoogleButton;
    private Button runtimeAuthLoginGateMicrosoftButton;
    private Button runtimeAuthLoginGateFacebookButton;
    private GameObject runtimeNicknameGatePanel;
    private TMP_Text runtimeNicknameGateStatusText;
    private TMP_InputField runtimeNicknameInputField;
    private Button runtimeNicknameConfirmButton;
    private string nicknamePromptAccountId = string.Empty;

    public float clickDelay = 0.3f;
    private bool authUiInitialized;
    private bool suppressAuthGateDuringSceneTransition;

    private const int NicknameMinLength = 3;
    private const int NicknameMaxLength = 24;

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
        SetupAuthUiBindings();
        ApplyMenuAccessGate();
    }

    private void OnEnable()
    {
        if (authUiInitialized)
            RefreshAuthStatusLabel();
    }

    private IEnumerator Start()
    {
        // One-frame delayed refresh to avoid timing glitches with intro/menu activation order.
        yield return null;
        if (authUiInitialized)
            RefreshAuthStatusLabel();
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
        if (authLoginGoogleButton != null)
            authLoginGoogleButton.onClick.RemoveListener(OnAuthGoogleClicked);
        if (authLoginMicrosoftButton != null)
            authLoginMicrosoftButton.onClick.RemoveListener(OnAuthMicrosoftClicked);
        if (authLoginFacebookButton != null)
            authLoginFacebookButton.onClick.RemoveListener(OnAuthFacebookClicked);
        if (authLogoutButton != null)
            authLogoutButton.onClick.RemoveListener(OnAuthLogoutClicked);
        if (runtimeAuthLoginGateGoogleButton != null)
            runtimeAuthLoginGateGoogleButton.onClick.RemoveListener(OnAuthGoogleClicked);
        if (runtimeAuthLoginGateMicrosoftButton != null)
            runtimeAuthLoginGateMicrosoftButton.onClick.RemoveListener(OnAuthMicrosoftClicked);
        if (runtimeAuthLoginGateFacebookButton != null)
            runtimeAuthLoginGateFacebookButton.onClick.RemoveListener(OnAuthFacebookClicked);
        if (runtimeNicknameConfirmButton != null)
            runtimeNicknameConfirmButton.onClick.RemoveListener(OnNicknameConfirmClicked);

        ExternalAuthService.StateChanged -= RefreshAuthStatusLabel;
    }

    // ======================
    // NEW GAME
    // ======================

    public void OnNewGameClicked()
    {
        if (!EnsureMenuUnlockedByAuthGate())
            return;

        suppressAuthGateDuringSceneTransition = true;
        SetLoginGateVisible(false);
        SetNicknameGateVisible(false);
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
        if (!EnsureMenuUnlockedByAuthGate())
            return;

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
        OpenLegalDocument(PrivacyPolicyPath);
    }

    public void OnEulaClicked()
    {
        OpenLegalDocument(EulaPath);
    }

    public void OnThirdPartyLicensesClicked()
    {
        OpenLegalDocument(ThirdPartyLicensesPath);
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

    private void OpenLegalDocument(string path)
    {
        string url = ResolveLegalUrl(path);
        if (!string.IsNullOrWhiteSpace(url))
            Application.OpenURL(url);
    }

    private string ResolveLegalUrl(string path)
    {
        string baseUrl = ResolveLegalBaseUrl();
        if (string.IsNullOrWhiteSpace(baseUrl))
            return string.Empty;

        string normalizedPath = string.IsNullOrWhiteSpace(path) ? "/" : path.Trim();
        if (!normalizedPath.StartsWith("/", StringComparison.Ordinal))
            normalizedPath = $"/{normalizedPath}";

        return $"{baseUrl}{normalizedPath}";
    }

    private string ResolveLegalBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(legalBaseUrlOverride))
            return legalBaseUrlOverride.Trim().TrimEnd('/');

        if (!string.IsNullOrWhiteSpace(ExternalAuthSettings.BrokerBaseUrl))
            return ExternalAuthSettings.BrokerBaseUrl.Trim().TrimEnd('/');

        string leaderboardBaseUrl = OnlineLeaderboardSettings.GetBaseUrl();
        if (!string.IsNullOrWhiteSpace(leaderboardBaseUrl))
            return leaderboardBaseUrl.Trim().TrimEnd('/');

        return DefaultLegalBaseUrl;
    }

    // ======================
    // AUTH
    // ======================

    public void OnAuthGoogleClicked()
    {
        ExternalAuthService.SignInWithProvider("google");
    }

    public void OnAuthMicrosoftClicked()
    {
        ExternalAuthService.SignInWithProvider("microsoft");
    }

    public void OnAuthFacebookClicked()
    {
        ExternalAuthService.SignInWithProvider("facebook");
    }

    public void OnAuthLogoutClicked()
    {
        ExternalAuthService.SignOut();
    }

    private void SetupAuthUiBindings()
    {
        ExternalAuthService.StateChanged -= RefreshAuthStatusLabel;
        authUiInitialized = false;

        bool authConfigured = IsAuthConfigured();
        if (!authConfigured)
        {
            SetAuthUiVisible(false);
            SetLoginGateVisible(false);
            SetNicknameGateVisible(false);
            nicknamePromptAccountId = string.Empty;
            ApplyMenuAccessGate();
            return;
        }

        bool missingAccountUi = authStatusText == null || authLogoutButton == null;
        bool missingLoginButtons =
            (IsProviderLoginEnabled("google") && authLoginGoogleButton == null)
            || (IsProviderLoginEnabled("microsoft") && authLoginMicrosoftButton == null)
            || (IsProviderLoginEnabled("facebook") && authLoginFacebookButton == null);
        if (createAuthPanelAtRuntime || missingAccountUi || missingLoginButtons)
            EnsureRuntimeAuthControls();

        if (authStatusText == null
            && authLoginGoogleButton == null
            && authLoginMicrosoftButton == null
            && authLoginFacebookButton == null
            && authLogoutButton == null)
        {
            SetAuthUiVisible(false);
            SetLoginGateVisible(false);
            SetNicknameGateVisible(false);
            nicknamePromptAccountId = string.Empty;
            ApplyMenuAccessGate();
            return;
        }

        EnsureRuntimeLoginGateControls();
        EnsureRuntimeNicknameGateControls();
        ApplyProviderButtonAvailability();

        BindAuthButton(authLoginGoogleButton, OnAuthGoogleClicked);
        BindAuthButton(authLoginMicrosoftButton, OnAuthMicrosoftClicked);
        BindAuthButton(authLoginFacebookButton, OnAuthFacebookClicked);
        BindAuthButton(authLogoutButton, OnAuthLogoutClicked);
        BindAuthButton(runtimeAuthLoginGateGoogleButton, OnAuthGoogleClicked);
        BindAuthButton(runtimeAuthLoginGateMicrosoftButton, OnAuthMicrosoftClicked);
        BindAuthButton(runtimeAuthLoginGateFacebookButton, OnAuthFacebookClicked);
        BindAuthButton(runtimeNicknameConfirmButton, OnNicknameConfirmClicked);

        authUiInitialized = true;
        ExternalAuthService.Initialize();
        ExternalAuthService.StateChanged += RefreshAuthStatusLabel;
        RefreshAuthStatusLabel();
    }

    private static void BindAuthButton(Button button, UnityEngine.Events.UnityAction handler)
    {
        if (button == null || handler == null)
            return;

        button.onClick.RemoveListener(handler);
        button.onClick.AddListener(handler);
    }

    private void RefreshAuthStatusLabel()
    {
        if (!authUiInitialized)
            return;

        bool authConfigured = IsAuthConfigured();
        bool hasSession = ExternalAuthService.IsSignedIn;
        bool lockedByAuthGate = IsMenuLockedByAuthGate();
        bool lockedByNicknameGate = IsMenuLockedByNicknameGate();

        string message = ExternalAuthService.StatusMessage;
        if (lockedByAuthGate && ExternalAuthService.State == ExternalAuthState.SignedOut)
            message = "Sign in to continue.";
        else if (lockedByNicknameGate)
            message = "Set your in-game nickname to continue.";

        if (authStatusText != null)
            authStatusText.text = message;

        if (runtimeAuthLoginGateStatusText != null)
            runtimeAuthLoginGateStatusText.text = message;

        if (authLogoutButton != null)
            authLogoutButton.interactable = hasSession;

        if (authConfigured && hasSession)
        {
            SetAuthUiVisible(true);
            HideInteractiveProviderButtons();
            if (authStatusText != null)
                authStatusText.gameObject.SetActive(true);
            if (authLogoutButton != null)
                authLogoutButton.gameObject.SetActive(true);
        }
        else
        {
            SetAuthUiVisible(false);
        }

        ApplyProviderButtonAvailability();
        RefreshNicknameGateState();
        if (suppressAuthGateDuringSceneTransition)
            SetLoginGateVisible(false);
        else
            SetLoginGateVisible(lockedByAuthGate);
        ApplyMenuAccessGate();
    }

    private void SetAuthUiVisible(bool visible)
    {
        if (runtimeAuthPanel != null)
            runtimeAuthPanel.SetActive(visible);

        if (authStatusText != null)
            authStatusText.gameObject.SetActive(visible);
        if (authLoginGoogleButton != null)
            authLoginGoogleButton.gameObject.SetActive(visible && IsProviderLoginEnabled("google"));
        if (authLoginMicrosoftButton != null)
            authLoginMicrosoftButton.gameObject.SetActive(visible && IsProviderLoginEnabled("microsoft"));
        if (authLoginFacebookButton != null)
            authLoginFacebookButton.gameObject.SetActive(visible && IsProviderLoginEnabled("facebook"));
        if (authLogoutButton != null)
            authLogoutButton.gameObject.SetActive(visible);
    }

    private bool EnsureMenuUnlockedByAuthGate()
    {
        if (!IsMenuLockedByAuthGate() && !IsMenuLockedByNicknameGate())
            return true;

        RefreshAuthStatusLabel();
        return false;
    }

    private bool IsAuthConfigured()
    {
        return ExternalAuthSettings.IsEnabled
            && !string.IsNullOrWhiteSpace(ExternalAuthSettings.BrokerBaseUrl)
            && HasAnyInteractiveAuthProviderEnabled();
    }

    private bool IsMenuLockedByAuthGate()
    {
        if (!requireAuthBeforeMenu)
            return false;

        return IsAuthConfigured() && !ExternalAuthService.IsSignedIn;
    }

    private bool IsMenuLockedByNicknameGate()
    {
        if (!requireAuthBeforeMenu)
            return false;

        if (!IsAuthConfigured() || !ExternalAuthService.IsSignedIn)
            return false;

        return !PlayerIdentityService.HasCustomExternalDisplayNameForActiveSession();
    }

    private void ApplyMenuAccessGate()
    {
        bool unlocked = !IsMenuLockedByAuthGate() && !IsMenuLockedByNicknameGate();
        if (newGameButton != null)
            newGameButton.interactable = unlocked;

        if (unlocked)
            return;

        if (leaderboardRoot != null)
            leaderboardRoot.SetActive(false);
        if (optionsRoot != null)
            optionsRoot.SetActive(false);
        if (mainMenuRoot != null)
            mainMenuRoot.SetActive(true);
    }

    private void SetLoginGateVisible(bool visible)
    {
        if (runtimeAuthLoginGatePanel != null)
        {
            runtimeAuthLoginGatePanel.SetActive(visible);
            if (visible)
                runtimeAuthLoginGatePanel.transform.SetAsLastSibling();
        }
    }

    private void SetNicknameGateVisible(bool visible)
    {
        if (runtimeNicknameGatePanel != null)
        {
            runtimeNicknameGatePanel.SetActive(visible);
            if (visible)
                runtimeNicknameGatePanel.transform.SetAsLastSibling();
        }
    }

    private void RefreshNicknameGateState()
    {
        bool visible = IsMenuLockedByNicknameGate();
        if (!visible)
        {
            nicknamePromptAccountId = string.Empty;
            SetNicknameGateVisible(false);
            return;
        }

        EnsureRuntimeNicknameGateControls();
        if (runtimeNicknameGatePanel == null)
            return;

        ExternalAuthSession session = ExternalAuthService.CurrentSession;
        string accountId = session?.account_id ?? string.Empty;
        if (!string.Equals(nicknamePromptAccountId, accountId, StringComparison.Ordinal))
        {
            nicknamePromptAccountId = accountId;
            if (runtimeNicknameInputField != null)
                runtimeNicknameInputField.text = BuildNicknameSuggestion(ExternalAuthService.CurrentDisplayName);
            if (runtimeNicknameGateStatusText != null)
                runtimeNicknameGateStatusText.text = $"Choose your in-game nickname ({NicknameMinLength}-{NicknameMaxLength} chars).";
        }

        SetNicknameGateVisible(true);
    }

    private void EnsureRuntimeNicknameGateControls()
    {
        if (runtimeNicknameGatePanel != null)
            return;

        Canvas parentCanvas = null;
        if (mainMenuRoot != null)
            parentCanvas = mainMenuRoot.GetComponentInParent<Canvas>();
        if (parentCanvas == null)
            parentCanvas = FindFirstObjectByType<Canvas>();
        if (parentCanvas == null)
            return;

        GameObject panelGo = new GameObject(
            "NicknameGatePanel",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image)
        );
        runtimeNicknameGatePanel = panelGo;
        RectTransform panelRt = panelGo.GetComponent<RectTransform>();
        panelRt.SetParent(parentCanvas.transform, false);
        panelRt.anchorMin = Vector2.zero;
        panelRt.anchorMax = Vector2.one;
        panelRt.pivot = new Vector2(0.5f, 0.5f);
        panelRt.offsetMin = Vector2.zero;
        panelRt.offsetMax = Vector2.zero;

        Image overlay = panelGo.GetComponent<Image>();
        overlay.color = new Color(0.01f, 0.02f, 0.05f, 0.88f);
        overlay.raycastTarget = true;

        GameObject cardGo = new GameObject(
            "NicknameGateCard",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image),
            typeof(VerticalLayoutGroup),
            typeof(ContentSizeFitter)
        );
        RectTransform cardRt = cardGo.GetComponent<RectTransform>();
        cardRt.SetParent(panelGo.transform, false);
        cardRt.anchorMin = new Vector2(0.5f, 0.5f);
        cardRt.anchorMax = new Vector2(0.5f, 0.5f);
        cardRt.pivot = new Vector2(0.5f, 0.5f);
        cardRt.sizeDelta = new Vector2(640f, 280f);
        cardRt.anchoredPosition = new Vector2(0f, 8f);

        Image cardImage = cardGo.GetComponent<Image>();
        cardImage.color = new Color(0.08f, 0.1f, 0.16f, 0.97f);

        VerticalLayoutGroup cardLayout = cardGo.GetComponent<VerticalLayoutGroup>();
        cardLayout.padding = new RectOffset(24, 24, 22, 22);
        cardLayout.spacing = 14f;
        cardLayout.childControlWidth = true;
        cardLayout.childControlHeight = true;
        cardLayout.childForceExpandWidth = false;
        cardLayout.childForceExpandHeight = false;

        ContentSizeFitter cardFitter = cardGo.GetComponent<ContentSizeFitter>();
        cardFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        cardFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        TMP_Text titleText = CreateAuthGateTitleText(cardGo.transform);
        titleText.text = "Choose your nickname";

        runtimeNicknameGateStatusText = CreateAuthStatusText(cardGo.transform);
        runtimeNicknameGateStatusText.text = $"Choose your in-game nickname ({NicknameMinLength}-{NicknameMaxLength} chars).";
        LayoutElement statusLayout = runtimeNicknameGateStatusText.GetComponent<LayoutElement>();
        if (statusLayout != null)
            statusLayout.preferredWidth = 580f;

        runtimeNicknameInputField = CreateNicknameInputField(cardGo.transform);
        if (runtimeNicknameInputField != null)
            runtimeNicknameInputField.characterLimit = NicknameMaxLength;

        runtimeNicknameConfirmButton = CreateLegalButton(cardGo.transform, "NicknameConfirmButton", "Save nickname");

        runtimeNicknameGatePanel.SetActive(false);
        runtimeNicknameGatePanel.transform.SetAsLastSibling();
    }

    private void OnNicknameConfirmClicked()
    {
        if (runtimeNicknameInputField == null)
            return;

        if (!TryNormalizeNicknameCandidate(
                runtimeNicknameInputField.text,
                out string normalizedNickname,
                out string validationMessage))
        {
            if (runtimeNicknameGateStatusText != null)
                runtimeNicknameGateStatusText.text = validationMessage;
            return;
        }

        PlayerIdentityService.SetDisplayName(normalizedNickname);
        if (runtimeNicknameGateStatusText != null)
            runtimeNicknameGateStatusText.text = "Nickname saved.";

        RefreshAuthStatusLabel();
    }

    private static TMP_InputField CreateNicknameInputField(Transform parent)
    {
        GameObject inputGo = new GameObject(
            "NicknameInput",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image),
            typeof(TMP_InputField),
            typeof(LayoutElement)
        );
        inputGo.transform.SetParent(parent, false);

        LayoutElement layout = inputGo.GetComponent<LayoutElement>();
        layout.preferredWidth = 580f;
        layout.preferredHeight = 46f;

        Image background = inputGo.GetComponent<Image>();
        background.color = new Color(0.06f, 0.08f, 0.12f, 0.95f);

        TMP_InputField inputField = inputGo.GetComponent<TMP_InputField>();
        inputField.lineType = TMP_InputField.LineType.SingleLine;
        inputField.contentType = TMP_InputField.ContentType.Standard;
        inputField.characterValidation = TMP_InputField.CharacterValidation.None;

        GameObject textAreaGo = new GameObject(
            "Text Area",
            typeof(RectTransform),
            typeof(RectMask2D)
        );
        RectTransform textAreaRt = textAreaGo.GetComponent<RectTransform>();
        textAreaRt.SetParent(inputGo.transform, false);
        textAreaRt.anchorMin = Vector2.zero;
        textAreaRt.anchorMax = Vector2.one;
        textAreaRt.offsetMin = new Vector2(12f, 8f);
        textAreaRt.offsetMax = new Vector2(-12f, -8f);

        GameObject placeholderGo = new GameObject(
            "Placeholder",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(TextMeshProUGUI)
        );
        RectTransform placeholderRt = placeholderGo.GetComponent<RectTransform>();
        placeholderRt.SetParent(textAreaGo.transform, false);
        placeholderRt.anchorMin = Vector2.zero;
        placeholderRt.anchorMax = Vector2.one;
        placeholderRt.offsetMin = Vector2.zero;
        placeholderRt.offsetMax = Vector2.zero;

        TMP_Text placeholderText = placeholderGo.GetComponent<TextMeshProUGUI>();
        placeholderText.text = "Enter nickname";
        placeholderText.fontSize = 22f;
        placeholderText.color = new Color(0.64f, 0.68f, 0.75f, 0.8f);
        placeholderText.alignment = TextAlignmentOptions.Left;
        placeholderText.raycastTarget = false;
        placeholderText.textWrappingMode = TextWrappingModes.NoWrap;
        if (TMP_Settings.defaultFontAsset != null)
            placeholderText.font = TMP_Settings.defaultFontAsset;

        GameObject inputTextGo = new GameObject(
            "Text",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(TextMeshProUGUI)
        );
        RectTransform inputTextRt = inputTextGo.GetComponent<RectTransform>();
        inputTextRt.SetParent(textAreaGo.transform, false);
        inputTextRt.anchorMin = Vector2.zero;
        inputTextRt.anchorMax = Vector2.one;
        inputTextRt.offsetMin = Vector2.zero;
        inputTextRt.offsetMax = Vector2.zero;

        TMP_Text inputText = inputTextGo.GetComponent<TextMeshProUGUI>();
        inputText.text = string.Empty;
        inputText.fontSize = 22f;
        inputText.color = new Color(0.9f, 0.94f, 0.99f, 0.98f);
        inputText.alignment = TextAlignmentOptions.Left;
        inputText.raycastTarget = false;
        inputText.textWrappingMode = TextWrappingModes.NoWrap;
        if (TMP_Settings.defaultFontAsset != null)
            inputText.font = TMP_Settings.defaultFontAsset;

        inputField.textViewport = textAreaRt;
        inputField.textComponent = inputText as TextMeshProUGUI;
        inputField.placeholder = placeholderText as Graphic;
        inputField.caretColor = new Color(0.96f, 0.98f, 1f, 0.98f);
        inputField.customCaretColor = true;

        return inputField;
    }

    private static string BuildNicknameSuggestion(string rawDisplayName)
    {
        string source = string.IsNullOrWhiteSpace(rawDisplayName) ? "Player" : rawDisplayName.Trim();
        int atSign = source.IndexOf('@');
        if (atSign > 0)
            source = source.Substring(0, atSign);

        StringBuilder builder = new(source.Length);
        for (int i = 0; i < source.Length; i++)
        {
            char c = source[i];
            if (IsNicknameCharacterAllowed(c))
                builder.Append(c);
        }

        string normalized = builder.ToString().Trim();
        if (normalized.Length > NicknameMaxLength)
            normalized = normalized.Substring(0, NicknameMaxLength).Trim();

        if (normalized.Length < NicknameMinLength)
            normalized = "Player";

        return normalized;
    }

    private static bool TryNormalizeNicknameCandidate(string raw, out string normalized, out string error)
    {
        normalized = string.Empty;
        error = $"Nickname must be {NicknameMinLength}-{NicknameMaxLength} characters.";

        if (string.IsNullOrWhiteSpace(raw))
        {
            error = "Nickname is required.";
            return false;
        }

        string trimmed = raw.Trim();
        if (trimmed.Length < NicknameMinLength || trimmed.Length > NicknameMaxLength)
            return false;

        for (int i = 0; i < trimmed.Length; i++)
        {
            if (!IsNicknameCharacterAllowed(trimmed[i]))
            {
                error = "Use only letters, numbers, spaces, '.', '-' or '_'.";
                return false;
            }
        }

        normalized = trimmed;
        error = string.Empty;
        return true;
    }

    private static bool IsNicknameCharacterAllowed(char c)
    {
        return char.IsLetterOrDigit(c)
            || c == ' '
            || c == '.'
            || c == '-'
            || c == '_';
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
        if (!EnsureMenuUnlockedByAuthGate())
            return;

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

    private void EnsureRuntimeAuthControls()
    {
        bool hasGoogle = !IsProviderLoginEnabled("google") || authLoginGoogleButton != null;
        bool hasMicrosoft = !IsProviderLoginEnabled("microsoft") || authLoginMicrosoftButton != null;
        bool hasFacebook = !IsProviderLoginEnabled("facebook") || authLoginFacebookButton != null;
        bool hasAccountUi = authStatusText != null && authLogoutButton != null;
        if (hasAccountUi && hasGoogle && hasMicrosoft && hasFacebook)
        {
            return;
        }

        Canvas parentCanvas = null;
        if (mainMenuRoot != null)
            parentCanvas = mainMenuRoot.GetComponentInParent<Canvas>();
        if (parentCanvas == null)
            parentCanvas = FindFirstObjectByType<Canvas>();
        if (parentCanvas == null)
            return;

        GameObject panelGo = new GameObject(
            "AuthPanel",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(VerticalLayoutGroup),
            typeof(ContentSizeFitter)
        );
        runtimeAuthPanel = panelGo;
        RectTransform panelRt = panelGo.GetComponent<RectTransform>();
        panelRt.SetParent(parentCanvas.transform, false);
        panelRt.anchorMin = new Vector2(0f, 0f);
        panelRt.anchorMax = new Vector2(0f, 0f);
        panelRt.pivot = new Vector2(0f, 0f);
        panelRt.anchoredPosition = new Vector2(16f, 58f);
        panelRt.sizeDelta = new Vector2(760f, 96f);

        VerticalLayoutGroup panelLayout = panelGo.GetComponent<VerticalLayoutGroup>();
        panelLayout.spacing = 6f;
        panelLayout.childControlWidth = true;
        panelLayout.childControlHeight = true;
        panelLayout.childForceExpandWidth = false;
        panelLayout.childForceExpandHeight = false;

        ContentSizeFitter fitter = panelGo.GetComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        if (authStatusText == null)
            authStatusText = CreateAuthStatusText(panelGo.transform);

        GameObject rowGo = new GameObject(
            "AuthButtonsRow",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(HorizontalLayoutGroup)
        );
        rowGo.transform.SetParent(panelGo.transform, false);

        HorizontalLayoutGroup rowLayout = rowGo.GetComponent<HorizontalLayoutGroup>();
        rowLayout.spacing = 8f;
        rowLayout.childControlWidth = true;
        rowLayout.childControlHeight = true;
        rowLayout.childForceExpandWidth = false;
        rowLayout.childForceExpandHeight = false;

        if (IsProviderLoginEnabled("google") && authLoginGoogleButton == null)
            authLoginGoogleButton = CreateLegalButton(rowGo.transform, "AuthGoogleButton", "Google");
        if (IsProviderLoginEnabled("microsoft") && authLoginMicrosoftButton == null)
            authLoginMicrosoftButton = CreateLegalButton(rowGo.transform, "AuthMicrosoftButton", "Microsoft");
        if (IsProviderLoginEnabled("facebook") && authLoginFacebookButton == null)
            authLoginFacebookButton = CreateLegalButton(rowGo.transform, "AuthFacebookButton", "Facebook");
        if (authLogoutButton == null)
            authLogoutButton = CreateLegalButton(rowGo.transform, "AuthLogoutButton", "Logout");
    }

    private static TMP_Text CreateAuthStatusText(Transform parent)
    {
        GameObject textGo = new GameObject(
            "AuthStatusText",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(TextMeshProUGUI),
            typeof(LayoutElement)
        );
        textGo.transform.SetParent(parent, false);

        LayoutElement layout = textGo.GetComponent<LayoutElement>();
        layout.preferredWidth = 760f;
        layout.preferredHeight = 28f;

        TMP_Text text = textGo.GetComponent<TextMeshProUGUI>();
        text.text = "External auth: not initialized.";
        text.fontSize = 16f;
        text.color = new Color(0.82f, 0.86f, 0.9f, 0.92f);
        text.alignment = TextAlignmentOptions.Left;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.raycastTarget = false;

        if (TMP_Settings.defaultFontAsset != null)
            text.font = TMP_Settings.defaultFontAsset;

        return text;
    }

    private void EnsureRuntimeLoginGateControls()
    {
        if (runtimeAuthLoginGatePanel != null)
            return;

        Canvas parentCanvas = null;
        if (mainMenuRoot != null)
            parentCanvas = mainMenuRoot.GetComponentInParent<Canvas>();
        if (parentCanvas == null)
            parentCanvas = FindFirstObjectByType<Canvas>();
        if (parentCanvas == null)
            return;

        GameObject panelGo = new GameObject(
            "AuthLoginGatePanel",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image)
        );
        runtimeAuthLoginGatePanel = panelGo;
        RectTransform panelRt = panelGo.GetComponent<RectTransform>();
        panelRt.SetParent(parentCanvas.transform, false);
        panelRt.anchorMin = Vector2.zero;
        panelRt.anchorMax = Vector2.one;
        panelRt.pivot = new Vector2(0.5f, 0.5f);
        panelRt.offsetMin = Vector2.zero;
        panelRt.offsetMax = Vector2.zero;

        Image overlay = panelGo.GetComponent<Image>();
        overlay.color = new Color(0.01f, 0.02f, 0.05f, 0.84f);
        overlay.raycastTarget = true;

        GameObject cardGo = new GameObject(
            "AuthLoginGateCard",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image),
            typeof(VerticalLayoutGroup),
            typeof(ContentSizeFitter)
        );
        RectTransform cardRt = cardGo.GetComponent<RectTransform>();
        cardRt.SetParent(panelGo.transform, false);
        cardRt.anchorMin = new Vector2(0.5f, 0.5f);
        cardRt.anchorMax = new Vector2(0.5f, 0.5f);
        cardRt.pivot = new Vector2(0.5f, 0.5f);
        cardRt.sizeDelta = new Vector2(620f, 220f);
        cardRt.anchoredPosition = new Vector2(0f, 12f);

        Image cardImage = cardGo.GetComponent<Image>();
        cardImage.color = new Color(0.07f, 0.1f, 0.16f, 0.96f);

        VerticalLayoutGroup cardLayout = cardGo.GetComponent<VerticalLayoutGroup>();
        cardLayout.padding = new RectOffset(24, 24, 22, 22);
        cardLayout.spacing = 12f;
        cardLayout.childControlWidth = true;
        cardLayout.childControlHeight = true;
        cardLayout.childForceExpandWidth = false;
        cardLayout.childForceExpandHeight = false;

        ContentSizeFitter cardFitter = cardGo.GetComponent<ContentSizeFitter>();
        cardFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        cardFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        TMP_Text titleText = CreateAuthGateTitleText(cardGo.transform);
        titleText.text = "Sign in to continue";

        runtimeAuthLoginGateStatusText = CreateAuthStatusText(cardGo.transform);
        runtimeAuthLoginGateStatusText.text = "Choose a provider to sign in.";
        LayoutElement statusLayout = runtimeAuthLoginGateStatusText.GetComponent<LayoutElement>();
        if (statusLayout != null)
            statusLayout.preferredWidth = 560f;

        GameObject rowGo = new GameObject(
            "AuthLoginGateButtons",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(HorizontalLayoutGroup)
        );
        rowGo.transform.SetParent(cardGo.transform, false);

        HorizontalLayoutGroup rowLayout = rowGo.GetComponent<HorizontalLayoutGroup>();
        rowLayout.spacing = 10f;
        rowLayout.childControlWidth = true;
        rowLayout.childControlHeight = true;
        rowLayout.childForceExpandWidth = false;
        rowLayout.childForceExpandHeight = false;

        if (IsProviderLoginEnabled("google"))
            runtimeAuthLoginGateGoogleButton = CreateLegalButton(rowGo.transform, "GateGoogle", "Google");
        if (IsProviderLoginEnabled("microsoft"))
            runtimeAuthLoginGateMicrosoftButton = CreateLegalButton(rowGo.transform, "GateMicrosoft", "Microsoft");
        if (IsProviderLoginEnabled("facebook"))
            runtimeAuthLoginGateFacebookButton = CreateLegalButton(rowGo.transform, "GateFacebook", "Facebook");

        runtimeAuthLoginGatePanel.SetActive(false);
        runtimeAuthLoginGatePanel.transform.SetAsLastSibling();
    }

    private static bool IsProviderLoginEnabled(string provider)
    {
        return ExternalAuthSettings.IsProviderLoginEnabled(provider);
    }

    private bool HasAnyInteractiveAuthProviderEnabled()
    {
        return IsProviderLoginEnabled("google")
            || IsProviderLoginEnabled("microsoft")
            || IsProviderLoginEnabled("facebook");
    }

    private void ApplyProviderButtonAvailability()
    {
        if (!IsProviderLoginEnabled("google"))
        {
            if (authLoginGoogleButton != null)
                authLoginGoogleButton.gameObject.SetActive(false);
            if (runtimeAuthLoginGateGoogleButton != null)
                runtimeAuthLoginGateGoogleButton.gameObject.SetActive(false);
        }

        if (!IsProviderLoginEnabled("microsoft"))
        {
            if (authLoginMicrosoftButton != null)
                authLoginMicrosoftButton.gameObject.SetActive(false);
            if (runtimeAuthLoginGateMicrosoftButton != null)
                runtimeAuthLoginGateMicrosoftButton.gameObject.SetActive(false);
        }

        if (!IsProviderLoginEnabled("facebook"))
        {
            if (authLoginFacebookButton != null)
                authLoginFacebookButton.gameObject.SetActive(false);
            if (runtimeAuthLoginGateFacebookButton != null)
                runtimeAuthLoginGateFacebookButton.gameObject.SetActive(false);
        }
    }

    private void HideInteractiveProviderButtons()
    {
        if (authLoginGoogleButton != null)
            authLoginGoogleButton.gameObject.SetActive(false);
        if (authLoginMicrosoftButton != null)
            authLoginMicrosoftButton.gameObject.SetActive(false);
        if (authLoginFacebookButton != null)
            authLoginFacebookButton.gameObject.SetActive(false);
        if (runtimeAuthLoginGateGoogleButton != null)
            runtimeAuthLoginGateGoogleButton.gameObject.SetActive(false);
        if (runtimeAuthLoginGateMicrosoftButton != null)
            runtimeAuthLoginGateMicrosoftButton.gameObject.SetActive(false);
        if (runtimeAuthLoginGateFacebookButton != null)
            runtimeAuthLoginGateFacebookButton.gameObject.SetActive(false);
    }

    private static TMP_Text CreateAuthGateTitleText(Transform parent)
    {
        GameObject textGo = new GameObject(
            "AuthGateTitle",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(TextMeshProUGUI),
            typeof(LayoutElement)
        );
        textGo.transform.SetParent(parent, false);

        LayoutElement layout = textGo.GetComponent<LayoutElement>();
        layout.preferredWidth = 560f;
        layout.preferredHeight = 44f;

        TMP_Text text = textGo.GetComponent<TextMeshProUGUI>();
        text.fontSize = 34f;
        text.color = new Color(0.94f, 0.96f, 0.99f, 0.98f);
        text.alignment = TextAlignmentOptions.Center;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Overflow;
        text.raycastTarget = false;

        if (TMP_Settings.defaultFontAsset != null)
            text.font = TMP_Settings.defaultFontAsset;

        return text;
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
