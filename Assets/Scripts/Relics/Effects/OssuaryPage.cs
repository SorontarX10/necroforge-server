using UnityEngine;
using GrassSim.Combat;
using GrassSim.Core;

[CreateAssetMenu(
    menuName = "GrassSim/Relics/Effects/Uncommon/Ossuary Page",
    fileName = "Relic_OssuaryPage"
)]
public class OssuaryPage : RelicEffect
{
    [Header("Summon Timing")]
    public float baseCooldown = 24f;
    public float cooldownReductionPerStack = 1.5f;
    public float baseDuration = 7f;
    public float durationBonusPerStack = 0.5f;

    [Header("Mark")]
    public float markInterval = 1f;
    public float markDuration = 2f;
    [Range(0f, 1f)] public float baseOutgoingReduction = 0.12f;
    [Range(0f, 1f)] public float outgoingReductionPerStack = 0.02f;
    public float baseIncomingDamageMultiplier = 1.1f;
    public float incomingDamageMultiplierPerStack = 0.02f;
    public float aggroRadius = 8f;
    public LayerMask enemyMask;

    [Header("Optional Visual Prefab")]
    public GameObject scribePrefab;

    public override void OnAcquire(PlayerRelicController player, int stacks)
    {
        Attach(player)?.Configure(this, stacks);
    }

    public override void OnStack(PlayerRelicController player, int stacks)
    {
        Attach(player)?.Configure(this, stacks);
    }

    private OssuaryPageRuntime Attach(PlayerRelicController player)
    {
        if (player == null)
            return null;

        OssuaryPageRuntime rt = player.GetComponent<OssuaryPageRuntime>();
        if (rt == null)
            rt = player.gameObject.AddComponent<OssuaryPageRuntime>();

        return rt;
    }
}

public class OssuaryPageRuntime : MonoBehaviour, IRelicBatchedUpdate, IRelicBatchedCadence
{
    private PlayerRelicController player;
    private OssuaryPage cfg;
    private int stacks;
    private float nextSpawnAt;
    private BoneScribeMinion activeScribe;
    private BoneScribeMinion pooledGeneratedScribe;

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
        if (activeScribe != null)
            ReleaseScribe(activeScribe, activeScribe.ReturnViaSimplePool);
    }

    private void OnDestroy()
    {
        if (activeScribe != null)
            ReleaseScribe(activeScribe, activeScribe.ReturnViaSimplePool);

        if (pooledGeneratedScribe != null)
            Destroy(pooledGeneratedScribe.gameObject);
        pooledGeneratedScribe = null;
    }

    public void Configure(OssuaryPage config, int stackCount)
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
        if (activeScribe == null && now >= nextSpawnAt)
        {
            SpawnScribe();
            float cooldown = cfg.baseCooldown - cfg.cooldownReductionPerStack * Mathf.Max(0, stacks - 1);
            nextSpawnAt = now + Mathf.Max(4f, cooldown);
        }
    }

    private void SpawnScribe()
    {
        float duration = cfg.baseDuration + cfg.durationBonusPerStack * Mathf.Max(0, stacks - 1);
        float outgoingReduction = Mathf.Clamp01(
            cfg.baseOutgoingReduction + cfg.outgoingReductionPerStack * Mathf.Max(0, stacks - 1)
        );
        float incomingMultiplier = Mathf.Max(
            1f,
            cfg.baseIncomingDamageMultiplier + cfg.incomingDamageMultiplierPerStack * Mathf.Max(0, stacks - 1)
        );

        bool usePrefabPool = cfg.scribePrefab != null;
        GameObject go = RentScribe(usePrefabPool);
        if (go == null)
            return;

        activeScribe = go.GetComponent<BoneScribeMinion>();
        if (activeScribe == null)
            activeScribe = go.AddComponent<BoneScribeMinion>();

        LayerMask mask = cfg.enemyMask.value != 0 ? cfg.enemyMask : LayerMask.GetMask("Enemy", "Zombie");
        activeScribe.Configure(
            owner: transform,
            aggroRadius: cfg.aggroRadius,
            markInterval: cfg.markInterval,
            markDuration: cfg.markDuration,
            outgoingReduction: outgoingReduction,
            incomingDamageMultiplier: incomingMultiplier,
            lifetime: duration,
            enemyMask: mask,
            runtimeOwner: this,
            returnViaSimplePool: usePrefabPool
        );

        RelicDamageText.PlayGeneratedEventFeedback(transform, RelicRarity.Uncommon, 1.0f);
    }

    private GameObject RentScribe(bool usePrefabPool)
    {
        Vector3 spawnPos = transform.position + transform.right * 1.2f;
        if (usePrefabPool)
        {
            if (cfg.scribePrefab == null)
                return null;

            GameObject pooled = SimplePool.Get(cfg.scribePrefab);
            pooled.transform.SetPositionAndRotation(spawnPos, Quaternion.identity);
            return pooled;
        }

        GameObject go = null;
        if (pooledGeneratedScribe != null)
        {
            go = pooledGeneratedScribe.gameObject;
            pooledGeneratedScribe = null;
            if (go != null)
                go.SetActive(true);
        }

        if (go == null)
        {
            go = RelicDamageText.CreateGeneratedScribe("BoneScribe", RelicRarity.Uncommon, 0.45f);
            if (go == null)
                return null;
        }

        go.transform.SetPositionAndRotation(spawnPos + Vector3.up * 1.2f, Quaternion.identity);
        return go;
    }

    internal void ReleaseScribe(BoneScribeMinion scribe, bool returnViaSimplePool)
    {
        if (scribe == null)
            return;

        if (activeScribe == scribe)
            activeScribe = null;

        GameObject go = scribe.gameObject;
        if (go == null)
            return;

        if (returnViaSimplePool)
        {
            SimplePool.Return(go);
            return;
        }

        go.SetActive(false);
        pooledGeneratedScribe = scribe;
    }
}

public class BoneScribeMinion : MonoBehaviour, IRelicBatchedUpdate, IRelicBatchedCadence
{
    private Transform owner;
    private float aggroRadius;
    private float markInterval;
    private float markDuration;
    private float outgoingReduction;
    private float incomingDamageMultiplier;
    private float despawnAt;
    private float nextMarkAt;
    private LayerMask enemyMask;
    private OssuaryPageRuntime runtimeOwner;
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
        float aggroRadius,
        float markInterval,
        float markDuration,
        float outgoingReduction,
        float incomingDamageMultiplier,
        float lifetime,
        LayerMask enemyMask,
        OssuaryPageRuntime runtimeOwner,
        bool returnViaSimplePool
    )
    {
        this.owner = owner;
        this.aggroRadius = Mathf.Max(1f, aggroRadius);
        this.markInterval = Mathf.Max(0.1f, markInterval);
        this.markDuration = Mathf.Max(0.1f, markDuration);
        this.outgoingReduction = Mathf.Clamp01(outgoingReduction);
        this.incomingDamageMultiplier = Mathf.Max(1f, incomingDamageMultiplier);
        this.enemyMask = enemyMask;
        this.runtimeOwner = runtimeOwner;
        ReturnViaSimplePool = returnViaSimplePool;
        released = false;
        nextMarkAt = 0f;
        despawnAt = Time.time + Mathf.Max(0.5f, lifetime);
        EnemyQueryService.ConfigureOwnerBudget(this, 12);
    }

    public bool IsBatchedUpdateActive => isActiveAndEnabled && !released;

    public float BatchedUpdateInterval => 0.04f;

    public RelicTickArchetype BatchedTickArchetype => RelicTickArchetype.EnemyDebuff;

    public void TickFromRelicBatch(float now, float deltaTime)
    {
        if (now >= despawnAt)
        {
            Release();
            return;
        }

        HoverNearOwner(deltaTime);

        if (now >= nextMarkAt)
        {
            nextMarkAt = now + markInterval;
            MarkNearestEnemy();
        }
    }

    private void HoverNearOwner(float deltaTime)
    {
        if (owner == null)
        {
            Release();
            return;
        }

        Vector3 targetPos = owner.position + owner.right * 1.2f + Vector3.up * 1.25f;
        transform.position = Vector3.Lerp(transform.position, targetPos, Mathf.Max(0f, deltaTime) * 8f);
    }

    private void MarkNearestEnemy()
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

        if (best == null)
            return;

        RelicOutgoingDamageDebuff outgoing = best.GetComponent<RelicOutgoingDamageDebuff>();
        if (outgoing == null)
            outgoing = best.gameObject.AddComponent<RelicOutgoingDamageDebuff>();
        outgoing.Apply(outgoingReduction, markDuration);

        RelicIncomingDamageTakenDebuff incoming = best.GetComponent<RelicIncomingDamageTakenDebuff>();
        if (incoming == null)
            incoming = best.gameObject.AddComponent<RelicIncomingDamageTakenDebuff>();
        incoming.Apply(incomingDamageMultiplier, markDuration);
    }

    private void Release()
    {
        if (released)
            return;

        released = true;
        if (runtimeOwner != null)
            runtimeOwner.ReleaseScribe(this, ReturnViaSimplePool);
        else if (ReturnViaSimplePool)
            SimplePool.Return(gameObject);
        else
            gameObject.SetActive(false);
    }
}
