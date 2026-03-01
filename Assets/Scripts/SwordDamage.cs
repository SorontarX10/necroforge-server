using UnityEngine;
using GrassSim.Combat;
using GrassSim.Core;
using GrassSim.Enemies;
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

        bool hitBoss = target.GetComponent<BossEnemyController>() != null;
        bool hitElite = !hitBoss && target.TryGetComponent<EnemyCombatant>(out EnemyCombatant enemyCombatant) && enemyCombatant.IsElite;
        PlayHitAudio(isCrit);
        CombatHitFeedback.TriggerPlayerMeleeHit(isCrit, hitElite, hitBoss);

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

    private void PlayHitAudio(bool isCrit)
    {
        if (hitAudioSource == null || !hitAudioSource.isActiveAndEnabled)
            return;

        AudioClip clip = isCrit ? swordCritClip : swordSlashClip;
        if (clip == null)
            return;

        hitAudioSource.PlayOneShot(clip, hitVolume);
    }
}

[DefaultExecutionOrder(900)]
public sealed class CombatHitFeedback : MonoBehaviour
{
    [Header("Hit Stop")]
    [SerializeField, Min(0f)] private float critHitStop = 0.045f;
    [SerializeField, Min(0f)] private float eliteHitStop = 0.05f;
    [SerializeField, Min(0f)] private float bossHitStop = 0.06f;
    [SerializeField, Min(0f)] private float maxHitStop = 0.06f;

    [Header("Camera Shake")]
    [SerializeField, Min(0f)] private float meleeShakeStrength = 0.022f;
    [SerializeField, Min(0f)] private float critShakeStrength = 0.04f;
    [SerializeField, Min(0f)] private float bossShakeStrength = 0.052f;
    [SerializeField, Min(0.01f)] private float meleeShakeDuration = 0.06f;
    [SerializeField, Min(0.01f)] private float critShakeDuration = 0.08f;
    [SerializeField, Min(0.01f)] private float bossShakeDuration = 0.1f;
    [SerializeField, Min(0f)] private float shakeDecay = 16f;

    [Header("Sync")]
    [SerializeField, Min(0)] private int maxFeedbackFrameDelay = 1;

    private static CombatHitFeedback instance;

    private Transform cameraTransform;
    private Vector3 cameraBaseLocalPosition;
    private bool cameraBaseCaptured;
    private float activeShakeStrength;
    private float shakeUntil;
    private float hitStopUntil;
    private bool hitStopActive;
    private float preHitStopTimeScale = 1f;
    private float baseFixedDeltaTime = 0.02f;
    private bool hasPendingFeedback;
    private bool pendingCrit;
    private bool pendingElite;
    private bool pendingBoss;
    private int pendingFrame = -1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        instance = null;
    }

    public static void TriggerPlayerMeleeHit(bool isCrit, bool hitElite, bool hitBoss)
    {
        CombatHitFeedback feedback = EnsureInstance();
        if (feedback == null)
            return;

        feedback.TriggerFeedback(isCrit, hitElite, hitBoss);
    }

    private static CombatHitFeedback EnsureInstance()
    {
        if (instance != null)
            return instance;

        instance = FindFirstObjectByType<CombatHitFeedback>();
        if (instance != null)
            return instance;

        GameObject go = new("CombatHitFeedback");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<CombatHitFeedback>();
        return instance;
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void LateUpdate()
    {
        ResolveCamera();
        ProcessPendingFeedback();
        UpdateHitStop();
        UpdateShake();
    }

    private void TriggerFeedback(bool isCrit, bool hitElite, bool hitBoss)
    {
        hasPendingFeedback = true;
        pendingCrit |= isCrit;
        pendingElite |= hitElite;
        pendingBoss |= hitBoss;
        if (pendingFrame < 0)
            pendingFrame = Time.frameCount;
    }

    private void ProcessPendingFeedback()
    {
        if (!hasPendingFeedback)
            return;

        int frameDelay = Mathf.Max(0, Time.frameCount - pendingFrame);
        if (frameDelay > Mathf.Max(0, maxFeedbackFrameDelay))
        {
            ClearPendingFeedback();
            return;
        }

        bool isCrit = pendingCrit;
        bool hitElite = pendingElite;
        bool hitBoss = pendingBoss;
        ClearPendingFeedback();

        float now = Time.unscaledTime;

        float targetShakeStrength = meleeShakeStrength;
        float targetShakeDuration = meleeShakeDuration;

        if (hitBoss)
        {
            targetShakeStrength = bossShakeStrength;
            targetShakeDuration = bossShakeDuration;
        }
        else if (isCrit)
        {
            targetShakeStrength = critShakeStrength;
            targetShakeDuration = critShakeDuration;
        }

        activeShakeStrength = Mathf.Max(activeShakeStrength, targetShakeStrength);
        shakeUntil = Mathf.Max(shakeUntil, now + Mathf.Max(0.01f, targetShakeDuration));

        float stop = 0f;
        if (hitBoss)
            stop = bossHitStop;
        else if (hitElite)
            stop = eliteHitStop;
        else if (isCrit)
            stop = critHitStop;

        if (stop > 0f)
            ApplyHitStop(Mathf.Min(stop, Mathf.Max(0f, maxHitStop)));

    }

    private void ClearPendingFeedback()
    {
        hasPendingFeedback = false;
        pendingCrit = false;
        pendingElite = false;
        pendingBoss = false;
        pendingFrame = -1;
    }

    private void ResolveCamera()
    {
        Camera cam = Camera.main;
        if (cam == null)
        {
            cameraTransform = null;
            cameraBaseCaptured = false;
            return;
        }

        if (cameraTransform == cam.transform)
            return;

        cameraTransform = cam.transform;
        cameraBaseLocalPosition = cameraTransform.localPosition;
        cameraBaseCaptured = true;
    }

    private void UpdateShake()
    {
        if (cameraTransform == null || !cameraBaseCaptured)
            return;

        float now = Time.unscaledTime;
        if (now >= shakeUntil || activeShakeStrength <= 0.0001f)
        {
            activeShakeStrength = 0f;
            cameraTransform.localPosition = cameraBaseLocalPosition;
            return;
        }

        Vector3 offset = Random.insideUnitSphere * activeShakeStrength;
        offset.z *= 0.35f;
        cameraTransform.localPosition = cameraBaseLocalPosition + offset;
        activeShakeStrength = Mathf.MoveTowards(
            activeShakeStrength,
            0f,
            Mathf.Max(0f, shakeDecay) * Time.unscaledDeltaTime
        );
    }

    private void ApplyHitStop(float duration)
    {
        if (duration <= 0f)
            return;

        if (ChoiceUiQueue.IsShowing)
            return;

        if (PauseMenuController.Instance != null && PauseMenuController.Instance.IsPaused)
            return;

        if (Time.timeScale <= 0f)
            return;

        float now = Time.unscaledTime;
        hitStopUntil = Mathf.Max(hitStopUntil, now + duration);
        if (hitStopActive)
            return;

        preHitStopTimeScale = Mathf.Max(0.01f, Time.timeScale);
        baseFixedDeltaTime = Time.fixedDeltaTime / preHitStopTimeScale;
        Time.timeScale = 0f;
        Time.fixedDeltaTime = 0f;
        hitStopActive = true;
    }

    private void UpdateHitStop()
    {
        if (!hitStopActive)
            return;

        if (Time.unscaledTime < hitStopUntil)
            return;

        Time.timeScale = preHitStopTimeScale;
        Time.fixedDeltaTime = baseFixedDeltaTime * preHitStopTimeScale;
        hitStopActive = false;
    }
}
