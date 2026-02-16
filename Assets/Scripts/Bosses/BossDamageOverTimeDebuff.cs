using UnityEngine;
using GrassSim.Combat;

public class BossDamageOverTimeDebuff : MonoBehaviour
    , IRelicBatchedUpdate
    , IRelicBatchedCadence
{
    [SerializeField] private string effectId;
    [SerializeField] private float tickInterval = 1f;
    [SerializeField] private float damagePerTick = 1f;
    [SerializeField] private float expiresAt;

    private float nextTickAt;
    private Combatant combatant;

    public string EffectId => effectId;

    private void Awake()
    {
        combatant = GetComponent<Combatant>();
        if (combatant == null)
            enabled = false;
    }

    private void OnEnable()
    {
        RelicBatchedTickSystem.Register(this);
    }

    private void OnDisable()
    {
        RelicBatchedTickSystem.Unregister(this);
    }

    public void Apply(string effectId, float duration, float dps, float interval)
    {
        this.effectId = effectId;
        tickInterval = Mathf.Max(0.1f, interval);
        damagePerTick = Mathf.Max(0.1f, dps * tickInterval);

        float newExpireTime = Time.time + Mathf.Max(0.1f, duration);
        expiresAt = Mathf.Max(expiresAt, newExpireTime);
        enabled = true;

        if (nextTickAt <= Time.time)
            nextTickAt = Time.time + tickInterval;
    }

    public bool IsBatchedUpdateActive => enabled;

    public float BatchedUpdateInterval => Mathf.Max(0.05f, tickInterval);

    public RelicTickArchetype BatchedTickArchetype => RelicTickArchetype.BossDot;

    public void TickFromRelicBatch(float now, float deltaTime)
    {
        if (combatant == null || combatant.IsDead)
        {
            ResetAndDisable();
            return;
        }

        if (now >= expiresAt)
        {
            ResetAndDisable();
            return;
        }

        if (now < nextTickAt)
            return;

        combatant.TakeDamage(damagePerTick);
        nextTickAt = now + tickInterval;
    }

    private void ResetAndDisable()
    {
        effectId = string.Empty;
        expiresAt = 0f;
        nextTickAt = 0f;
        enabled = false;
    }
}
