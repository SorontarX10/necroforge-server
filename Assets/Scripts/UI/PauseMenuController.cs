using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;

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

        pauseRoot.SetActive(false);
        if (optionsRoot) optionsRoot.SetActive(false);
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

        pauseRoot.SetActive(true);

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    public void Resume()
    {
        isPaused = false;
        Time.timeScale = 1f;

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
        Time.timeScale = 1f;

        var music = FindFirstObjectByType<MusicPhaseController>();
        if (music != null)
            music.StopAllMusic();

        SceneManager.LoadScene("MainMenu");
    }
}
