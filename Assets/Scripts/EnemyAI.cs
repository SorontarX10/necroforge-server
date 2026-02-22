using GrassSim.Core;
using GrassSim.Enemies;
using UnityEngine;

[RequireComponent(typeof(Rigidbody), typeof(Collider))]
public class EnemyAI : MonoBehaviour
{
    [Header("Horde System")]
    [SerializeField] private bool disableHordeAISystem;
    [SerializeField] private bool allowBossSystemTick = false;

    [Header("Wander / Chase Settings")]
    public float wanderRadius = 5f;
    public float wanderIntervalMin = 2f;
    public float wanderIntervalMax = 5f;
    public float wanderSpeed = 2f;

    public float detectionRadius = 8f;
    public float chaseSpeed = 3.5f;

    [Header("Attack Settings")]
    public float attackRadius = 2.2f;
    public float attackCooldown = 1.5f;
    private float lastAttackTime = -999f;

    private Vector3 initialPosition;
    private Vector3 wanderTarget;
    private float nextWanderTime;

    private Transform player;
    private Rigidbody rb;
    private float nextPlayerResolveAt;
    private const float PlayerResolveInterval = 0.25f;
    private bool isBossUnit;
    private bool registeredToHorde;
    private EnemyCombatant enemyCombatant;

    public int LastSystemTickFrame { get; private set; } = -1;

    private bool CanBeSystemDriven => !disableHordeAISystem && (!isBossUnit || allowBossSystemTick);

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;

        float moveMultiplier = DifficultyContext.EnemyMoveSpeedMultiplier;
        float detectionMultiplier = DifficultyContext.EnemyDetectionRangeMultiplier;
        wanderSpeed = Mathf.Max(0f, wanderSpeed * moveMultiplier);
        chaseSpeed = Mathf.Max(0f, chaseSpeed * moveMultiplier);
        detectionRadius = Mathf.Max(0.1f, detectionRadius * detectionMultiplier);

        initialPosition = transform.position;
        ChooseNewWanderTarget();

        player = PlayerLocator.GetTransform();
        if (player == null)
            Debug.LogWarning("EnemyAI: player transform not found.");

        isBossUnit = GetComponent<BossEnemyController>() != null;
        enemyCombatant = GetComponent<EnemyCombatant>();
    }

    private void OnEnable()
    {
        registeredToHorde = false;

        if (CanBeSystemDriven)
            registeredToHorde = HordeAISystem.TryRegister(this);
    }

    private void OnDisable()
    {
        if (registeredToHorde)
            HordeAISystem.Unregister(this);

        registeredToHorde = false;
    }

    private void FixedUpdate()
    {
        if (registeredToHorde)
            return;

        TickRuntime();
    }

    internal void TickFromHordeSystem()
    {
        if (!isActiveAndEnabled)
            return;

        LastSystemTickFrame = Time.frameCount;
        TickRuntime();
    }

    internal bool ShouldForceNearTick(Transform focus, float nearDistanceSqr)
    {
        if (!isActiveAndEnabled || focus == null)
            return false;

        float dx = transform.position.x - focus.position.x;
        float dz = transform.position.z - focus.position.z;
        return dx * dx + dz * dz <= nearDistanceSqr;
    }

    private void TickRuntime()
    {
        if (enemyCombatant != null && !enemyCombatant.CanAct)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            return;
        }

        if (player == null && Time.time >= nextPlayerResolveAt)
        {
            nextPlayerResolveAt = Time.time + PlayerResolveInterval;
            player = PlayerLocator.GetTransform();
        }

        if (player != null)
        {
            Vector3 toPlayer = player.position - transform.position;
            toPlayer.y = 0f;
            float distanceSqr = toPlayer.sqrMagnitude;
            float attackRadiusSqr = attackRadius * attackRadius;
            float detectionRadiusSqr = detectionRadius * detectionRadius;

            if (distanceSqr <= attackRadiusSqr)
            {
                TryAttack();
                return;
            }
            else if (distanceSqr <= detectionRadiusSqr)
            {
                ChasePlayerPhysics();
                return;
            }
        }

        WanderPhysics();
    }

    private void TryAttack()
    {
        if (Time.time - lastAttackTime < attackCooldown)
            return;

        lastAttackTime = Time.time;
        PerformAttackMotion();
    }

    private void PerformAttackMotion()
    {
        if (player == null)
            return;

        Vector3 dir = player.position - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.01f)
            return;

        Quaternion targetRot = Quaternion.LookRotation(dir.normalized);
        rb.MoveRotation(targetRot);
    }

    private void ChasePlayerPhysics()
    {
        if (player == null)
            return;

        Vector3 dir = player.position - transform.position;
        dir.y = 0f;
        float dist = dir.magnitude;
        if (dist > 0.05f)
        {
            Vector3 moveDir = dir.normalized;
            Vector3 desiredVelocity = moveDir * chaseSpeed;
            rb.MovePosition(rb.position + desiredVelocity * Time.fixedDeltaTime);

            Quaternion targetRot = Quaternion.LookRotation(moveDir);
            rb.MoveRotation(Quaternion.RotateTowards(rb.rotation, targetRot, 720f * Time.fixedDeltaTime));
        }
    }

    private void WanderPhysics()
    {
        if (Time.time >= nextWanderTime)
            ChooseNewWanderTarget();

        Vector3 dir = wanderTarget - transform.position;
        dir.y = 0f;
        float dist = dir.magnitude;
        if (dist > 0.2f)
        {
            Vector3 moveDir = dir.normalized;
            Vector3 desiredVelocity = moveDir * wanderSpeed;
            rb.MovePosition(rb.position + desiredVelocity * Time.fixedDeltaTime);

            Quaternion targetRot = Quaternion.LookRotation(moveDir);
            rb.MoveRotation(Quaternion.RotateTowards(rb.rotation, targetRot, 720f * Time.fixedDeltaTime));
        }
    }

    private void ChooseNewWanderTarget()
    {
        Vector2 rand2 = Random.insideUnitCircle * wanderRadius;
        wanderTarget = initialPosition + new Vector3(rand2.x, 0f, rand2.y);
        nextWanderTime = Time.time + Random.Range(wanderIntervalMin, wanderIntervalMax);
    }
}
