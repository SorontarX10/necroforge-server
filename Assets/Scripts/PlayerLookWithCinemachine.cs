using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class PlayerLookSyncWithCinemachine : MonoBehaviour
{
    [Header("References")]
    public Transform cameraFollow;

    [Header("Settings")]
    [Tooltip("Max rotation speed in degrees per second.")]
    public float rotationSpeed = 720f;
    [Tooltip("Lower values make turning faster, higher values smoother.")]
    public float turnSmoothTime = 0.07f;
    [Tooltip("Small deadzone to avoid snapping on tiny input noise.")]
    public float minMoveInput = 0.08f;

    private float turnVelocity;

    private void Awake()
    {
        cameraFollow ??= ResolveCameraFollow();
        if (cameraFollow != null)
            return;

        Debug.LogWarning(
            "PlayerLookSyncWithCinemachine: missing cameraFollow and no active camera found. Disabling component.",
            this
        );
        enabled = false;
    }

    private void Update()
    {
        SyncRotationWithCamera();
    }

    private void SyncRotationWithCamera()
    {
        Vector2 moveInput = ReadMoveInput();
        if (moveInput.sqrMagnitude < minMoveInput * minMoveInput)
        {
            turnVelocity = Mathf.MoveTowards(turnVelocity, 0f, Time.deltaTime * 30f);
            return;
        }

        Vector3 movementInput = new Vector3(moveInput.x, 0f, moveInput.y).normalized;
        float cameraYaw = cameraFollow.eulerAngles.y;
        float targetAngle = Mathf.Atan2(movementInput.x, movementInput.z) * Mathf.Rad2Deg + cameraYaw;

        float currentAngle = transform.eulerAngles.y;
        float smoothTime = Mathf.Max(0.01f, turnSmoothTime);
        float maxTurnSpeed = Mathf.Max(1f, rotationSpeed);
        float smoothedAngle = Mathf.SmoothDampAngle(
            currentAngle,
            targetAngle,
            ref turnVelocity,
            smoothTime,
            maxTurnSpeed,
            Time.deltaTime
        );

        transform.rotation = Quaternion.Euler(0f, smoothedAngle, 0f);
    }

    private static Vector2 ReadMoveInput()
    {
        Keyboard keyboard = Keyboard.current;
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

        return new Vector2(Mathf.Clamp(horizontal, -1f, 1f), Mathf.Clamp(vertical, -1f, 1f));
    }

    private static Transform ResolveCameraFollow()
    {
        if (Camera.main != null)
            return Camera.main.transform;

        Camera[] cameras = FindObjectsByType<Camera>(FindObjectsSortMode.None);
        for (int i = 0; i < cameras.Length; i++)
        {
            Camera cam = cameras[i];
            if (cam == null || !cam.isActiveAndEnabled || !cam.gameObject.activeInHierarchy)
                continue;

            return cam.transform;
        }

        return null;
    }

    private void OnValidate()
    {
        rotationSpeed = Mathf.Max(1f, rotationSpeed);
        turnSmoothTime = Mathf.Clamp(turnSmoothTime, 0.01f, 0.4f);
        minMoveInput = Mathf.Clamp(minMoveInput, 0.01f, 0.4f);
    }
}
