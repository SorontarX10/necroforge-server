using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;
using GrassSim.Core;
using GrassSim.Telemetry;
using GrassSim.UI;

public class PauseMenuController : MonoBehaviour
{
    public static PauseMenuController Instance { get; private set; }
    public bool IsPaused => isPaused;

    [Header("Roots")]
    public GameObject pauseRoot;
    public GameObject optionsRoot;

    bool isPaused = false;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        if (pauseRoot != null)
            pauseRoot.SetActive(false);

        if (optionsRoot) optionsRoot.SetActive(false);
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    void Update()
    {
        if (GameTimerController.Instance != null &&
            GameTimerController.Instance.gameEnded)
            return;

        bool togglePressed = false;
        if (Keyboard.current != null)
        {
#if UNITY_EDITOR
            togglePressed =
                Keyboard.current.pKey.wasPressedThisFrame
                || Keyboard.current.escapeKey.wasPressedThisFrame;
#else
            togglePressed = Keyboard.current.escapeKey.wasPressedThisFrame;
#endif
        }

        if (togglePressed)
        {
            if (!isPaused)
                Pause();
            else
                Resume();
        }
    }

    public void Pause()
    {
        isPaused = true;
        Time.timeScale = 0f;

        if (pauseRoot != null)
            pauseRoot.SetActive(true);

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    public void Resume()
    {
        isPaused = false;
        Time.timeScale = 1f;

        if (pauseRoot != null)
            pauseRoot.SetActive(false);
        if (optionsRoot) optionsRoot.SetActive(false);

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    public void OpenOptions()
    {
        if (optionsRoot)
            optionsRoot.SetActive(true);
    }

    public void CloseOptions()
    {
        if (optionsRoot)
            optionsRoot.SetActive(false);
    }

    public void QuitToMainMenu()
    {
        PrepareForMainMenuTransition();
        SceneManager.LoadScene("MainMenu");
    }

    public static void PrepareForMainMenuTransition()
    {
        if (ShouldReportQuitToMainMenu())
        {
            GameplayTelemetryHub.ReportRunExitRequested(
                new GameplayTelemetryHub.RunExitSample(GetRunTimeSeconds(), "quit_to_main_menu")
            );
        }

        Time.timeScale = 1f;
        AudioListener.pause = false;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        ChoiceUiQueue.Clear();
        PlayerLocator.Invalidate();
        MapCollectibleRegistry.ResetRuntimeState();

        var music = FindFirstObjectByType<MusicPhaseController>();
        if (music != null)
            music.StopAllMusic();

        StopAllSceneAudioSources();
        CleanupPersistentRuntimeObjects();
        AudioUtils.StopAllAndReset();
    }

    private static void CleanupPersistentRuntimeObjects()
    {
        GlobalLoadingCamera loadingCamera = GlobalLoadingCamera.Instance;
        if (loadingCamera != null)
            loadingCamera.Shutdown();

        HordeAISystem horde = Object.FindFirstObjectByType<HordeAISystem>();
        if (horde != null)
            Object.Destroy(horde.gameObject);

        BossHealthBarUI.ResetSharedCanvas();
    }

    private static void StopAllSceneAudioSources()
    {
        AudioSource[] sources = Object.FindObjectsByType<AudioSource>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None
        );

        for (int i = 0; i < sources.Length; i++)
        {
            AudioSource source = sources[i];
            if (source == null)
                continue;

            source.Stop();
            source.clip = null;
        }
    }

    private static float GetRunTimeSeconds()
    {
        if (GameTimerController.Instance != null)
            return Mathf.Max(0f, GameTimerController.Instance.elapsedTime);

        return 0f;
    }

    private static bool ShouldReportQuitToMainMenu()
    {
        string activeScene = SceneManager.GetActiveScene().name;
        if (!string.Equals(activeScene, "Game", System.StringComparison.OrdinalIgnoreCase))
            return false;

        return GameTimerController.Instance != null && !GameTimerController.Instance.gameEnded;
    }
}
