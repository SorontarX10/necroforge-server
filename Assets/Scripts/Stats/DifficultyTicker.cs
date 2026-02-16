using UnityEngine;
using GrassSim.Stats;

public class DifficultyTicker : MonoBehaviour
{
    [Header("Legacy Tick Scaling")]
    [Tooltip("Used only when adaptive curve is disabled.")]
    public float tickInterval = 10f;

    [Tooltip("Used only when adaptive curve is disabled.")]
    public int difficultyStep = 1;

    [Header("Adaptive Curve (Recommended)")]
    [Tooltip("Computes difficulty from run time and ignores static inspector jumps.")]
    public bool useAdaptiveCurve = true;
    [Min(1)] public int minimumDifficulty = 1;
    [Min(0.01f)] public float earlyDiffPerMinute = 2.0f;  // 0-2 min
    [Min(0.01f)] public float midDiffPerMinute = 1.0f;    // 2-5 min
    [Min(0.01f)] public float lateDiffPerMinute = 0.65f;  // 5-10 min
    [Min(0.01f)] public float endDiffPerMinute = 0.45f;   // 10+ min

    [Header("Adaptive Breakpoints (seconds)")]
    [Min(1f)] public float earlyPhaseEnd = 120f;
    [Min(1f)] public float midPhaseEnd = 300f;
    [Min(1f)] public float latePhaseEnd = 600f;

    private float timer;
    private float fallbackElapsed;

    private void Update()
    {
        if (WorldStats.Instance == null)
            return;

        if (useAdaptiveCurve)
        {
            int target = EvaluateAdaptiveDifficulty();
            int clamped = Mathf.Max(minimumDifficulty, target);
            if (WorldStats.Instance.difficulty != clamped)
            {
                WorldStats.Instance.difficulty = clamped;
                WorldStats.Instance.NotifyChanged();
            }

            return;
        }

        timer += Time.deltaTime;
        if (timer < tickInterval)
            return;

        timer -= tickInterval;
        TickLegacy();
    }

    private int EvaluateAdaptiveDifficulty()
    {
        float elapsed = GetElapsedTime();
        float diff = minimumDifficulty;

        float e1 = Mathf.Max(0.01f, earlyPhaseEnd);
        float e2 = Mathf.Max(e1 + 0.01f, midPhaseEnd);
        float e3 = Mathf.Max(e2 + 0.01f, latePhaseEnd);

        float earlyDuration = Mathf.Clamp(elapsed, 0f, e1);
        float midDuration = Mathf.Clamp(elapsed, e1, e2) - e1;
        float lateDuration = Mathf.Clamp(elapsed, e2, e3) - e2;
        float endDuration = Mathf.Max(0f, elapsed - e3);

        diff += earlyDuration * (earlyDiffPerMinute / 60f);
        diff += midDuration * (midDiffPerMinute / 60f);
        diff += lateDuration * (lateDiffPerMinute / 60f);
        diff += endDuration * (endDiffPerMinute / 60f);

        return Mathf.RoundToInt(diff);
    }

    private float GetElapsedTime()
    {
        if (GameTimerController.Instance != null)
            return Mathf.Max(0f, GameTimerController.Instance.elapsedTime);

        fallbackElapsed += Time.deltaTime;
        return fallbackElapsed;
    }

    private void TickLegacy()
    {
        WorldStats.Instance.difficulty += difficultyStep;
        if (WorldStats.Instance.difficulty < minimumDifficulty)
            WorldStats.Instance.difficulty = minimumDifficulty;

        WorldStats.Instance.NotifyChanged();
    }
}
