using UnityEngine;
using GrassSim.Core;
using System.Collections.Generic;

namespace GrassSim.Combat
{
    /// <summary>
    /// Combatant = runtime HP container for enemies.
    /// For player: acts as a proxy to PlayerProgressionController.
    /// </summary>
    public class Combatant : MonoBehaviour
    {
        [Header("Enemy runtime HP")]
        [SerializeField] public float currentHealth;
        [SerializeField] private float maxHealth;

        private bool isDead;
        private bool initialized;
        private float popupBaseHeight = 1.5f;
        private readonly List<IIncomingDamageGate> incomingDamageGates = new(4);
        private bool damageGatesCached;

        private PlayerProgressionController player;

        public bool IsDead => player != null ? player.IsDead : isDead;
        public bool IsPlayer => player != null;

        public float CurrentHealth => player != null ? player.CurrentHealth : currentHealth;

        public float MaxHealth => player != null ? player.MaxHealth : maxHealth;

        public event System.Action OnHealthChanged;

        private void Awake()
        {
            player = GetComponent<PlayerProgressionController>();

            if (player != null)
            {
                return;
            }

            if (!initialized)
            {
                currentHealth = Mathf.Max(1f, currentHealth);
                maxHealth = Mathf.Max(currentHealth, maxHealth);
                isDead = false;
                initialized = true;
            }

            ResolvePopupBaseHeight();
            RefreshIncomingDamageGatesCache();
        }

        private void OnEnable()
        {
            damageGatesCached = false;
            RefreshIncomingDamageGatesCache();
        }

        /// <summary>
        /// Enemy-only init
        /// </summary>
        public void Initialize(float enemyMaxHealth)
        {
            if (player != null)
                return;

            maxHealth = Mathf.Max(1f, enemyMaxHealth);
            currentHealth = maxHealth;
            isDead = false;
            initialized = true;
            ResolvePopupBaseHeight();
        }

        public void TakeDamage(float damage, Transform atacker = null)
        {
            ApplyDamageInternal(damage, atacker, false, Color.red, 36f, null, Color.clear, 24f);
        }

        public void TakeDamageWithText(
            float damage,
            Transform atacker,
            Color damageTextColor,
            float damageFontSize = 36f,
            string effectText = null,
            Color? effectTextColor = null,
            float effectFontSize = 24f
        )
        {
            Color resolvedEffectColor = effectTextColor ?? damageTextColor;
            ApplyDamageInternal(
                damage,
                atacker,
                true,
                damageTextColor,
                damageFontSize,
                effectText,
                resolvedEffectColor,
                effectFontSize
            );
        }

        private void ApplyDamageInternal(
            float damage,
            Transform atacker,
            bool customPopupStyle,
            Color damageTextColor,
            float damageFontSize,
            string effectText,
            Color effectTextColor,
            float effectFontSize
        )
        {
            if (damage <= 0f || IsDead)
                return;

            Combatant attackerCombatant =
                atacker != null ? atacker.GetComponentInParent<Combatant>() : null;

            if (!damageGatesCached)
                RefreshIncomingDamageGatesCache();

            for (int i = incomingDamageGates.Count - 1; i >= 0; i--)
            {
                IIncomingDamageGate gate = incomingDamageGates[i];
                if (gate == null)
                {
                    incomingDamageGates.RemoveAt(i);
                    continue;
                }

                if (gate.ShouldBlockIncomingDamage(attackerCombatant, damage))
                    return;
            }

            if (player != null)
            {
                player.TakeDamage(damage);
                if (player.IsDead)
                    Die();
                return;
            }

            float healthBefore = currentHealth;
            currentHealth -= damage;
            OnHealthChanged?.Invoke();

            float appliedDamage = Mathf.Clamp(damage, 0f, Mathf.Max(0f, healthBefore));
            if (appliedDamage > 0f)
            {
                Color popupColor = customPopupStyle ? damageTextColor : Color.red;
                float popupSize = customPopupStyle ? damageFontSize : 36f;
                SpawnFloatingText(appliedDamage.ToString("0.##"), popupColor, popupSize, 0f);

                if (customPopupStyle && !string.IsNullOrWhiteSpace(effectText))
                    SpawnFloatingText(effectText, effectTextColor, effectFontSize, 0.36f);
            }

            if (currentHealth <= 0f)
            {
                currentHealth = 0f;
                Die();
            }
        }

        public void Heal(float amount)
        {
            if (amount <= 0f || IsDead)
                return;

            if (player != null)
            {
                player.Heal(amount);
                return;
            }

            currentHealth = Mathf.Min(maxHealth, currentHealth + amount);
        }

        protected virtual void Die()
        {
            if (isDead)
                return;

            isDead = true;
            SendMessage("OnCombatantDied", SendMessageOptions.DontRequireReceiver);
        }

        private void ResolvePopupBaseHeight()
        {
            popupBaseHeight = 1.5f;

            CapsuleCollider capsule = GetComponentInChildren<CapsuleCollider>();
            if (capsule != null)
                popupBaseHeight = Mathf.Max(popupBaseHeight, capsule.height + 0.15f);

            CharacterController cc = GetComponentInChildren<CharacterController>();
            if (cc != null)
                popupBaseHeight = Mathf.Max(popupBaseHeight, cc.height + 0.15f);

            Renderer rend = GetComponentInChildren<Renderer>();
            if (rend != null)
                popupBaseHeight = Mathf.Max(popupBaseHeight, rend.bounds.size.y + 0.1f);
        }

        private void SpawnFloatingText(string text, Color color, float fontSize, float extraYOffset)
        {
            if (FloatingTextSystem.Instance == null)
                return;

            Vector3 pos = transform.position + Vector3.up * (popupBaseHeight + extraYOffset);
            FloatingTextSystem.Instance.SpawnText(pos, text, color, fontSize);
        }

        public void RefreshIncomingDamageGatesCache()
        {
            incomingDamageGates.Clear();
            GetComponents(incomingDamageGates);
            damageGatesCached = true;
        }
    }
}
