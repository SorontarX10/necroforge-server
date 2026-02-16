using UnityEngine;
using GrassSim.Combat;
using GrassSim.Enemies;

public class EnemyDamageDealer : MonoBehaviour
{
    public float hitCooldown = 0.6f;
    [SerializeField, Min(0.05f)] private float ownerRefRefreshInterval = 0.25f;
    [SerializeField] private bool ignoreTriggerTargets = true;
    [SerializeField] private bool shareCooldownAcrossOwnerHitboxes = true;

    public AudioSource audioSource;
    public AudioClip slash_1;
    public AudioClip slash_2;
    public AudioClip slash_3;

    [SerializeField]
    private LayerMask validTargetLayers;

    private Combatant owner;
    private EnemyCombatant enemy;
    private BossEnemyController ownerBossController;
    private RelicOutgoingDamageDebuff ownerOutgoingDebuff;
    private BossSpecialEffects ownerBossEffects;
    private Combatant cachedTargetCombatant;
    private PlayerRelicController cachedTargetRelics;
    private float lastHitTime;
    private float nextOwnerRefRefreshAt;

    private void Awake()
    {
        owner = GetComponentInParent<Combatant>();
        enemy = GetComponentInParent<EnemyCombatant>();
        ResolveOwnerRuntimeRefs(force: true);
    }

    private void OnEnable()
    {
        lastHitTime = -999f;
        cachedTargetCombatant = null;
        cachedTargetRelics = null;
        ResolveOwnerRuntimeRefs(force: true);
    }

    private void OnTriggerEnter(Collider other)
    {
        TryDealDamage(other);
    }

    private void OnTriggerStay(Collider other)
    {
        TryDealDamage(other);
    }

    private void TryDealDamage(Collider other)
    {
        if (ignoreTriggerTargets && other.isTrigger)
            return;

        if ((validTargetLayers.value & (1 << other.gameObject.layer)) == 0)
            return;

        ResolveOwnerRuntimeRefs(force: false);
        if (enemy == null || enemy.stats == null)
            return;

        if (shareCooldownAcrossOwnerHitboxes)
        {
            if (Time.time < enemy.sharedMeleeHitAvailableAt)
                return;
        }
        else if (Time.time < lastHitTime + hitCooldown)
        {
            return;
        }

        Combatant target = other.GetComponentInParent<Combatant>();
        if (target == null || target == owner || target.IsDead)
            return;
        if (ownerBossController != null && !ownerBossController.CanDealDamage)
            return;

        float damage = enemy.stats.damage;
        if (ownerOutgoingDebuff != null)
            damage *= ownerOutgoingDebuff.GetDamageMultiplier();

        if (target != cachedTargetCombatant)
        {
            cachedTargetCombatant = target;
            cachedTargetRelics = target.GetComponent<PlayerRelicController>();
        }

        if (cachedTargetRelics != null)
            damage = cachedTargetRelics.ModifyIncomingDamage(owner, damage);

        if (damage <= 0f)
            return;

        SlashAudioPlay();
        target.TakeDamage(damage);

        if (ownerBossEffects != null)
            ownerBossEffects.ApplyOnHit(target);

        lastHitTime = Time.time;
        if (shareCooldownAcrossOwnerHitboxes)
            enemy.sharedMeleeHitAvailableAt = Time.time + Mathf.Max(0.05f, hitCooldown);
    }

    private void ResolveOwnerRuntimeRefs(bool force)
    {
        if (!force && Time.time < nextOwnerRefRefreshAt)
            return;

        nextOwnerRefRefreshAt = Time.time + Mathf.Max(0.05f, ownerRefRefreshInterval);

        if (owner == null)
            owner = GetComponentInParent<Combatant>();

        if (enemy == null)
            enemy = GetComponentInParent<EnemyCombatant>();

        ownerBossController = owner != null ? owner.GetComponent<BossEnemyController>() : null;
        ownerOutgoingDebuff = owner != null ? owner.GetComponent<RelicOutgoingDamageDebuff>() : null;
        ownerBossEffects = owner != null ? owner.GetComponent<BossSpecialEffects>() : null;
    }

    private void SlashAudioPlay()
    {
        if (audioSource == null)
            return;

        int clipVersion = Random.Range(0, 3);
        switch (clipVersion)
        {
            case 0: audioSource.PlayOneShot(slash_1); break;
            case 1: audioSource.PlayOneShot(slash_2); break;
            case 2: audioSource.PlayOneShot(slash_3); break;
        }
    }
}
