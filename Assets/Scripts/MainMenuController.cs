using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using UnityEngine.Rendering.Universal;

public class MainMenuController : MonoBehaviour
{
    [Header("Roots")]
    public GameObject mainMenuRoot;
    public GameObject leaderboardRoot;
    public GameObject optionsRoot;

    [Header("Main Buttons")]
    public Button newGameButton;

    [Header("Leaderboard")]
    public TMP_Text[] leaderboardEntries;

    public float clickDelay = 0.3f;

    void Awake()
    {
        // 🔒 Start: menu główne widoczne, leaderboard ukryty
        if (mainMenuRoot) mainMenuRoot.SetActive(true);
        if (leaderboardRoot) leaderboardRoot.SetActive(false);
        if (leaderboardRoot) optionsRoot.SetActive(false);
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

        // 🔥 CRITICAL: reset URP camera stack in Main Menu
        Camera cam = Camera.main;
        if (cam != null)
        {
            var additionalData = cam.GetUniversalAdditionalCameraData();
            if (additionalData != null && additionalData.cameraStack != null)
            {
                additionalData.cameraStack.Clear();
            }
        }

        SceneManager.LoadScene("Loading");
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

    void RefreshLeaderboard()
    {
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
}
