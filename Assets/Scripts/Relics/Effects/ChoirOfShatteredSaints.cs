using System.Collections.Generic;
using UnityEngine;
using GrassSim.Combat;
using GrassSim.Core;

[CreateAssetMenu(
    menuName = "GrassSim/Relics/Effects/Mythic/Choir Of Shattered Saints",
    fileName = "Relic_ChoirOfShatteredSaints"
)]
public class ChoirOfShatteredSaints : RelicEffect, ISpeedModifier
{
    [Header("Rank")]
    public float killWindow = 4f;
    [Min(0.1f)] public float targetTtkSeconds = 1.05f;
    public int maxRank = 5;
    public float speedPerRank = 0.05f;

    [Header("Skulls")]
    public float orbitRadius = 2f;
    public float orbitHeight = 1.4f;
    public float orbitSpeed = 125f;
    public float fireInterval = 0.8f;
    public float fireRange = 12f;
    public float baseSkullDamage = 18f;
    public float skullDamagePerStack = 3f;
    public LayerMask enemyMask;
    public GameObject skullPrefab;

    [Header("Skull Prefab Tuning")]
    [Min(0.01f)] public float skullPrefabScale = 0.08f;
    public Vector3 skullPrefabRotation = new(0f, 180f, 0f);

    [Header("Shock Pulse")]
    public float shockPulseRadius = 5f;
    public float baseShockDamage = 35f;
    public float shockDamagePerRemovedRank = 12f;

    public override void OnAcquire(PlayerRelicController player, int stacks)
    {
        Attach(player)?.Configure(this, stacks);
    }

    public override void OnStack(PlayerRelicController player, int stacks)
    {
        Attach(player)?.Configure(this, stacks);
    }

    public float GetSpeedBonus(PlayerRelicController player, int stacks)
    {
        var rt = player != null ? player.GetComponent<ChoirOfShatteredSaintsRuntime>() : null;
        if (rt == null)
            return 0f;

        return rt.Rank * Mathf.Max(0f, speedPerRank);
    }

    private ChoirOfShatteredSaintsRuntime Attach(PlayerRelicController player)
    {
        if (player == null)
            return null;

        var rt = player.GetComponent<ChoirOfShatteredSaintsRuntime>();
        if (rt == null)
            rt = player.gameObject.AddComponent<ChoirOfShatteredSaintsRuntime>();

        return rt;
    }
}

public class ChoirOfShatteredSaintsRuntime : MonoBehaviour
{
    private readonly List<ChoirOfShatteredSaintsSkull> skulls = new();
    private readonly Queue<ChoirOfShatteredSaintsSkull> skullPool = new();

    private const int MaxSkullPoolSize = 10;

    private PlayerRelicController player;
    private ChoirOfShatteredSaints cfg;
    private int stacks;
    private bool subscribed;
    private int rank;
    private float lastKillAt = -999f;

    public int Rank => rank;

    private void Awake()
    {
        player = GetComponent<PlayerRelicController>();
    }

    private void OnEnable()
    {
        TrySubscribe();
    }

    private void OnDisable()
    {
        TryUnsubscribe();
        ClearSkulls();
    }

    private void OnDestroy()
    {
        ClearSkulls();
        while (skullPool.Count > 0)
        {
            ChoirOfShatteredSaintsSkull pooled = skullPool.Dequeue();
            if (pooled != null)
                Destroy(pooled.gameObject);
        }
    }

    public void Configure(ChoirOfShatteredSaints config, int stackCount)
    {
        cfg = config;
        stacks = Mathf.Max(1, stackCount);
        RefreshSkulls();
        TrySubscribe();
    }

    private void TrySubscribe()
    {
        if (subscribed || player == null)
            return;

        player.OnMeleeKill += OnMeleeKill;
        player.OnDamageTaken += OnDamageTaken;
        subscribed = true;
    }

    private void TryUnsubscribe()
    {
        if (!subscribed || player == null)
            return;

        player.OnMeleeKill -= OnMeleeKill;
        player.OnDamageTaken -= OnDamageTaken;
        subscribed = false;
    }

    private void OnMeleeKill(Combatant target, float damage, bool isCrit)
    {
        if (cfg == null)
            return;

        RelicProcPacingService.NotifyMeleeKill(player);
        float window = RelicProcPacingService.GetKillWindow(
            player,
            cfg.killWindow,
            cfg.targetTtkSeconds
        );
        if (Time.time - lastKillAt <= window)
            SetRank(rank + 1);
        else
            SetRank(1);

        lastKillAt = Time.time;
    }

    private void OnDamageTaken(float amount)
    {
        if (cfg == null || rank <= 0)
            return;

        int removed = Mathf.Min(2, rank);
        SetRank(rank - removed);
        TriggerShockPulse(removed);
    }

    private void SetRank(int value)
    {
        int clamped = Mathf.Clamp(value, 0, Mathf.Max(1, cfg.maxRank));
        if (clamped == rank)
            return;

        bool increased = clamped > rank;
        rank = clamped;
        RefreshSkulls();
        player?.Progression?.NotifyStatsChanged();

        if (increased)
            RelicDamageText.PlayGeneratedEventFeedback(transform, RelicRarity.Mythic, 1.08f);
    }

    private void RefreshSkulls()
    {
        ClearSkulls();
        if (cfg == null || rank <= 0)
            return;

        LayerMask mask = cfg.enemyMask.value != 0 ? cfg.enemyMask : LayerMask.GetMask("Enemy", "Zombie");
        float skullDamage = cfg.baseSkullDamage + cfg.skullDamagePerStack * Mathf.Max(0, stacks - 1);

        for (int i = 0; i < rank; i++)
        {
            ChoirOfShatteredSaintsSkull skull = RentSkull();
            if (skull == null)
                continue;

            skull.Configure(
                owner: transform,
                slotIndex: i,
                slotCount: rank,
                orbitRadius: cfg.orbitRadius,
                orbitHeight: cfg.orbitHeight,
                orbitSpeed: cfg.orbitSpeed,
                fireInterval: cfg.fireInterval,
                fireRange: cfg.fireRange,
                damage: skullDamage,
                enemyMask: mask
            );

            skulls.Add(skull);
        }
    }

    private ChoirOfShatteredSaintsSkull RentSkull()
    {
        ChoirOfShatteredSaintsSkull skull = null;
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

    private ChoirOfShatteredSaintsSkull CreateSkullInstance()
    {
        GameObject go;
        if (cfg.skullPrefab != null)
        {
            go = TryInstantiateSkullPrefab(cfg.skullPrefab);
            if (go != null)
            {
                RelicDamageText.StyleSpawnedSkullVisual(
                    go,
                    RelicRarity.Mythic,
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
                "ChoirShatteredSaintSkull",
                RelicRarity.Mythic,
                0.33f
            );
            if (go != null)
                go.transform.position = transform.position;
        }

        if (go == null)
            return null;

        ChoirOfShatteredSaintsSkull skull = go.GetComponent<ChoirOfShatteredSaintsSkull>();
        if (skull == null)
            skull = go.AddComponent<ChoirOfShatteredSaintsSkull>();

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
            Debug.LogWarning($"[ChoirOfShatteredSaints] Failed to instantiate skull prefab '{prefab.name}'. Falling back to generated skull. {ex.Message}", this);
        }

        return null;
    }

    private void ClearSkulls()
    {
        for (int i = 0; i < skulls.Count; i++)
        {
            ChoirOfShatteredSaintsSkull skull = skulls[i];
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

    private void TriggerShockPulse(int removedRanks)
    {
        if (cfg == null || removedRanks <= 0)
            return;

        float damage = cfg.baseShockDamage + cfg.shockDamagePerRemovedRank * removedRanks;
        damage = Mathf.Max(1f, damage);

        LayerMask mask = cfg.enemyMask.value != 0 ? cfg.enemyMask : LayerMask.GetMask("Enemy", "Zombie");
        Collider[] hits;
        if (mask.value != 0)
            hits = EnemyQueryService.OverlapSphere(transform.position, cfg.shockPulseRadius, mask, QueryTriggerInteraction.Ignore, this);
        else
            hits = EnemyQueryService.OverlapSphere(transform.position, cfg.shockPulseRadius, ~0, QueryTriggerInteraction.Ignore, this);

        for (int i = 0, hitCount = EnemyQueryService.GetLastHitCount(this); i < hitCount; i++)
        {
            var col = hits[i];
            if (col == null)
                continue;

            var combatant = EnemyQueryService.GetCombatant(col);
            if (combatant == null || combatant.IsDead)
                continue;

            if (combatant.GetComponent<PlayerProgressionController>() != null)
                continue;

            RelicDamageText.Deal(combatant, damage, transform, cfg);
        }
    }
}

public class ChoirOfShatteredSaintsSkull : MonoBehaviour, IRelicBatchedUpdate, IRelicBatchedCadence
{
    private Transform owner;
    private int slotIndex;
    private int slotCount;
    private float orbitRadius;
    private float orbitHeight;
    private float orbitSpeed;
    private float fireInterval;
    private float fireRange;
    private float damage;
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
        float fireRange,
        float damage,
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
        this.fireRange = Mathf.Max(0.5f, fireRange);
        this.damage = Mathf.Max(1f, damage);
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
            Fire();
        }
    }

    private void Fire()
    {
        Collider[] hits;
        if (enemyMask.value != 0)
            hits = EnemyQueryService.OverlapSphere(transform.position, fireRange, enemyMask, QueryTriggerInteraction.Ignore, this);
        else
            hits = EnemyQueryService.OverlapSphere(transform.position, fireRange, ~0, QueryTriggerInteraction.Ignore, this);

        Combatant best = null;
        float bestSqr = float.PositiveInfinity;

        for (int i = 0, hitCount = EnemyQueryService.GetLastHitCount(this); i < hitCount; i++)
        {
            var col = hits[i];
            if (col == null)
                continue;

            var c = EnemyQueryService.GetCombatant(col);
            if (c == null || c.IsDead)
                continue;

            if (c.GetComponent<PlayerProgressionController>() != null)
                continue;

            float sqr = (c.transform.position - transform.position).sqrMagnitude;
            if (sqr < bestSqr)
            {
                bestSqr = sqr;
                best = c;
            }
        }

        if (best != null)
            RelicDamageText.Deal(best, damage, owner, null, "Choir of Shattered Saints");
    }
}

