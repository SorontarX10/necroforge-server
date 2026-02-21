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

    [Header("Version")]
    [SerializeField] private TMP_Text versionText;
    [SerializeField] private string versionPrefix = "v";

    [Header("Main Buttons")]
    public Button newGameButton;

    [Header("Leaderboard")]
    public TMP_Text[] leaderboardEntries;

    public float clickDelay = 0.3f;

    void Awake()
    {
        // Start: main menu visible, secondary panels hidden.
        if (mainMenuRoot) mainMenuRoot.SetActive(true);
        if (leaderboardRoot) leaderboardRoot.SetActive(false);
        if (optionsRoot) optionsRoot.SetActive(false);

        ResetMainMenuCameraStack();
        ApplyVersionLabel();
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
