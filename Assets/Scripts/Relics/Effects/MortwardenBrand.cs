using System.Collections.Generic;
using UnityEngine;
using GrassSim.Combat;
using GrassSim.Core;

[CreateAssetMenu(
    menuName = "GrassSim/Relics/Effects/Rare/Mortwarden Brand",
    fileName = "Relic_MortwardenBrand"
)]
public class MortwardenBrand : RelicEffect
{
    [Header("Brand")]
    public float overkillFactorToBrand = 1f;
    public float brandDuration = 6f;

    [Header("Detonation")]
    public float explosionRadius = 5f;
    public float baseExplosionDamage = 45f;
    public float explosionDamagePerStack = 8f;
    public float baseKnockback = 8f;
    public float knockbackPerStack = 1f;
    public LayerMask enemyMask;

    public override void OnAcquire(PlayerRelicController player, int stacks)
    {
        Attach(player)?.Configure(this, stacks);
    }

    public override void OnStack(PlayerRelicController player, int stacks)
    {
        Attach(player)?.Configure(this, stacks);
    }

    private MortwardenBrandRuntime Attach(PlayerRelicController player)
    {
        if (player == null)
            return null;

        var rt = player.GetComponent<MortwardenBrandRuntime>();
        if (rt == null)
            rt = player.gameObject.AddComponent<MortwardenBrandRuntime>();

        return rt;
    }
}

public class MortwardenBrandRuntime : MonoBehaviour, IRelicBatchedUpdate, IRelicBatchedCadence
{
    private struct BrandMarker
    {
        public Vector3 position;
        public float expiresAt;
    }

    private readonly List<BrandMarker> brands = new();

    private PlayerRelicController player;
    private MortwardenBrand cfg;
    private int stacks;
    private bool subscribed;

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
        brands.Clear();
    }

    public void Configure(MortwardenBrand config, int stackCount)
    {
        cfg = config;
        stacks = Mathf.Max(1, stackCount);
        EnemyQueryService.ConfigureOwnerBudget(this, RelicQueryBudgetProfiles.For(RelicTickArchetype.EnemyDebuff));
        TrySubscribe();
    }

    public bool IsBatchedUpdateActive => isActiveAndEnabled;

    public float BatchedUpdateInterval => 0.25f;

    public RelicTickArchetype BatchedTickArchetype => RelicTickArchetype.EnemyDebuff;

    public void TickFromRelicBatch(float now, float deltaTime)
    {
        CleanupExpired(now);
    }

    private void TrySubscribe()
    {
        if (subscribed || player == null)
            return;

        player.OnMeleeKill += OnMeleeKill;
        player.OnDodged += OnDodged;
        subscribed = true;
    }

    private void TryUnsubscribe()
    {
        if (!subscribed || player == null)
            return;

        player.OnMeleeKill -= OnMeleeKill;
        player.OnDodged -= OnDodged;
        subscribed = false;
    }

    private void OnMeleeKill(Combatant target, float damage, bool isCrit)
    {
        if (cfg == null || target == null)
            return;

        float threshold = Mathf.Max(1f, target.MaxHealth * Mathf.Max(0f, cfg.overkillFactorToBrand));
        if (damage < threshold)
            return;

        brands.Add(new BrandMarker
        {
            position = target.transform.position,
            expiresAt = Time.time + Mathf.Max(0.1f, cfg.brandDuration)
        });
    }

    private void OnDodged()
    {
        if (cfg == null || brands.Count == 0)
            return;

        float damage = cfg.baseExplosionDamage + cfg.explosionDamagePerStack * Mathf.Max(0, stacks - 1);
        float knockback = cfg.baseKnockback + cfg.knockbackPerStack * Mathf.Max(0, stacks - 1);
        LayerMask mask = cfg.enemyMask.value != 0 ? cfg.enemyMask : LayerMask.GetMask("Enemy", "Zombie");

        for (int b = 0; b < brands.Count; b++)
        {
            Vector3 pos = brands[b].position;

            Collider[] hits;
            if (mask.value != 0)
                hits = EnemyQueryService.OverlapSphere(pos, cfg.explosionRadius, mask, QueryTriggerInteraction.Ignore, this);
            else
                hits = EnemyQueryService.OverlapSphere(pos, cfg.explosionRadius, ~0, QueryTriggerInteraction.Ignore, this);

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

                var rb = combatant.GetComponent<Rigidbody>();
                if (rb != null && !rb.isKinematic)
                {
                    Vector3 dir = combatant.transform.position - pos;
                    dir.y = 0.2f;
                    if (dir.sqrMagnitude < 0.001f)
                        dir = Vector3.up;
                    rb.AddForce(dir.normalized * knockback, ForceMode.VelocityChange);
                }
            }
        }

        brands.Clear();
    }

    private void CleanupExpired(float now)
    {
        if (brands.Count == 0)
            return;

        for (int i = brands.Count - 1; i >= 0; i--)
        {
            if (now >= brands[i].expiresAt)
                brands.RemoveAt(i);
        }
    }
}

