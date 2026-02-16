using UnityEngine;

public class RelicMoveSpeedDebuff : MonoBehaviour
    , IRelicBatchedUpdate
    , IRelicBatchedCadence
{
    private float slowPercent;
    private float expiresAt;

    private ZombieAI zombieAI;
    private EnemyAI enemyAI;

    private bool applied;
    private float baseZombieMoveSpeed;
    private float baseZombieRotationSpeed;
    private float baseEnemyWanderSpeed;
    private float baseEnemyChaseSpeed;

    public void Apply(float slowPct, float duration)
    {
        if (duration <= 0f)
            return;

        slowPct = Mathf.Clamp(slowPct, 0f, 0.9f);
        if (slowPct <= 0f)
            return;

        slowPercent = Mathf.Max(slowPercent, slowPct);
        expiresAt = Mathf.Max(expiresAt, Time.time + duration);
        enabled = true;

        if (!applied)
            ApplySlowState();
        else
            RefreshSlowState();
    }

    private void Awake()
    {
        zombieAI = GetComponent<ZombieAI>();
        enemyAI = GetComponent<EnemyAI>();
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
            RemoveSlowState();
            slowPercent = 0f;
            expiresAt = 0f;
            enabled = false;
        }
    }

    private void OnDisable()
    {
        RelicBatchedTickSystem.Unregister(this);

        if (applied)
            RemoveSlowState();
    }

    private void ApplySlowState()
    {
        if (zombieAI != null)
        {
            baseZombieMoveSpeed = zombieAI.moveSpeed;
            baseZombieRotationSpeed = zombieAI.rotationSpeed;
        }

        if (enemyAI != null)
        {
            baseEnemyWanderSpeed = enemyAI.wanderSpeed;
            baseEnemyChaseSpeed = enemyAI.chaseSpeed;
        }

        applied = true;
        RefreshSlowState();
    }

    private void RefreshSlowState()
    {
        float multiplier = Mathf.Clamp01(1f - slowPercent);

        if (zombieAI != null)
        {
            zombieAI.moveSpeed = baseZombieMoveSpeed * multiplier;
            zombieAI.rotationSpeed = baseZombieRotationSpeed * multiplier;
        }

        if (enemyAI != null)
        {
            enemyAI.wanderSpeed = baseEnemyWanderSpeed * multiplier;
            enemyAI.chaseSpeed = baseEnemyChaseSpeed * multiplier;
        }
    }

    private void RemoveSlowState()
    {
        if (zombieAI != null)
        {
            zombieAI.moveSpeed = baseZombieMoveSpeed;
            zombieAI.rotationSpeed = baseZombieRotationSpeed;
        }

        if (enemyAI != null)
        {
            enemyAI.wanderSpeed = baseEnemyWanderSpeed;
            enemyAI.chaseSpeed = baseEnemyChaseSpeed;
        }

        applied = false;
    }
}
