using UnityEngine;

public class CameraAttackLock : MonoBehaviour
{
    [Header("Refs")]
    [Tooltip("PlayerCombatInput z gracza")]
    public PlayerCombatInput combatInput;

    [Header("Options")]
    public bool lockDuringAttack = true;

    private Behaviour freeLook; // CinemachineFreeLook jako Behaviour
    private bool wasLocked;

    void Awake()
    {
        // znajdź FreeLook NA TYM SAMYM OBIEKCIE
        freeLook = GetComponent<Behaviour>();

        if (combatInput == null)
        {
            Debug.LogError(
                "CameraAttackLock: nie przypisano PlayerCombatInput!",
                this
            );
        }
    }

    void LateUpdate()
    {
        if (combatInput == null || freeLook == null)
            return;

        bool shouldLock = lockDuringAttack && combatInput.IsAttacking();

        if (shouldLock && !wasLocked)
        {
            freeLook.enabled = false;
            wasLocked = true;
        }
        else if (!shouldLock && wasLocked)
        {
            freeLook.enabled = true;
            wasLocked = false;
        }
    }
}
