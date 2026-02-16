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

    [Header("Fade")]
    public float fadeDuration = 1.5f;

    [Header("Typography")]
    [SerializeField] private float characterSpacing = 2f;
    [SerializeField] private float wordSpacing = 0f;

    private bool shown;

    private void Awake()
    {
        if (background != null)
            background.color = new Color(0, 0, 0, 0);

        if (panel != null)
            panel.SetActive(false);

        ApplyTypography();
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
        RefreshLeaderboard();
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

    private void RefreshLeaderboard()
    {
        if (leaderboardEntries == null)
            return;

        var entries = LocalLeaderboardService.GetTopEntries(leaderboardEntries.Length);

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
        gameObject.SetActive(false);
    }

    private void OnEnable()
    {
        StartCoroutine(FadeIn());
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
}
