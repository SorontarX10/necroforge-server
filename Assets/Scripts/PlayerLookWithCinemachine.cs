using UnityEngine;

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
        if (cameraFollow == null)
        {
            Debug.LogError(
                "PlayerLookSyncWithCinemachine: przypisz cameraFollow (MainCamera / VirtualCamera Follow)!",
                this
            );
            enabled = false;
            return;
        }
    }

    void Update()
    {
        SyncRotationWithCamera();
    }

    private void SyncRotationWithCamera()
    {
        // Pobierz wektor ruchu (input lub CharacterController)
        Vector3 movementInput = new Vector3(
            Input.GetAxis("Horizontal"),
            0f,
            Input.GetAxis("Vertical")
        );

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
}
