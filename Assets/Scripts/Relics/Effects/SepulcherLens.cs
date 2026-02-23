using System.Collections.Generic;
using UnityEngine;
using GrassSim.Combat;
using GrassSim.Core;

[CreateAssetMenu(
    menuName = "GrassSim/Relics/Effects/Mythic/Sepulcher Lens",
    fileName = "Relic_SepulcherLens"
)]
public class SepulcherLens : RelicEffect
{
    [Header("Trigger")]
    public int critsRequired = 3;
    public float riftDuration = 3f;

    [Header("Rift")]
    public float riftLength = 14f;
    public float riftRadius = 1f;

    [Header("Duplication")]
    [Range(0f, 1f)] public float duplicateDamagePercent = 0.7f;
    public LayerMask enemyMask;

    public override void OnAcquire(PlayerRelicController player, int stacks)
    {
        Attach(player)?.Configure(this, stacks);
    }

    public override void OnStack(PlayerRelicController player, int stacks)
    {
        Attach(player)?.Configure(this, stacks);
    }

    private SepulcherLensRuntime Attach(PlayerRelicController player)
    {
        if (player == null)
            return null;

        var rt = player.GetComponent<SepulcherLensRuntime>();
        if (rt == null)
            rt = player.gameObject.AddComponent<SepulcherLensRuntime>();

        return rt;
    }
}

public class SepulcherLensRuntime : MonoBehaviour
{
    private static readonly Color RiftColor = new(0.5f, 0.78f, 1f, 0.95f);

    private PlayerRelicController player;
    private SepulcherLens cfg;
    private int stacks;
    private bool subscribed;
    private int critCounter;

    private float riftEndsAt;
    private Vector3 riftStart;
    private Vector3 riftEnd;

    private bool RiftActive => Time.time < riftEndsAt;

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
    }

    public void Configure(SepulcherLens config, int stackCount)
    {
        cfg = config;
        stacks = Mathf.Max(1, stackCount);
        EnemyQueryService.ConfigureOwnerBudget(this, RelicQueryBudgetProfiles.For(RelicTickArchetype.EnemyDebuff));
        TrySubscribe();
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

    private void OnMeleeHit(Combatant target, float damage, bool isCrit)
    {
        if (cfg == null || target == null || target.IsDead || damage <= 0f)
            return;

        if (isCrit)
        {
            critCounter++;
            if (critCounter >= Mathf.Max(1, cfg.critsRequired))
            {
                critCounter = 0;
                OpenRift(target);
            }
        }

        if (!RiftActive)
            return;

        if (!IsTargetWithinRift(target.transform.position))
            return;

        Combatant farthest = FindFarthestEnemyOnRift(target);
        if (farthest == null)
            return;

        Vector3 from = target.transform.position + Vector3.up * 1.05f;
        Vector3 to = farthest.transform.position + Vector3.up * 1.05f;
        RelicGeneratedVfx.SpawnTravelOrb(from, to, 0.21f, RiftColor, 0.22f, "SepulcherLens_DuplicateOrb");
        RelicGeneratedVfx.SpawnBeam(from, to, 0.04f, RiftColor, 0.12f, "SepulcherLens_DuplicateBeam");

        float duplicatedDamage = Mathf.Max(1f, damage * Mathf.Clamp01(cfg.duplicateDamagePercent));
        RelicDamageText.Deal(farthest, duplicatedDamage, transform, cfg);
    }

    private void OpenRift(Combatant anchorTarget)
    {
        Vector3 start = transform.position + Vector3.up * 1f;
        Vector3 dir = anchorTarget.transform.position - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.001f)
            dir = transform.forward;
        dir.Normalize();

        riftStart = start;
        riftEnd = start + dir * Mathf.Max(1f, cfg.riftLength);
        riftEndsAt = Time.time + Mathf.Max(0.2f, cfg.riftDuration);

        RelicGeneratedVfx.SpawnBeam(
            riftStart + Vector3.up * 0.06f,
            riftEnd + Vector3.up * 0.06f,
            Mathf.Max(0.05f, cfg.riftRadius * 0.3f),
            RiftColor,
            Mathf.Max(0.2f, cfg.riftDuration),
            "SepulcherLens_Rift"
        );
    }

    private bool IsTargetWithinRift(Vector3 point)
    {
        float sqrDistance = SqrDistancePointToSegment(point, riftStart, riftEnd);
        float radius = Mathf.Max(0.1f, cfg.riftRadius);
        return sqrDistance <= radius * radius;
    }

    private Combatant FindFarthestEnemyOnRift(Combatant exclude)
    {
        LayerMask mask = cfg.enemyMask.value != 0 ? cfg.enemyMask : LayerMask.GetMask("Enemy", "Zombie");
        float radius = Mathf.Max(0.1f, cfg.riftRadius);

        Collider[] hits;
        if (mask.value != 0)
            hits = EnemyQueryService.OverlapCapsule(riftStart, riftEnd, radius, mask, QueryTriggerInteraction.Ignore, this);
        else
            hits = EnemyQueryService.OverlapCapsule(riftStart, riftEnd, radius, ~0, QueryTriggerInteraction.Ignore, this);

        Combatant best = null;
        float bestProjection = float.NegativeInfinity;
        var seen = new HashSet<int>();

        Vector3 axis = (riftEnd - riftStart).normalized;
        float axisLen = Vector3.Distance(riftStart, riftEnd);

        for (int i = 0, hitCount = EnemyQueryService.GetLastHitCount(this); i < hitCount; i++)
        {
            var col = hits[i];
            if (col == null)
                continue;

            var c = EnemyQueryService.GetCombatant(col);
            if (c == null || c.IsDead || c == exclude)
                continue;

            if (c.GetComponent<PlayerProgressionController>() != null)
                continue;

            if (!seen.Add(c.GetInstanceID()))
                continue;

            float projection = Vector3.Dot(c.transform.position - riftStart, axis);
            if (projection < 0f || projection > axisLen)
                continue;

            if (projection > bestProjection)
            {
                bestProjection = projection;
                best = c;
            }
        }

        return best;
    }

    private static float SqrDistancePointToSegment(Vector3 p, Vector3 a, Vector3 b)
    {
        Vector3 ab = b - a;
        float t = Vector3.Dot(p - a, ab) / Mathf.Max(0.0001f, ab.sqrMagnitude);
        t = Mathf.Clamp01(t);
        Vector3 closest = a + ab * t;
        return (p - closest).sqrMagnitude;
    }
}



