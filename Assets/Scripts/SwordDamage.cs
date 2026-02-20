using UnityEngine;
using GrassSim.Combat;
using GrassSim.Core;
using GrassSim.Telemetry;
using GrassSim.UI;

public class SwordDamage : MonoBehaviour
{
    private static readonly Color NormalHitColor = new Color(1f, 0.22f, 0.22f, 1f);
    private static readonly Color CritHitColor = new Color(1f, 0.9f, 0.2f, 1f);

    [Header("Base Damage")]
    public float baseDamage = 10f;

    [Header("Audio - Hit")]
    public AudioClip swordSlashClip;
    public AudioClip swordCritClip;
    public float hitVolume = 1f;

    private AudioSource hitAudioSource;

    private Combatant ownerCombatant;
    private WeaponController weapon;
    private PlayerProgressionController progression;
    private PlayerRelicController relics;
    private ICombatInput combatInputSource;
    private float lifeStealWindowStart = -1f;
    private float lifeStealWindowRecovered;

    private void Awake()
    {
        ownerCombatant = GetComponentInParent<Combatant>();
        weapon = GetComponentInParent<WeaponController>();
        progression = GetComponentInParent<PlayerProgressionController>();
        relics = GetComponentInParent<PlayerRelicController>();
        combatInputSource = GetComponentInParent<ICombatInput>();

        hitAudioSource = gameObject.AddComponent<AudioSource>();
        hitAudioSource.playOnAwake = false;
        hitAudioSource.spatialBlend = 1f;
        hitAudioSource.dopplerLevel = 0f;
    }

    private void OnTriggerEnter(Collider other)
    {
        Combatant target = other.GetComponentInParent<Combatant>();

        if (target == null) return;
        if (target == ownerCombatant) return;
        if (target.IsDead) return;
        if (weapon == null) return;

        relics?.NotifyBeforeMeleeHit(target);
        float damage = baseDamage;

        damage *= weapon.GetDamageMultiplier();

        float swingIntensity = Mathf.Clamp01(weapon.GetLastSwingIntensity());
        damage *= swingIntensity;

        if (combatInputSource != null && !combatInputSource.IsAttacking())
            damage *= 0.3f;

        float damageBeforeCrit = Mathf.Max(0f, damage);

        bool isCrit = false;
        float critChance = weapon.GetCritChance();

        if (Random.value < critChance)
        {
            float critMultiplier = weapon.GetCritMultiplier();
            damage *= critMultiplier;
            isCrit = true;

            if (swordCritClip != null)
                hitAudioSource.PlayOneShot(swordCritClip, hitVolume);
        }
        else
        {
            if (swordSlashClip != null)
                hitAudioSource.PlayOneShot(swordSlashClip, hitVolume);
        }

        var incomingDamageDebuff = target.GetComponent<RelicIncomingDamageTakenDebuff>();
        if (incomingDamageDebuff != null)
            damage *= incomingDamageDebuff.GetIncomingDamageMultiplier();

        bool wasAlive = !target.IsDead;
        Color dmgColor = isCrit ? CritHitColor : NormalHitColor;
        float fontSize = isCrit ? 48f : 36f;
        target.TakeDamageWithText(damage, transform, dmgColor, fontSize);
        relics?.NotifyMeleeHitDealt(target, damage, isCrit);

        if (wasAlive && target.IsDead)
            relics?.NotifyMeleeKill(target, damage, isCrit);

        float lifeStealRequested = weapon.GetLifeStealRequested();
        float lifeStealEffective = weapon.GetLifeSteal();
        if (lifeStealEffective > 0f && progression != null)
        {
            float maxHealth = progression.MaxHealth;
            float lifestealDamageBase = damageBeforeCrit;
            float rawHeal = lifestealDamageBase * lifeStealEffective;
            float perHitCappedHeal = CombatBalanceCaps.ClampLifeStealHealPerHit(rawHeal, maxHealth);
            if (perHitCappedHeal <= 0f)
                return;

            float healthBefore = progression.CurrentHealth;
            float lifeStealPerSecondCap = CombatBalanceCaps.GetLifeStealHealPerSecondCap(maxHealth);
            float perSecondCappedHeal = ApplyLifeStealPerSecondCap(perHitCappedHeal, lifeStealPerSecondCap);
            if (perSecondCappedHeal <= 0f)
                return;

            progression.Heal(perSecondCappedHeal);
            float healthAfter = progression.CurrentHealth;
            float appliedHeal = Mathf.Max(0f, healthAfter - healthBefore);
            float overheal = Mathf.Max(0f, perSecondCappedHeal - appliedHeal);

            float runTimeSeconds = GameTimerController.Instance != null
                ? Mathf.Max(0f, GameTimerController.Instance.elapsedTime)
                : 0f;
            GameplayTelemetryHub.ReportLifeStealApplied(new GameplayTelemetryHub.LifeStealAppliedSample(
                runTimeSeconds,
                lifestealDamageBase,
                lifeStealRequested,
                lifeStealEffective,
                rawHeal,
                perHitCappedHeal,
                perSecondCappedHeal,
                appliedHeal,
                overheal,
                lifeStealPerSecondCap,
                healthBefore,
                healthAfter,
                maxHealth
            ));

            if (appliedHeal > 0f)
            {
                FloatingTextSystem.Instance?.Spawn(
                    progression.transform.position + Vector3.up * 1.6f,
                    appliedHeal,
                    Color.green,
                    32f
                );
            }
        }
    }

    private float ApplyLifeStealPerSecondCap(float heal, float perSecondCap)
    {
        if (heal <= 0f || perSecondCap <= 0f)
            return 0f;

        float now = Time.time;
        if (lifeStealWindowStart < 0f || now - lifeStealWindowStart >= 1f)
        {
            lifeStealWindowStart = now;
            lifeStealWindowRecovered = 0f;
        }

        float remaining = Mathf.Max(0f, perSecondCap - lifeStealWindowRecovered);
        if (remaining <= 0f)
            return 0f;

        float allowed = Mathf.Min(heal, remaining);
        lifeStealWindowRecovered += allowed;
        return Mathf.Max(0f, allowed);
    }
}
