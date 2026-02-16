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
        combatant.Initialize(maxHealth);
        stamina = maxStamina;
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        sensor.AddObservation(combatant.currentHealth / maxHealth);
        sensor.AddObservation(stamina / maxStamina);
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        // AI logic (movement / attack) – bez zmian
    }
}
