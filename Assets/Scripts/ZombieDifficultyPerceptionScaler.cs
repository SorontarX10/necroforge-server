using UnityEngine;

public class ZombieDifficultyPerceptionScaler : MonoBehaviour
{
    [Header("Base perception (copied once)")]
    public float baseViewRadius;
    public float baseViewAngle;

    private ZombiePerception perception;
    private bool capturedBase;

    void Awake()
    {
        perception = GetComponent<ZombiePerception>();
        if (!perception)
            return;

        CaptureBaseIfNeeded();
        ApplyDifficulty();
    }

    private void OnEnable()
    {
        if (!perception)
            perception = GetComponent<ZombiePerception>();

        if (!perception)
            return;

        CaptureBaseIfNeeded();
        ApplyDifficulty();
    }

    private void CaptureBaseIfNeeded()
    {
        if (capturedBase)
            return;

        baseViewRadius = perception.viewRadius;
        baseViewAngle = perception.viewAngle;
        capturedBase = true;
    }

    void ApplyDifficulty()
    {
        perception.viewRadius = baseViewRadius * DifficultyContext.EnemyDetectionRangeMultiplier;

        // Keep the field of view angle unchanged.
        perception.viewAngle = baseViewAngle;
    }
}
