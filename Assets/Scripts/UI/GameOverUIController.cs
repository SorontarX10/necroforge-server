using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using UnityEngine.SceneManagement;
using GrassSim.Upgrades;

public class GameOverUIController : MonoBehaviour
{
    [Header("UI")]
    public Image background;
    public GameObject panel;

    public TMP_Text finalScoreText;
    public TMP_Text killsText;
    public TMP_Text timeText;

    public TMP_Text[] leaderboardEntries;
    public TMP_Text leaderboardStatusText;
    public TMP_Text leaderboardMyRankText;
    public Button leaderboardRetryButton;

    [Header("Fade")]
    public float fadeDuration = 1.5f;

    [Header("Typography")]
    [SerializeField] private float characterSpacing = 2f;
    [SerializeField] private float wordSpacing = 0f;

    private bool shown;
    private Coroutine leaderboardRoutine;
    private GameRunStats lastRunStats;
    private bool hasPendingSubmit;

    private void Awake()
    {
        if (background != null)
            background.color = new Color(0, 0, 0, 0);

        if (panel != null)
            panel.SetActive(false);

        ApplyTypography();
        SetupLeaderboardUiBindings();
    }

    public void Show(GameRunStats stats)
    {
        if (shown)
            return;

        shown = true;
        gameObject.SetActive(true);
        if (panel != null)
            panel.SetActive(true);

        ApplyTypography();

        if (finalScoreText != null)
            finalScoreText.text = $"FINAL SCORE: {stats.finalScore}";
        if (killsText != null)
            killsText.text = $"Kills: {stats.kills}";
        if (timeText != null)
            timeText.text = $"Time Survived: {FormatTime(stats.timeSurvived)}";

        LocalLeaderboardService.SaveScore(stats.finalScore);
        lastRunStats = stats;
        hasPendingSubmit = true;
        if (leaderboardRoutine != null)
            StopCoroutine(leaderboardRoutine);
        leaderboardRoutine = StartCoroutine(SubmitAndRefreshLeaderboardRoutine());
    }

    private IEnumerator FadeIn()
    {
        float t = 0f;
        while (t < fadeDuration)
        {
            t += Time.unscaledDeltaTime;
            if (background != null)
                background.color = new Color(0, 0, 0, t / fadeDuration);
            yield return null;
        }
        if (background != null)
            background.color = new Color(0, 0, 0, 1);
    }

    private IEnumerator SubmitAndRefreshLeaderboardRoutine()
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

        string submitStatus = string.Empty;
        if (hasPendingSubmit)
        {
            OnlineLeaderboardApiClient.SubmitRunResult submitResult = null;
            yield return OnlineLeaderboardApiClient.SubmitRun(lastRunStats, result => { submitResult = result; });
            if (submitResult != null && submitResult.success)
            {
                hasPendingSubmit = false;
                submitStatus = DescribeSubmitStatus(submitResult);
            }
            else
            {
                string submitError = submitResult?.error ?? "unknown_error";
                Debug.LogWarning($"[Leaderboard] Online submit failed: {submitError}");
                submitStatus = "Online submit failed. Local score saved.";
            }
        }

        OnlineLeaderboardApiClient.FetchTopResult fetchResult = null;
        yield return OnlineLeaderboardApiClient.FetchTopEntries(
            leaderboardEntries != null ? leaderboardEntries.Length : 10,
            result => { fetchResult = result; }
        );

        if (fetchResult != null && fetchResult.success)
        {
            ApplyOnlineEntries(fetchResult.entries);
            if (fetchResult.isStale)
            {
                SetStatus(ComposeStatus(
                    submitStatus,
                    $"Online unavailable ({SanitizeError(fetchResult.error)}). Showing last synced leaderboard."
                ));
                SetRetryVisible(true);
            }
            else
            {
                string syncStatus = hasPendingSubmit
                    ? "Online synced, but submit is still pending."
                    : fetchResult.entries.Count > 0
                        ? "Online leaderboard synced."
                        : "Online leaderboard synced. No online scores yet.";
                SetStatus(ComposeStatus(submitStatus, syncStatus));
                SetRetryVisible(hasPendingSubmit);
            }
        }
        else
        {
            string fetchError = fetchResult?.error ?? "unknown_error";
            ApplyLocalEntries();
            SetStatus(ComposeStatus(
                submitStatus,
                $"Online unavailable ({SanitizeError(fetchError)}). Showing local leaderboard."
            ));
            SetMyRank("Your rank: offline");
            SetRetryVisible(true);
            yield break;
        }

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
    }

    private void ApplyLocalEntries()
    {
        if (leaderboardEntries == null)
            return;

        LocalLeaderboardService.Entry[] entries = LocalLeaderboardService.GetTopEntries(leaderboardEntries.Length);

        for (int i = 0; i < leaderboardEntries.Length; i++)
        {
            if (i < entries.Length)
            {
                leaderboardEntries[i].text =
                    $"{i + 1}. {entries[i].score}  ({entries[i].date})";
            }
            else
            {
                leaderboardEntries[i].text = $"{i + 1}. ---";
            }
        }
    }

    private void ApplyOnlineEntries(System.Collections.Generic.List<OnlineLeaderboardApiClient.LeaderboardEntry> entries)
    {
        if (leaderboardEntries == null)
            return;

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

    private string FormatTime(float t)
    {
        int m = Mathf.FloorToInt(t / 60f);
        int s = Mathf.FloorToInt(t % 60f);
        return $"{m:00}:{s:00}";
    }

    public void RestartRun()
    {
        Time.timeScale = 1f;

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        
        UpgradeWeightRuntime.Instance.ResetWeights();

        SceneManager.LoadScene("Loading");
    }

    public void ResetUI()
    {
        shown = false;
        hasPendingSubmit = false;
        lastRunStats = default;
        SetStatus(string.Empty);
        SetMyRank(string.Empty);
        SetRetryVisible(false);
        gameObject.SetActive(false);
    }

    private void OnEnable()
    {
        StartCoroutine(FadeIn());
    }

    private void OnDestroy()
    {
        if (leaderboardRetryButton != null)
            leaderboardRetryButton.onClick.RemoveListener(OnLeaderboardRetryClicked);
    }

    public void OnLeaderboardRetryClicked()
    {
        if (!shown)
            return;

        if (leaderboardRoutine != null)
            StopCoroutine(leaderboardRoutine);

        leaderboardRoutine = StartCoroutine(SubmitAndRefreshLeaderboardRoutine());
    }

    private void ApplyTypography()
    {
        ApplyTypographyTo(finalScoreText);
        ApplyTypographyTo(killsText);
        ApplyTypographyTo(timeText);

        if (leaderboardEntries == null)
            return;

        for (int i = 0; i < leaderboardEntries.Length; i++)
            ApplyTypographyTo(leaderboardEntries[i]);
    }

    private void ApplyTypographyTo(TMP_Text text)
    {
        if (text == null)
            return;

        text.characterSpacing = characterSpacing;
        text.wordSpacing = wordSpacing;
    }

    private void SetupLeaderboardUiBindings()
    {
        if (leaderboardRetryButton != null)
        {
            leaderboardRetryButton.onClick.RemoveListener(OnLeaderboardRetryClicked);
            leaderboardRetryButton.onClick.AddListener(OnLeaderboardRetryClicked);
            leaderboardRetryButton.gameObject.SetActive(false);
        }
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

    private static string ComposeStatus(string prefix, string suffix)
    {
        bool hasPrefix = !string.IsNullOrWhiteSpace(prefix);
        bool hasSuffix = !string.IsNullOrWhiteSpace(suffix);
        if (hasPrefix && hasSuffix)
            return $"{prefix} {suffix}";
        if (hasPrefix)
            return prefix;
        return hasSuffix ? suffix : string.Empty;
    }

    private static string DescribeSubmitStatus(OnlineLeaderboardApiClient.SubmitRunResult submitResult)
    {
        if (submitResult == null || !submitResult.success)
            return string.Empty;

        switch (submitResult.validationState)
        {
            case "accepted":
                return string.Empty;
            case "manual_review":
                return "Run submitted for review.";
            case "shadow_banned":
                return "Run submitted but hidden from the public leaderboard.";
            case "rejected":
                return "Run rejected by leaderboard validation. Local score saved.";
            default:
                return $"Run submitted with state: {SanitizeError(submitResult.validationState)}.";
        }
    }
}
