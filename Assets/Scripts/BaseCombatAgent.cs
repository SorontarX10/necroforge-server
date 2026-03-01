using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using GrassSim.Combat;
using UnityEngine.InputSystem;

public abstract class BaseCombatAgent : Agent
{
    [Header("Movement")]
    public Rigidbody rb;
    public float moveSpeed = 4f;

    [Header("Combat")]
    public Combatant combatant;
    public WeaponController weaponController;
    public MLCombatInput mlInput;

    [Header("Vitals (Agent-owned)")]
    public float maxHealth = 100f;
    public float stamina = 100f;
    public float maxStamina = 100f;

    protected Transform currentTarget;
    protected float lastDistanceToTarget;
    public float targetStamina;

    private StatsRecorder stats;

    public override void Initialize()
    {
        base.Initialize();
        stats = Academy.Instance.StatsRecorder;

        if (!rb) rb = GetComponent<Rigidbody>();
        if (!combatant) combatant = GetComponent<Combatant>();
        if (!weaponController) weaponController = GetComponentInChildren<WeaponController>();
        if (!mlInput) mlInput = GetComponent<MLCombatInput>();
    }

    public override void OnEpisodeBegin()
    {
        if (combatant != null)
            combatant.Initialize(maxHealth);

        stamina = maxStamina;
        lastDistanceToTarget = 999f;
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        currentTarget = FindClosestTarget("Player", "Enemy", "Zombie");

        float dist = GetDistanceToTarget();
        sensor.AddObservation(dist);

        Vector3 dir = currentTarget
            ? (currentTarget.position - transform.position).normalized
            : Vector3.zero;
        sensor.AddObservation(dir);

        // own stamina
        sensor.AddObservation(stamina);

        // target stamina normalized (keep your existing signal)
        float denom = Mathf.Max(1f, maxStamina);
        sensor.AddObservation(targetStamina / denom);

        // own health normalized
        float hp = combatant != null ? combatant.currentHealth : 0f;
        sensor.AddObservation(hp / Mathf.Max(1f, maxHealth));
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        var cont = actions.ContinuousActions;

        float moveX = cont[0];
        float moveZ = cont[1];

        Vector3 movement = new Vector3(moveX, 0f, moveZ).normalized * moveSpeed * Time.fixedDeltaTime;
        rb.MovePosition(rb.position + movement);

        if (mlInput != null)
            mlInput.moveDirection = movement;
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var cont = actionsOut.ContinuousActions;
        Vector2 move = ReadMoveInput();
        Vector2 look = ReadLookInput();

        cont[0] = move.x;
        cont[1] = move.y;
        cont[2] = Mathf.Clamp(look.x, -1f, 1f);
        cont[3] = Mathf.Clamp(look.y, -1f, 1f);
    }

    private static Vector2 ReadMoveInput()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null)
            return Vector2.zero;

        float horizontal = 0f;
        float vertical = 0f;

        if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed)
            horizontal -= 1f;
        if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed)
            horizontal += 1f;
        if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed)
            vertical -= 1f;
        if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed)
            vertical += 1f;

        return new Vector2(Mathf.Clamp(horizontal, -1f, 1f), Mathf.Clamp(vertical, -1f, 1f));
    }

    private static Vector2 ReadLookInput()
    {
        var mouse = Mouse.current;
        if (mouse == null)
            return Vector2.zero;

        return mouse.delta.ReadValue();
    }

    protected float GetDistanceToTarget()
    {
        if (currentTarget == null) return 999f;
        return Vector3.Distance(transform.position, currentTarget.position);
    }

    protected Transform FindClosestTarget(params string[] tags)
    {
        Transform closest = null;
        float best = float.MaxValue;

        foreach (string tag in tags)
        {
            var gos = GameObject.FindGameObjectsWithTag(tag);
            foreach (var go in gos)
            {
                if (go == null) continue;
                float d = Vector3.Distance(transform.position, go.transform.position);
                if (d < best)
                {
                    best = d;
                    closest = go.transform;
                }
            }
        }

        return closest;
    }

    public void NotifyDeath()
    {
        float r = -3000.0f;
        AddReward(r);
        stats?.Add("Reward/OnDeath", r);
        EndEpisode();
    }

    public void NotifyOutOfBounds()
    {
        float r = -3000.0f;
        AddReward(r);
        stats?.Add("Reward/OOB", r);
    }
}
