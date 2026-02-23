using System.Collections.Generic;
using UnityEngine;
using GrassSim.Combat;
using GrassSim.Core;

[CreateAssetMenu(
    menuName = "GrassSim/Relics/Effects/Legendary/Ossuary Choir",
    fileName = "Relic_OssuaryChoir"
)]
public class OssuaryChoir : RelicEffect
{
    [Header("Trigger")]
    [Min(1)] public int killsRequired = 6;
    public float killWindow = 5f;
    [Min(0.1f)] public float targetTtkSeconds = 1.15f;

    [Header("Choir")]
    [Min(1)] public int skullCount = 3;
    public float baseDuration = 8f;
    public float durationPerStack = 0.4f;

    [Header("Skull Orbit")]
    public float orbitRadius = 1.8f;
    public float orbitHeight = 1.4f;
    public float orbitSpeed = 130f;

    [Header("Skull Attack")]
    public float fireInterval = 0.8f;
    public float boltRange = 14f;
    public float baseBoltDamage = 26f;
    public float boltDamagePerStack = 5f;
    public LayerMask enemyMask;

    [Header("Optional Visual Prefab")]
    public GameObject skullPrefab;

    [Header("Skull Prefab Tuning")]
    [Min(0.01f)] public float skullPrefabScale = 0.08f;
    public Vector3 skullPrefabRotation = new(0f, 180f, 0f);

    public override void OnAcquire(PlayerRelicController player, int stacks)
    {
        Attach(player)?.Configure(this, stacks);
    }

    public override void OnStack(PlayerRelicController player, int stacks)
    {
        Attach(player)?.Configure(this, stacks);
    }

    private OssuaryChoirRuntime Attach(PlayerRelicController player)
    {
        if (player == null)
            return null;

        OssuaryChoirRuntime rt = player.GetComponent<OssuaryChoirRuntime>();
        if (rt == null)
            rt = player.gameObject.AddComponent<OssuaryChoirRuntime>();

        return rt;
    }
}

public class OssuaryChoirRuntime : MonoBehaviour, IRelicBatchedUpdate, IRelicBatchedCadence
{
    private readonly List<float> killTimes = new();
    private readonly List<OssuaryChoirSkull> skulls = new();
    private readonly Queue<OssuaryChoirSkull> skullPool = new();

    private const int MaxSkullPoolSize = 10;

    private PlayerRelicController player;
    private OssuaryChoir cfg;
    private int stacks;
    private bool subscribed;
    private float choirEndsAt;
    public bool IsChoirActiveNow => Time.time < choirEndsAt;
    public float ChoirTimeRemaining => Mathf.Max(0f, choirEndsAt - Time.time);
    public int ActiveSkullCount => skulls.Count;
    public int KillProgress => killTimes.Count;
    public int RequiredKills
    {
        get
        {
            if (cfg == null)
                return 0;

            return RelicProcPacingService.GetKillsRequired(
                player,
                Mathf.Max(1, cfg.killsRequired),
                cfg.targetTtkSeconds
            );
        }
    }
    public float KillWindowSeconds
    {
        get
        {
            if (cfg == null)
                return 0f;

            return RelicProcPacingService.GetKillWindow(
                player,
                cfg.killWindow,
                cfg.targetTtkSeconds
            );
        }
    }

    private void Awake()
    {
        player = GetComponent<PlayerRelicController>();
    }

    private void OnEnable()
    {
        RelicBatchedTickSystem.Register(this);
        TrySubscribe();
    }

    private void OnDisable()
    {
        RelicBatchedTickSystem.Unregister(this);
        TryUnsubscribe();
        killTimes.Clear();
        ClearSkulls();
    }

    private void OnDestroy()
    {
        ClearSkulls();
        while (skullPool.Count > 0)
        {
            OssuaryChoirSkull pooled = skullPool.Dequeue();
            if (pooled != null)
                Destroy(pooled.gameObject);
        }
    }

    public void Configure(OssuaryChoir config, int stackCount)
    {
        cfg = config;
        stacks = Mathf.Max(1, stackCount);
        TrySubscribe();
    }

    public bool IsBatchedUpdateActive => isActiveAndEnabled && cfg != null;

    public float BatchedUpdateInterval => 0.1f;

    public RelicTickArchetype BatchedTickArchetype => RelicTickArchetype.PlayerState;

    public void TickFromRelicBatch(float now, float deltaTime)
    {
        if (choirEndsAt > 0f && now >= choirEndsAt)
            EndChoir();

        float pacedWindow = player != null
            ? RelicProcPacingService.GetKillWindow(player, cfg.killWindow, cfg.targetTtkSeconds)
            : Mathf.Max(0.2f, cfg.killWindow);
        CleanupOldKills(pacedWindow, now);
    }

    private bool IsChoirActive(float now)
    {
        return now < choirEndsAt;
    }

    private void TrySubscribe()
    {
        if (subscribed || player == null)
            return;

        player.OnMeleeKill += OnMeleeKill;
        subscribed = true;
    }

    private void TryUnsubscribe()
    {
        if (!subscribed || player == null)
            return;

        player.OnMeleeKill -= OnMeleeKill;
        subscribed = false;
    }

    private void OnMeleeKill(Combatant target, float damage, bool isCrit)
    {
        if (cfg == null)
            return;

        RelicProcPacingService.NotifyMeleeKill(player);

        int requiredKills = RelicProcPacingService.GetKillsRequired(
            player,
            Mathf.Max(1, cfg.killsRequired),
            cfg.targetTtkSeconds
        );
        float pacedWindow = RelicProcPacingService.GetKillWindow(
            player,
            cfg.killWindow,
            cfg.targetTtkSeconds
        );

        killTimes.Add(Time.time);
        CleanupOldKills(pacedWindow, Time.time);

        if (killTimes.Count < requiredKills)
            return;

        killTimes.Clear();
        StartChoir();
    }

    private void StartChoir()
    {
        float duration = cfg.baseDuration + cfg.durationPerStack * Mathf.Max(0, stacks - 1);
        choirEndsAt = Time.time + Mathf.Max(0.2f, duration);

        if (skulls.Count > 0)
            return;

        int count = Mathf.Max(1, cfg.skullCount);
        float boltDamage = cfg.baseBoltDamage + cfg.boltDamagePerStack * Mathf.Max(0, stacks - 1);
        LayerMask mask = cfg.enemyMask.value != 0 ? cfg.enemyMask : LayerMask.GetMask("Enemy", "Zombie");

        for (int i = 0; i < count; i++)
        {
            OssuaryChoirSkull skull = RentSkull();
            if (skull == null)
                continue;

            skull.Configure(
                owner: transform,
                slotIndex: i,
                slotCount: count,
                orbitRadius: cfg.orbitRadius,
                orbitHeight: cfg.orbitHeight,
                orbitSpeed: cfg.orbitSpeed,
                fireInterval: cfg.fireInterval,
                boltRange: cfg.boltRange,
                boltDamage: boltDamage,
                enemyMask: mask
            );

            skulls.Add(skull);
        }

        RelicDamageText.PlayGeneratedEventFeedback(transform, RelicRarity.Legendary, 1.1f);
    }

    private void EndChoir()
    {
        choirEndsAt = 0f;
        ClearSkulls();
    }

    private void ClearSkulls()
    {
        for (int i = 0; i < skulls.Count; i++)
        {
            OssuaryChoirSkull skull = skulls[i];
            if (skull == null)
                continue;

            if (skullPool.Count >= MaxSkullPoolSize)
            {
                Destroy(skull.gameObject);
                continue;
            }

            skull.gameObject.SetActive(false);
            skullPool.Enqueue(skull);
        }

        skulls.Clear();
    }

    private OssuaryChoirSkull RentSkull()
    {
        OssuaryChoirSkull skull = null;
        while (skullPool.Count > 0 && skull == null)
            skull = skullPool.Dequeue();

        if (skull == null)
            skull = CreateSkullInstance();

        if (skull == null)
            return null;

        GameObject go = skull.gameObject;
        go.SetActive(true);
        go.transform.position = transform.position;
        go.transform.rotation = Quaternion.identity;
        return skull;
    }

    private OssuaryChoirSkull CreateSkullInstance()
    {
        if (cfg == null)
            return null;

        GameObject go;
        if (cfg.skullPrefab != null)
        {
            go = TryInstantiateSkullPrefab(cfg.skullPrefab);
            if (go != null)
            {
                RelicDamageText.StyleSpawnedSkullVisual(
                    go,
                    RelicRarity.Legendary,
                    cfg.skullPrefabScale,
                    cfg.skullPrefabRotation
                );
            }
        }
        else
        {
            go = null;
        }

        if (go == null)
        {
            go = RelicDamageText.CreateGeneratedSkull(
                "OssuaryChoirSkull",
                RelicRarity.Legendary,
                0.35f
            );
            go.transform.position = transform.position;
        }

        if (go == null)
            return null;

        OssuaryChoirSkull skull = go.GetComponent<OssuaryChoirSkull>();
        if (skull == null)
            skull = go.AddComponent<OssuaryChoirSkull>();

        return skull;
    }

    private GameObject TryInstantiateSkullPrefab(GameObject prefab)
    {
        if (prefab == null)
            return null;

        try
        {
            UnityEngine.Object spawned = Instantiate((UnityEngine.Object)prefab, transform.position, Quaternion.identity);
            if (spawned is GameObject go)
                return go;

            if (spawned is Component component)
                return component.gameObject;

            if (spawned != null)
                Destroy(spawned);
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[OssuaryChoir] Failed to instantiate skull prefab '{prefab.name}'. Falling back to generated skull. {ex.Message}", this);
        }

        return null;
    }

    private void CleanupOldKills(float effectiveWindow, float now)
    {
        if (killTimes.Count == 0)
            return;

        float minTime = now - Mathf.Max(0.2f, effectiveWindow);
        for (int i = killTimes.Count - 1; i >= 0; i--)
        {
            if (killTimes[i] < minTime)
                killTimes.RemoveAt(i);
        }
    }
}

public class OssuaryChoirSkull : MonoBehaviour, IRelicBatchedUpdate, IRelicBatchedCadence
{
    private Transform owner;
    private int slotIndex;
    private int slotCount;
    private float orbitRadius;
    private float orbitHeight;
    private float orbitSpeed;
    private float fireInterval;
    private float boltRange;
    private float boltDamage;
    private LayerMask enemyMask;

    private float nextFireAt;

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
        int slotIndex,
        int slotCount,
        float orbitRadius,
        float orbitHeight,
        float orbitSpeed,
        float fireInterval,
        float boltRange,
        float boltDamage,
        LayerMask enemyMask
    )
    {
        this.owner = owner;
        this.slotIndex = Mathf.Max(0, slotIndex);
        this.slotCount = Mathf.Max(1, slotCount);
        this.orbitRadius = Mathf.Max(0.1f, orbitRadius);
        this.orbitHeight = orbitHeight;
        this.orbitSpeed = orbitSpeed;
        this.fireInterval = Mathf.Max(0.05f, fireInterval);
        this.boltRange = Mathf.Max(0.5f, boltRange);
        this.boltDamage = Mathf.Max(1f, boltDamage);
        this.enemyMask = enemyMask;
        nextFireAt = Time.time + Random.Range(0f, this.fireInterval * 0.5f);
        EnemyQueryService.ConfigureOwnerBudget(this, 10);
    }

    public bool IsBatchedUpdateActive => isActiveAndEnabled;

    public float BatchedUpdateInterval => 0.033f;

    public RelicTickArchetype BatchedTickArchetype => RelicTickArchetype.PlayerAura;

    public void TickFromRelicBatch(float now, float deltaTime)
    {
        if (owner == null)
        {
            gameObject.SetActive(false);
            return;
        }

        float angle = now * orbitSpeed + (360f * slotIndex / slotCount);
        Vector3 offset = Quaternion.Euler(0f, angle, 0f) * Vector3.forward * orbitRadius;
        transform.position = owner.position + Vector3.up * orbitHeight + offset;

        if (now >= nextFireAt)
        {
            nextFireAt = now + fireInterval;
            FireAtNearestEnemy();
        }
    }

    private void FireAtNearestEnemy()
    {
        Collider[] hits;
        if (enemyMask.value != 0)
            hits = EnemyQueryService.OverlapSphere(transform.position, boltRange, enemyMask, QueryTriggerInteraction.Ignore, this);
        else
            hits = EnemyQueryService.OverlapSphere(transform.position, boltRange, ~0, QueryTriggerInteraction.Ignore, this);

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

        if (best != null)
            RelicDamageText.Deal(best, boltDamage, owner, null, "Ossuary Choir");
    }
}
