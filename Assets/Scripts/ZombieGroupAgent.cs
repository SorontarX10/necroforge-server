using UnityEngine;

/// <summary>
/// Legacy placeholder: grouping behavior is intentionally disabled.
/// Kept only to avoid missing-script references in existing prefabs.
/// </summary>
public class ZombieGroupAgent : MonoBehaviour
{
    [SerializeField] private bool autoDisable = true;

    private void Awake()
    {
        if (autoDisable)
            enabled = false;
    }

    public Vector3 GetCohesionDirection()
    {
        return Vector3.zero;
    }
}
