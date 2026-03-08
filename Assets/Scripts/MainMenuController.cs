using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;
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
    }

    private void OnDestroy()
    {
        if (leaderboardRetryButton != null)
            leaderboardRetryButton.onClick.RemoveListener(OnLeaderboardRetryClicked);
        if (leaderboardOverlayButton != null)
            leaderboardOverlayButton.onClick.RemoveListener(OnLeaderboardOverlayClicked);
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
}
