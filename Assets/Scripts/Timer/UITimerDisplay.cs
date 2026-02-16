using UnityEngine;
using TMPro;

public class UITimerDisplay : MonoBehaviour
{
    [SerializeField] private TMP_Text timerText;

    private void Start()
    {
        GameTimerController.Instance.OnTimerTick += UpdateTimer;
    }

    private void OnDestroy()
    {
        if (GameTimerController.Instance != null)
            GameTimerController.Instance.OnTimerTick -= UpdateTimer;
    }

    private void UpdateTimer(float time)
    {
        int minutes = Mathf.FloorToInt(time / 60f);
        int seconds = Mathf.FloorToInt(time % 60f);
        timerText.text = $"{minutes:00}:{seconds:00}";
    }
}
