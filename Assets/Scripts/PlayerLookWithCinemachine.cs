using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class PlayerLookSyncWithCinemachine : MonoBehaviour
{
    [Header("References")]
    public Transform cameraFollow;  // przypisz obiekt kamery (rig Cinemachine / główna kamera)

    [Header("Settings")]
    [Tooltip("Szybkość obracania postaci względem kierunku kamery")]
    public float rotationSpeed = 720f; // stopnie na sekundę

    private CharacterController cc;

    void Awake()
    {
        cc = GetComponent<CharacterController>();
        cameraFollow ??= ResolveCameraFollow();
        if (cameraFollow != null)
            return;

        Debug.LogWarning(
            "PlayerLookSyncWithCinemachine: brak cameraFollow i nie znaleziono aktywnej kamery. Komponent zostaje wyłączony.",
            this
        );
        enabled = false;
    }

    void Update()
    {
        SyncRotationWithCamera();
    }

    private void SyncRotationWithCamera()
    {
        Vector2 moveInput = ReadMoveInput();
        Vector3 movementInput = new Vector3(moveInput.x, 0f, moveInput.y);

        // Jeżeli gracz się nie rusza — nie zmieniaj rotacji
        if (movementInput.sqrMagnitude < 0.001f)
            return;

        movementInput.Normalize();

        // Kierunek patrzenia kamery na osi Y
        float cameraYaw = cameraFollow.eulerAngles.y;

        // Oblicz docelowy kierunek świata, do którego gracz powinien się obrócić
        float targetAngle = Mathf.Atan2(movementInput.x, movementInput.z) * Mathf.Rad2Deg + cameraYaw;

        // Aktualny i docelowy quaternion
        Quaternion targetRotation = Quaternion.Euler(0f, targetAngle, 0f);

        // Płynna rotacja: ograniczona prędkością rotationSpeed
        transform.rotation =
            Quaternion.RotateTowards(
                transform.rotation,
                targetRotation,
                rotationSpeed * Time.deltaTime
            );
    }

    private static Vector2 ReadMoveInput()
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
}
