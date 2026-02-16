using UnityEngine;

public class GameOverController : MonoBehaviour
{
    public GameOverUIController gameOverUI;

    private bool ended;

    public void OnPlayerDied()
    {
        if (ended) return;
        EndGame();
    }

    private void OnTimeEnded()
    {
        if (ended) return;
        EndGame();
    }

    private void EndGame()
    {
        ended = true;

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        Time.timeScale = 0f;

        var stats = GameRunStats.Collect();
        gameOverUI.Show(stats);
    }

    private void OnEnable()
    {
        ended = false;

        if (GameTimerController.Instance != null)
            GameTimerController.Instance.OnGameEnded += OnTimeEnded;
    }

    private void OnDisable()
    {
        if (GameTimerController.Instance != null)
            GameTimerController.Instance.OnGameEnded -= OnTimeEnded;
    }

    private void OnDestroy()
    {
        if (GameTimerController.Instance != null)
            GameTimerController.Instance.OnGameEnded -= OnTimeEnded;
    }
}
