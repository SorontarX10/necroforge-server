using UnityEngine;
using GrassSim.Combat;
using GrassSim.Core;

[CreateAssetMenu(
    menuName = "GrassSim/Relics/Effects/Common/Ossuary Bell",
    fileName = "Relic_OssuaryBell"
)]
public class OssuaryBell : RelicEffect
{
    [Header("Summon Timing")]
    public float baseCooldown = 20f;
    public float cooldownReductionPerStack = 1.5f;
    public float baseDuration = 8f;
    public float durationBonusPerStack = 0.5f;

    [Header("Minion Stats")]
    public float baseHealth = 90f;
    public float healthPerStack = 15f;
    public float baseDamage = 14f;
    public float damagePerStack = 2f;
    public float attackInterval = 0.9f;
    public float moveSpeed = 4.8f;
    public float aggroRadius = 10f;
    public float attackRange = 1.8f;
    public LayerMask enemyMask;

    [Header("Optional Visual Prefab")]
    public GameObject minionPrefab;

    public override void OnAcquire(PlayerRelicController player, int stacks)
    {
        Attach(player)?.Configure(this, stacks);
    }

    public override void OnStack(PlayerRelicController player, int stacks)
    {
        Attach(player)?.Configure(this, stacks);
    }

    private OssuaryBellRuntime Attach(PlayerRelicController player)
    {
        if (player == null)
            return null;

        OssuaryBellRuntime rt = player.GetComponent<OssuaryBellRuntime>();
        if (rt == null)
            rt = player.gameObject.AddComponent<OssuaryBellRuntime>();
        return rt;
    }
}

public class OssuaryBellRuntime : MonoBehaviour, IRelicBatchedUpdate, IRelicBatchedCadence
{
    private PlayerRelicController player;
    private OssuaryBell cfg;
    private int stacks;
    private float nextSpawnAt;
    private OssuarySkeletonMinion activeMinion;
    private OssuarySkeletonMinion pooledGeneratedMinion;

    private void Awake()
    {
        player = GetComponent<PlayerRelicController>();
    }

    private void OnEnable()
    {
        RelicBatchedTickSystem.Register(this);
    }

    private void OnDisable()
    {
        RelicBatchedTickSystem.Unregister(this);
        if (activeMinion != null)
            ReleaseMinion(activeMinion, activeMinion.ReturnViaSimplePool);
    }

    private void OnDestroy()
    {
        if (activeMinion != null)
            ReleaseMinion(activeMinion, activeMinion.ReturnViaSimplePool);

        if (pooledGeneratedMinion != null)
            Destroy(pooledGeneratedMinion.gameObject);
        pooledGeneratedMinion = null;
    }

    public void Configure(OssuaryBell config, int stackCount)
    {
        cfg = config;
        stacks = Mathf.Max(1, stackCount);
        if (nextSpawnAt <= 0f)
            nextSpawnAt = Time.time + 1.5f;
    }

    public bool IsBatchedUpdateActive => isActiveAndEnabled && cfg != null;

    public float BatchedUpdateInterval => 0.1f;

    public RelicTickArchetype BatchedTickArchetype => RelicTickArchetype.PlayerState;

    public void TickFromRelicBatch(float now, float deltaTime)
    {
        if (now < nextSpawnAt)
            return;

        SpawnMinion();
        float cooldown = cfg.baseCooldown - cfg.cooldownReductionPerStack * Mathf.Max(0, stacks - 1);
        nextSpawnAt = now + Mathf.Max(4f, cooldown);
    }

    private void SpawnMinion()
    {
        float duration = cfg.baseDuration + cfg.durationBonusPerStack * Mathf.Max(0, stacks - 1);
        float hp = cfg.baseHealth + cfg.healthPerStack * Mathf.Max(0, stacks - 1);
        float dmg = cfg.baseDamage + cfg.damagePerStack * Mathf.Max(0, stacks - 1);

        bool usePrefabPool = cfg.minionPrefab != null;
        GameObject go = RentMinion(usePrefabPool);
        if (go == null)
            return;

        OssuarySkeletonMinion minion = go.GetComponent<OssuarySkeletonMinion>();
        if (minion == null)
            minion = go.AddComponent<OssuarySkeletonMinion>();

        LayerMask mask = cfg.enemyMask.value != 0 ? cfg.enemyMask : LayerMask.GetMask("Enemy", "Zombie");
        minion.Configure(
            owner: transform,
            maxHealth: hp,
            damage: dmg,
            attackInterval: cfg.attackInterval,
            moveSpeed: cfg.moveSpeed,
            aggroRadius: cfg.aggroRadius,
            attackRange: cfg.attackRange,
            lifetime: duration,
            enemyMask: mask,
            sourceEffect: cfg,
            runtimeOwner: this,
            returnViaSimplePool: usePrefabPool
        );

        activeMinion = minion;
        RelicDamageText.PlayGeneratedEventFeedback(transform, RelicRarity.Common, 0.95f);
    }

    private GameObject RentMinion(bool usePrefabPool)
    {
        Vector3 spawnPos = transform.position + transform.forward;
        if (usePrefabPool)
        {
            if (cfg.minionPrefab == null)
                return null;

            GameObject pooled = SimplePool.Get(cfg.minionPrefab);
            pooled.transform.SetPositionAndRotation(spawnPos, Quaternion.identity);
            return pooled;
        }

        GameObject go = null;
        if (pooledGeneratedMinion != null)
        {
            go = pooledGeneratedMinion.gameObject;
            pooledGeneratedMinion = null;
            if (go != null)
                go.SetActive(true);
        }

        if (go == null)
        {
            go = RelicDamageText.CreateGeneratedMinionBody("OssuarySkeleton", RelicRarity.Common);
            if (go == null)
                return null;
        }

        go.transform.SetPositionAndRotation(spawnPos, Quaternion.identity);
        return go;
    }

    internal void ReleaseMinion(OssuarySkeletonMinion minion, bool returnViaSimplePool)
    {
        if (minion == null)
            return;

        if (activeMinion == minion)
            activeMinion = null;

        GameObject go = minion.gameObject;
        if (go == null)
            return;

        if (returnViaSimplePool)
        {
            SimplePool.Return(go);
            return;
        }

        go.SetActive(false);
        pooledGeneratedMinion = minion;
    }
}

public class OssuarySkeletonMinion : MonoBehaviour, IRelicBatchedUpdate, IRelicBatchedCadence
{
    private Transform owner;
    private Combatant target;
    private float maxHealth;
    private float currentHealth;
    private float damage;
    private float attackInterval;
    private float moveSpeed;
    private float aggroRadius;
    private float attackRange;
    private float despawnAt;
    private LayerMask enemyMask;
    private RelicEffect sourceEffect;
    private float nextAttackAt;
    private float nextScanAt;
    private OssuaryBellRuntime runtimeOwner;
    private bool released;

    public bool ReturnViaSimplePool { get; private set; }

    private void OnEnable()
    {
        RelicBatchedTickSystem.Register(this);
    }

    private void OnDisable()
    {
        RelicBatchedTickSystem.Unregister(this);
    }

    public void Configure(
        Transform owner,
        float maxHealth,
        float damage,
        float attackInterval,
        float moveSpeed,
        float aggroRadius,
        float attackRange,
        float lifetime,
        LayerMask enemyMask,
        RelicEffect sourceEffect,
        OssuaryBellRuntime runtimeOwner,
        bool returnViaSimplePool
    )
    {
        this.owner = owner;
        this.maxHealth = Mathf.Max(1f, maxHealth);
        this.currentHealth = this.maxHealth;
        this.damage = Mathf.Max(1f, damage);
        this.attackInterval = Mathf.Max(0.1f, attackInterval);
        this.moveSpeed = Mathf.Max(0.1f, moveSpeed);
        this.aggroRadius = Mathf.Max(1f, aggroRadius);
        this.attackRange = Mathf.Max(0.25f, attackRange);
        this.enemyMask = enemyMask;
        this.sourceEffect = sourceEffect;
        this.runtimeOwner = runtimeOwner;
        ReturnViaSimplePool = returnViaSimplePool;
        target = null;
        released = false;
        nextAttackAt = 0f;
        nextScanAt = 0f;
        despawnAt = Time.time + Mathf.Max(0.5f, lifetime);
        EnemyQueryService.ConfigureOwnerBudget(this, 16);
    }

    public bool IsBatchedUpdateActive => isActiveAndEnabled && !released;

    public float BatchedUpdateInterval => 0.033f;

    public RelicTickArchetype BatchedTickArchetype => RelicTickArchetype.EnemyControl;

    public void TickFromRelicBatch(float now, float deltaTime)
    {
        if (now >= despawnAt)
        {
            Release();
            return;
        }

        if (target == null || target.IsDead || now >= nextScanAt)
        {
            nextScanAt = now + 0.25f;
            target = FindNearestEnemy();
        }

        if (target == null)
        {
            HoverNearOwner(deltaTime);
            return;
        }

        Vector3 toTarget = target.transform.position - transform.position;
        toTarget.y = 0f;

        float distance = toTarget.magnitude;
        if (distance > 0.001f)
            transform.rotation = Quaternion.LookRotation(toTarget.normalized, Vector3.up);

        if (distance > attackRange)
        {
            Vector3 step = toTarget.normalized * (moveSpeed * Mathf.Max(0f, deltaTime));
            if (step.sqrMagnitude > toTarget.sqrMagnitude)
                step = toTarget;

            transform.position += step;
            return;
        }

        if (now >= nextAttackAt)
        {
            nextAttackAt = now + attackInterval;
            RelicDamageText.Deal(target, damage, transform, sourceEffect, "Ossuary Bell");
        }
    }

    private Combatant FindNearestEnemy()
    {
        Collider[] hits;
        if (enemyMask.value != 0)
            hits = EnemyQueryService.OverlapSphere(transform.position, aggroRadius, enemyMask, QueryTriggerInteraction.Ignore, this);
        else
            hits = EnemyQueryService.OverlapSphere(transform.position, aggroRadius, ~0, QueryTriggerInteraction.Ignore, this);

        Combatant best = null;
        float bestSqr = float.PositiveInfinity;

        for (int i = 0, hitCount = EnemyQueryService.GetLastHitCount(this); i < hitCount; i++)
        {
            Collider col = hits[i];
            if (col == null)
                continue;

            Combatant combatant = EnemyQueryService.GetCombatant(col);
            if (combatant == null || combatant.IsDead)
                continue;

            if (combatant.IsPlayer)
                continue;

            float sqr = (combatant.transform.position - transform.position).sqrMagnitude;
            if (sqr < bestSqr)
            {
                bestSqr = sqr;
                best = combatant;
            }
        }

        return best;
    }

    private void HoverNearOwner(float deltaTime)
    {
        if (owner == null)
        {
            Release();
            return;
        }

        Vector3 anchor = owner.position + owner.right * 1.5f;
        Vector3 delta = anchor - transform.position;
        delta.y = 0f;
        if (delta.sqrMagnitude <= 0.01f)
            return;

        Vector3 step = delta.normalized * (moveSpeed * 0.8f * Mathf.Max(0f, deltaTime));
        if (step.sqrMagnitude > delta.sqrMagnitude)
            step = delta;

        transform.position += step;
    }

    private void Release()
    {
        if (released)
            return;

        released = true;
        if (runtimeOwner != null)
            runtimeOwner.ReleaseMinion(this, ReturnViaSimplePool);
        else if (ReturnViaSimplePool)
            SimplePool.Return(gameObject);
        else
            gameObject.SetActive(false);
    }
}
