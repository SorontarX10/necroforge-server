using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;
using GrassSim.Core;
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

#if UNITY_EDITOR
        if (Keyboard.current != null && Keyboard.current.pKey.wasPressedThisFrame)
#else
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
#endif
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
        Time.timeScale = 1f;
        AudioListener.pause = false;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        ChoiceUiQueue.Clear();
        PlayerLocator.Invalidate();

        var music = FindFirstObjectByType<MusicPhaseController>();
        if (music != null)
            music.StopAllMusic();

        AudioUtils.StopAllAndReset();
    }
}
