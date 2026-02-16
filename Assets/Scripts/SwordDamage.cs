using UnityEngine;
using GrassSim.Combat;
using GrassSim.Core;
using GrassSim.UI;

public class SwordDamage : MonoBehaviour
{
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
        Color dmgColor = isCrit ? Color.yellow : Color.red;
        float fontSize = isCrit ? 48f : 36f;
        target.TakeDamageWithText(damage, transform, dmgColor, fontSize);
        relics?.NotifyMeleeHitDealt(target, damage, isCrit);

        if (wasAlive && target.IsDead)
            relics?.NotifyMeleeKill(target, damage, isCrit);

        float lifeSteal = weapon.GetLifeSteal();
        if (lifeSteal > 0f && progression != null)
        {
            float heal = damage * lifeSteal;
            progression.Heal(heal);

            FloatingTextSystem.Instance?.Spawn(
                progression.transform.position + Vector3.up * 1.6f,
                heal,
                Color.green,
                32f
            );
        }
    }
}
