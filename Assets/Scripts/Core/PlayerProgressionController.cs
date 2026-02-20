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

        // Hook for UI
        public event Action<List<UpgradeOption>> OnLevelUpOptionsRolled;
        public event Action<bool> OnUpgradeMenuStateChanged;
        public event Action<UpgradeOption> OnUpgradeApplied;
        public event Action OnStatsChanged;

        public AudioSource audioSource;
        public AudioClip upgradeClick;

        private int rollCounter = 0;
        private int pendingLevelUps;
        private PlayerRelicController relics;

        private void Awake()
        {
            if (baseStats == null)
                Debug.LogError("PlayerProgressionController: baseStats not assigned.", this);

            stats = new RuntimeStats(baseStats);
            relics = GetComponent<PlayerRelicController>();
            currentHealth = MaxHealth;
            currentStamina = MaxStamina;
        }

        private void Update()
        {
            TickRegen(Time.deltaTime);
        }

        private void TickRegen(float dt)
        {
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

            float reduction = stats.damageReduction;
            if (relics != null)
                reduction += relics.GetDamageReductionBonus();

            reduction = GrassSim.Combat.CombatBalanceCaps.ClampDamageReduction(reduction);
            if (reduction > 0f)
            {
                finalAmount = amount * (1f - reduction);
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
            int levelsGained = xp.AddExpAndGetLevelUps(amount);
            if (levelsGained <= 0)
                return;

            pendingLevelUps += levelsGained;
            TryStartNextUpgradeRoll();
        }

        private void TryStartNextUpgradeRoll()
        {
            if (IsChoosingUpgrade || pendingLevelUps <= 0)
                return;

            if (upgradeLibrary == null)
                return;

            pendingLevelUps--;
            IsChoosingUpgrade = true;

            OnUpgradeMenuStateChanged?.Invoke(true);

            Time.timeScale = 0f;

            int seed = unchecked(Environment.TickCount + (rollCounter++ * 10007));
            var options = upgradeLibrary.RollOptions(3, seed);

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
