using UnityEngine;
using GrassSim.Combat;
using GrassSim.Core;
using GrassSim.Stats;
using GrassSim.Enhancers;
using UnityEngine.InputSystem;

public class WeaponController : MonoBehaviour
{
    [Header("References")]
    public Transform weaponPivot;
    public Combatant owner;
    public ICombatInput combatInputSource;
    [SerializeField] private Collider weaponHitbox;
    [SerializeField] private SwordMeshRotator swordMeshRotator;

    [Header("Swing Settings")]
    public float pitchSpeed = 220f;
    public float yawSpeed = 360f;
    public float deadzone = 0.01f;
    public float maxAnglePerFrame = 35f;

    [Header("Swing Speed Stat")]
    public float baseSwingSpeedMultiplier = 1f;

    [Header("Stamina")]
    public bool requireStamina = false;
    public float staminaDrainPerSecond = 12f;
    public float staminaRegenPerSecond = 10f;
    [Range(0.01f, 1f)]
    public float minSwingStaminaMultiplier = 0.01f;

    [Header("Bounce")]
    public float bouncePitchStrength = 25f;
    public float bounceYawStrength = 35f;
    public float bounceDamping = 0.85f;
    public float bounceStopThreshold = 0.5f;

    [Header("Debug")]
    public bool debugLogs = false;

    [Header("Audio – Sword Swing")]
    public AudioSource swingLoopSource;

    [Header("Swing Energy")]
    public float energyBuildSpeed = 2.5f;   // jak szybko rośnie przy ruchu
    public float energyDecaySpeed = 3.5f;   // jak szybko spada bez ruchu

    [Header("Inertia")]
    public float acceleration = 18f;
    public float deceleration = 22f;

    [Header("Reset")]
    public KeyCode resetKey = KeyCode.Mouse1;
    public float resetSpeed = 8f;

    private bool isResetting;


    private Vector2 swingVelocity;
    private float swingEnergy; // 0..1

    public float minSwingVolume = 0.05f;
    public float maxSwingVolume = 0.9f;

    public float minSwingPitch = 0.9f;
    public float maxSwingPitch = 1.1f;

    public float audioSmooth = 12f;

    [Header("Swing Settings")]
    public Vector2 lastSwing { get; private set; }
    public float lastSwingSpeed { get; private set; }
    public float lastSwingIntensity { get; private set; }

    private Quaternion lastRotation;

    private Vector2 bounceVelocity;
    private bool hasBounce;

    private PlayerProgressionController playerProg;
    private PlayerRelicController relics;
    private Quaternion weaponIdleLocalRot;
    private BaseCombatAgent agent;
    private bool attacking;

    private float nextDiagTime;
    private WeaponEnhancerSystem enhancerSystem;
    private Vector3 weaponHitboxBaseLocalScale = Vector3.one;
    private Vector3 weaponHitboxBaseLocalPosition = Vector3.zero;
    private int weaponLengthAxis = 2;
    private bool hasHitboxLengthAnchor;
    private float hitboxAnchorAxisValue;
    private int relicCacheFrame = -1;
    private float cachedRelicDamageMultiplier = 1f;
    private float cachedRelicCritChanceBonus;
    private float cachedRelicCritMultiplierBonus;
    private float cachedRelicLifeStealBonus;
    private float cachedRelicSwingSpeedBonus;
    private float cachedRelicSwordLengthBonus;
    private float cachedRelicStaminaSwingOverride;

    private void Awake()
    {
        if (weaponPivot == null) weaponPivot = transform;
        if (owner == null) owner = GetComponentInParent<Combatant>();
        if (combatInputSource == null) combatInputSource = GetComponentInParent<ICombatInput>();

        playerProg = GetComponentInParent<PlayerProgressionController>();
        if (playerProg != null)
            playerProg.OnUpgradeMenuStateChanged += HandleUpgradeMenuState;
        relics = GetComponentInParent<PlayerRelicController>();

        weaponIdleLocalRot = weaponPivot.localRotation;
        agent = GetComponentInParent<BaseCombatAgent>();

        lastRotation = weaponPivot.rotation;
        enhancerSystem = GetComponentInChildren<WeaponEnhancerSystem>(true);
        if (weaponHitbox != null)
        {
            weaponHitboxBaseLocalScale = weaponHitbox.transform.localScale;
            weaponHitboxBaseLocalPosition = weaponHitbox.transform.localPosition;
            weaponLengthAxis = ResolveWeaponLengthAxis();
            CacheHitboxLengthAnchor();
        }

        if (swingLoopSource != null)
        {
            swingLoopSource.loop = true;
            swingLoopSource.playOnAwake = false;
        }
    }

    private void Update()
    {
        if (playerProg != null && playerProg.IsChoosingUpgrade)
        {
            if (swingLoopSource != null && swingLoopSource.isPlaying)
                swingLoopSource.Stop();
            return;
        }

        if (owner == null || owner.IsDead) return;
        if (weaponPivot == null || combatInputSource == null) return;

        // 🔁 RESET (PPM) – tylko gdy NIE atakujemy
        if (!combatInputSource.IsAttacking() && WasResetPressedThisFrame())
            isResetting = true;

        if (isResetting)
        {
            weaponPivot.localRotation = Quaternion.Slerp(
                weaponPivot.localRotation,
                weaponIdleLocalRot,
                Time.deltaTime * resetSpeed
            );

            if (Quaternion.Angle(weaponPivot.localRotation, weaponIdleLocalRot) < 0.5f)
            {
                weaponPivot.localRotation = weaponIdleLocalRot;
                isResetting = false;
            }

            if (swordMeshRotator != null)
                swordMeshRotator.ResetMeshRotation();

            UpdateSwingAudio();
            return;
        }

        if (hasBounce)
        {
            ApplyBounce();
            return;
        }

        if (!combatInputSource.IsAttacking())
        {
            TryRegenerateStamina(Time.deltaTime);
            lastSwing = Vector2.zero;
            lastSwingSpeed = 0f;
            lastSwingIntensity = 0f;
            lastRotation = weaponPivot.rotation;

            swingEnergy = Mathf.MoveTowards(
                swingEnergy,
                0f,
                energyDecaySpeed * Time.deltaTime
            );

            swingVelocity = Vector2.Lerp(
                swingVelocity,
                Vector2.zero,
                deceleration * Time.deltaTime
            );

            lastSwingIntensity = swingEnergy * swingEnergy;
            UpdateSwingAudio();
            return;
        }

        Vector2 swing = combatInputSource.GetSwingInput();
        if (swing.sqrMagnitude < deadzone * deadzone)
        {
            UpdateSwingAudio();
            return;
        }

        lastSwing = swing;

        float swingSpeed = Mathf.Clamp01(swing.magnitude / 50f);
        float swingSpeedMul = GetSwingSpeedMultiplier();

        swingEnergy += swingSpeed * energyBuildSpeed * swingSpeedMul * Time.deltaTime;
        swingEnergy = Mathf.Clamp01(swingEnergy);

        float staminaMul = GetStaminaSwingMultiplier();

        lastSwingIntensity =
            swingEnergy * staminaMul *
            Mathf.Lerp(0.8f, 1.25f, Mathf.Clamp01(swingSpeedMul - 1f));

        TryDrainStamina(lastSwingIntensity);

        Vector2 targetVelocity = swing * new Vector2(
            yawSpeed * swingSpeedMul,
            pitchSpeed * swingSpeedMul
        );

        swingVelocity = Vector2.Lerp(
            swingVelocity,
            targetVelocity,
            acceleration * Time.deltaTime
        );

        float yaw = -swingVelocity.x * Time.deltaTime;
        float pitch = -swingVelocity.y * Time.deltaTime;

        yaw = Mathf.Clamp(yaw, -maxAnglePerFrame, maxAnglePerFrame);
        pitch = Mathf.Clamp(pitch, -maxAnglePerFrame, maxAnglePerFrame);

        yaw *= staminaMul;
        pitch *= staminaMul;

        weaponPivot.Rotate(Vector3.right, pitch, Space.Self);
        weaponPivot.Rotate(Vector3.forward, yaw, Space.Self);

        // ===============================
        // WYLICZENIE KIERUNKU RUCHU MIECZA
        // ===============================
        Quaternion deltaRot = weaponPivot.rotation * Quaternion.Inverse(lastRotation);

        // jeśli ostrze jest bokiem, zamień forward -> right
        Vector3 moveDir = deltaRot * Vector3.forward;
        moveDir.y = 0f;

        if (moveDir.sqrMagnitude > 0.0001f)
        {
            moveDir.Normalize();

            // 🔥 TU WYWOŁUJESZ OBRÓT MESHA
            if (swordMeshRotator != null)
                swordMeshRotator.RotateSwordMesh(moveDir);
        }

        float deltaAngle = Quaternion.Angle(lastRotation, weaponPivot.rotation);
        lastSwingSpeed = deltaAngle / Mathf.Max(Time.deltaTime, 0.0001f);
        lastRotation = weaponPivot.rotation;


        UpdateSwingAudio();
    }

    private void LateUpdate()
    {
        ApplySwordLengthScale();
    }

    public void AddBounce(Vector3 collisionNormalWorld)
    {
        if (weaponPivot == null) weaponPivot = transform;

        Vector3 localNormal = weaponPivot.InverseTransformDirection(collisionNormalWorld);
        float k = Mathf.Max(0.25f, lastSwingIntensity);

        bounceVelocity = new Vector2(
            -localNormal.x * bounceYawStrength * k,
            -localNormal.y * bouncePitchStrength * k
        );

        hasBounce = true;
    }

    private void ApplyBounce()
    {
        Vector2 delta = bounceVelocity * Time.deltaTime;

        weaponPivot.Rotate(Vector3.right, delta.y, Space.Self);
        weaponPivot.Rotate(Vector3.forward, delta.x, Space.Self);

        bounceVelocity *= bounceDamping;

        if (bounceVelocity.magnitude < bounceStopThreshold)
        {
            bounceVelocity = Vector2.zero;
            hasBounce = false;
        }
    }

    private bool WasResetPressedThisFrame()
    {
        return WasResetPressedWithInputSystem();
    }

    private bool WasResetPressedWithInputSystem()
    {
        var mouse = Mouse.current;
        switch (resetKey)
        {
            case KeyCode.Mouse0:
                return mouse != null && mouse.leftButton.wasPressedThisFrame;
            case KeyCode.Mouse1:
                return mouse != null && mouse.rightButton.wasPressedThisFrame;
            case KeyCode.Mouse2:
                return mouse != null && mouse.middleButton.wasPressedThisFrame;
            case KeyCode.Mouse3:
                return mouse != null && mouse.forwardButton.wasPressedThisFrame;
            case KeyCode.Mouse4:
                return mouse != null && mouse.backButton.wasPressedThisFrame;
        }

        var keyboard = Keyboard.current;
        if (keyboard == null)
            return false;

        switch (resetKey)
        {
            case KeyCode.Space:
                return keyboard.spaceKey.wasPressedThisFrame;
            case KeyCode.Tab:
                return keyboard.tabKey.wasPressedThisFrame;
            case KeyCode.Escape:
                return keyboard.escapeKey.wasPressedThisFrame;
            case KeyCode.Return:
            case KeyCode.KeypadEnter:
                return keyboard.enterKey.wasPressedThisFrame;
            case KeyCode.LeftShift:
                return keyboard.leftShiftKey.wasPressedThisFrame;
            case KeyCode.RightShift:
                return keyboard.rightShiftKey.wasPressedThisFrame;
            case KeyCode.LeftControl:
                return keyboard.leftCtrlKey.wasPressedThisFrame;
            case KeyCode.RightControl:
                return keyboard.rightCtrlKey.wasPressedThisFrame;
            case KeyCode.LeftAlt:
                return keyboard.leftAltKey.wasPressedThisFrame;
            case KeyCode.RightAlt:
                return keyboard.rightAltKey.wasPressedThisFrame;
        }

        return false;
    }

    private bool TryDrainStamina(float dt)
    {
        float cost = Mathf.Sqrt(staminaDrainPerSecond * dt);

        if (playerProg == null && agent == null)
            return !requireStamina;

        if (playerProg != null)
        {
            bool ok = playerProg.TrySpendStamina(cost);
            return ok || !requireStamina;
        }

        if (agent != null)
        {
            if (agent.stamina >= cost)
            {
                agent.stamina -= cost;
                return true;
            }
            return !requireStamina;
        }

        return !requireStamina;
    }

    private void TryRegenerateStamina(float dt)
    {
        float regen = staminaRegenPerSecond * dt;

        if (playerProg != null)
        {
            playerProg.AddStamina(regen);
            return;
        }

        if (agent != null)
        {
            agent.stamina = Mathf.Min(agent.stamina + regen, agent.maxStamina);
        }
    }

    public float GetLastSwingIntensity()
    {
        return lastSwingIntensity;
    }

    private void UpdateSwingAudio()
    {
        if (swingLoopSource == null)
            return;

        if (!swingLoopSource.isActiveAndEnabled)
            return;

        attacking = combatInputSource != null && combatInputSource.IsAttacking();

        if (!attacking || lastSwingIntensity <= deadzone)
        {
            // Fade out
            swingLoopSource.volume = Mathf.Lerp(
                swingLoopSource.volume,
                0f,
                Time.deltaTime * audioSmooth
            );

            if (swingLoopSource.volume < 0.01f && swingLoopSource.isPlaying)
                swingLoopSource.Stop();

            return;
        }

        if (!swingLoopSource.isPlaying)
            swingLoopSource.Play();

        float intensity01 = Mathf.Clamp01(lastSwingIntensity);

        float targetVolume = Mathf.Lerp(
            minSwingVolume,
            maxSwingVolume,
            intensity01
        );

        // Opcjonalnie: speed też wpływa na pitch
        float speed01 = Mathf.Clamp01(lastSwingSpeed / 720f);

        float staminaMul = GetStaminaSwingMultiplier();

        float targetPitch = Mathf.Lerp(
            minSwingPitch,
            maxSwingPitch,
            Mathf.Max(intensity01, speed01) * staminaMul
        );

        swingLoopSource.volume = Mathf.Lerp(
            swingLoopSource.volume,
            targetVolume,
            Time.deltaTime * audioSmooth
        );

        swingLoopSource.pitch = Mathf.Lerp(
            swingLoopSource.pitch,
            targetPitch,
            Time.deltaTime * audioSmooth
        );
    }

    private void HandleUpgradeMenuState(bool open)
    {
        if (open)
            HardStopCombatAndAudio();
        else
            ResumeCombatAfterUpgrade();
    }

    private void HardStopCombatAndAudio()
    {
        // 1) zatrzymaj loop audio NATYCHMIAST
        if (swingLoopSource != null)
        {
            swingLoopSource.Stop();
            swingLoopSource.time = 0f;
        }

        // 2) wyłącz hitbox, żeby nie było "ciągłego ataku"
        if (weaponHitbox != null)
            weaponHitbox.enabled = false;

        // 3) reset ataku
        if (combatInputSource != null)
            attacking = false;

        // 4) wróć bronią do idle pozycji
        if (weaponPivot != null)
            weaponPivot.localRotation = weaponIdleLocalRot;
    }

    private void OnDestroy()
    {
        if (playerProg != null)
            playerProg.OnUpgradeMenuStateChanged -= HandleUpgradeMenuState;
    }

    private float GetStaminaSwingMultiplier()
    {
        EnsureRelicFrameCache();
        float overrideMultiplier = cachedRelicStaminaSwingOverride;

        if (playerProg != null && playerProg.stats != null)
        {
            float max = Mathf.Max(1f, playerProg.stats.maxStamina);
            float current = Mathf.Clamp(playerProg.currentStamina, 0f, max);
            float t = current / max;

            float staminaMultiplier = Mathf.Lerp(minSwingStaminaMultiplier, 1f, t * t);
            return Mathf.Clamp(Mathf.Max(staminaMultiplier, overrideMultiplier), minSwingStaminaMultiplier, 1f);
        }

        if (agent != null)
        {
            float max = Mathf.Max(1f, agent.maxStamina);
            float t = Mathf.Clamp01(agent.stamina / max);
            float staminaMultiplier = Mathf.Lerp(minSwingStaminaMultiplier, 1f, t * t);
            return Mathf.Clamp(Mathf.Max(staminaMultiplier, overrideMultiplier), minSwingStaminaMultiplier, 1f);
        }

        return Mathf.Clamp(Mathf.Max(1f, overrideMultiplier), minSwingStaminaMultiplier, 1f);
    }

    private void ResumeCombatAfterUpgrade()
    {
        // 1) przywróć hitbox
        if (weaponHitbox != null)
            weaponHitbox.enabled = true;

        // 2) reset stanu ataku
        attacking = false;
        lastSwing = Vector2.zero;
        lastSwingSpeed = 0f;
        lastSwingIntensity = 0f;

        // 3) reset rotacji broni
        if (weaponPivot != null)
        {
            weaponPivot.localRotation = weaponIdleLocalRot;
            lastRotation = weaponPivot.rotation;
        }

        // 4) upewnij się, że audio jest w stanie idle
        if (swingLoopSource != null)
        {
            swingLoopSource.Stop();
            swingLoopSource.time = 0f;
        }
    }

    // ===========================
    // ✅ ENHANCER-AWARE STATS API
    // ===========================

    public float GetSwingSpeedMultiplier()
    {
        float mul = baseSwingSpeedMultiplier;
        EnsureRelicFrameCache();

        if (playerProg != null && playerProg.stats != null)
        {
            float swing = playerProg.stats.swingSpeed;
            swing += cachedRelicSwingSpeedBonus;

            mul *= Mathf.Max(0.1f, swing);
        }

        if (enhancerSystem != null)
            mul = enhancerSystem.GetEffectiveValue(
                StatType.SwingSpeed,
                mul
            );

        return Mathf.Max(0.05f, mul);
    }

    public float GetDamageMultiplier()
    {
        float baseDamage = 1f;

        if (playerProg != null && playerProg.stats != null)
            baseDamage *= playerProg.stats.damage;

        if (enhancerSystem != null)
            baseDamage = enhancerSystem.GetEffectiveValue(
                StatType.Damage,
                baseDamage
            );

        EnsureRelicFrameCache();
        baseDamage *= cachedRelicDamageMultiplier;

        return Mathf.Max(0f, baseDamage);
    }

    public float GetCritChance()
    {
        float baseCrit = 0f;

        if (playerProg != null && playerProg.stats != null)
            baseCrit = playerProg.stats.critChance;

        if (enhancerSystem != null)
            baseCrit = enhancerSystem.GetEffectiveValue(
                StatType.CritChance,
                baseCrit
            );

        EnsureRelicFrameCache();
        baseCrit += cachedRelicCritChanceBonus;

        return CombatBalanceCaps.ClampCritChance(baseCrit);
    }

    public float GetCritMultiplier()
    {
        float baseCrit = 1f;

        if (playerProg != null && playerProg.stats != null)
            baseCrit = Mathf.Max(1f, playerProg.stats.critMultiplier);

        if (enhancerSystem != null)
            baseCrit = enhancerSystem.GetEffectiveValue(
                StatType.CritMultiplier,
                baseCrit
            );

        EnsureRelicFrameCache();
        baseCrit += cachedRelicCritMultiplierBonus;

        return CombatBalanceCaps.ClampCritMultiplier(baseCrit);
    }

    public float GetLifeSteal()
    {
        return CombatBalanceCaps.ApplyLifeStealDiminishing(GetLifeStealRequested());
    }

    public float GetLifeStealRequested()
    {
        float baseLS = 0f;

        if (playerProg != null && playerProg.stats != null)
            baseLS = playerProg.stats.lifeSteal;

        if (enhancerSystem != null)
            baseLS = enhancerSystem.GetEffectiveValue(
                StatType.LifeSteal,
                baseLS
            );

        EnsureRelicFrameCache();
        baseLS += cachedRelicLifeStealBonus;

        return Mathf.Clamp01(Mathf.Max(0f, baseLS));
    }

    public float GetSwordLengthMultiplier()
    {
        float mul = 1f;
        EnsureRelicFrameCache();
        mul += cachedRelicSwordLengthBonus;

        return Mathf.Max(0.1f, mul);
    }

    private void ApplySwordLengthScale()
    {
        if (weaponHitbox == null)
            return;

        float lengthMul = GetSwordLengthMultiplier();
        Vector3 targetScale = weaponHitboxBaseLocalScale;
        float axisValue = GetAxis(targetScale, weaponLengthAxis);
        SetAxis(ref targetScale, weaponLengthAxis, axisValue * lengthMul);

        if ((weaponHitbox.transform.localScale - targetScale).sqrMagnitude > 0.000001f)
            weaponHitbox.transform.localScale = targetScale;

        ApplyHitboxLengthAnchorCompensation(lengthMul);
    }

    private int ResolveWeaponLengthAxis()
    {
        if (TryGetWeaponHitboxLocalBounds(out Bounds bounds))
            return DominantAxis(bounds.size);

        return DominantAxis(weaponHitboxBaseLocalScale);
    }

    private void CacheHitboxLengthAnchor()
    {
        hasHitboxLengthAnchor = false;
        hitboxAnchorAxisValue = 0f;

        if (weaponHitbox == null)
            return;

        if (!TryGetWeaponHitboxLocalBounds(out Bounds bounds))
            return;

        float axisScale = Mathf.Abs(GetAxis(weaponHitboxBaseLocalScale, weaponLengthAxis));
        if (axisScale <= 0.00001f)
            return;

        float baseAxisCenter = GetAxis(weaponHitboxBaseLocalPosition, weaponLengthAxis);
        float minAxis = GetAxis(bounds.min, weaponLengthAxis) * axisScale;
        float maxAxis = GetAxis(bounds.max, weaponLengthAxis) * axisScale;
        float minEndpoint = baseAxisCenter + minAxis;
        float maxEndpoint = baseAxisCenter + maxAxis;

        hitboxAnchorAxisValue = Mathf.Abs(minEndpoint) <= Mathf.Abs(maxEndpoint)
            ? minAxis
            : maxAxis;

        hasHitboxLengthAnchor = true;
    }

    private void ApplyHitboxLengthAnchorCompensation(float lengthMul)
    {
        if (weaponHitbox == null)
            return;

        Vector3 targetLocalPosition = weaponHitboxBaseLocalPosition;
        if (hasHitboxLengthAnchor)
        {
            float baseAxis = GetAxis(targetLocalPosition, weaponLengthAxis);
            float anchorDelta = hitboxAnchorAxisValue - (hitboxAnchorAxisValue * lengthMul);
            SetAxis(ref targetLocalPosition, weaponLengthAxis, baseAxis + anchorDelta);
        }

        if ((weaponHitbox.transform.localPosition - targetLocalPosition).sqrMagnitude > 0.000001f)
            weaponHitbox.transform.localPosition = targetLocalPosition;
    }

    private bool TryGetWeaponHitboxLocalBounds(out Bounds bounds)
    {
        bounds = default;
        if (weaponHitbox == null)
            return false;

        if (weaponHitbox is BoxCollider box)
        {
            bounds = new Bounds(box.center, box.size);
            return true;
        }

        if (weaponHitbox is CapsuleCollider capsule)
        {
            Vector3 size = Vector3.one * (capsule.radius * 2f);
            int axis = Mathf.Clamp(capsule.direction, 0, 2);
            SetAxis(ref size, axis, Mathf.Max(size[axis], capsule.height));
            bounds = new Bounds(capsule.center, size);
            return true;
        }

        if (weaponHitbox is SphereCollider sphere)
        {
            float diameter = sphere.radius * 2f;
            bounds = new Bounds(sphere.center, Vector3.one * diameter);
            return true;
        }

        if (weaponHitbox is MeshCollider meshCollider && meshCollider.sharedMesh != null)
        {
            bounds = meshCollider.sharedMesh.bounds;
            return true;
        }

        MeshFilter meshFilter = weaponHitbox.GetComponent<MeshFilter>();
        if (meshFilter != null && meshFilter.sharedMesh != null)
        {
            bounds = meshFilter.sharedMesh.bounds;
            return true;
        }

        return false;
    }

    private static int DominantAxis(Vector3 v)
    {
        float ax = Mathf.Abs(v.x);
        float ay = Mathf.Abs(v.y);
        float az = Mathf.Abs(v.z);

        if (ax >= ay && ax >= az)
            return 0;
        if (ay >= ax && ay >= az)
            return 1;
        return 2;
    }

    private static float GetAxis(Vector3 v, int axis)
    {
        return axis switch
        {
            0 => v.x,
            1 => v.y,
            _ => v.z
        };
    }

    private static void SetAxis(ref Vector3 v, int axis, float value)
    {
        switch (axis)
        {
            case 0:
                v.x = value;
                break;
            case 1:
                v.y = value;
                break;
            default:
                v.z = value;
                break;
        }
    }

    private void EnsureRelicFrameCache()
    {
        int frame = Time.frameCount;
        if (relicCacheFrame == frame)
            return;

        relicCacheFrame = frame;
        cachedRelicDamageMultiplier = 1f;
        cachedRelicCritChanceBonus = 0f;
        cachedRelicCritMultiplierBonus = 0f;
        cachedRelicLifeStealBonus = 0f;
        cachedRelicSwingSpeedBonus = 0f;
        cachedRelicSwordLengthBonus = 0f;
        cachedRelicStaminaSwingOverride = 0f;

        if (relics == null)
            return;

        cachedRelicDamageMultiplier = relics.GetDamageMultiplier();
        cachedRelicCritChanceBonus = relics.GetCritChanceBonus();
        cachedRelicCritMultiplierBonus = relics.GetCritMultiplierBonus();
        cachedRelicLifeStealBonus = relics.GetLifeStealBonus();
        cachedRelicSwingSpeedBonus = relics.GetSwingSpeedBonus();
        cachedRelicSwordLengthBonus = relics.GetSwordLengthBonus();
        cachedRelicStaminaSwingOverride = relics.GetStaminaSwingMultiplierOverride();
    }
}
