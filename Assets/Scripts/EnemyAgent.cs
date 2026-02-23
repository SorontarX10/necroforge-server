using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Sensors;
using Unity.MLAgents.Actuators;
using GrassSim.Combat;

public class EnemyAgent : Agent
{
    public Combatant combatant;

    [Header("Vitals (Agent-owned)")]
    public float maxHealth = 50f;
    public float stamina = 100f;
    public float maxStamina = 100f;

    public override void Initialize()
    {
        if (!combatant)
            combatant = GetComponent<Combatant>();
    }

    public override void OnEpisodeBegin()
    {
        if (!combatant)
            combatant = GetComponent<Combatant>();

        if (!combatant)
        {
            Debug.LogWarning("[EnemyAgent] Missing Combatant component. Disabling agent.", this);
            enabled = false;
            return;
        }

        float resolvedMaxHealth = Mathf.Max(1f, maxHealth);
        combatant.Initialize(resolvedMaxHealth);
        stamina = Mathf.Max(0f, maxStamina);
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        if (!combatant)
        {
            sensor.AddObservation(0f);
            sensor.AddObservation(0f);
            return;
        }

        float hpDenominator = Mathf.Max(1f, maxHealth);
        float staminaDenominator = Mathf.Max(0.01f, maxStamina);

        sensor.AddObservation(combatant.currentHealth / hpDenominator);
        sensor.AddObservation(stamina / staminaDenominator);
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        // AI logic (movement / attack) – bez zmian
    }
}
