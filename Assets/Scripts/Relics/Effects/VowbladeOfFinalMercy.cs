using UnityEngine;
using GrassSim.Combat;
using GrassSim.Enemies;

[CreateAssetMenu(
    menuName = "GrassSim/Relics/Effects/Legendary/Vowblade Of Final Mercy",
    fileName = "Relic_VowbladeOfFinalMercy"
)]
public class VowbladeOfFinalMercy : RelicEffect
{
    [Header("Pattern")]
    [Min(2)] public int hitsToTrigger = 3;

    [Header("Execute")]
    [Range(0f, 1f)] public float executeHealthThresholdPercent = 0.18f;

    [Header("Bonus Damage")]
    public float baseNonExecuteMultiplier = 1.5f;
    public float nonExecuteMultiplierPerStack = 0.1f;
    public float eliteHealthThreshold = 260f;
    public float baseBossBonusDamage = 85f;
    public float bossBonusDamagePerStack = 18f;
    [Range(0f, 1f)] public float bossBonusMaxHealthPercent = 0.14f;

    public override void OnAcquire(PlayerRelicController player, int stacks)
    {
        Attach(player)?.Configure(this, stacks);
    }

    public override void OnStack(PlayerRelicController player, int stacks)
    {
        Attach(player)?.Configure(this, stacks);
    }

    private VowbladeOfFinalMercyRuntime Attach(PlayerRelicController player)
    {
        if (player == null)
            return null;

        var rt = player.GetComponent<VowbladeOfFinalMercyRuntime>();
        if (rt == null)
            rt = player.gameObject.AddComponent<VowbladeOfFinalMercyRuntime>();

        return rt;
    }
}

public class VowbladeOfFinalMercyRuntime : MonoBehaviour
{
    private static readonly Color MercyCutColor = new(1f, 0.62f, 0.22f, 0.95f);

    private PlayerRelicController player;
    private VowbladeOfFinalMercy cfg;
    private int stacks;
    private bool subscribed;

    private Combatant markedTarget;
    private int markedHits;

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

    public void Configure(VowbladeOfFinalMercy config, int stackCount)
    {
        cfg = config;
        stacks = Mathf.Max(1, stackCount);
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
        if (cfg == null || target == null || target.IsDead)
            return;

        if (markedTarget == null || markedTarget.IsDead || markedTarget != target)
        {
            markedTarget = target;
            markedHits = 1;
            return;
        }

        markedHits++;
        if (markedHits < Mathf.Max(2, cfg.hitsToTrigger))
            return;

        TriggerMercyCut(target, damage);
        markedTarget = null;
        markedHits = 0;
    }

    private void TriggerMercyCut(Combatant target, float recentHitDamage)
    {
        if (target == null || target.IsDead)
            return;

        Vector3 targetPos = target.transform.position + Vector3.up * 0.04f;
        float healthPct = target.CurrentHealth / Mathf.Max(1f, target.MaxHealth);
        if (healthPct <= Mathf.Clamp01(cfg.executeHealthThresholdPercent))
        {
            RelicGeneratedVfx.SpawnGroundCircle(
                targetPos,
                1.1f,
                MercyCutColor,
                0.42f,
                null,
                default,
                "VowbladeFinalMercy_Execute"
            );
            RelicDamageText.Deal(target, target.CurrentHealth + 5f, transform, cfg);
            return;
        }

        bool isBoss = target.GetComponent<BossEnemyController>() != null;
        EnemyCombatant enemy = target.GetComponent<EnemyCombatant>();
        bool isElite = (enemy != null && enemy.IsElite)
            || target.MaxHealth >= Mathf.Max(1f, cfg.eliteHealthThreshold);

        float bonusDamage;
        if (isBoss || isElite)
        {
            float flat = cfg.baseBossBonusDamage + cfg.bossBonusDamagePerStack * Mathf.Max(0, stacks - 1);
            float pct = target.MaxHealth * Mathf.Clamp01(cfg.bossBonusMaxHealthPercent);
            bonusDamage = Mathf.Max(flat, pct);
        }
        else
        {
            float mul = cfg.baseNonExecuteMultiplier + cfg.nonExecuteMultiplierPerStack * Mathf.Max(0, stacks - 1);
            bonusDamage = recentHitDamage * Mathf.Max(0f, mul);
        }

        Vector3 slashDir = Vector3.ProjectOnPlane(target.transform.position - transform.position, Vector3.up);
        if (slashDir.sqrMagnitude < 0.001f)
            slashDir = transform.forward;

        RelicGeneratedVfx.SpawnLineWave(
            targetPos + Vector3.up * 0.02f,
            slashDir,
            2.4f,
            0.65f,
            MercyCutColor,
            0.24f,
            "VowbladeFinalMercy_Cut"
        );
        RelicDamageText.Deal(target, Mathf.Max(1f, bonusDamage), transform, cfg);
    }
}
