using GrassSim.Core;
using GrassSim.Enemies;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class ZombieAI : MonoBehaviour
{
    [Header("Horde System")]
    [SerializeField] private bool disableHordeAISystem;
    [SerializeField] private bool allowBossSystemTick = false;

    [Header("Movement")]
    public float moveSpeed = 2.2f;
    public float rotationSpeed = 8f;
    public float attackStopDistance = 1.8f;

    [Header("Detection")]
    public float detectionRadius = 8f;
    public LayerMask targetLayers;

    [Header("Wander")]
    public float wanderRadius = 6f;
    public float wanderInterval = 3f;

    [Header("Separation")]
    public float separationRadius = 1.4f;
    public float separationStrength = 0.4f;
    public LayerMask zombieLayer;

    [Header("Optimization")]
    [Min(0.02f)] public float targetRefreshInterval = 0.24f;
    [Min(0.02f)] public float separationRefreshInterval = 0.18f;
    [Min(1)] public int maxQueryCallsPerFrame = 2;
    [Min(8f)] public float nearAIDistance = 16f;
    [Min(10f)] public float midAIDistance = 28f;
    [Range(1f, 3f)] public float midDistanceScanMultiplier = 1.45f;
    [Range(1f, 5f)] public float farDistanceScanMultiplier = 2.35f;
    [Range(0f, 1f)] public float farDistanceSeparationScale = 0.3f;
    [Min(0.05f)] public float playerResolveInterval = 0.4f;

    [Header("Debug")]
    public bool debugSpeed;
    public float debugInterval = 0.5f;

    private Rigidbody rb;
    private Transform currentTarget;
    private Transform cachedTarget;
    private Transform player;
    private Animator animator;
    private Vector3 cachedSeparation;
    private float nextTargetScanTime;
    private float nextSeparationScanTime;
    private float nextPlayerResolveAt;
    private float baseTargetRefreshInterval;
    private float baseSeparationRefreshInterval;
    private float runtimeScanMultiplier = 1f;
    private float runtimeSeparationScale = 1f;
    private float nextDebugLogTime;
    private bool isBossUnit;
    private bool registeredToHorde;
    private EnemyCombatant enemyCombatant;

    public int LastSystemTickFrame { get; private set; } = -1;

    private bool CanBeSystemDriven => !disableHordeAISystem && (!isBossUnit || allowBossSystemTick);

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();

        rb.useGravity = true;
        rb.constraints =
            RigidbodyConstraints.FreezeRotationX |
            RigidbodyConstraints.FreezeRotationZ;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.linearDamping = 3f;
        rb.angularDamping = 10f;

        animator = GetComponentInChildren<Animator>();
        player = PlayerLocator.GetTransform();
        isBossUnit = GetComponent<BossEnemyController>() != null;
        enemyCombatant = GetComponent<EnemyCombatant>();

        baseTargetRefreshInterval = Mathf.Max(0.02f, targetRefreshInterval);
        baseSeparationRefreshInterval = Mathf.Max(0.02f, separationRefreshInterval);

        nextTargetScanTime = Time.time + Random.Range(0f, baseTargetRefreshInterval);
        nextSeparationScanTime = Time.time + Random.Range(0f, baseSeparationRefreshInterval);
        nextPlayerResolveAt = Time.time + Random.Range(0f, Mathf.Max(0.05f, playerResolveInterval));

        EnemyQueryService.ConfigureOwnerBudget(this, Mathf.Max(1, maxQueryCallsPerFrame));
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

        return HorizontalDistanceSqr(transform.position, focus.position) <= nearDistanceSqr;
    }

    private void TickRuntime()
    {
        if (enemyCombatant != null && !enemyCombatant.CanAct)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            if (animator != null)
                animator.SetFloat("Speed", 0f);
            return;
        }

        ResolvePlayerIfNeeded();
        UpdateRuntimeAiLod();
        RefreshTargetIfNeeded();
        RefreshSeparationIfNeeded();
        currentTarget = cachedTarget;

        Vector3 moveDir = Vector3.zero;
        if (currentTarget != null)
        {
            Vector3 toTarget = currentTarget.position - transform.position;
            toTarget.y = 0f;
            float sqr = toTarget.sqrMagnitude;
            if (sqr > 0.001f)
                moveDir = toTarget * (1f / Mathf.Sqrt(sqr));
        }

        float attackStopDistanceSqr = attackStopDistance * attackStopDistance;
        if (currentTarget == null || HorizontalDistanceSqr(transform.position, currentTarget.position) > attackStopDistanceSqr)
            moveDir += cachedSeparation * (separationStrength * runtimeSeparationScale);

        if (moveDir.sqrMagnitude > 0.001f)
        {
            moveDir.Normalize();

            Vector3 vel = rb.linearVelocity;
            vel.x = moveDir.x * moveSpeed;
            vel.z = moveDir.z * moveSpeed;
            rb.linearVelocity = vel;

            Quaternion targetRot = Quaternion.LookRotation(moveDir);
            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                targetRot,
                rotationSpeed * Time.fixedDeltaTime
            );
        }
        else
        {
            rb.linearVelocity = Vector3.zero;
        }

        if (animator != null)
            animator.SetFloat("Speed", new Vector2(rb.linearVelocity.x, rb.linearVelocity.z).magnitude);

        if (debugSpeed && Time.time >= nextDebugLogTime)
        {
            Debug.Log(
                $"[ZombieAI] Speed={rb.linearVelocity.magnitude:F2} lodScanMul={runtimeScanMultiplier:F2} target={(currentTarget ? currentTarget.name : "none")}",
                this
            );
            nextDebugLogTime = Time.time + debugInterval;
        }
    }

    private void ResolvePlayerIfNeeded()
    {
        if (player != null && player.gameObject.activeInHierarchy)
            return;

        if (Time.time < nextPlayerResolveAt)
            return;

        nextPlayerResolveAt = Time.time + Mathf.Max(0.05f, playerResolveInterval);
        player = PlayerLocator.GetTransform();
    }

    private void UpdateRuntimeAiLod()
    {
        runtimeScanMultiplier = 1f;
        runtimeSeparationScale = 1f;

        if (player == null)
        {
            runtimeScanMultiplier = farDistanceScanMultiplier;
            runtimeSeparationScale = farDistanceSeparationScale;
            return;
        }

        float distSqr = HorizontalDistanceSqr(transform.position, player.position);
        float nearSqr = nearAIDistance * nearAIDistance;
        float midSqr = midAIDistance * midAIDistance;

        if (distSqr <= nearSqr)
            return;

        if (distSqr <= midSqr)
        {
            runtimeScanMultiplier = midDistanceScanMultiplier;
            runtimeSeparationScale = Mathf.Lerp(1f, farDistanceSeparationScale, 0.45f);
            return;
        }

        runtimeScanMultiplier = farDistanceScanMultiplier;
        runtimeSeparationScale = farDistanceSeparationScale;
    }

    private void RefreshTargetIfNeeded()
    {
        float interval = Mathf.Max(0.02f, baseTargetRefreshInterval * runtimeScanMultiplier);
        if (Time.time < nextTargetScanTime)
            return;

        nextTargetScanTime = Time.time + interval;

        if (player != null && player.gameObject.activeInHierarchy)
        {
            cachedTarget = player;
            return;
        }

        cachedTarget = null;
    }

    private void RefreshSeparationIfNeeded()
    {
        if (separationRadius <= 0.001f || runtimeSeparationScale <= 0.001f)
        {
            cachedSeparation = Vector3.zero;
            return;
        }

        float interval = Mathf.Max(0.02f, baseSeparationRefreshInterval * runtimeScanMultiplier);
        if (Time.time < nextSeparationScanTime)
            return;

        nextSeparationScanTime = Time.time + interval;

        Collider[] hits = EnemyQueryService.OverlapSphere(
            transform.position,
            separationRadius,
            zombieLayer,
            QueryTriggerInteraction.Ignore,
            this,
            maxQueriesPerFrame: Mathf.Max(1, maxQueryCallsPerFrame)
        );

        int hitCount = EnemyQueryService.GetLastHitCount(this);
        Vector3 force = Vector3.zero;
        int count = 0;

        for (int i = 0; i < hitCount; i++)
        {
            Collider hit = hits[i];
            if (hit == null)
                continue;

            Transform candidate = hit.transform;
            if (candidate == transform || candidate.IsChildOf(transform))
                continue;

            Vector3 diff = transform.position - candidate.position;
            diff.y = 0f;

            float distSqr = diff.sqrMagnitude;
            if (distSqr < 0.000001f)
                continue;

            float dist = Mathf.Sqrt(distSqr);
            float weight = (separationRadius - dist) / separationRadius;
            if (weight <= 0f)
                continue;

            force += (diff / dist) * Mathf.Clamp01(weight);
            count++;
        }

        cachedSeparation = count > 0 ? force / count : Vector3.zero;
    }

    private static float HorizontalDistanceSqr(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x;
        float dz = a.z - b.z;
        return dx * dx + dz * dz;
    }

    private void OnValidate()
    {
        nearAIDistance = Mathf.Max(8f, nearAIDistance);
        midAIDistance = Mathf.Max(nearAIDistance + 1f, midAIDistance);
        farDistanceScanMultiplier = Mathf.Max(midDistanceScanMultiplier, farDistanceScanMultiplier);
        maxQueryCallsPerFrame = Mathf.Max(1, maxQueryCallsPerFrame);
        playerResolveInterval = Mathf.Max(0.05f, playerResolveInterval);
    }
}
