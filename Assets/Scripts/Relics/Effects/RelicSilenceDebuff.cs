using System.Collections.Generic;
using UnityEngine;

public class RelicSilenceDebuff : MonoBehaviour
    , IRelicBatchedUpdate
    , IRelicBatchedCadence
{
    private float expiresAt;

    private readonly List<Behaviour> disabledComponents = new();
    private bool applied;

    public void Apply(float duration)
    {
        if (duration <= 0f)
            return;

        expiresAt = Mathf.Max(expiresAt, Time.time + duration);
        enabled = true;
        if (!applied)
            ApplySilenceState();
    }

    private void OnEnable()
    {
        RelicBatchedTickSystem.Register(this);
    }

    public bool IsBatchedUpdateActive => enabled;

    public float BatchedUpdateInterval => 0.1f;

    public RelicTickArchetype BatchedTickArchetype => RelicTickArchetype.EnemyControl;

    public void TickFromRelicBatch(float now, float deltaTime)
    {
        if (!applied)
            return;

        if (now >= expiresAt)
        {
            RemoveSilenceState();
            expiresAt = 0f;
            enabled = false;
        }
    }

    private void OnDisable()
    {
        RelicBatchedTickSystem.Unregister(this);

        if (applied)
            RemoveSilenceState();
    }

    private void ApplySilenceState()
    {
        // Silence only attack dealers; movement AI stays active.
        var dealers = GetComponentsInChildren<EnemyDamageDealer>(true);
        for (int i = 0; i < dealers.Length; i++)
        {
            var dealer = dealers[i];
            if (dealer == null || !dealer.enabled)
                continue;

            disabledComponents.Add(dealer);
            dealer.enabled = false;
        }

        applied = true;
    }

    private void RemoveSilenceState()
    {
        for (int i = 0; i < disabledComponents.Count; i++)
        {
            var component = disabledComponents[i];
            if (component != null)
                component.enabled = true;
        }

        disabledComponents.Clear();
        applied = false;
    }
}
