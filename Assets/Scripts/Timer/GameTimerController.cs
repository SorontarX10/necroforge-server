using UnityEngine;
using System;
using GrassSim.Upgrades;

public class GameTimerController : MonoBehaviour
{
    public static GameTimerController Instance { get; private set; }

    [Header("Timer")]
    public float elapsedTime { get; private set; }

    [Header("Phase thresholds (seconds)")]
    public float phaseBTime = 300f; // X
    public float phaseCTime = 600f; // X
    public float endGameTime = 900f; // Z
    [Header("Run End Settings")]
    [SerializeField] private bool endRunOnTimeLimit;
    [SerializeField] private bool switchToEndPhaseAtTimeLimit = true;

    public UpgradeLibrary upgradeLibrary;

    public enum TimerPhase
    {
        PhaseA,
        PhaseB,
        PhaseC,
        End
    }

    public TimerPhase CurrentPhase { get; private set; } = TimerPhase.PhaseA;

    public event Action<float> OnTimerTick;
    public event Action<TimerPhase> OnPhaseChanged;
    public event Action OnGameEnded;

    public bool gameEnded;

    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        if (GetComponent<BossEncounterController>() == null)
            gameObject.AddComponent<BossEncounterController>();
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    private void Start()
    {
        if (upgradeLibrary == null)
        {
            Debug.LogError("[GameTimerController] UpgradeLibrary NOT assigned!");
            return;
        }

        if (UpgradeWeightRuntime.Instance == null)
        {
            Debug.LogError("[GameTimerController] UpgradeWeightRuntime.Instance == null");
            return;
        }

        UpgradeWeightRuntime.Instance.InitializeFromLibrary(upgradeLibrary);
    }

    private void Update()
    {
        if (gameEnded) return;

        elapsedTime += Time.deltaTime;
        OnTimerTick?.Invoke(elapsedTime);

        

        UpdatePhase();
    }

    private void UpdatePhase()
    {
        if (elapsedTime >= endGameTime)
        {
            if (switchToEndPhaseAtTimeLimit && CurrentPhase != TimerPhase.End)
            {
                CurrentPhase = TimerPhase.End;
                OnPhaseChanged?.Invoke(CurrentPhase);
            }

            if (endRunOnTimeLimit && !gameEnded)
            {
                gameEnded = true;
                OnGameEnded?.Invoke();
            }

            return;
        }

        if (elapsedTime >= phaseBTime && CurrentPhase == TimerPhase.PhaseA)
        {
            CurrentPhase = TimerPhase.PhaseB;
            OnPhaseChanged?.Invoke(CurrentPhase);
        }

        if (elapsedTime >= phaseCTime && CurrentPhase == TimerPhase.PhaseB)
        {
            CurrentPhase = TimerPhase.PhaseC;
            OnPhaseChanged?.Invoke(CurrentPhase);
        }
    }

    public void ResetTimer()
    {
        elapsedTime = 0f;
        gameEnded = false;
        CurrentPhase = TimerPhase.PhaseA;

        // odśwież UI i muzykę od razu
        OnTimerTick?.Invoke(elapsedTime);
        OnPhaseChanged?.Invoke(CurrentPhase);
    }
}
