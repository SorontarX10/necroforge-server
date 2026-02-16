using UnityEngine;

public class SwordMeshRotator : MonoBehaviour
{
    public Quaternion initialLocalRotation;
    public float rotationSpeed = 14f;

    private void Start()
    {
        initialLocalRotation = transform.localRotation;
    }

    public void RotateSwordMesh(Vector3 moveDirWorld)
    {
        // kierunek w świecie → zamieniamy na lokalny układ parenta
        Vector3 localDir = transform.parent
            ? transform.parent.InverseTransformDirection(moveDirWorld)
            : moveDirWorld;

        localDir.y = 0f;
        if (localDir.sqrMagnitude < 0.0001f)
            return;

        localDir.Normalize();
        
        Quaternion axisFix = Quaternion.Euler(0f, -90f, 0f); // dopasuj raz
        Quaternion targetLocal = Quaternion.LookRotation(localDir, Vector3.up) * axisFix;

        transform.localRotation = Quaternion.Slerp(
            transform.localRotation,
            targetLocal,
            Time.deltaTime * rotationSpeed
        );
    }

    public void ResetMeshRotation()
    {
        transform.localRotation = initialLocalRotation;
    }
}

