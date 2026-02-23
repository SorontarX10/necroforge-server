using System;
using System.Collections.Generic;
using UnityEngine;
using GrassSim.Stats;
using GrassSim.Upgrades;
using GrassSim.Progression;
using GrassSim.Telemetry;

namespace GrassSim.Core
{
    public class PlayerProgressionController : MonoBehaviour
    {
        [Header("Base stats")]
        public PlayerStatsData baseStats;

        [Header("Upgrades")]
        public UpgradeLibrary upgradeLibrary;

        [Header("Experience")]
        [SerializeField] private bool enableLeveling = true;

        [Header("Runtime")]
        public RuntimeStats stats;
        public PlayerExperience xp = new PlayerExperience();

        [Header("Current resources")]
        [SerializeField] public float currentHealth;
        [SerializeField] public float currentStamina;
        [SerializeField] private float temporaryBarrier;

        public float CurrentHealth => currentHealth;
        public float MaxHealth => GetEffectiveMaxHealth();
        public float CurrentStamina => currentStamina;
        public float MaxStamina => stats.maxStamina;
        public float CurrentBarrier => temporaryBarrier;

        public bool IsDead => currentHealth <= 0f;
        public bool IsChoosingUpgrade { get; private set; }
        public bool LevelingEnabled
        {
            get => enableLeveling;
            set
            {
                if (enableLeveling == value)
                    return;

                enableLeveling = value;
                if (enableLeveling)
                    return;

                pendingLevelUps = 0;
                if (!IsChoosingUpgrade)
                    return;

                IsChoosingUpgrade = false;
                OnUpgradeMenuStateChanged?.Invoke(false);
                Time.timeScale = 1f;
            }
        }

        // Hook for UI
        public event Action<List<UpgradeOption>> OnLevelUpOptionsRolled;
        public event Action<bool> OnUpgradeMenuStateChanged;
        public event Action<UpgradeOption> OnUpgradeApplied;
        public event Action OnStatsChanged;
        public event Action OnChoiceActionStateChanged;

        public AudioSource audioSource;
        public AudioClip upgradeClick;

        private int rollCounter = 0;
        private int pendingLevelUps;
        private PlayerRelicController relics;
        private float lastIncomingDamageAt = -100f;
        private int chainedIncomingHits;

        [Header("Incoming Damage Smoothing")]
        [SerializeField] private float chainedHitWindowSeconds = 0.35f;
        [SerializeField] private float chainedHitStepReduction = 0.08f;
        [SerializeField] private float chainedHitMaxReduction = 0.4f;

        [Header("Choice Action Limits")]
        [SerializeField, Min(0)] private int maxBanishesPerRun = 5;
        [SerializeField, Min(0)] private int maxRerollsPerRun = 10;

        private int banishesRemaining;
        private int rerollsRemaining;
        private readonly HashSet<StatType> banishedUpgradeStats = new();
        private readonly HashSet<string> banishedRelicIds = new(StringComparer.Ordinal);

        public int BanishesRemaining => banishesRemaining;
        public int RerollsRemaining => rerollsRemaining;

        private void Awake()
        {
            if (baseStats == null)
                Debug.LogError("PlayerProgressionController: baseStats not assigned.", this);

            stats = new RuntimeStats(baseStats);
            relics = GetComponent<PlayerRelicController>();
            currentHealth = MaxHealth;
            currentStamina = MaxStamina;
            ResetChoiceActionState();
        }

        private void Update()
        {
            TickRegen(Time.deltaTime);
        }

        private void TickRegen(float dt)
        {
            if (GameSettings.GodMode)
            {
                float godModeMaxHealth = MaxHealth;
                float godModeMaxStamina = MaxStamina;
                bool changed = false;

                if (!Mathf.Approximately(currentHealth, godModeMaxHealth))
                {
                    currentHealth = godModeMaxHealth;
                    changed = true;
                }

                if (!Mathf.Approximately(currentStamina, godModeMaxStamina))
                {
                    currentStamina = godModeMaxStamina;
                    changed = true;
                }

                if (changed)
                    OnStatsChanged?.Invoke();

                return;
            }

            float maxHealth = MaxHealth;
            if (stats.healthRegen > 0f && currentHealth < maxHealth)
            {
                currentHealth = Mathf.Min(
                    maxHealth,
                    currentHealth + stats.healthRegen * dt
                );
            }

            float staminaRegen = GetEffectiveStaminaRegen();
            if (staminaRegen > 0f && currentStamina < MaxStamina)
            {
                currentStamina = Mathf.Min(
                    MaxStamina,
                    currentStamina + staminaRegen * dt
                );
            }
        }

        // ================= DAMAGE / HEAL =================

        public void TakeDamage(float amount)
        {
            if (GameSettings.GodMode)
                return;

            if (amount <= 0f || IsDead)
                return;

            if (relics == null)
                relics = GetComponent<PlayerRelicController>();

            float runTime = GetRunTimeSeconds();
            float maxHealth = MaxHealth;
            float healthBefore = currentHealth;

            float dodgeChance = stats.dodgeChance;
            if (relics != null)
                dodgeChance += relics.GetDodgeChanceBonus();

            dodgeChance = GrassSim.Combat.CombatBalanceCaps.ClampDodgeChance(dodgeChance);
            if (dodgeChance > 0f && UnityEngine.Random.value < dodgeChance)
            {
                SpawnCombatText("Dodge", Color.white);
                relics?.NotifyDodged();
                ReportIncomingDamageTelemetry(
                    runTime,
                    rawDamage: amount,
                    reduction: 0f,
                    damageAfterReduction: amount,
                    chainedHitReduction: 0f,
                    chainedHitCount: 0,
                    dodged: true,
                    blocked: false,
                    barrierBefore: temporaryBarrier,
                    barrierAbsorbed: 0f,
                    barrierAfter: temporaryBarrier,
                    finalDamage: 0f,
                    healthBefore: healthBefore,
                    healthAfter: healthBefore,
                    maxHealth: maxHealth
                );
                return;
            }

            if (relics != null && relics.TryBlockIncomingHit())
            {
                SpawnCombatText("Ward", new Color(0.6f, 0.9f, 1f));
                ReportIncomingDamageTelemetry(
                    runTime,
                    rawDamage: amount,
                    reduction: 0f,
                    damageAfterReduction: amount,
                    chainedHitReduction: 0f,
                    chainedHitCount: 0,
                    dodged: false,
                    blocked: true,
                    barrierBefore: temporaryBarrier,
                    barrierAbsorbed: 0f,
                    barrierAfter: temporaryBarrier,
                    finalDamage: 0f,
                    healthBefore: healthBefore,
                    healthAfter: healthBefore,
                    maxHealth: maxHealth
                );
                return;
            }

            float finalAmount = amount;
            float chainedHitReduction = 0f;
            int chainedHitCount = 0;

            float reduction = stats.damageReduction;
            if (relics != null)
                reduction += relics.GetDamageReductionBonus();

            reduction = GrassSim.Combat.CombatBalanceCaps.ClampDamageReduction(reduction);
            if (reduction > 0f)
            {
                finalAmount = amount * (1f - reduction);
            }

            if (finalAmount > 0f)
            {
                float now = Time.unscaledTime;
                float hitWindow = Mathf.Max(0.01f, chainedHitWindowSeconds);
                if (now - lastIncomingDamageAt <= hitWindow)
                    chainedIncomingHits = Mathf.Clamp(chainedIncomingHits + 1, 0, 16);
                else
                    chainedIncomingHits = 0;

                lastIncomingDamageAt = now;
                chainedHitCount = chainedIncomingHits;
                if (chainedIncomingHits > 0)
                {
                    float stepReduction = Mathf.Clamp01(chainedHitStepReduction);
                    float maxReduction = Mathf.Clamp01(chainedHitMaxReduction);
                    chainedHitReduction = Mathf.Clamp(chainedIncomingHits * stepReduction, 0f, maxReduction);
                    finalAmount *= (1f - chainedHitReduction);
                }
            }

            float damageAfterReduction = finalAmount;
            float barrierBefore = temporaryBarrier;
            float barrierAbsorbed = 0f;

            if (temporaryBarrier > 0f && finalAmount > 0f)
            {
                barrierAbsorbed = Mathf.Min(temporaryBarrier, finalAmount);
                temporaryBarrier -= barrierAbsorbed;
                finalAmount -= barrierAbsorbed;
            }

            if (finalAmount <= 0f)
            {
                ReportIncomingDamageTelemetry(
                    runTime,
                    rawDamage: amount,
                    reduction: reduction,
                    damageAfterReduction: damageAfterReduction,
                    chainedHitReduction: chainedHitReduction,
                    chainedHitCount: chainedHitCount,
                    dodged: false,
                    blocked: false,
                    barrierBefore: barrierBefore,
                    barrierAbsorbed: barrierAbsorbed,
                    barrierAfter: temporaryBarrier,
                    finalDamage: 0f,
                    healthBefore: healthBefore,
                    healthAfter: healthBefore,
                    maxHealth: maxHealth
                );
                return;
            }

            currentHealth = Mathf.Max(0f, currentHealth - finalAmount);
            relics?.NotifyDamageTaken(finalAmount);

            ReportIncomingDamageTelemetry(
                runTime,
                rawDamage: amount,
                reduction: reduction,
                damageAfterReduction: damageAfterReduction,
                chainedHitReduction: chainedHitReduction,
                chainedHitCount: chainedHitCount,
                dodged: false,
                blocked: false,
                barrierBefore: barrierBefore,
                barrierAbsorbed: barrierAbsorbed,
                barrierAfter: temporaryBarrier,
                finalDamage: finalAmount,
                healthBefore: healthBefore,
                healthAfter: currentHealth,
                maxHealth: maxHealth
            );

            if (IsDead)
                SendMessage("OnCombatantDied", SendMessageOptions.DontRequireReceiver);
        }

        public void Heal(float amount)
        {
            if (amount <= 0f || IsDead)
                return;

            if (relics == null)
                relics = GetComponent<PlayerRelicController>();

            float before = currentHealth;
            float maxHealth = MaxHealth;
            currentHealth = Mathf.Min(maxHealth, currentHealth + amount);
            float healed = Mathf.Max(0f, currentHealth - before);
            float overheal = Mathf.Max(0f, amount - healed);
            relics?.NotifyHealed(healed, overheal);
        }

        // ================= STAMINA =================

        public bool TrySpendStamina(float cost)
        {
            if (GameSettings.GodMode)
            {
                currentStamina = MaxStamina;
                return true;
            }

            if (cost <= 0f) return true;
            if (currentStamina < cost) return false;
 
            currentStamina -= cost;
            return true;
        }

        public void AddStamina(float amount)
        {
            if (amount <= 0f) return;
            currentStamina = Mathf.Min(MaxStamina, currentStamina + amount);
        }

        // ================= EXPERIENCE =================

        public void AddExp(int amount)
        {
            if (!enableLeveling)
                return;

            int levelsGained = xp.AddExpAndGetLevelUps(amount);
            if (levelsGained <= 0)
                return;

            pendingLevelUps += levelsGained;
            TryStartNextUpgradeRoll();
        }

        public List<UpgradeOption> RollUpgradeOptions(int count)
        {
            if (upgradeLibrary == null || count <= 0)
                return new List<UpgradeOption>();

            int seed = NextUpgradeRollSeed();
            return upgradeLibrary.RollOptions(count, seed, IsUpgradeBanished);
        }

        public void CancelCurrentUpgradeChoice()
        {
            if (!IsChoosingUpgrade)
                return;

            IsChoosingUpgrade = false;
            OnUpgradeMenuStateChanged?.Invoke(false);
            Time.timeScale = 1f;
            TryStartNextUpgradeRoll();
        }

        public bool IsUpgradeBanished(StatType stat)
        {
            return banishedUpgradeStats.Contains(stat);
        }

        public bool IsRelicBanished(string relicId)
        {
            return !string.IsNullOrWhiteSpace(relicId) && banishedRelicIds.Contains(relicId);
        }

        public bool TrySpendReroll()
        {
            if (rerollsRemaining <= 0)
                return false;

            rerollsRemaining--;
            NotifyChoiceActionStateChanged();
            return true;
        }

        public bool TryBanishUpgrade(UpgradeOption option)
        {
            if (option == null)
                return false;

            if (banishedUpgradeStats.Contains(option.stat))
                return false;

            if (banishesRemaining <= 0)
                return false;

            banishesRemaining--;
            banishedUpgradeStats.Add(option.stat);
            NotifyChoiceActionStateChanged();
            return true;
        }

        public bool TryBanishRelic(RelicDefinition relic)
        {
            if (relic == null || string.IsNullOrWhiteSpace(relic.id))
                return false;

            if (banishedRelicIds.Contains(relic.id))
                return false;

            if (banishesRemaining <= 0)
                return false;

            banishesRemaining--;
            banishedRelicIds.Add(relic.id);
            NotifyChoiceActionStateChanged();
            return true;
        }

        private void TryStartNextUpgradeRoll()
        {
            if (!enableLeveling)
                return;

            if (IsChoosingUpgrade || pendingLevelUps <= 0)
                return;

            if (upgradeLibrary == null)
                return;

            pendingLevelUps--;
            IsChoosingUpgrade = true;

            OnUpgradeMenuStateChanged?.Invoke(true);

            Time.timeScale = 0f;

            List<UpgradeOption> options = RollUpgradeOptions(3);

            OnLevelUpOptionsRolled?.Invoke(options);

            if (options == null || options.Count == 0)
            {
                IsChoosingUpgrade = false;
                OnUpgradeMenuStateChanged?.Invoke(false);
                Time.timeScale = 1f;
                TryStartNextUpgradeRoll();
            }
        }

        public void ApplyUpgrade(UpgradeOption option)
        {
            if (option == null) return;

            float oldMaxHp = MaxHealth;
            float oldMaxStam = MaxStamina;

            stats.Apply(option.stat, option.value);

            float newMaxHp = MaxHealth;
            float newMaxStam = MaxStamina;

            if (!Mathf.Approximately(newMaxHp, oldMaxHp) && oldMaxHp > 0f)
                currentHealth = Mathf.Clamp(
                    newMaxHp * (currentHealth / oldMaxHp),
                    1f,
                    newMaxHp
                );

            if (!Mathf.Approximately(newMaxStam, oldMaxStam) && oldMaxStam > 0f)
                currentStamina = Mathf.Clamp(
                    newMaxStam * (currentStamina / oldMaxStam),
                    0f,
                    newMaxStam
                );

            IsChoosingUpgrade = false;

            audioSource.PlayOneShot(upgradeClick, 1f);

            OnUpgradeApplied?.Invoke(option);
            OnStatsChanged?.Invoke();
            OnUpgradeMenuStateChanged?.Invoke(false);

            Time.timeScale = 1f;
            TryStartNextUpgradeRoll();
        }

        private void SpawnCombatText(object value, Color color)
        {
            if (FloatingTextSystem.Instance == null)
                return;

            Vector3 pos = transform.position + Vector3.up * 1.8f;

            if (value is float f)
                FloatingTextSystem.Instance.Spawn(pos, f, color, 42f);
            else
                FloatingTextSystem.Instance.SpawnText(pos, value.ToString(), color, 42f);
        }

        public void NotifyStatsChanged()
        {
            OnStatsChanged?.Invoke();
        }

        public float GetEffectiveMaxHealth()
        {
            if (relics == null)
                relics = GetComponent<PlayerRelicController>();

            float bonus = relics != null ? relics.GetMaxHealthBonus() : 0f;
            return Mathf.Max(1f, stats.maxHealth + bonus);
        }

        public float GetEffectiveStaminaRegen()
        {
            if (relics == null)
                relics = GetComponent<PlayerRelicController>();

            float bonus = relics != null ? relics.GetStaminaRegenBonus() : 0f;
            return Mathf.Max(0f, stats.staminaRegen + bonus);
        }

        public void AddBarrier(float amount, float maxCap)
        {
            if (amount <= 0f || maxCap <= 0f)
                return;

            temporaryBarrier = Mathf.Clamp(temporaryBarrier + amount, 0f, maxCap);
        }

        private int NextUpgradeRollSeed()
        {
            return unchecked(Environment.TickCount + (rollCounter++ * 10007));
        }

        private void ResetChoiceActionState()
        {
            banishesRemaining = Mathf.Max(0, maxBanishesPerRun);
            rerollsRemaining = Mathf.Max(0, maxRerollsPerRun);
            banishedUpgradeStats.Clear();
            banishedRelicIds.Clear();
            NotifyChoiceActionStateChanged();
        }

        private void NotifyChoiceActionStateChanged()
        {
            OnChoiceActionStateChanged?.Invoke();
        }

        private static float GetRunTimeSeconds()
        {
            if (GameTimerController.Instance != null)
                return Mathf.Max(0f, GameTimerController.Instance.elapsedTime);

            return 0f;
        }

        private static void ReportIncomingDamageTelemetry(
            float runTime,
            float rawDamage,
            float reduction,
            float damageAfterReduction,
            float chainedHitReduction,
            int chainedHitCount,
            bool dodged,
            bool blocked,
            float barrierBefore,
            float barrierAbsorbed,
            float barrierAfter,
            float finalDamage,
            float healthBefore,
            float healthAfter,
            float maxHealth
        )
        {
            GameplayTelemetryHub.ReportIncomingDamage(
                new GameplayTelemetryHub.IncomingDamageSample(
                    runTime,
                    rawDamage,
                    reduction,
                    damageAfterReduction,
                    chainedHitReduction,
                    chainedHitCount,
                    dodged,
                    blocked,
                    barrierBefore,
                    barrierAbsorbed,
                    barrierAfter,
                    finalDamage,
                    healthBefore,
                    healthAfter,
                    maxHealth
                )
            );
        }
    }

    /// <summary>
    /// Lightweight runtime cache for player references.
    /// Avoids repeated scene-wide Find* calls in hot paths.
    /// </summary>
    public static class PlayerLocator
    {
        private const float ResolveRetryInterval = 0.25f;

        private static PlayerProgressionController cachedProgression;
        private static Transform cachedTransform;
        private static float nextResolveAt;

        public static PlayerProgressionController GetProgression()
        {
            ResolveIfNeeded();
            return cachedProgression;
        }

        public static Transform GetTransform()
        {
            ResolveIfNeeded();
            return cachedTransform;
        }

        public static void Invalidate()
        {
            cachedProgression = null;
            cachedTransform = null;
            nextResolveAt = 0f;
        }

        private static void ResolveIfNeeded()
        {
            if (IsCachedReferenceValid())
                return;

            if (Time.unscaledTime < nextResolveAt)
                return;

            nextResolveAt = Time.unscaledTime + ResolveRetryInterval;

            cachedProgression = UnityEngine.Object.FindFirstObjectByType<PlayerProgressionController>();
            cachedTransform = cachedProgression != null ? cachedProgression.transform : null;
        }

        private static bool IsCachedReferenceValid()
        {
            if (cachedProgression == null)
                return false;

            GameObject go = cachedProgression.gameObject;
            if (go == null || !go.scene.IsValid())
                return false;

            if (cachedTransform == null)
                cachedTransform = cachedProgression.transform;

            return cachedTransform != null;
        }
    }
}
