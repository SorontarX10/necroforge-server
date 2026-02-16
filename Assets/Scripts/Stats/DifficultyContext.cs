using UnityEngine;
using GrassSim.Stats;

public static class DifficultyContext
{
    private const float SpawnDensityMultiplier = 0.9f;

    public static int Difficulty
    {
        get
        {
            if (WorldStats.Instance == null)
                return 1;

            return Mathf.Max(1, WorldStats.Instance.difficulty);
        }
    }

    private static float RunSeconds
    {
        get
        {
            if (GameTimerController.Instance == null)
                return 0f;

            return Mathf.Max(0f, GameTimerController.Instance.elapsedTime);
        }
    }

    // 1 at run start, 0 after ~2.5 minutes.
    private static float OpeningPressure01 =>
        1f - Mathf.Clamp01(RunSeconds / 150f);

    // 0 before 5:00, 1 at 10:00+.
    private static float LateSnowball01 =>
        Mathf.Clamp01((RunSeconds - 300f) / 300f);

    // --- Multipliers ---

    public static float EnemyHealthMultiplier =>
        (1f + 0.07f * (Difficulty - 1)) *
        (1f + 0.10f * OpeningPressure01);

    public static float EnemyDamageMultiplier =>
        (1f + 0.06f * (Difficulty - 1)) *
        (1f + 0.2f * OpeningPressure01);

    public static float EnemyAttackSpeedMultiplier =>
        (1f + 0.045f * (Difficulty - 1)) *
        (1f + 0.1f * OpeningPressure01);

    public static float ExpMultiplier
    {
        get
        {
            float earlyPenalty = Mathf.Lerp(0.85f, 1f, Mathf.Clamp01(RunSeconds / 120f));
            float baseMul = 1f + 0.13f * (Difficulty - 1);
            float lateBonus = 0.18f * LateSnowball01;
            return (baseMul + lateBonus) * earlyPenalty;
        }
    }

    public static float EnemyDetectionRangeMultiplier =>
        (1f + 0.07f * (Difficulty - 1)) *
        (1f + 0.26f * OpeningPressure01);

    public static float EnemyMoveSpeedMultiplier =>
        (1f + 0.05f * (Difficulty - 1)) *
        (1f + 0.2f * OpeningPressure01);

    // Spawn cap scaling.
    public static int ScaleSpawnCap(int baseCap)
    {
        if (baseCap <= 0)
            return 0;

        float diffScale = 1f + 0.07f * (Difficulty - 1) + 0.04f;
        float openingBoost = 1f + 0.14f * OpeningPressure01;
        float lateFlatten = Mathf.Lerp(1f, 0.9f, LateSnowball01);
        float mul = diffScale * openingBoost * lateFlatten * SpawnDensityMultiplier;

        return Mathf.Max(baseCap, Mathf.RoundToInt(baseCap * mul));
    }
}
