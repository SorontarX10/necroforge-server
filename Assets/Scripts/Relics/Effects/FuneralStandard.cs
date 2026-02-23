using UnityEngine;

[CreateAssetMenu(
    menuName = "GrassSim/Relics/Effects/Uncommon/Funeral Standard",
    fileName = "Relic_FuneralStandard"
)]
public class FuneralStandard : RelicEffect, ISpeedModifier, IDamageReductionModifier
{
    [Header("Timing")]
    public float baseCooldown = 22f;
    public float cooldownReductionPerStack = 1.2f;
    public float baseDuration = 6f;
    public float durationPerStack = 0.5f;

    [Header("Aura")]
    public float auraRadius = 4.5f;
    public float baseSpeedBonus = 0.12f;
    public float speedBonusPerStack = 0.02f;
    public float baseDamageReductionBonus = 0.15f;
    public float damageReductionPerStack = 0.02f;

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

    public float GetSpeedBonus(PlayerRelicController player, int stacks)
    {
        var rt = player != null ? player.GetComponent<FuneralStandardRuntime>() : null;
        if (rt == null || !rt.IsInAura)
            return 0f;

        return baseSpeedBonus + speedBonusPerStack * Mathf.Max(0, stacks - 1);
    }

    public float GetDamageReductionBonus(PlayerRelicController player, int stacks)
    {
        var rt = player != null ? player.GetComponent<FuneralStandardRuntime>() : null;
        if (rt == null || !rt.IsInAura)
            return 0f;

        return baseDamageReductionBonus + damageReductionPerStack * Mathf.Max(0, stacks - 1);
    }

    private FuneralStandardRuntime Attach(PlayerRelicController player)
    {
        if (player == null)
            return null;

        var rt = player.GetComponent<FuneralStandardRuntime>();
        if (rt == null)
            rt = player.gameObject.AddComponent<FuneralStandardRuntime>();

        return rt;
    }
}

public class FuneralStandardRuntime : MonoBehaviour, IRelicBatchedUpdate, IRelicBatchedCadence
{
    private PlayerRelicController player;
    private FuneralStandard cfg;
    private int stacks;

    private float nextPlantAt;
    private float standardEndsAt;
    private Vector3 standardPosition;
    private GameObject standardVisual;
    private GameObject generatedStandardPrefab;
    private bool isInAura;

    public bool IsInAura => isInAura;

    private void Awake()
    {
        player = GetComponent<PlayerRelicController>();
    }

    private void OnEnable()
    {
        RelicBatchedTickSystem.Register(this);
    }

    public void Configure(FuneralStandard config, int stackCount)
    {
        cfg = config;
        stacks = Mathf.Max(1, stackCount);

        if (nextPlantAt <= 0f)
            nextPlantAt = Time.time + 1.5f;
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

        bool nowInAura = false;
        if (IsStandardActive(now))
        {
            Vector3 delta = transform.position - standardPosition;
            delta.y = 0f;
            nowInAura = delta.sqrMagnitude <= cfg.auraRadius * cfg.auraRadius;
        }

        if (nowInAura != isInAura)
        {
            isInAura = nowInAura;
            player?.Progression?.NotifyStatsChanged();
        }
    }

    private void OnDisable()
    {
        RelicBatchedTickSystem.Unregister(this);
        if (isInAura)
        {
            isInAura = false;
            player?.Progression?.NotifyStatsChanged();
        }

        CleanupVisual();
    }

    private void OnDestroy()
    {
        if (generatedStandardPrefab != null)
            Destroy(generatedStandardPrefab);
    }

    private bool IsStandardActive(float now)
    {
        return now < standardEndsAt;
    }

    private void PlantStandard()
    {
        float duration = cfg.baseDuration + cfg.durationPerStack * Mathf.Max(0, stacks - 1);
        float cooldown = cfg.baseCooldown - cfg.cooldownReductionPerStack * Mathf.Max(0, stacks - 1);

        standardPosition = transform.position;
        standardPosition.y = transform.position.y;
        standardEndsAt = Time.time + Mathf.Max(0.5f, duration);
        nextPlantAt = Time.time + Mathf.Max(4f, cooldown);

        CleanupVisual();

        GameObject visualPrefab = ResolveVisualPrefab();
        if (visualPrefab != null)
        {
            standardVisual = RelicVfxTickSystem.Rent(visualPrefab, standardPosition, Quaternion.identity);
            SetVisualNonBlocking(standardVisual);
        }

        RelicDamageText.PlayGeneratedEventFeedback(transform, RelicRarity.Uncommon, 1.05f);
    }

    private void EndStandard()
    {
        standardEndsAt = 0f;

        if (isInAura)
        {
            isInAura = false;
            player?.Progression?.NotifyStatsChanged();
        }

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
            "FuneralStandardAura",
            cfg.auraRadius,
            RelicRarity.Uncommon,
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
}
