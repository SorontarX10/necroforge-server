using UnityEngine;
using UnityEngine.InputSystem;
using GrassSim.Core;
using GrassSim.Enhancers;
using GrassSim.Stats;

[RequireComponent(typeof(CharacterController))]
public class PlayerControllerCC : MonoBehaviour
{
    [Header("Movement Settings")]
    public float moveSpeed = 5f;

    [Header("Jump Settings")]
    public float jumpHeight = 2f;

    [Header("Ground Check")]
    public Transform groundCheck;
    public float groundDistance = 0.2f;
    public LayerMask groundMask;

    [Header("Camera Reference")]
    public Transform cameraT;  // assign MainCamera transform here

    private CharacterController controller;
    private PlayerControls controls;

    private Vector2 moveInput = Vector2.zero;
    private bool jumpRequested = false;
    private bool isGrounded = false;
    private Vector3 velocity = Vector3.zero;

    private Animator animator;
    private PlayerProgressionController progression;
    private PlayerRelicController relics;
    private WeaponEnhancerSystem enhancerSystem;
    private float speed;

    void Awake()
    {
        controller = GetComponent<CharacterController>();

        controls = new PlayerControls();
        controls.Gameplay.Move.performed += ctx => moveInput = ctx.ReadValue<Vector2>();
        controls.Gameplay.Move.canceled += _ => moveInput = Vector2.zero;
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
        ApplyGravity();
        GroundCheck();
        RotateToCamera();
        MoveCharacter();
        HandleJump();

        speed = moveInput.magnitude;
        animator.SetFloat("Speed", speed);
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
}
