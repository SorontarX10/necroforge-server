using UnityEngine;
using GrassSim.Combat;

[CreateAssetMenu(
    menuName = "GrassSim/Relics/Effects/Uncommon/Penitent Quill",
    fileName = "Relic_PenitentQuill"
)]
public class PenitentQuill : RelicEffect
{
    [Header("Record")]
    public float recordCooldown = 15f;

    [Header("Penance Window")]
    [Min(1)] public int hitsRequired = 5;
    public float comboWindow = 4f;
    public float extraHealMultiplierPerStack = 0.1f;

    public override void OnAcquire(PlayerRelicController player, int stacks)
    {
        Attach(player)?.Configure(this, stacks);
    }

    public override void OnStack(PlayerRelicController player, int stacks)
    {
        Attach(player)?.Configure(this, stacks);
    }

    private PenitentQuillRuntime Attach(PlayerRelicController player)
    {
        if (player == null)
            return null;

        var rt = player.GetComponent<PenitentQuillRuntime>();
        if (rt == null)
            rt = player.gameObject.AddComponent<PenitentQuillRuntime>();

        return rt;
    }
}

public class PenitentQuillRuntime : MonoBehaviour
{
    private PlayerRelicController player;
    private PenitentQuill cfg;
    private int stacks;
    private bool subscribed;

    private float nextRecordAt;
    private float recordedDamage;
    private int hitsInWindow;
    private float comboEndsAt;

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

    public void Configure(PenitentQuill config, int stackCount)
    {
        cfg = config;
        stacks = Mathf.Max(1, stackCount);
        TrySubscribe();
    }

    private void TrySubscribe()
    {
        if (subscribed || player == null)
            return;

        player.OnDamageTaken += OnDamageTaken;
        player.OnMeleeHitDealt += OnMeleeHit;
        subscribed = true;
    }

    private void TryUnsubscribe()
    {
        if (!subscribed || player == null)
            return;

        player.OnDamageTaken -= OnDamageTaken;
        player.OnMeleeHitDealt -= OnMeleeHit;
        subscribed = false;
    }

    private void OnDamageTaken(float amount)
    {
        if (cfg == null || amount <= 0f)
            return;

        if (Time.time < nextRecordAt)
            return;

        recordedDamage = amount;
        nextRecordAt = Time.time + Mathf.Max(0.1f, cfg.recordCooldown);
        hitsInWindow = 0;
        comboEndsAt = 0f;
    }

    private void OnMeleeHit(Combatant target, float damage, bool isCrit)
    {
        if (cfg == null || recordedDamage <= 0f)
            return;

        if (Time.time > comboEndsAt)
        {
            comboEndsAt = Time.time + Mathf.Max(0.1f, cfg.comboWindow);
            hitsInWindow = 0;
        }

        hitsInWindow++;
        if (hitsInWindow < Mathf.Max(1, cfg.hitsRequired))
            return;

        float healMultiplier = 1f + cfg.extraHealMultiplierPerStack * Mathf.Max(0, stacks - 1);
        float healAmount = Mathf.Max(1f, recordedDamage * Mathf.Max(0f, healMultiplier));
        player?.Progression?.Heal(healAmount);

        recordedDamage = 0f;
        hitsInWindow = 0;
        comboEndsAt = 0f;
    }
}
