using UnityEngine;

public class ZombieDifficultyDetectionScaler : MonoBehaviour
{
    public float baseDetectionRadius;
    public float baseMoveSpeed;
    private ZombieAI ai;
    private bool capturedBase;

    void Awake()
    {
        ai = GetComponent<ZombieAI>();
        if (!ai)
            return;

        CaptureBaseIfNeeded();
        ApplyDifficulty();
    }

    private void OnEnable()
    {
        if (!ai)
            ai = GetComponent<ZombieAI>();

        if (!ai)
            return;

        CaptureBaseIfNeeded();
        ApplyDifficulty();
    }

    private void CaptureBaseIfNeeded()
    {
        if (capturedBase)
            return;

        baseDetectionRadius = ai.detectionRadius;
        baseMoveSpeed = ai.moveSpeed;
        capturedBase = true;
    }

    private void ApplyDifficulty()
    {
        ai.detectionRadius = baseDetectionRadius * DifficultyContext.EnemyDetectionRangeMultiplier;
        ai.moveSpeed = baseMoveSpeed * DifficultyContext.EnemyMoveSpeedMultiplier;
    }
}
