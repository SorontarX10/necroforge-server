using UnityEngine;
using UnityEngine.SceneManagement;

public class QuitToMainMenu : MonoBehaviour
{
    public void Quit()
    {
        // ⛔ ZATRZYMAJ MUZYKĘ
        var music = FindFirstObjectByType<MusicPhaseController>();
        if (music != null)
        {
            Destroy(music.gameObject);
        }

        // przywróć czas i kursor
        Time.timeScale = 1f;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        SceneManager.LoadScene("MainMenu");
    }
}
