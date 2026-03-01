using UnityEngine;
using GrassSim.Combat;
using GrassSim.Core;

[CreateAssetMenu(
    menuName = "GrassSim/Relics/Effects/Mythic/Boneforge Standard",
    fileName = "Relic_BoneforgeStandard"
)]
public class BoneforgeStandard : RelicEffect, ISwordLengthModifier, ICritMultiplierModifier
{
    [Header("Timing")]
    public float baseCooldown = 25f;
    public float cooldownReductionPerStack = 1f;
    public float baseDuration = 7f;
    public float durationPerStack = 0.4f;

    [Header("Circle")]
    public float radius = 5f;
    public float lingerDuration = 2f;

    [Header("Buffs")]
    public float baseSwordLengthBonus = 0.35f;
    public float swordLengthBonusPerStack = 0.03f;
    public float baseCritDamageBonus = 0.6f;
    public float critDamageBonusPerStack = 0.08f;

    [Header("Bone Shards")]
    public float shardDamagePercent = 0.35f;
    public float shardRadius = 3f;
    public int maxShardTargets = 4;
    public LayerMask enemyMask;

    [Header("Optional Visual Prefab")]
    public GameObject standardPrefab;

    public override void OnAcquire(PlayerRelicController player, int stacks)
    {
        Attach(player)?.Configure(this, stacks);
    }

    public override void OnStack(PlayerRelicController player, int stacks)
    {
        Attach(player)?.Configure(this, stacks);
    }

    public float GetSwordLengthBonus(PlayerRelicController player, int stacks)
    {
        var rt = player != null ? player.GetComponent<BoneforgeStandardRuntime>() : null;
        if (rt == null || !rt.IsBuffActive)
            return 0f;

        return baseSwordLengthBonus + swordLengthBonusPerStack * Mathf.Max(0, stacks - 1);
    }

    public float GetCritMultiplierBonus(PlayerRelicController player, int stacks)
    {
        var rt = player != null ? player.GetComponent<BoneforgeStandardRuntime>() : null;
        if (rt == null || !rt.IsBuffActive)
            return 0f;

        return baseCritDamageBonus + critDamageBonusPerStack * Mathf.Max(0, stacks - 1);
    }

    private BoneforgeStandardRuntime Attach(PlayerRelicController player)
    {
        if (player == null)
            return null;

        var rt = player.GetComponent<BoneforgeStandardRuntime>();
        if (rt == null)
            rt = player.gameObject.AddComponent<BoneforgeStandardRuntime>();

        return rt;
    }
}

public class BoneforgeStandardRuntime : MonoBehaviour, IRelicBatchedUpdate, IRelicBatchedCadence
{
    private PlayerRelicController player;
    private BoneforgeStandard cfg;
    private int stacks;
    private bool subscribed;
    private bool wasBuffActive;

    private float nextPlantAt;
    private float standardEndsAt;
    private float buffEndsAt;
    private Vector3 standardPos;
    private GameObject standardVisual;
    private GameObject generatedStandardPrefab;

    public bool IsBuffActive => Time.time < buffEndsAt;

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
        if (wasBuffActive)
        {
            wasBuffActive = false;
            buffEndsAt = 0f;
            player?.Progression?.NotifyStatsChanged();
        }

        CleanupVisual();
    }

    private void OnDestroy()
    {
        if (generatedStandardPrefab != null)
            Destroy(generatedStandardPrefab);
    }

    public void Configure(BoneforgeStandard config, int stackCount)
    {
        cfg = config;
        stacks = Mathf.Max(1, stackCount);
        EnemyQueryService.ConfigureOwnerBudget(this, RelicQueryBudgetProfiles.For(BatchedTickArchetype));

        if (nextPlantAt <= 0f)
            nextPlantAt = Time.time + 1.5f;

        TrySubscribe();
    }

    public bool IsBatchedUpdateActive => isActiveAndEnabled && cfg != null;

    public float BatchedUpdateInterval => 0.06f;

    public RelicTickArchetype BatchedTickArchetype => RelicTickArchetype.PlayerAura;

    public void TickFromRelicBatch(float now, float deltaTime)
    {
        if (standardEndsAt > 0f && now >= standardEndsAt)
            EndStandard();

        if (!IsStandardActive(now) && now >= nextPlantAt)
            PlantStandard();

        if (IsStandardActive(now))
        {
            Vector3 delta = transform.position - standardPos;
            delta.y = 0f;
            if (delta.sqrMagnitude <= cfg.radius * cfg.radius)
                buffEndsAt = Mathf.Max(buffEndsAt, now + Mathf.Max(0f, cfg.lingerDuration));
        }

        bool nowActive = IsBuffActive;
        if (nowActive != wasBuffActive)
        {
            wasBuffActive = nowActive;
            player?.Progression?.NotifyStatsChanged();
        }
    }

    private bool IsStandardActive(float now)
    {
        return now < standardEndsAt;
    }

    private void TrySubscribe()
    {
        if (subscribed || player == null)
            return;

        player.OnMeleeHitDealt += OnMeleeHit;
        subscribed = true;
    }

    private void TryUnsubscribe()
    {
        if (!subscribed || player == null)
            return;

        player.OnMeleeHitDealt -= OnMeleeHit;
        subscribed = false;
    }

    private void PlantStandard()
    {
        float duration = cfg.baseDuration + cfg.durationPerStack * Mathf.Max(0, stacks - 1);
        float cooldown = cfg.baseCooldown - cfg.cooldownReductionPerStack * Mathf.Max(0, stacks - 1);

        standardPos = transform.position;
        standardEndsAt = Time.time + Mathf.Max(0.5f, duration);
        nextPlantAt = Time.time + Mathf.Max(3f, cooldown);
        buffEndsAt = Mathf.Max(buffEndsAt, Time.time + 0.1f);

        CleanupVisual();

        GameObject visualPrefab = ResolveVisualPrefab();
        if (visualPrefab != null)
        {
            standardVisual = RelicVfxTickSystem.Rent(visualPrefab, standardPos, Quaternion.identity);
            SetVisualNonBlocking(standardVisual);
            RelicVisualRarityTint.Apply(standardVisual, RelicRarity.Mythic);
        }

        RelicDamageText.PlayGeneratedEventFeedback(transform, RelicRarity.Mythic, 1.2f);
    }

    private void EndStandard()
    {
        standardEndsAt = 0f;
        CleanupVisual();
    }

    private void CleanupVisual()
    {
        if (standardVisual != null)
        {
            RelicVfxTickSystem.Untrack(standardVisual.transform);
            RelicVfxTickSystem.Return(standardVisual);
            standardVisual = null;
        }
    }

    private GameObject ResolveVisualPrefab()
    {
        if (cfg == null)
            return null;

        if (cfg.standardPrefab != null)
            return cfg.standardPrefab;

        if (generatedStandardPrefab != null)
            return generatedStandardPrefab;

        generatedStandardPrefab = RelicDamageText.CreateGeneratedStandard(
            "BoneforgeStandard",
            cfg.radius,
            RelicRarity.Mythic,
            withBanner: true
        );

        if (generatedStandardPrefab == null)
            return null;

        generatedStandardPrefab.transform.SetParent(transform, false);
        generatedStandardPrefab.SetActive(false);
        generatedStandardPrefab.hideFlags = HideFlags.HideAndDontSave;
        return generatedStandardPrefab;
    }

    private static void SetVisualNonBlocking(GameObject visualRoot)
    {
        if (visualRoot == null)
            return;

        Collider[] colliders = visualRoot.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider col = colliders[i];
            if (col == null)
                continue;

            col.enabled = false;
        }
    }

    private void OnMeleeHit(Combatant target, float damage, bool isCrit)
    {
        if (!IsBuffActive || cfg == null || target == null || target.IsDead || damage <= 0f)
            return;

        LayerMask mask = cfg.enemyMask.value != 0 ? cfg.enemyMask : LayerMask.GetMask("Enemy", "Zombie");
        Collider[] hits;
        if (mask.value != 0)
            hits = EnemyQueryService.OverlapSphere(target.transform.position, cfg.shardRadius, mask, QueryTriggerInteraction.Ignore, this);
        else
            hits = EnemyQueryService.OverlapSphere(target.transform.position, cfg.shardRadius, ~0, QueryTriggerInteraction.Ignore, this);

        float shardDamage = Mathf.Max(1f, damage * Mathf.Max(0f, cfg.shardDamagePercent));
        int applied = 0;

        for (int i = 0, hitCount = EnemyQueryService.GetLastHitCount(this); i < hitCount; i++)
        {
            var col = hits[i];
            if (col == null)
                continue;

            var enemy = EnemyQueryService.GetCombatant(col);
            if (enemy == null || enemy.IsDead || enemy == target)
                continue;

            if (enemy.GetComponent<PlayerProgressionController>() != null)
                continue;

            RelicDamageText.Deal(enemy, shardDamage, transform, cfg);
            applied++;
            if (applied >= Mathf.Max(1, cfg.maxShardTargets))
                break;
        }
    }
}

