using UnityEngine;
using GrassSim.Combat;
using GrassSim.Core;

[CreateAssetMenu(
    menuName = "GrassSim/Relics/Effects/Uncommon/Censer Of Rebuke",
    fileName = "Relic_CenserOfRebuke"
)]
public class CenserOfRebuke : RelicEffect
{
    [Header("Timing")]
    public float baseCooldown = 30f;
    public float cooldownReductionPerStack = 1.5f;
    public float baseDuration = 5f;
    public float durationPerStack = 0.4f;

    [Header("Cloud")]
    public float radius = 5f;
    [Range(0f, 1f)] public float baseOutgoingReduction = 0.2f;
    [Range(0f, 1f)] public float outgoingReductionPerStack = 0.03f;
    public float baseHealPerSecond = 6f;
    public float healPerSecondPerStack = 1f;
    public LayerMask enemyMask;

    [Header("Optional Visual Prefab")]
    public GameObject cloudPrefab;

    public override void OnAcquire(PlayerRelicController player, int stacks)
    {
        Attach(player)?.Configure(this, stacks);
    }

    public override void OnStack(PlayerRelicController player, int stacks)
    {
        Attach(player)?.Configure(this, stacks);
    }

    private CenserOfRebukeRuntime Attach(PlayerRelicController player)
    {
        if (player == null)
            return null;

        var rt = player.GetComponent<CenserOfRebukeRuntime>();
        if (rt == null)
            rt = player.gameObject.AddComponent<CenserOfRebukeRuntime>();

        return rt;
    }
}

public class CenserOfRebukeRuntime : MonoBehaviour, IRelicBatchedUpdate, IRelicBatchedCadence
{
    private PlayerRelicController player;
    private CenserOfRebuke cfg;
    private int stacks;

    private float nextCloudAt;
    private float cloudEndsAt;
    private float nextDebuffTickAt;
    private GameObject cloudVisual;
    private bool cloudVisualFromPrefabPool;
    private GameObject cachedGeneratedCloudVisual;

    private void Awake()
    {
        player = GetComponent<PlayerRelicController>();
    }

    public void Configure(CenserOfRebuke config, int stackCount)
    {
        cfg = config;
        stacks = Mathf.Max(1, stackCount);
        EnemyQueryService.ConfigureOwnerBudget(this, RelicQueryBudgetProfiles.For(BatchedTickArchetype));

        if (nextCloudAt <= 0f)
            nextCloudAt = Time.time + 2f;
    }

    private void OnEnable()
    {
        RelicBatchedTickSystem.Register(this);
    }

    public bool IsBatchedUpdateActive => isActiveAndEnabled && cfg != null;

    public float BatchedUpdateInterval => 0.05f;

    public RelicTickArchetype BatchedTickArchetype => RelicTickArchetype.PlayerAura;

    public void TickFromRelicBatch(float now, float deltaTime)
    {
        if (cfg == null)
            return;

        if (cloudEndsAt > 0f && now >= cloudEndsAt)
        {
            DeactivateCloud();
            return;
        }

        if (!IsCloudActive(now) && now >= nextCloudAt)
            ActivateCloud();

        if (IsCloudActive(now))
        {
            float healPerSec = cfg.baseHealPerSecond + cfg.healPerSecondPerStack * Mathf.Max(0, stacks - 1);
            if (healPerSec > 0f)
                player?.Progression?.Heal(healPerSec * deltaTime);

            if (now >= nextDebuffTickAt)
            {
                nextDebuffTickAt = now + 0.2f;
                ApplyCloudDebuff();
            }

        }
    }

    private void OnDisable()
    {
        if (cachedGeneratedCloudVisual != null)
            cachedGeneratedCloudVisual.SetActive(false);

        RelicBatchedTickSystem.Unregister(this);
        CleanupVisual();
    }

    private bool IsCloudActive(float now)
    {
        return now < cloudEndsAt;
    }

    private void ActivateCloud()
    {
        float duration = cfg.baseDuration + cfg.durationPerStack * Mathf.Max(0, stacks - 1);
        float cooldown = cfg.baseCooldown - cfg.cooldownReductionPerStack * Mathf.Max(0, stacks - 1);

        cloudEndsAt = Time.time + Mathf.Max(0.5f, duration);
        nextCloudAt = Time.time + Mathf.Max(5f, cooldown);
        nextDebuffTickAt = 0f;

        CleanupVisual();

        if (cfg.cloudPrefab != null)
        {
            cloudVisual = RelicVfxTickSystem.Rent(cfg.cloudPrefab, transform.position, Quaternion.identity);
            cloudVisualFromPrefabPool = true;
        }
        else
        {
            if (cachedGeneratedCloudVisual == null)
            {
                cachedGeneratedCloudVisual = RelicDamageText.CreateGeneratedCloud(
                    "CenserCloud",
                    cfg.radius,
                    RelicRarity.Uncommon
                );
            }

            cachedGeneratedCloudVisual.SetActive(true);
            cloudVisual = cachedGeneratedCloudVisual;
            cloudVisualFromPrefabPool = false;
            cloudVisual.transform.position = transform.position + Vector3.up * 0.1f;
        }

        if (cloudVisual != null)
            RelicVfxTickSystem.Track(transform, cloudVisual.transform, Vector3.up * 0.1f);

        RelicDamageText.PlayGeneratedEventFeedback(transform, RelicRarity.Uncommon, 1.05f);
    }

    private void DeactivateCloud()
    {
        cloudEndsAt = 0f;
        CleanupVisual();
    }

    private void CleanupVisual()
    {
        if (cloudVisual == null)
            return;

        RelicVfxTickSystem.Untrack(cloudVisual.transform);
        if (cloudVisualFromPrefabPool)
            RelicVfxTickSystem.Return(cloudVisual);
        else
            cloudVisual.SetActive(false);

        cloudVisual = null;
        cloudVisualFromPrefabPool = false;
    }

    private void ApplyCloudDebuff()
    {
        float reduction = Mathf.Clamp01(
            cfg.baseOutgoingReduction + cfg.outgoingReductionPerStack * Mathf.Max(0, stacks - 1)
        );

        if (reduction <= 0f)
            return;

        LayerMask mask = cfg.enemyMask.value != 0 ? cfg.enemyMask : LayerMask.GetMask("Enemy", "Zombie");

        Collider[] hits;
        if (mask.value != 0)
            hits = EnemyQueryService.OverlapSphere(transform.position, cfg.radius, mask, QueryTriggerInteraction.Ignore, this);
        else
            hits = EnemyQueryService.OverlapSphere(transform.position, cfg.radius, ~0, QueryTriggerInteraction.Ignore, this);

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

            var debuff = combatant.GetComponent<RelicOutgoingDamageDebuff>();
            if (debuff == null)
                debuff = combatant.gameObject.AddComponent<RelicOutgoingDamageDebuff>();

            debuff.Apply(reduction, 0.35f);
        }
    }
}

