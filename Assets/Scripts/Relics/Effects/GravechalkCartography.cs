using System.Collections.Generic;
using UnityEngine;
using GrassSim.Combat;
using GrassSim.Core;

[CreateAssetMenu(
    menuName = "GrassSim/Relics/Effects/Rare/Gravechalk Cartography",
    fileName = "Relic_GravechalkCartography"
)]
public class GravechalkCartography : RelicEffect
{
    [Header("Trail")]
    public float distancePerLine = 8f;
    public float lineDuration = 5f;
    public float lineLength = 6f;
    public float lineRadius = 0.55f;
    [Range(0f, 0.6f)] public float distanceReductionPerStackPercent = 0.08f;

    [Header("Shackle")]
    [Range(0f, 1f)] public float slowPercent = 0.4f;
    [Range(0f, 0.2f)] public float extraSlowPercentPerStack = 0.02f;
    public float incomingDamageMultiplier = 1.12f;
    public float extraIncomingMultiplierPerStack = 0.02f;
    public float shackleDuration = 2f;
    public float extraShackleDurationPerStack = 0.15f;
    public LayerMask enemyMask;

    public override void OnAcquire(PlayerRelicController player, int stacks)
    {
        Attach(player)?.Configure(this, stacks);
    }

    public override void OnStack(PlayerRelicController player, int stacks)
    {
        Attach(player)?.Configure(this, stacks);
    }

    private GravechalkCartographyRuntime Attach(PlayerRelicController player)
    {
        if (player == null)
            return null;

        var rt = player.GetComponent<GravechalkCartographyRuntime>();
        if (rt == null)
            rt = player.gameObject.AddComponent<GravechalkCartographyRuntime>();

        return rt;
    }
}

public class GravechalkCartographyRuntime : MonoBehaviour, IRelicBatchedUpdate, IRelicBatchedCadence
{
    private struct GraveLine
    {
        public Vector3 p0;
        public Vector3 p1;
        public float expiresAt;
    }

    private readonly List<GraveLine> activeLines = new();

    private GravechalkCartography cfg;
    private int stacks;
    private float movedSinceLastLine;
    private Vector3 lastPos;
    private float nextTickAt;

    public void Configure(GravechalkCartography config, int stackCount)
    {
        cfg = config;
        stacks = Mathf.Max(1, stackCount);
        lastPos = transform.position;
        EnemyQueryService.ConfigureOwnerBudget(this, 8);
    }

    private void OnEnable()
    {
        RelicBatchedTickSystem.Register(this);
        lastPos = transform.position;
    }

    private void OnDisable()
    {
        RelicBatchedTickSystem.Unregister(this);
    }

    public bool IsBatchedUpdateActive => isActiveAndEnabled && cfg != null;

    public float BatchedUpdateInterval => 0.05f;

    public RelicTickArchetype BatchedTickArchetype => RelicTickArchetype.PlayerAura;

    public void TickFromRelicBatch(float now, float deltaTime)
    {
        if (cfg == null)
            return;

        float dtDist = Vector3.Distance(lastPos, transform.position);
        if (dtDist > 0f)
        {
            movedSinceLastLine += dtDist;
            lastPos = transform.position;
        }

        float distanceScale = 1f - Mathf.Clamp01(cfg.distanceReductionPerStackPercent) * Mathf.Max(0, stacks - 1);
        float effectiveDistancePerLine = Mathf.Max(0.6f, cfg.distancePerLine * Mathf.Max(0.25f, distanceScale));
        while (movedSinceLastLine >= effectiveDistancePerLine)
        {
            movedSinceLastLine -= effectiveDistancePerLine;
            SpawnLine();
        }

        CleanupExpired();

        if (now >= nextTickAt)
        {
            nextTickAt = now + 0.15f;
            ApplyLineEffects();
        }
    }

    private void SpawnLine()
    {
        Vector3 origin = transform.position;
        origin.y += 0.2f;

        Vector3 dir = transform.forward;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.001f)
            dir = Vector3.forward;
        dir.Normalize();

        float halfLength = Mathf.Max(0.5f, cfg.lineLength * 0.5f);
        var line = new GraveLine
        {
            p0 = origin - dir * halfLength,
            p1 = origin + dir * halfLength,
            expiresAt = Time.time + Mathf.Max(0.2f, cfg.lineDuration)
        };

        activeLines.Add(line);
    }

    private void ApplyLineEffects()
    {
        if (activeLines.Count == 0)
            return;

        LayerMask mask = cfg.enemyMask.value != 0 ? cfg.enemyMask : LayerMask.GetMask("Enemy", "Zombie");
        float radius = Mathf.Max(0.1f, cfg.lineRadius);
        float shackleDuration = Mathf.Max(0.1f, cfg.shackleDuration + cfg.extraShackleDurationPerStack * Mathf.Max(0, stacks - 1));
        float slowPercent = Mathf.Clamp01(cfg.slowPercent + cfg.extraSlowPercentPerStack * Mathf.Max(0, stacks - 1));
        float incomingMultiplier = Mathf.Max(1f, cfg.incomingDamageMultiplier + cfg.extraIncomingMultiplierPerStack * Mathf.Max(0, stacks - 1));

        for (int i = 0; i < activeLines.Count; i++)
        {
            var line = activeLines[i];

            Collider[] hits;
            if (mask.value != 0)
                hits = EnemyQueryService.OverlapCapsule(line.p0, line.p1, radius, mask, QueryTriggerInteraction.Ignore, this);
            else
                hits = EnemyQueryService.OverlapCapsule(line.p0, line.p1, radius, ~0, QueryTriggerInteraction.Ignore, this);

            for (int h = 0, hitCount = EnemyQueryService.GetLastHitCount(this); h < hitCount; h++)
            {
                var col = hits[h];
                if (col == null)
                    continue;

                var combatant = EnemyQueryService.GetCombatant(col);
                if (combatant == null || combatant.IsDead)
                    continue;

                if (combatant.GetComponent<PlayerProgressionController>() != null)
                    continue;

                var slow = combatant.GetComponent<RelicMoveSpeedDebuff>();
                if (slow == null)
                    slow = combatant.gameObject.AddComponent<RelicMoveSpeedDebuff>();
                slow.Apply(slowPercent, shackleDuration);

                var incoming = combatant.GetComponent<RelicIncomingDamageTakenDebuff>();
                if (incoming == null)
                    incoming = combatant.gameObject.AddComponent<RelicIncomingDamageTakenDebuff>();
                incoming.Apply(incomingMultiplier, shackleDuration);
            }
        }
    }

    private void CleanupExpired()
    {
        if (activeLines.Count == 0)
            return;

        float now = Time.time;
        for (int i = activeLines.Count - 1; i >= 0; i--)
        {
            if (now >= activeLines[i].expiresAt)
                activeLines.RemoveAt(i);
        }
    }
}



