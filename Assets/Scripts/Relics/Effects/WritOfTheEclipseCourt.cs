using UnityEngine;
using GrassSim.Combat;
using GrassSim.Core;
using GrassSim.Enemies;

[CreateAssetMenu(
    menuName = "GrassSim/Relics/Effects/Mythic/Writ Of The Eclipse Court",
    fileName = "Relic_WritOfTheEclipseCourt"
)]
public class WritOfTheEclipseCourt : RelicEffect
{
    [Header("Seals")]
    public int sealsRequired = 10;
    public float eliteHealthThreshold = 260f;

    [Header("Court")]
    public float courtDuration = 5f;
    public float courtRadius = 5f;
    public float incomingDamageMultiplier = 1.25f;
    public float tickInterval = 0.2f;
    public float silenceTickDuration = 0.35f;
    public LayerMask enemyMask;

    [Header("Detonation")]
    public float detonationRadius = 4.5f;
    public float baseDetonationDamage = 90f;
    public float detonationDamagePerStack = 15f;

    public override void OnAcquire(PlayerRelicController player, int stacks)
    {
        Attach(player)?.Configure(this, stacks);
    }

    public override void OnStack(PlayerRelicController player, int stacks)
    {
        Attach(player)?.Configure(this, stacks);
    }

    private WritOfTheEclipseCourtRuntime Attach(PlayerRelicController player)
    {
        if (player == null)
            return null;

        var rt = player.GetComponent<WritOfTheEclipseCourtRuntime>();
        if (rt == null)
            rt = player.gameObject.AddComponent<WritOfTheEclipseCourtRuntime>();

        return rt;
    }
}

public class WritOfTheEclipseCourtRuntime : MonoBehaviour, IRelicBatchedUpdate, IRelicBatchedCadence
{
    private static readonly Color CourtColor = RelicRarityColors.Get(RelicRarity.Mythic);

    private PlayerRelicController player;
    private WritOfTheEclipseCourt cfg;
    private int stacks;
    private bool subscribed;

    private int seals;
    private bool primed;

    private float courtEndsAt;
    private float nextTickAt;
    private Vector3 courtCenter;

    private bool CourtActive => Time.time < courtEndsAt;

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
    }

    public void Configure(WritOfTheEclipseCourt config, int stackCount)
    {
        cfg = config;
        stacks = Mathf.Max(1, stackCount);
        EnemyQueryService.ConfigureOwnerBudget(this, RelicQueryBudgetProfiles.For(BatchedTickArchetype));
        TrySubscribe();
    }

    public bool IsBatchedUpdateActive => isActiveAndEnabled && cfg != null && CourtActive;

    public float BatchedUpdateInterval => 0.05f;

    public RelicTickArchetype BatchedTickArchetype => RelicTickArchetype.PlayerAura;

    public void TickFromRelicBatch(float now, float deltaTime)
    {
        if (cfg == null || !CourtActive)
            return;

        if (now >= nextTickAt)
        {
            nextTickAt = now + Mathf.Max(0.05f, cfg.tickInterval);
            ApplyCourtDebuffs();
        }
    }

    private void TrySubscribe()
    {
        if (subscribed || player == null)
            return;

        player.OnMeleeHitDealt += OnMeleeHit;
        player.OnMeleeKill += OnMeleeKill;
        subscribed = true;
    }

    private void TryUnsubscribe()
    {
        if (!subscribed || player == null)
            return;

        player.OnMeleeHitDealt -= OnMeleeHit;
        player.OnMeleeKill -= OnMeleeKill;
        subscribed = false;
    }

    private void OnMeleeHit(Combatant target, float damage, bool isCrit)
    {
        if (cfg == null || target == null || target.IsDead)
            return;

        if (primed)
        {
            primed = false;
            seals = 0;
            OpenCourt(target.transform.position);
            return;
        }

        if (!IsEliteOrBoss(target))
            return;

        seals = Mathf.Min(Mathf.Max(1, cfg.sealsRequired), seals + 1);
        if (seals >= Mathf.Max(1, cfg.sealsRequired))
            primed = true;
    }

    private void OnMeleeKill(Combatant target, float damage, bool isCrit)
    {
        if (!CourtActive || target == null)
            return;

        Vector3 delta = target.transform.position - courtCenter;
        delta.y = 0f;
        if (delta.sqrMagnitude > cfg.courtRadius * cfg.courtRadius)
            return;

        DetonateAt(target.transform.position);
    }

    private void OpenCourt(Vector3 center)
    {
        courtCenter = center;
        courtCenter.y = transform.position.y;
        courtEndsAt = Time.time + Mathf.Max(0.2f, cfg.courtDuration);
        nextTickAt = 0f;

        RelicGeneratedVfx.SpawnGroundCircle(
            courtCenter + Vector3.up * 0.05f,
            Mathf.Max(0.8f, cfg.courtRadius),
            CourtColor,
            Mathf.Max(0.2f, cfg.courtDuration),
            null,
            default,
            "WritEclipseCourt_Field"
        );
        RelicDamageText.PlayGeneratedEventFeedback(transform, RelicRarity.Mythic, 1.18f);
    }

    private void ApplyCourtDebuffs()
    {
        if (cfg == null)
            return;

        LayerMask mask = cfg.enemyMask.value != 0 ? cfg.enemyMask : LayerMask.GetMask("Enemy", "Zombie");
        Collider[] hits;
        if (mask.value != 0)
            hits = EnemyQueryService.OverlapSphere(courtCenter, cfg.courtRadius, mask, QueryTriggerInteraction.Ignore, this);
        else
            hits = EnemyQueryService.OverlapSphere(courtCenter, cfg.courtRadius, ~0, QueryTriggerInteraction.Ignore, this);

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

            var silence = c.GetComponent<RelicSilenceDebuff>();
            if (silence == null)
                silence = c.gameObject.AddComponent<RelicSilenceDebuff>();
            silence.Apply(cfg.silenceTickDuration);

            var incoming = c.GetComponent<RelicIncomingDamageTakenDebuff>();
            if (incoming == null)
                incoming = c.gameObject.AddComponent<RelicIncomingDamageTakenDebuff>();
            incoming.Apply(Mathf.Max(1f, cfg.incomingDamageMultiplier), cfg.silenceTickDuration);
        }
    }

    private void DetonateAt(Vector3 center)
    {
        if (cfg == null)
            return;

        RelicGeneratedVfx.SpawnGroundCircle(
            center + Vector3.up * 0.05f,
            Mathf.Max(0.7f, cfg.detonationRadius),
            CourtColor,
            0.46f,
            null,
            default,
            "WritEclipseCourt_Detonation"
        );

        float damage = cfg.baseDetonationDamage + cfg.detonationDamagePerStack * Mathf.Max(0, stacks - 1);
        damage = Mathf.Max(1f, damage);

        LayerMask mask = cfg.enemyMask.value != 0 ? cfg.enemyMask : LayerMask.GetMask("Enemy", "Zombie");
        Collider[] hits;
        if (mask.value != 0)
            hits = EnemyQueryService.OverlapSphere(center, cfg.detonationRadius, mask, QueryTriggerInteraction.Ignore, this);
        else
            hits = EnemyQueryService.OverlapSphere(center, cfg.detonationRadius, ~0, QueryTriggerInteraction.Ignore, this);

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

            RelicDamageText.Deal(c, damage, transform, cfg);
        }
    }

    private bool IsEliteOrBoss(Combatant c)
    {
        if (c == null)
            return false;

        if (c.GetComponent<BossEnemyController>() != null)
            return true;

        EnemyCombatant enemy = c.GetComponent<EnemyCombatant>();
        if (enemy != null && enemy.IsElite)
            return true;

        return c.MaxHealth >= Mathf.Max(1f, cfg.eliteHealthThreshold);
    }
}

