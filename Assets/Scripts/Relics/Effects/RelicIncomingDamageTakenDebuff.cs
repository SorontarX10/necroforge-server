using UnityEngine;

public class RelicIncomingDamageTakenDebuff : MonoBehaviour
    , IRelicBatchedUpdate
    , IRelicBatchedCadence
{
    private float incomingDamageMultiplier = 1f;
    private float expiresAt;

    public bool IsActive => Time.time < expiresAt && incomingDamageMultiplier > 1f;

    public void Apply(float multiplier, float duration)
    {
        if (duration <= 0f)
            return;

        multiplier = Mathf.Max(1f, multiplier);
        if (multiplier <= 1f)
            return;

        incomingDamageMultiplier = Mathf.Max(incomingDamageMultiplier, multiplier);
        expiresAt = Mathf.Max(expiresAt, Time.time + duration);
        enabled = true;
    }

    public float GetIncomingDamageMultiplier()
    {
        return IsActive ? incomingDamageMultiplier : 1f;
    }

    private void OnEnable()
    {
        RelicBatchedTickSystem.Register(this);
    }

    private void OnDisable()
    {
        RelicBatchedTickSystem.Unregister(this);
    }

    public bool IsBatchedUpdateActive => enabled;

    public float BatchedUpdateInterval => 0.1f;

    public RelicTickArchetype BatchedTickArchetype => RelicTickArchetype.EnemyDebuff;

    public void TickFromRelicBatch(float now, float deltaTime)
    {
        if (now >= expiresAt)
        {
            incomingDamageMultiplier = 1f;
            expiresAt = 0f;
            enabled = false;
        }
    }
}
