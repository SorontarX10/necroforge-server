using System.Collections.Generic;
using GrassSim.Combat;
using GrassSim.Core;
using GrassSim.Enemies;
using UnityEngine;

[DefaultExecutionOrder(-350)]
public class HordeAISystem : MonoBehaviour
{
    [Header("Zombie Tick Budget")]
    [SerializeField, Min(1)] private int minFarZombieTicksPerFixed = 12;
    [SerializeField, Min(1)] private int maxFarZombieTicksPerFixed = 84;
    [SerializeField, Min(4f)] private float nearZombieDistance = 18f;

    [Header("Enemy Tick Budget")]
    [SerializeField, Min(1)] private int minFarEnemyTicksPerFixed = 8;
    [SerializeField, Min(1)] private int maxFarEnemyTicksPerFixed = 56;

    [Header("Perception Tick Budget")]
    [SerializeField, Min(1)] private int minPerceptionTicksPerFrame = 24;
    [SerializeField, Min(1)] private int maxPerceptionTicksPerFrame = 140;

    [Header("Maintenance")]
    [SerializeField, Min(0.1f)] private float cleanupInterval = 0.75f;

    [Header("ECS Combat")]
    [SerializeField] private bool enableSystemMeleeForHorde = true;
    [SerializeField, Min(0f)] private float meleeRangePadding = 0.04f;
    [SerializeField, Min(0.05f)] private float defaultMeleeCooldown = 0.65f;

    private static HordeAISystem instance;
    private static bool shuttingDown;

    public struct RuntimeSnapshot
    {
        public int frame;
        public int agentCount;
        public int zombieCount;
        public int enemyCount;
        public int perceptionCount;
        public int nearTicksThisFixed;
        public int farZombieTicksThisFixed;
        public int farEnemyTicksThisFixed;
        public int perceptionTicksThisFrame;
        public float fixedUpdateDurationMs;
        public float updateDurationMs;
    }

    private enum AgentKind : byte
    {
        Zombie,
        Enemy
    }

    private struct AgentState
    {
        public AgentKind kind;
        public int ownerId;
        public ZombieAI zombie;
        public EnemyAI enemy;
        public Transform transform;
        public Rigidbody rb;
        public Animator animator;
        public Vector3 anchor;
        public Vector3 wanderTarget;
        public float nextWanderAt;
        public Combatant ownerCombatant;
        public EnemyCombatant enemyCombatant;
        public EnemyDamageDealer primaryDamageDealer;
        public EnemyDamageDealer[] damageDealers;
        public bool[] damageDealerEnabledStates;
        public BossEnemyController bossController;
        public RelicOutgoingDamageDebuff outgoingDebuff;
        public BossSpecialEffects bossEffects;
        public float attackRange;
        public float attackCooldown;
        public float nextAttackAt;
        public bool systemMeleeEnabled;
        public int lastTickFrame;
    }

    private readonly List<AgentState> agentStates = new(512);
    private readonly Dictionary<int, int> stateIndexByOwnerId = new(512);

    private readonly HashSet<ZombieAI> zombieSet = new();
    private readonly HashSet<EnemyAI> enemySet = new();
    private readonly List<ZombiePerception> perceptionAgents = new(256);
    private readonly HashSet<ZombiePerception> perceptionSet = new();

    private int zombieCursor;
    private int enemyCursor;
    private int perceptionCursor;
    private float nextCleanupAt;
    private float nearZombieDistanceSqr;
    private Transform player;
    private Combatant playerCombatant;
    private PlayerRelicController playerRelics;
    private RuntimeSnapshot lastRuntimeSnapshot;
    private int lastNearTicksThisFixed;
    private int lastFarZombieTicksThisFixed;
    private int lastFarEnemyTicksThisFixed;
    private int lastPerceptionTicksThisFrame;
    private float lastFixedUpdateDurationMs;
    private float lastUpdateDurationMs;

    public static bool HasLiveInstance => instance != null && !shuttingDown;

    public static bool TryGetRuntimeSnapshot(out RuntimeSnapshot snapshot)
    {
        if (!HasLiveInstance)
        {
            snapshot = default;
            return false;
        }

        snapshot = instance.GetRuntimeSnapshot();
        return true;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        instance = null;
        shuttingDown = false;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        EnsureInstance();
    }

    public static bool TryRegister(ZombieAI ai)
    {
        HordeAISystem sys = EnsureInstance();
        return sys != null && sys.Register(ai);
    }

    public static bool TryRegister(EnemyAI ai)
    {
        HordeAISystem sys = EnsureInstance();
        return sys != null && sys.Register(ai);
    }

    public static bool TryRegister(ZombiePerception perception)
    {
        HordeAISystem sys = EnsureInstance();
        return sys != null && sys.Register(perception);
    }

    public static void Unregister(ZombieAI ai)
    {
        if (!HasLiveInstance || ai == null)
            return;

        instance.UnregisterInternal(ai);
    }

    public static void Unregister(EnemyAI ai)
    {
        if (!HasLiveInstance || ai == null)
            return;

        instance.UnregisterInternal(ai);
    }

    public static void Unregister(ZombiePerception perception)
    {
        if (!HasLiveInstance || perception == null)
            return;

        instance.UnregisterInternal(perception);
    }

    public static bool IsDriving(ZombieAI ai)
    {
        return HasLiveInstance && ai != null && instance.zombieSet.Contains(ai);
    }

    public static bool IsDriving(EnemyAI ai)
    {
        return HasLiveInstance && ai != null && instance.enemySet.Contains(ai);
    }

    public static bool IsDriving(ZombiePerception perception)
    {
        return HasLiveInstance && perception != null && instance.perceptionSet.Contains(perception);
    }

    private static HordeAISystem EnsureInstance()
    {
        if (shuttingDown)
            return null;

        if (instance != null)
            return instance;

        instance = FindFirstObjectByType<HordeAISystem>();
        if (instance != null)
            return instance;

        GameObject go = new("HordeAISystem");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<HordeAISystem>();
        return instance;
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);
        nearZombieDistanceSqr = Mathf.Max(1f, nearZombieDistance * nearZombieDistance);
    }

    private void OnApplicationQuit()
    {
        shuttingDown = true;
    }

    private void OnDestroy()
    {
        if (instance == this)
            instance = null;
    }

    private void Update()
    {
        float updateStart = Time.realtimeSinceStartup;
        ResolvePlayerIfNeeded();
        TickPerceptionAgents();
        CleanupIfNeeded();
        lastUpdateDurationMs = (Time.realtimeSinceStartup - updateStart) * 1000f;
        RefreshRuntimeSnapshot();
    }

    private void FixedUpdate()
    {
        float fixedStart = Time.realtimeSinceStartup;
        ResolvePlayerIfNeeded();
        TickAgentStates();
        CleanupIfNeeded();
        lastFixedUpdateDurationMs = (Time.realtimeSinceStartup - fixedStart) * 1000f;
        RefreshRuntimeSnapshot();
    }

    private bool Register(ZombieAI ai)
    {
        if (ai == null)
            return false;

        Rigidbody rb = ai.GetComponent<Rigidbody>();
        if (rb == null)
            return false;

        int ownerId = ai.GetInstanceID();
        zombieSet.Add(ai);

        Combatant ownerCombatant = ai.GetComponent<Combatant>();
        EnemyCombatant enemyCombatant = ai.GetComponent<EnemyCombatant>();
        EnemyDamageDealer[] damageDealers = ai.GetComponentsInChildren<EnemyDamageDealer>(true);
        EnemyDamageDealer primaryDamageDealer = GetPrimaryDamageDealer(damageDealers);
        BossEnemyController bossController = ownerCombatant != null ? ownerCombatant.GetComponent<BossEnemyController>() : null;
        // Zombies must deal damage only via hand hitbox contact (EnemyDamageDealer), not ECS range checks.
        bool meleeEnabled = false;

        bool[] dealerEnabledStates = meleeEnabled
            ? DisableDamageDealers(damageDealers)
            : null;

        // Use stop distance for ECS melee to match contact feel and avoid long-range hits.
        float attackRange = Mathf.Max(0.2f, ai.attackStopDistance);
        float attackCooldown = ResolveCooldownFromDealers(
            damageDealers,
            Mathf.Max(0.05f, defaultMeleeCooldown)
        );

        AgentState state = new AgentState
        {
            kind = AgentKind.Zombie,
            ownerId = ownerId,
            zombie = ai,
            enemy = null,
            transform = ai.transform,
            rb = rb,
            animator = ai.GetComponentInChildren<Animator>(),
            anchor = ai.transform.position,
            wanderTarget = PickWanderTarget(ai.transform.position, ai.wanderRadius),
            nextWanderAt = Time.time + Mathf.Max(0.1f, ai.wanderInterval),
            ownerCombatant = ownerCombatant,
            enemyCombatant = enemyCombatant,
            primaryDamageDealer = primaryDamageDealer,
            damageDealers = damageDealers,
            damageDealerEnabledStates = dealerEnabledStates,
            bossController = bossController,
            outgoingDebuff = ownerCombatant != null ? ownerCombatant.GetComponent<RelicOutgoingDamageDebuff>() : null,
            bossEffects = ownerCombatant != null ? ownerCombatant.GetComponent<BossSpecialEffects>() : null,
            attackRange = attackRange,
            attackCooldown = attackCooldown,
            nextAttackAt = Time.time + Random.Range(0f, attackCooldown),
            systemMeleeEnabled = meleeEnabled,
            lastTickFrame = -1
        };

        AddOrUpdateState(ownerId, state);
        return true;
    }

    private bool Register(EnemyAI ai)
    {
        if (ai == null)
            return false;

        Rigidbody rb = ai.GetComponent<Rigidbody>();
        if (rb == null)
            return false;

        int ownerId = ai.GetInstanceID();
        enemySet.Add(ai);

        Combatant ownerCombatant = ai.GetComponent<Combatant>();
        EnemyCombatant enemyCombatant = ai.GetComponent<EnemyCombatant>();
        EnemyDamageDealer[] damageDealers = ai.GetComponentsInChildren<EnemyDamageDealer>(true);
        EnemyDamageDealer primaryDamageDealer = GetPrimaryDamageDealer(damageDealers);
        BossEnemyController bossController = ownerCombatant != null ? ownerCombatant.GetComponent<BossEnemyController>() : null;
        bool meleeEnabled = enableSystemMeleeForHorde && bossController == null && ownerCombatant != null && enemyCombatant != null;

        bool[] dealerEnabledStates = meleeEnabled
            ? DisableDamageDealers(damageDealers)
            : null;

        float attackRange = Mathf.Max(0.2f, ai.attackRadius);
        float attackCooldown = ResolveCooldownFromDealers(
            damageDealers,
            Mathf.Max(0.05f, ai.attackCooldown)
        );

        AgentState state = new AgentState
        {
            kind = AgentKind.Enemy,
            ownerId = ownerId,
            zombie = null,
            enemy = ai,
            transform = ai.transform,
            rb = rb,
            animator = ai.GetComponentInChildren<Animator>(),
            anchor = ai.transform.position,
            wanderTarget = PickWanderTarget(ai.transform.position, ai.wanderRadius),
            nextWanderAt = Time.time + Random.Range(ai.wanderIntervalMin, ai.wanderIntervalMax),
            ownerCombatant = ownerCombatant,
            enemyCombatant = enemyCombatant,
            primaryDamageDealer = primaryDamageDealer,
            damageDealers = damageDealers,
            damageDealerEnabledStates = dealerEnabledStates,
            bossController = bossController,
            outgoingDebuff = ownerCombatant != null ? ownerCombatant.GetComponent<RelicOutgoingDamageDebuff>() : null,
            bossEffects = ownerCombatant != null ? ownerCombatant.GetComponent<BossSpecialEffects>() : null,
            attackRange = attackRange,
            attackCooldown = attackCooldown,
            nextAttackAt = Time.time + Random.Range(0f, attackCooldown),
            systemMeleeEnabled = meleeEnabled,
            lastTickFrame = -1
        };

        AddOrUpdateState(ownerId, state);
        return true;
    }

    private bool Register(ZombiePerception perception)
    {
        if (perception == null || perceptionSet.Contains(perception))
            return false;

        perceptionSet.Add(perception);
        perceptionAgents.Add(perception);
        return true;
    }

    private void AddOrUpdateState(int ownerId, AgentState state)
    {
        if (stateIndexByOwnerId.TryGetValue(ownerId, out int existingIndex))
        {
            RestoreExternalComponents(agentStates[existingIndex]);
            agentStates[existingIndex] = state;
            return;
        }

        stateIndexByOwnerId.Add(ownerId, agentStates.Count);
        agentStates.Add(state);
    }

    private void UnregisterInternal(ZombieAI ai)
    {
        if (!zombieSet.Remove(ai))
            return;

        RemoveState(ai.GetInstanceID());
    }

    private void UnregisterInternal(EnemyAI ai)
    {
        if (!enemySet.Remove(ai))
            return;

        RemoveState(ai.GetInstanceID());
    }

    private void UnregisterInternal(ZombiePerception perception)
    {
        if (!perceptionSet.Remove(perception))
            return;

        perceptionAgents.Remove(perception);
    }

    private void RemoveState(int ownerId)
    {
        if (!stateIndexByOwnerId.TryGetValue(ownerId, out int index))
            return;

        AgentState removed = agentStates[index];
        RestoreExternalComponents(removed);

        int lastIndex = agentStates.Count - 1;
        AgentState last = agentStates[lastIndex];

        agentStates[index] = last;
        agentStates.RemoveAt(lastIndex);
        stateIndexByOwnerId.Remove(ownerId);

        if (index < agentStates.Count)
            stateIndexByOwnerId[last.ownerId] = index;
    }

    private void ResolvePlayerIfNeeded()
    {
        if (player != null && player.gameObject.activeInHierarchy)
        {
            if (playerCombatant == null || playerCombatant.transform != player)
                ResolvePlayerCombatRefs();
            return;
        }

        player = PlayerLocator.GetTransform();
        ResolvePlayerCombatRefs();
    }

    private void ResolvePlayerCombatRefs()
    {
        if (player == null)
        {
            playerCombatant = null;
            playerRelics = null;
            return;
        }

        playerCombatant = player.GetComponent<Combatant>();
        if (playerCombatant == null)
            playerCombatant = player.GetComponentInParent<Combatant>();

        playerRelics = player.GetComponent<PlayerRelicController>();
        if (playerRelics == null)
            playerRelics = player.GetComponentInParent<PlayerRelicController>();
    }

    private RuntimeSnapshot GetRuntimeSnapshot()
    {
        RuntimeSnapshot snapshot = lastRuntimeSnapshot;
        snapshot.frame = Time.frameCount;
        snapshot.agentCount = agentStates.Count;
        snapshot.zombieCount = zombieSet.Count;
        snapshot.enemyCount = enemySet.Count;
        snapshot.perceptionCount = perceptionAgents.Count;
        return snapshot;
    }

    private void RefreshRuntimeSnapshot()
    {
        lastRuntimeSnapshot.frame = Time.frameCount;
        lastRuntimeSnapshot.agentCount = agentStates.Count;
        lastRuntimeSnapshot.zombieCount = zombieSet.Count;
        lastRuntimeSnapshot.enemyCount = enemySet.Count;
        lastRuntimeSnapshot.perceptionCount = perceptionAgents.Count;
        lastRuntimeSnapshot.nearTicksThisFixed = lastNearTicksThisFixed;
        lastRuntimeSnapshot.farZombieTicksThisFixed = lastFarZombieTicksThisFixed;
        lastRuntimeSnapshot.farEnemyTicksThisFixed = lastFarEnemyTicksThisFixed;
        lastRuntimeSnapshot.perceptionTicksThisFrame = lastPerceptionTicksThisFrame;
        lastRuntimeSnapshot.fixedUpdateDurationMs = lastFixedUpdateDurationMs;
        lastRuntimeSnapshot.updateDurationMs = lastUpdateDurationMs;
    }

    private void TickAgentStates()
    {
        int count = agentStates.Count;
        if (count == 0)
        {
            lastNearTicksThisFixed = 0;
            lastFarZombieTicksThisFixed = 0;
            lastFarEnemyTicksThisFixed = 0;
            return;
        }

        nearZombieDistanceSqr = Mathf.Max(1f, nearZombieDistance * nearZombieDistance);
        int frame = Time.frameCount;
        int nearProcessed = 0;

        // Near agents run every fixed step to keep responsiveness.
        for (int i = 0; i < count; i++)
        {
            AgentState state = agentStates[i];
            if (!IsStateValid(state))
                continue;

            if (!IsNearPlayer(state))
                continue;

            TickState(ref state);
            state.lastTickFrame = frame;
            agentStates[i] = state;
            nearProcessed++;
        }

        int zombieBudget = ComputeBudget(zombieSet.Count, minFarZombieTicksPerFixed, maxFarZombieTicksPerFixed, 3);
        int enemyBudget = ComputeBudget(enemySet.Count, minFarEnemyTicksPerFixed, maxFarEnemyTicksPerFixed, 3);

        int farZombieProcessed = TickFarStates(AgentKind.Zombie, zombieBudget, ref zombieCursor, frame);
        int farEnemyProcessed = TickFarStates(AgentKind.Enemy, enemyBudget, ref enemyCursor, frame);

        lastNearTicksThisFixed = nearProcessed;
        lastFarZombieTicksThisFixed = farZombieProcessed;
        lastFarEnemyTicksThisFixed = farEnemyProcessed;
    }

    private int TickFarStates(AgentKind kind, int budget, ref int cursor, int frame)
    {
        int count = agentStates.Count;
        if (count == 0 || budget <= 0)
            return 0;

        int processed = 0;
        int safety = 0;
        int safetyLimit = count * 3;

        while (processed < budget && safety < safetyLimit)
        {
            if (cursor >= count)
                cursor = 0;

            int index = cursor++;
            safety++;

            AgentState state = agentStates[index];
            if (state.kind != kind || state.lastTickFrame == frame || !IsStateValid(state) || IsNearPlayer(state))
                continue;

            TickState(ref state);
            state.lastTickFrame = frame;
            agentStates[index] = state;
            processed++;
        }

        return processed;
    }

    private void TickState(ref AgentState state)
    {
        if (state.kind == AgentKind.Zombie)
        {
            TickZombie(ref state);
            return;
        }

        TickEnemy(ref state);
    }

    private void TickZombie(ref AgentState state)
    {
        ZombieAI ai = state.zombie;
        Rigidbody rb = state.rb;
        Transform tr = state.transform;
        if (ai == null || rb == null || tr == null)
            return;

        Vector3 position = tr.position;
        Vector3 moveDir = Vector3.zero;
        bool hasPlayerTarget = false;

        if (player != null && player.gameObject.activeInHierarchy)
        {
            Vector3 toPlayer = player.position - position;
            toPlayer.y = 0f;
            float sqrToPlayer = toPlayer.sqrMagnitude;
            if (sqrToPlayer <= ai.detectionRadius * ai.detectionRadius)
            {
                hasPlayerTarget = true;
                if (sqrToPlayer > 0.001f)
                    moveDir = toPlayer * (1f / Mathf.Sqrt(sqrToPlayer));
            }
        }

        if (!hasPlayerTarget)
        {
            bool reachedWanderTarget = HorizontalDistanceSqr(position, state.wanderTarget) <= 0.16f;
            if (Time.time >= state.nextWanderAt || reachedWanderTarget)
            {
                state.wanderTarget = PickWanderTarget(position, ai.wanderRadius);
                state.nextWanderAt = Time.time + Mathf.Max(0.1f, ai.wanderInterval);
            }

            Vector3 toWander = state.wanderTarget - position;
            toWander.y = 0f;
            float sqr = toWander.sqrMagnitude;
            if (sqr > 0.01f)
                moveDir = toWander * (1f / Mathf.Sqrt(sqr));
        }

        if (moveDir.sqrMagnitude > 0.001f)
        {
            moveDir.Normalize();
            Vector3 velocity = rb.linearVelocity;
            velocity.x = moveDir.x * ai.moveSpeed;
            velocity.z = moveDir.z * ai.moveSpeed;
            rb.linearVelocity = velocity;

            Quaternion targetRot = Quaternion.LookRotation(moveDir);
            tr.rotation = Quaternion.Slerp(tr.rotation, targetRot, ai.rotationSpeed * Time.fixedDeltaTime);
        }
        else
        {
            Vector3 velocity = rb.linearVelocity;
            velocity.x = 0f;
            velocity.z = 0f;
            rb.linearVelocity = velocity;
        }

        if (state.animator != null)
            state.animator.SetFloat("Speed", new Vector2(rb.linearVelocity.x, rb.linearVelocity.z).magnitude);

        EnforceSystemMeleeOwnership(state);
        TrySystemMeleeDamage(ref state);
    }

    private void TickEnemy(ref AgentState state)
    {
        EnemyAI ai = state.enemy;
        Rigidbody rb = state.rb;
        Transform tr = state.transform;
        if (ai == null || rb == null || tr == null)
            return;

        Vector3 position = tr.position;
        Vector3 moveDir = Vector3.zero;
        float speed = 0f;
        bool facePlayerOnly = false;

        if (player != null && player.gameObject.activeInHierarchy)
        {
            Vector3 toPlayer = player.position - position;
            toPlayer.y = 0f;
            float sqrToPlayer = toPlayer.sqrMagnitude;

            float attackRadiusSqr = ai.attackRadius * ai.attackRadius;
            float detectionRadiusSqr = ai.detectionRadius * ai.detectionRadius;

            if (sqrToPlayer <= attackRadiusSqr)
            {
                facePlayerOnly = sqrToPlayer > 0.0001f;
            }
            else if (sqrToPlayer <= detectionRadiusSqr && sqrToPlayer > 0.0001f)
            {
                moveDir = toPlayer * (1f / Mathf.Sqrt(sqrToPlayer));
                speed = ai.chaseSpeed;
            }
        }

        if (moveDir == Vector3.zero && !facePlayerOnly)
        {
            bool reachedWanderTarget = HorizontalDistanceSqr(position, state.wanderTarget) <= 0.16f;
            if (Time.time >= state.nextWanderAt || reachedWanderTarget)
            {
                state.wanderTarget = PickWanderTarget(state.anchor, ai.wanderRadius);
                float min = Mathf.Max(0.1f, ai.wanderIntervalMin);
                float max = Mathf.Max(min + 0.05f, ai.wanderIntervalMax);
                state.nextWanderAt = Time.time + Random.Range(min, max);
            }

            Vector3 toWander = state.wanderTarget - position;
            toWander.y = 0f;
            float sqr = toWander.sqrMagnitude;
            if (sqr > 0.04f)
            {
                moveDir = toWander * (1f / Mathf.Sqrt(sqr));
                speed = ai.wanderSpeed;
            }
        }

        if (moveDir.sqrMagnitude > 0.001f)
        {
            moveDir.Normalize();
            Vector3 velocity = rb.linearVelocity;
            velocity.x = moveDir.x * speed;
            velocity.z = moveDir.z * speed;
            rb.linearVelocity = velocity;

            Quaternion targetRot = Quaternion.LookRotation(moveDir);
            rb.MoveRotation(Quaternion.RotateTowards(rb.rotation, targetRot, 720f * Time.fixedDeltaTime));
        }
        else
        {
            Vector3 velocity = rb.linearVelocity;
            velocity.x = 0f;
            velocity.z = 0f;
            rb.linearVelocity = velocity;

            if (facePlayerOnly && player != null)
            {
                Vector3 faceDir = player.position - position;
                faceDir.y = 0f;
                if (faceDir.sqrMagnitude > 0.001f)
                {
                    Quaternion targetRot = Quaternion.LookRotation(faceDir.normalized);
                    rb.MoveRotation(Quaternion.RotateTowards(rb.rotation, targetRot, 720f * Time.fixedDeltaTime));
                }
            }
        }

        EnforceSystemMeleeOwnership(state);
        TrySystemMeleeDamage(ref state);
    }

    private void TrySystemMeleeDamage(ref AgentState state)
    {
        if (!state.systemMeleeEnabled || player == null || playerCombatant == null || playerCombatant.IsDead)
            return;

        if (Time.time < state.nextAttackAt)
            return;

        float range = Mathf.Max(0.2f, state.attackRange + meleeRangePadding);
        if (HorizontalDistanceSqr(state.transform.position, player.position) > range * range)
            return;

        Combatant owner = state.ownerCombatant;
        if (owner == null || owner.IsDead)
        {
            state.nextAttackAt = Time.time + 0.2f;
            return;
        }

        if (state.bossController != null && !state.bossController.CanDealDamage)
        {
            state.nextAttackAt = Time.time + 0.1f;
            return;
        }

        float damage = ResolveAgentDamage(state);
        if (damage <= 0f)
        {
            state.nextAttackAt = Time.time + Mathf.Max(0.05f, state.attackCooldown);
            return;
        }

        if (playerRelics != null)
            damage = playerRelics.ModifyIncomingDamage(owner, damage);

        if (damage <= 0f)
        {
            state.nextAttackAt = Time.time + Mathf.Max(0.05f, state.attackCooldown);
            return;
        }

        playerCombatant.TakeDamage(damage, owner.transform);

        if (state.bossEffects != null)
            state.bossEffects.ApplyOnHit(playerCombatant);

        PlaySlashAudio(state.primaryDamageDealer);
        state.nextAttackAt = Time.time + Mathf.Max(0.05f, state.attackCooldown);
    }

    private static float ResolveAgentDamage(AgentState state)
    {
        if (state.enemyCombatant == null || state.enemyCombatant.stats == null)
            return 0f;

        float damage = state.enemyCombatant.stats.damage;
        if (state.outgoingDebuff != null)
            damage *= state.outgoingDebuff.GetDamageMultiplier();

        return damage;
    }

    private static void PlaySlashAudio(EnemyDamageDealer dealer)
    {
        if (dealer == null || dealer.audioSource == null)
            return;

        AudioClip clip = null;
        int variant = Random.Range(0, 3);
        switch (variant)
        {
            case 0:
                clip = dealer.slash_1;
                break;
            case 1:
                clip = dealer.slash_2;
                break;
            default:
                clip = dealer.slash_3;
                break;
        }

        if (clip != null)
            dealer.audioSource.PlayOneShot(clip);
    }

    private void TickPerceptionAgents()
    {
        int count = perceptionAgents.Count;
        if (count == 0)
        {
            lastPerceptionTicksThisFrame = 0;
            return;
        }

        int budget = ComputeBudget(count, minPerceptionTicksPerFrame, maxPerceptionTicksPerFrame, 2);
        int processed = 0;
        int safety = 0;
        int safetyLimit = count * 2;

        while (processed < budget && safety < safetyLimit)
        {
            if (perceptionCursor >= count)
                perceptionCursor = 0;

            ZombiePerception perception = perceptionAgents[perceptionCursor];
            perceptionCursor++;
            safety++;

            if (perception == null || !perception.isActiveAndEnabled)
                continue;

            UpdatePerceptionFromEcs(perception);
            processed++;
        }

        lastPerceptionTicksThisFrame = processed;
    }

    private void UpdatePerceptionFromEcs(ZombiePerception perception)
    {
        if (player == null || !player.gameObject.activeInHierarchy)
        {
            perception.SetVisibleTargetsFromSystem(null, null);
            return;
        }

        Transform tr = perception.transform;
        Vector3 toPlayer = player.position - tr.position;
        float sqr = toPlayer.sqrMagnitude;
        float maxSqr = perception.viewRadius * perception.viewRadius;
        if (sqr <= 0.0001f || sqr > maxSqr)
        {
            perception.SetVisibleTargetsFromSystem(null, null);
            return;
        }

        float distance = Mathf.Sqrt(sqr);
        Vector3 dir = toPlayer / distance;
        float minDot = Mathf.Cos(perception.viewAngle * 0.5f * Mathf.Deg2Rad);
        bool visible = Vector3.Dot(tr.forward, dir) >= minDot;

        if (visible && perception.obstacleMask != 0)
        {
            Vector3 rayOrigin = tr.position + Vector3.up * 0.5f;
            if (Physics.Raycast(rayOrigin, dir, distance, perception.obstacleMask, QueryTriggerInteraction.Ignore))
                visible = false;
        }

        perception.SetVisibleTargetsFromSystem(visible ? player : null, null);
    }

    private static void RestoreExternalComponents(AgentState state)
    {
        EnemyDamageDealer[] dealers = state.damageDealers;
        bool[] previousStates = state.damageDealerEnabledStates;
        if (dealers == null || previousStates == null)
            return;

        int restoreCount = Mathf.Min(dealers.Length, previousStates.Length);
        for (int i = 0; i < restoreCount; i++)
        {
            EnemyDamageDealer dealer = dealers[i];
            if (dealer == null)
                continue;

            bool shouldBeEnabled = previousStates[i];
            if (shouldBeEnabled && !dealer.enabled)
                dealer.enabled = true;
        }
    }

    private static void EnforceSystemMeleeOwnership(AgentState state)
    {
        if (!state.systemMeleeEnabled || state.damageDealers == null)
            return;

        for (int i = 0; i < state.damageDealers.Length; i++)
        {
            EnemyDamageDealer dealer = state.damageDealers[i];
            if (dealer != null && dealer.enabled)
                dealer.enabled = false;
        }
    }

    private static EnemyDamageDealer GetPrimaryDamageDealer(EnemyDamageDealer[] dealers)
    {
        if (dealers == null || dealers.Length == 0)
            return null;

        for (int i = 0; i < dealers.Length; i++)
        {
            if (dealers[i] != null)
                return dealers[i];
        }

        return null;
    }

    private static bool[] DisableDamageDealers(EnemyDamageDealer[] dealers)
    {
        if (dealers == null || dealers.Length == 0)
            return null;

        bool[] states = new bool[dealers.Length];
        for (int i = 0; i < dealers.Length; i++)
        {
            EnemyDamageDealer dealer = dealers[i];
            if (dealer == null)
                continue;

            states[i] = dealer.enabled;
            if (dealer.enabled)
                dealer.enabled = false;
        }

        return states;
    }

    private static float ResolveCooldownFromDealers(EnemyDamageDealer[] dealers, float fallbackCooldown)
    {
        float cooldown = Mathf.Max(0.05f, fallbackCooldown);
        if (dealers == null || dealers.Length == 0)
            return cooldown;

        for (int i = 0; i < dealers.Length; i++)
        {
            EnemyDamageDealer dealer = dealers[i];
            if (dealer == null)
                continue;

            cooldown = Mathf.Max(cooldown, Mathf.Max(0.05f, dealer.hitCooldown));
        }

        return cooldown;
    }

    private bool IsStateValid(AgentState state)
    {
        if (state.transform == null || state.rb == null)
            return false;

        if (state.kind == AgentKind.Zombie)
            return state.zombie != null && state.zombie.isActiveAndEnabled;

        return state.enemy != null && state.enemy.isActiveAndEnabled;
    }

    private bool IsNearPlayer(AgentState state)
    {
        if (player == null)
            return false;

        return HorizontalDistanceSqr(state.transform.position, player.position) <= nearZombieDistanceSqr;
    }

    private void CleanupIfNeeded()
    {
        if (Time.unscaledTime < nextCleanupAt)
            return;

        nextCleanupAt = Time.unscaledTime + Mathf.Max(0.1f, cleanupInterval);
        CleanupAgentStates();
        CleanupPerceptions();
    }

    private void CleanupAgentStates()
    {
        for (int i = agentStates.Count - 1; i >= 0; i--)
        {
            AgentState state = agentStates[i];
            if (IsStateValid(state))
                continue;

            RemoveState(state.ownerId);
        }

        zombieSet.RemoveWhere(z => z == null || !z.isActiveAndEnabled);
        enemySet.RemoveWhere(e => e == null || !e.isActiveAndEnabled);
    }

    private void CleanupPerceptions()
    {
        for (int i = perceptionAgents.Count - 1; i >= 0; i--)
        {
            ZombiePerception p = perceptionAgents[i];
            if (p != null && p.isActiveAndEnabled)
                continue;

            perceptionAgents.RemoveAt(i);
        }

        perceptionSet.RemoveWhere(p => p == null || !p.isActiveAndEnabled);
    }

    private static Vector3 PickWanderTarget(Vector3 center, float radius)
    {
        Vector2 rnd = Random.insideUnitCircle * Mathf.Max(0.2f, radius);
        return center + new Vector3(rnd.x, 0f, rnd.y);
    }

    private static float HorizontalDistanceSqr(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x;
        float dz = a.z - b.z;
        return dx * dx + dz * dz;
    }

    private static int ComputeBudget(int count, int minBudget, int maxBudget, int divider)
    {
        if (count <= 0)
            return 0;

        int dynamicBudget = minBudget + count / Mathf.Max(1, divider);
        return Mathf.Clamp(dynamicBudget, minBudget, maxBudget);
    }
}
