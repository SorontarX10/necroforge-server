using UnityEngine;
using UnityEngine.InputSystem;
using GrassSim.Combat;
using GrassSim.Core;
using GrassSim.Enhancers;
using GrassSim.Enemies;
using GrassSim.Stats;

[RequireComponent(typeof(CharacterController))]
public class PlayerControllerCC : MonoBehaviour
{
    [Header("Movement Settings")]
    public float moveSpeed = 5f;

    [Header("Jump Settings")]
    public float jumpHeight = 2f;

    [Header("Enemy Head Knockback")]
    [SerializeField] private bool enableHeadKnockback = true;
    [SerializeField, Min(0.05f)] private float headKnockbackCooldown = 0.25f;
    [SerializeField, Min(0.1f)] private float headKnockbackHorizontalForce = 1.6f;
    [SerializeField, Min(0.1f)] private float headKnockbackVerticalSpeed = 1f;
    [SerializeField, Min(0.1f)] private float headKnockbackMultiplier = 1f;
    [SerializeField, Min(0.1f)] private float minEffectiveHeadKnockbackHorizontal = 1.2f;
    [SerializeField, Min(0.1f)] private float minEffectiveHeadKnockbackVertical = 0.8f;
    [SerializeField, Min(0f)] private float knockbackFallBoostScale = 0.036f;
    [SerializeField, Min(0.1f)] private float knockbackDampingWhileActive = 10f;
    [SerializeField, Min(0.01f)] private float knockbackStrongPhaseDuration = 0.016f;
    [SerializeField, Min(0f)] private float minFallSpeedForHeadKnockback = 0.2f;
    [SerializeField, Range(0.01f, 0.5f)] private float headTopTolerance = 0.16f;
    [SerializeField, Range(0f, 1f)] private float headContactAboveCenterBias = 0.15f;
    [SerializeField, Range(0.05f, 0.95f)] private float dogTopSurfaceNormalMin = 0.12f;
    [SerializeField, Min(0.1f)] private float externalVelocityDamping = 12f;

    [Header("Ground Check")]
    public Transform groundCheck;
    public float groundDistance = 0.2f;
    public LayerMask groundMask;

    [Header("Camera Reference")]
    public Transform cameraT;  // assign MainCamera transform here

    [Header("Animation")]
    [SerializeField, Min(0f)] private float speedDampTime = 0.08f;
    [SerializeField, Min(0f)] private float speedAnimationResponse = 14f;

    private CharacterController controller;
    private PlayerControls controls;

    private Vector2 actionMoveInput = Vector2.zero;
    private Vector2 moveInput = Vector2.zero;
    private bool jumpRequested = false;
    private bool isGrounded = false;
    private Vector3 velocity = Vector3.zero;
    private Vector3 externalVelocity = Vector3.zero;
    private float nextHeadKnockbackAt = -999f;

    private Animator animator;
    private PlayerProgressionController progression;
    private PlayerRelicController relics;
    private WeaponEnhancerSystem enhancerSystem;
    private float speed;
    private float smoothedMoveMagnitude;
    private bool wasGroundedBeforeGravity;
    private float strongKnockbackUntil = -999f;

    void Awake()
    {
        controller = GetComponent<CharacterController>();

        controls = new PlayerControls();
        controls.Gameplay.Move.performed += ctx => actionMoveInput = ctx.ReadValue<Vector2>();
        controls.Gameplay.Move.canceled += _ => actionMoveInput = Vector2.zero;
        controls.Gameplay.Jump.performed += _ => jumpRequested = true;

        Cursor.lockState = CursorLockMode.Locked;

        animator = GetComponent<Animator>();
        progression = GetComponent<PlayerProgressionController>();
        relics = GetComponent<PlayerRelicController>();
        enhancerSystem = GetComponentInChildren<WeaponEnhancerSystem>(true);
    }

    void OnEnable()
    {
        controls.Gameplay.Enable();
    }

    void OnDisable()
    {
        controls.Gameplay.Disable();
    }

    void Update()
    {
        wasGroundedBeforeGravity = isGrounded;
        RefreshInputState();
        ApplyGravity();
        GroundCheck();
        RotateToCamera();
        MoveCharacter();
        ApplyExternalVelocity();
        HandleJump();

        speed = moveInput.magnitude;
        float speedLerp = 1f - Mathf.Exp(-Mathf.Max(0f, speedAnimationResponse) * Time.deltaTime);
        smoothedMoveMagnitude = Mathf.Lerp(smoothedMoveMagnitude, speed, speedLerp);
        if (animator != null)
            animator.SetFloat("Speed", smoothedMoveMagnitude, speedDampTime, Time.deltaTime);
    }

    void RefreshInputState()
    {
        moveInput = actionMoveInput;

        Vector2 keyboardMove = ReadKeyboardMoveInput();
        if (keyboardMove.sqrMagnitude > 0.0001f)
            moveInput = keyboardMove;

        var keyboard = Keyboard.current;
        if (keyboard != null && keyboard.spaceKey.wasPressedThisFrame)
            jumpRequested = true;

        moveInput = Vector2.ClampMagnitude(moveInput, 1f);
    }

    private static Vector2 ReadKeyboardMoveInput()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null)
            return Vector2.zero;

        float horizontal = 0f;
        float vertical = 0f;

        if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed)
            horizontal -= 1f;
        if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed)
            horizontal += 1f;
        if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed)
            vertical -= 1f;
        if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed)
            vertical += 1f;

        return new Vector2(horizontal, vertical);
    }

    void ApplyGravity()
    {
        velocity.y += Physics.gravity.y * Time.deltaTime;
        controller.Move(velocity * Time.deltaTime);
    }

    void GroundCheck()
    {
        Vector3 origin = (groundCheck != null)
            ? groundCheck.position + Vector3.up * 0.05f
            : transform.position + Vector3.down * (controller.height * 0.5f - 0.1f);

        isGrounded = Physics.Raycast(origin, Vector3.down, groundDistance + 0.1f, groundMask);
        if (isGrounded && velocity.y < 0f)
            velocity.y = -2f;
    }

    void RotateToCamera()
    {
        if (cameraT == null)
            return;

        Vector3 camForward = cameraT.forward;
        camForward.y = 0f;
        if (camForward.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.LookRotation(camForward.normalized);
    }

    void MoveCharacter()
    {
        if (IsMovementBlocked())
            return;

        // Movement relative to camera forward/ right
        Vector3 forward = cameraT.forward;
        forward.y = 0f;
        forward.Normalize();

        Vector3 right = cameraT.right;
        right.y = 0f;
        right.Normalize();

        Vector3 moveDir = forward * moveInput.y + right * moveInput.x;
        if (moveDir.sqrMagnitude > 0f)
        {
            float finalSpeed = GetFinalMoveSpeed();
            controller.Move(moveDir * finalSpeed * Time.deltaTime);
        }
    }

    void HandleJump()
    {
        if (IsMovementBlocked())
        {
            jumpRequested = false;
            return;
        }

        if (jumpRequested && isGrounded)
        {
            velocity.y = Mathf.Sqrt(jumpHeight * -2f * Physics.gravity.y);
        }
        jumpRequested = false;
    }

    void ApplyExternalVelocity()
    {
        if (externalVelocity.sqrMagnitude <= 0.0001f)
        {
            externalVelocity = Vector3.zero;
            return;
        }

        controller.Move(externalVelocity * Time.deltaTime);
        float damping = externalVelocityDamping;
        if (Time.time < strongKnockbackUntil)
            damping = Mathf.Min(damping, Mathf.Max(0.1f, knockbackDampingWhileActive));

        externalVelocity = Vector3.MoveTowards(
            externalVelocity,
            Vector3.zero,
            Mathf.Max(0.1f, damping) * Time.deltaTime
        );
    }

    float GetFinalMoveSpeed()
    {
        float finalSpeed = moveSpeed;

        if (progression != null && progression.stats != null)
            finalSpeed = progression.stats.speed;

        if (relics != null)
            finalSpeed += relics.GetSpeedBonus();

        if (enhancerSystem != null)
            finalSpeed = enhancerSystem.GetEffectiveValue(StatType.Speed, finalSpeed);

        return Mathf.Max(0f, finalSpeed);
    }

    bool IsMovementBlocked()
    {
        var root = GetComponent<BossRootDebuff>();
        return root != null && root.IsRooted;
    }

    void OnControllerColliderHit(ControllerColliderHit hit)
    {
        if (!enableHeadKnockback || hit.collider == null)
            return;

        if (Time.time < nextHeadKnockbackAt)
            return;

        if (!ShouldApplyHeadKnockback(hit, out EnemyCombatant enemy))
            return;

        Vector3 away = transform.position - enemy.transform.position;
        away.y = 0f;
        if (away.sqrMagnitude < 0.0001f)
            away = -transform.forward;

        away.Normalize();
        float knockbackMul = Mathf.Max(1f, headKnockbackMultiplier);
        float horizontalImpulse = Mathf.Max(minEffectiveHeadKnockbackHorizontal, headKnockbackHorizontalForce) * knockbackMul;
        float verticalImpulse = Mathf.Max(minEffectiveHeadKnockbackVertical, headKnockbackVerticalSpeed) * knockbackMul;

        float requiredFallSpeed = Mathf.Max(0f, minFallSpeedForHeadKnockback);
        float fallingSpeed = Mathf.Max(0f, -velocity.y - requiredFallSpeed);
        if (fallingSpeed > 0f)
        {
            float fallBoost = Mathf.Clamp01(fallingSpeed / 8f) * Mathf.Max(0f, knockbackFallBoostScale);
            horizontalImpulse *= 1f + fallBoost;
            verticalImpulse *= 1f + fallBoost * 0.85f;
        }

        externalVelocity = away * horizontalImpulse;
        velocity.y = Mathf.Max(velocity.y, verticalImpulse);
        strongKnockbackUntil = Time.time + Mathf.Max(0.01f, knockbackStrongPhaseDuration);
        jumpRequested = false;
        nextHeadKnockbackAt = Time.time + Mathf.Max(0.05f, headKnockbackCooldown);
    }

    bool ShouldApplyHeadKnockback(ControllerColliderHit hit, out EnemyCombatant enemy)
    {
        enemy = hit.collider.GetComponentInParent<EnemyCombatant>();
        if (enemy == null)
            return false;

        Combatant enemyCombatant = enemy.GetComponent<Combatant>();
        if (enemyCombatant != null && enemyCombatant.IsDead)
            return false;

        float requiredFallSpeed = Mathf.Max(0f, minFallSpeedForHeadKnockback);
        bool fallingOnTarget =
            !wasGroundedBeforeGravity
            && (velocity.y <= -requiredFallSpeed || hit.moveDirection.y < -0.1f);
        if (!fallingOnTarget)
            return false;

        if (IsDogEnemy(enemy))
        {
            return hit.normal.y >= Mathf.Clamp(dogTopSurfaceNormalMin, 0.05f, 0.95f);
        }

        float topNormalMin = 0.18f;
        if (hit.normal.y < topNormalMin)
            return false;

        float enemyTopY = hit.collider.bounds.max.y;
        float enemyCenterY = hit.collider.bounds.center.y;
        float playerFeetY = controller.bounds.min.y;
        float topTolerance = Mathf.Clamp(headTopTolerance, 0.01f, 0.5f);
        bool feetCloseToTop = playerFeetY >= enemyTopY - topTolerance;
        bool contactClearlyAboveCenter =
            hit.point.y >= enemyCenterY + Mathf.Clamp01(headContactAboveCenterBias) * hit.collider.bounds.extents.y;

        if (!feetCloseToTop && !contactClearlyAboveCenter)
            return false;

        return true;
    }

    private static bool IsDogEnemy(EnemyCombatant enemy)
    {
        if (enemy == null)
            return false;

        BossEnemyController boss = enemy.GetComponent<BossEnemyController>();
        if (boss != null && boss.ArchetypeLabel != null)
        {
            if (boss.ArchetypeLabel.IndexOf("dog", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }

        string enemyName = enemy.name;
        return !string.IsNullOrWhiteSpace(enemyName)
            && enemyName.IndexOf("dog", System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private void OnValidate()
    {
        speedDampTime = Mathf.Max(0f, speedDampTime);
        speedAnimationResponse = Mathf.Max(0f, speedAnimationResponse);
        headContactAboveCenterBias = Mathf.Clamp01(headContactAboveCenterBias);
        minEffectiveHeadKnockbackHorizontal = Mathf.Max(0.1f, minEffectiveHeadKnockbackHorizontal);
        minEffectiveHeadKnockbackVertical = Mathf.Max(0.1f, minEffectiveHeadKnockbackVertical);
        knockbackFallBoostScale = Mathf.Max(0f, knockbackFallBoostScale);
        knockbackDampingWhileActive = Mathf.Max(0.1f, knockbackDampingWhileActive);
        knockbackStrongPhaseDuration = Mathf.Max(0.01f, knockbackStrongPhaseDuration);
    }
}
