using UnityEngine;

public class RelicRootDebuff : MonoBehaviour
    , IRelicBatchedUpdate
    , IRelicBatchedFixedUpdate
    , IRelicBatchedCadence
{
    private float expiresAt;

    private ZombieAI zombieAI;
    private EnemyAI enemyAI;
    private Rigidbody rb;

    private bool applied;
    private float cachedZombieMoveSpeed;
    private float cachedZombieRotationSpeed;
    private float cachedEnemyWanderSpeed;
    private float cachedEnemyChaseSpeed;

    public void Apply(float duration)
    {
        if (duration <= 0f)
            return;

        expiresAt = Mathf.Max(expiresAt, Time.time + duration);
        enabled = true;
        if (!applied)
            ApplyRootState();
    }

    private void Awake()
    {
        zombieAI = GetComponent<ZombieAI>();
        enemyAI = GetComponent<EnemyAI>();
        rb = GetComponent<Rigidbody>();
    }

    private void OnEnable()
    {
        RelicBatchedTickSystem.Register(this);
    }

    public bool IsBatchedUpdateActive => enabled;

    public float BatchedUpdateInterval => 0.05f;

    public RelicTickArchetype BatchedTickArchetype => RelicTickArchetype.EnemyControl;

    public void TickFromRelicBatch(float now, float deltaTime)
    {
        if (!applied)
            return;

        if (now >= expiresAt)
        {
            RemoveRootState();
            expiresAt = 0f;
            enabled = false;
        }
    }

    public bool IsBatchedFixedUpdateActive => applied && rb != null;

    public void TickFromRelicBatchFixed(float fixedDeltaTime)
    {
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
    }

    private void OnDisable()
    {
        RelicBatchedTickSystem.Unregister(this);

        if (applied)
            RemoveRootState();
    }

    private void ApplyRootState()
    {
        if (zombieAI != null)
        {
            cachedZombieMoveSpeed = zombieAI.moveSpeed;
            cachedZombieRotationSpeed = zombieAI.rotationSpeed;
            zombieAI.moveSpeed = 0f;
            zombieAI.rotationSpeed = 0f;
        }

        if (enemyAI != null)
        {
            cachedEnemyWanderSpeed = enemyAI.wanderSpeed;
            cachedEnemyChaseSpeed = enemyAI.chaseSpeed;
            enemyAI.wanderSpeed = 0f;
            enemyAI.chaseSpeed = 0f;
        }

        applied = true;
    }

    private void RemoveRootState()
    {
        if (zombieAI != null)
        {
            zombieAI.moveSpeed = cachedZombieMoveSpeed;
            zombieAI.rotationSpeed = cachedZombieRotationSpeed;
        }

        if (enemyAI != null)
        {
            enemyAI.wanderSpeed = cachedEnemyWanderSpeed;
            enemyAI.chaseSpeed = cachedEnemyChaseSpeed;
        }

        applied = false;
    }
}
