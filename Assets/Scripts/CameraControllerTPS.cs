using GrassSim.UI;
using UnityEngine;
using UnityEngine.InputSystem;

public class CameraControllerTPS : MonoBehaviour
{
    [Header("References")]
    public Transform player;
    public Transform cameraT;
    public PlayerCombatInput combatInput;

    [Header("Settings")]
    public float mouseSensitivity;
    public float distance = 5f;
    public float pivotHeightOffset = 1.7f;
    public float pitchMin = -10f;
    public float pitchMax = 80f;
    [SerializeField, Min(0f)] private float inputResponse = 22f;
    [SerializeField, Min(0f)] private float pivotFollowSmoothing = 18f;
    [SerializeField, Min(0f)] private float lookAheadDistance = 0.15f;
    [SerializeField, Min(0f)] private float lookAheadSmoothing = 9f;

    private float yaw;
    private float pitch;
    private Vector2 smoothedMouseDelta;
    private Vector3 smoothedPivotPosition;
    private Vector3 lookAheadOffset;
    private Vector3 lastPlayerPosition;

    private void Start()
    {
        if (player == null || cameraT == null)
        {
            Debug.LogError("[Camera TPS] Assign player and cameraT.", this);
            enabled = false;
            return;
        }

        mouseSensitivity = GameSettings.MouseSensitivity * 10f;
        yaw = transform.eulerAngles.y;
        pitch = 10f;
        lastPlayerPosition = player.position;
        smoothedPivotPosition = player.position + Vector3.up * pivotHeightOffset;
        transform.position = smoothedPivotPosition;
    }

    private void LateUpdate()
    {
        if (player == null)
            return;

        bool blockedByChoice = ChoiceUiQueue.IsShowing;
        bool blockedByPause = PauseMenuController.Instance != null && PauseMenuController.Instance.IsPaused;
        bool blockedByAttack = combatInput != null && combatInput.IsAttacking();
        bool blockInput = blockedByChoice || blockedByPause || blockedByAttack;

        Vector2 rawMouseDelta = Vector2.zero;
        if (blockInput)
        {
            // Hard-stop camera drag inertia while modal choice/pause/attack lock is active.
            smoothedMouseDelta = Vector2.zero;
        }
        else if (Mouse.current != null)
        {
            rawMouseDelta = Mouse.current.delta.ReadValue();
        }

        float inputLerp = 1f - Mathf.Exp(-Mathf.Max(0f, inputResponse) * Time.deltaTime);
        smoothedMouseDelta = Vector2.Lerp(smoothedMouseDelta, rawMouseDelta, inputLerp);

        yaw += smoothedMouseDelta.x * mouseSensitivity * 0.01f;
        pitch -= smoothedMouseDelta.y * mouseSensitivity * 0.01f;
        pitch = Mathf.Clamp(pitch, pitchMin, pitchMax);

        Vector3 playerPosition = player.position;
        Vector3 horizontalVelocity = (playerPosition - lastPlayerPosition) / Mathf.Max(Time.deltaTime, 0.0001f);
        horizontalVelocity.y = 0f;
        lastPlayerPosition = playerPosition;

        Vector3 targetLookAhead = horizontalVelocity.sqrMagnitude > 0.04f
            ? horizontalVelocity.normalized * lookAheadDistance
            : Vector3.zero;
        float lookAheadLerp = 1f - Mathf.Exp(-Mathf.Max(0f, lookAheadSmoothing) * Time.deltaTime);
        lookAheadOffset = Vector3.Lerp(lookAheadOffset, targetLookAhead, lookAheadLerp);

        Vector3 targetPivotPos = playerPosition + Vector3.up * pivotHeightOffset + lookAheadOffset;
        float pivotLerp = 1f - Mathf.Exp(-Mathf.Max(0f, pivotFollowSmoothing) * Time.deltaTime);
        smoothedPivotPosition = Vector3.Lerp(smoothedPivotPosition, targetPivotPos, pivotLerp);
        transform.position = smoothedPivotPosition;

        transform.rotation = Quaternion.Euler(pitch, yaw, 0f);

        cameraT.position = transform.position - transform.forward * distance;
        cameraT.LookAt(transform.position);
    }

    private void OnEnable()
    {
        GameSettings.OnMouseSensitivityChanged += RefreshSensitivity;
    }

    private void OnDisable()
    {
        GameSettings.OnMouseSensitivityChanged -= RefreshSensitivity;
    }

    private void RefreshSensitivity()
    {
        mouseSensitivity = GameSettings.MouseSensitivity * 10f;
    }

    private void OnValidate()
    {
        inputResponse = Mathf.Max(0f, inputResponse);
        pivotFollowSmoothing = Mathf.Max(0f, pivotFollowSmoothing);
        lookAheadDistance = Mathf.Max(0f, lookAheadDistance);
        lookAheadSmoothing = Mathf.Max(0f, lookAheadSmoothing);
    }
}
