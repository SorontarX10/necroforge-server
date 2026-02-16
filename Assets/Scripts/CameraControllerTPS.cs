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

    private float yaw;
    private float pitch;

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
    }

    private void LateUpdate()
    {
        if (player == null)
            return;

        Vector3 pivotPos = player.position + Vector3.up * pivotHeightOffset;
        transform.position = pivotPos;

        float mouseX = 0f;
        float mouseY = 0f;

        bool cameraInputBlockedByChoice = ChoiceUiQueue.IsShowing;
        bool cameraInputBlockedByPause = PauseMenuController.Instance != null && PauseMenuController.Instance.IsPaused;
        bool cameraInputBlockedByAttack = combatInput != null && combatInput.IsAttacking();

        // Keep camera still while choice modal or pause menu is open, and while attacking.
        if (!cameraInputBlockedByChoice && !cameraInputBlockedByPause && !cameraInputBlockedByAttack)
        {
            Vector2 mouseDelta = Mouse.current != null
                ? Mouse.current.delta.ReadValue()
                : Vector2.zero;

            mouseX = mouseDelta.x * mouseSensitivity * 0.01f;
            mouseY = mouseDelta.y * mouseSensitivity * 0.01f;
        }

        yaw += mouseX;
        pitch -= mouseY;
        pitch = Mathf.Clamp(pitch, pitchMin, pitchMax);

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
}
