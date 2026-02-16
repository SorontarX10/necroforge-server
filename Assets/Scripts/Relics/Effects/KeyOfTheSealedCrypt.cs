using UnityEngine;
using GrassSim.Combat;

[CreateAssetMenu(
    menuName = "GrassSim/Relics/Effects/Mythic/Key Of The Sealed Crypt",
    fileName = "Relic_KeyOfTheSealedCrypt"
)]
public class KeyOfTheSealedCrypt : RelicEffect, IStaminaSwingOverrideModifier
{
    [Header("Blood Drive")]
    [Range(0.01f, 1f)] public float staminaThresholdPercent = 0.1f;
    public float bloodDriveDuration = 4f;
    public float cooldown = 12f;

    [Header("Cost")]
    [Range(0f, 1f)] public float healthCostPerSwingPercent = 0.05f;

    [Header("Rewards")]
    public float staminaOnKill = 12f;
    [Range(0f, 1f)] public float healPercentOnKill = 0.03f;

    public override void OnAcquire(PlayerRelicController player, int stacks)
    {
        Attach(player)?.Configure(this, stacks);
    }

    public override void OnStack(PlayerRelicController player, int stacks)
    {
        Attach(player)?.Configure(this, stacks);
    }

    public float GetStaminaSwingMultiplierOverride(PlayerRelicController player, int stacks)
    {
        var rt = player != null ? player.GetComponent<KeyOfTheSealedCryptRuntime>() : null;
        return rt != null && rt.IsBloodDriveActive ? 1f : 0f;
    }

    private KeyOfTheSealedCryptRuntime Attach(PlayerRelicController player)
    {
        if (player == null)
            return null;

        var rt = player.GetComponent<KeyOfTheSealedCryptRuntime>();
        if (rt == null)
            rt = player.gameObject.AddComponent<KeyOfTheSealedCryptRuntime>();

        return rt;
    }
}

public class KeyOfTheSealedCryptRuntime : MonoBehaviour, IRelicBatchedUpdate, IRelicBatchedCadence
{
    private PlayerRelicController player;
    private KeyOfTheSealedCrypt cfg;
    private int stacks;
    private bool subscribed;
    private bool wasActive;

    private float bloodDriveEndsAt;
    private float nextReadyAt;

    public bool IsBloodDriveActive => Time.time < bloodDriveEndsAt;

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

    public void Configure(KeyOfTheSealedCrypt config, int stackCount)
    {
        cfg = config;
        stacks = Mathf.Max(1, stackCount);
        TrySubscribe();
    }

    public bool IsBatchedUpdateActive => isActiveAndEnabled && cfg != null && player != null && player.Progression != null;

    public float BatchedUpdateInterval => 0.05f;

    public RelicTickArchetype BatchedTickArchetype => RelicTickArchetype.PlayerState;

    public void TickFromRelicBatch(float now, float deltaTime)
    {
        TryActivateBloodDrive();

        bool nowActive = IsBloodDriveActive;
        if (nowActive != wasActive)
        {
            wasActive = nowActive;
            player.Progression.NotifyStatsChanged();
        }
    }

    private void TrySubscribe()
    {
        if (subscribed || player == null)
            return;

        player.OnBeforeMeleeHit += OnBeforeMeleeHit;
        player.OnMeleeKill += OnMeleeKill;
        subscribed = true;
    }

    private void TryUnsubscribe()
    {
        if (!subscribed || player == null)
            return;

        player.OnBeforeMeleeHit -= OnBeforeMeleeHit;
        player.OnMeleeKill -= OnMeleeKill;
        subscribed = false;
    }

    private void ActivateBloodDrive()
    {
        float duration = Mathf.Max(0.15f, cfg.bloodDriveDuration);
        bloodDriveEndsAt = Time.time + duration;
        nextReadyAt = Time.time + Mathf.Max(0.5f, cfg.cooldown);
        player?.Progression?.NotifyStatsChanged();
    }

    private void OnBeforeMeleeHit(Combatant target)
    {
        if (cfg == null || player == null || player.Progression == null)
            return;

        TryActivateBloodDrive();
        if (!IsBloodDriveActive)
            return;

        float hp = player.Progression.CurrentHealth;
        if (hp <= 0f)
            return;

        float cost = hp * Mathf.Clamp01(cfg.healthCostPerSwingPercent);
        if (cost <= 0f)
            return;

        player.Progression.currentHealth = Mathf.Max(0f, hp - cost);
        player.NotifyDamageTaken(cost);

        if (player.Progression.IsDead)
            player.gameObject.SendMessage("OnCombatantDied", SendMessageOptions.DontRequireReceiver);
    }

    private void OnMeleeKill(Combatant target, float damage, bool isCrit)
    {
        if (!IsBloodDriveActive || cfg == null || player == null || player.Progression == null)
            return;

        float staminaGain = cfg.staminaOnKill;
        if (staminaGain > 0f)
            player.Progression.AddStamina(staminaGain);

        float healPct = Mathf.Clamp01(cfg.healPercentOnKill);
        float heal = player.Progression.MaxHealth * healPct;
        if (heal > 0f)
            player.Progression.Heal(heal);
    }

    private void TryActivateBloodDrive()
    {
        if (IsBloodDriveActive || Time.time < nextReadyAt)
            return;

        if (IsBelowStaminaThreshold())
            ActivateBloodDrive();
    }

    private bool IsBelowStaminaThreshold()
    {
        if (cfg == null || player == null || player.Progression == null)
            return false;

        float maxStamina = Mathf.Max(0.0001f, player.Progression.MaxStamina);
        float staminaPct = player.Progression.CurrentStamina / maxStamina;
        float threshold = Mathf.Clamp01(cfg.staminaThresholdPercent);
        return staminaPct <= threshold + 0.0005f;
    }
}
