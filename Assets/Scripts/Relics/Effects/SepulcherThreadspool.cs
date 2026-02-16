using System.Collections.Generic;
using UnityEngine;
using GrassSim.Combat;
using GrassSim.Core;

[CreateAssetMenu(
    menuName = "GrassSim/Relics/Effects/Rare/Sepulcher Threadspool",
    fileName = "Relic_SepulcherThreadspool"
)]
public class SepulcherThreadspool : RelicEffect
{
    [Header("Pattern")]
    public float patternWindow = 3f;
    public float stitchDuration = 4f;

    [Header("Damage Share")]
    public float baseSharePercent = 0.5f;
    public float sharePercentPerStack = 0.05f;

    public override void OnAcquire(PlayerRelicController player, int stacks)
    {
        Attach(player)?.Configure(this, stacks);
    }

    public override void OnStack(PlayerRelicController player, int stacks)
    {
        Attach(player)?.Configure(this, stacks);
    }

    private SepulcherThreadspoolRuntime Attach(PlayerRelicController player)
    {
        if (player == null)
            return null;

        var rt = player.GetComponent<SepulcherThreadspoolRuntime>();
        if (rt == null)
            rt = player.gameObject.AddComponent<SepulcherThreadspoolRuntime>();

        return rt;
    }
}

public class SepulcherThreadspoolRuntime : MonoBehaviour, IRelicBatchedUpdate, IRelicBatchedCadence
{
    private class StitchLink
    {
        public Combatant a;
        public Combatant b;
        public float expiresAt;
    }

    private readonly List<Combatant> recentHits = new();
    private readonly List<StitchLink> links = new();

    private PlayerRelicController player;
    private SepulcherThreadspool cfg;
    private int stacks;
    private bool subscribed;
    private float lastHitAt;

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
        recentHits.Clear();
        links.Clear();
    }

    public void Configure(SepulcherThreadspool config, int stackCount)
    {
        cfg = config;
        stacks = Mathf.Max(1, stackCount);
        TrySubscribe();
    }

    public bool IsBatchedUpdateActive => isActiveAndEnabled;

    public float BatchedUpdateInterval => 0.1f;

    public RelicTickArchetype BatchedTickArchetype => RelicTickArchetype.EnemyDebuff;

    public void TickFromRelicBatch(float now, float deltaTime)
    {
        CleanupLinks(now);
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

        if (Time.time - lastHitAt > Mathf.Max(0.1f, cfg.patternWindow))
            recentHits.Clear();
        lastHitAt = Time.time;

        recentHits.Add(target);
        if (recentHits.Count > 3)
            recentHits.RemoveAt(0);

        TryCreateLinkFromPattern();
        ApplySharedDamage(target, damage);
    }

    private void TryCreateLinkFromPattern()
    {
        if (recentHits.Count < 3)
            return;

        Combatant first = recentHits[0];
        Combatant second = recentHits[1];
        Combatant third = recentHits[2];
        if (first == null || second == null || third == null)
            return;

        if (first != third || first == second)
            return;

        if (first.IsDead || second.IsDead)
            return;

        for (int i = 0; i < links.Count; i++)
        {
            var link = links[i];
            if ((link.a == first && link.b == second) || (link.a == second && link.b == first))
            {
                link.expiresAt = Time.time + Mathf.Max(0.1f, cfg.stitchDuration);
                return;
            }
        }

        links.Add(new StitchLink
        {
            a = first,
            b = second,
            expiresAt = Time.time + Mathf.Max(0.1f, cfg.stitchDuration)
        });
    }

    private void ApplySharedDamage(Combatant hitTarget, float damage)
    {
        if (links.Count == 0)
            return;

        float sharePct = cfg.baseSharePercent + cfg.sharePercentPerStack * Mathf.Max(0, stacks - 1);
        sharePct = Mathf.Clamp(sharePct, 0f, 1f);
        if (sharePct <= 0f)
            return;

        float sharedDamage = Mathf.Max(1f, damage * sharePct);

        for (int i = 0; i < links.Count; i++)
        {
            var link = links[i];
            if (link.a == null || link.b == null || link.a.IsDead || link.b.IsDead)
                continue;

            Combatant other = null;
            if (link.a == hitTarget)
                other = link.b;
            else if (link.b == hitTarget)
                other = link.a;

            if (other == null || other.IsDead)
                continue;

            if (other.GetComponent<PlayerProgressionController>() != null)
                continue;

            RelicDamageText.Deal(other, sharedDamage, transform, cfg);
        }
    }

    private void CleanupLinks(float now)
    {
        if (links.Count == 0)
            return;

        for (int i = links.Count - 1; i >= 0; i--)
        {
            var link = links[i];
            if (link == null || link.a == null || link.b == null || link.a.IsDead || link.b.IsDead || now >= link.expiresAt)
                links.RemoveAt(i);
        }
    }
}
