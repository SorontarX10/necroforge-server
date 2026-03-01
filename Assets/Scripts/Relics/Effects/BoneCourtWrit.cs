using UnityEngine;
using GrassSim.Combat;
using GrassSim.Core;

[CreateAssetMenu(
    menuName = "GrassSim/Relics/Effects/Rare/Bone Court Writ",
    fileName = "Relic_BoneCourtWrit"
)]
public class BoneCourtWrit : RelicEffect
{
    [Header("Timing")]
    public float baseCooldown = 28f;
    public float cooldownReductionPerStack = 1.2f;
    public float baseDuration = 5f;
    public float durationPerStack = 0.4f;

    [Header("Circle")]
    public float radius = 5.5f;
    [Range(0f, 1f)] public float baseOutgoingReduction = 0.25f;
    [Range(0f, 1f)] public float outgoingReductionPerStack = 0.03f;
    [Range(0f, 1f)] public float baseSlowPercent = 0.3f;
    [Range(0f, 1f)] public float slowPercentPerStack = 0.03f;
    public LayerMask enemyMask;

    [Header("Optional Visual Prefab")]
    public GameObject circlePrefab;

    public override void OnAcquire(PlayerRelicController player, int stacks)
    {
        Attach(player)?.Configure(this, stacks);
    }

    public override void OnStack(PlayerRelicController player, int stacks)
    {
        Attach(player)?.Configure(this, stacks);
    }

    private BoneCourtWritRuntime Attach(PlayerRelicController player)
    {
        if (player == null)
            return null;

        var rt = player.GetComponent<BoneCourtWritRuntime>();
        if (rt == null)
            rt = player.gameObject.AddComponent<BoneCourtWritRuntime>();

        return rt;
    }
}

public class BoneCourtWritRuntime : MonoBehaviour, IRelicBatchedUpdate, IRelicBatchedCadence
{
    private static readonly Color JudgementRarityColor = RelicRarityColors.Get(RelicRarity.Rare);

    private PlayerRelicController player;
    private BoneCourtWrit cfg;
    private int stacks;

    private float nextCastAt;
    private float endsAt;
    private float nextTickAt;
    private GameObject visual;
    private bool visualFromPrefabPool;
    private GameObject cachedGeneratedVisual;

    private void Awake()
    {
        player = GetComponent<PlayerRelicController>();
    }

    public void Configure(BoneCourtWrit config, int stackCount)
    {
        cfg = config;
        stacks = Mathf.Max(1, stackCount);
        EnemyQueryService.ConfigureOwnerBudget(this, RelicQueryBudgetProfiles.For(BatchedTickArchetype));

        if (nextCastAt <= 0f)
            nextCastAt = Time.time + 1.5f;
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

        if (endsAt > 0f && now >= endsAt)
        {
            Deactivate();
            return;
        }

        if (!IsActive(now) && now >= nextCastAt)
            Activate();

        if (IsActive(now))
        {
            if (now >= nextTickAt)
            {
                nextTickAt = now + 0.2f;
                ApplyCourtEffects();
            }

        }
    }

    private void OnDisable()
    {
        if (cachedGeneratedVisual != null)
            cachedGeneratedVisual.SetActive(false);

        RelicBatchedTickSystem.Unregister(this);
        CleanupVisual();
    }

    private bool IsActive(float now)
    {
        return now < endsAt;
    }

    private void Activate()
    {
        float duration = cfg.baseDuration + cfg.durationPerStack * Mathf.Max(0, stacks - 1);
        float cooldown = cfg.baseCooldown - cfg.cooldownReductionPerStack * Mathf.Max(0, stacks - 1);
        endsAt = Time.time + Mathf.Max(0.5f, duration);
        nextCastAt = Time.time + Mathf.Max(5f, cooldown);
        nextTickAt = 0f;

        CleanupVisual();
        if (cfg.circlePrefab != null)
        {
            visual = RelicVfxTickSystem.Rent(cfg.circlePrefab, transform.position + Vector3.up * 0.05f, Quaternion.identity);
            visualFromPrefabPool = true;
            RelicVisualRarityTint.Apply(visual, JudgementRarityColor, 0.5f);
        }
        else
        {
            if (cachedGeneratedVisual == null)
            {
                cachedGeneratedVisual = RelicDamageText.CreateGeneratedAuraCircle(
                    "BoneCourtWritCircle",
                    cfg.radius,
                    RelicRarity.Rare,
                    edgeAccentColor: JudgementRarityColor
                );
            }

            cachedGeneratedVisual.SetActive(true);
            cachedGeneratedVisual.transform.position = transform.position + Vector3.up * 0.05f;
            visual = cachedGeneratedVisual;
            visualFromPrefabPool = false;
        }

        if (visual != null)
            RelicVfxTickSystem.Track(transform, visual.transform, Vector3.up * 0.05f);

        RelicDamageText.PlayGeneratedEventFeedback(transform, RelicRarity.Rare, 1.05f);
    }

    private void Deactivate()
    {
        endsAt = 0f;
        CleanupVisual();
    }

    private void CleanupVisual()
    {
        if (visual == null)
            return;

        RelicVfxTickSystem.Untrack(visual.transform);
        if (visualFromPrefabPool)
            RelicVfxTickSystem.Return(visual);
        else
            visual.SetActive(false);

        visual = null;
        visualFromPrefabPool = false;
    }

    private void ApplyCourtEffects()
    {
        float outgoingReduction = cfg.baseOutgoingReduction + cfg.outgoingReductionPerStack * Mathf.Max(0, stacks - 1);
        outgoingReduction = Mathf.Clamp01(outgoingReduction);

        float slowPercent = cfg.baseSlowPercent + cfg.slowPercentPerStack * Mathf.Max(0, stacks - 1);
        slowPercent = Mathf.Clamp01(slowPercent);

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

            var outgoing = combatant.GetComponent<RelicOutgoingDamageDebuff>();
            if (outgoing == null)
                outgoing = combatant.gameObject.AddComponent<RelicOutgoingDamageDebuff>();
            outgoing.Apply(outgoingReduction, 0.35f);

            var slow = combatant.GetComponent<RelicMoveSpeedDebuff>();
            if (slow == null)
                slow = combatant.gameObject.AddComponent<RelicMoveSpeedDebuff>();
            slow.Apply(slowPercent, 0.35f);
        }
    }
}

public static class RelicVisualRarityTint
{
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    public static void Apply(GameObject root, RelicRarity rarity, float baseEmission = 0.42f)
    {
        Apply(root, RelicRarityColors.Get(rarity), baseEmission);
    }

    public static void Apply(GameObject root, Color rarityColor, float baseEmission = 0.42f)
    {
        if (root == null)
            return;

        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        if (renderers == null || renderers.Length == 0)
            return;

        float emission = Mathf.Max(0f, baseEmission);
        MaterialPropertyBlock props = new MaterialPropertyBlock();

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null)
                continue;

            Material mat = renderer.sharedMaterial;
            if (mat == null)
                continue;

            Color tint = rarityColor;
            float emissionMul = 1f;
            string lowerName = renderer.gameObject.name.ToLowerInvariant();

            if (lowerName.Contains("banner") || lowerName.Contains("flag"))
            {
                tint.a = Mathf.Clamp(tint.a, 0.7f, 1f);
                emissionMul = 1.5f;
            }
            else if (lowerName.Contains("aura") || lowerName.Contains("edge") || lowerName.Contains("ring") || lowerName.Contains("circle"))
            {
                emissionMul = 1.65f;
            }

            renderer.GetPropertyBlock(props);
            if (mat.HasProperty(BaseColorId))
                props.SetColor(BaseColorId, tint);
            if (mat.HasProperty(ColorId))
                props.SetColor(ColorId, tint);
            if (mat.HasProperty(EmissionColorId))
            {
                mat.EnableKeyword("_EMISSION");
                props.SetColor(EmissionColorId, tint * (emission * emissionMul));
            }

            renderer.SetPropertyBlock(props);
        }
    }
}

