using UnityEngine;
public class MLCombatInput : MonoBehaviour, ICombatInput
{
    [Header("ML Output (stub)")]
    [Range(-1f, 1f)] public float swingX;
    [Range(-1f, 1f)] public float swingY;
    public bool attack;
    public Vector3 moveDirection;

    // ===== ICombatInput =====

    public Vector2 GetSwingInput()
    {
        return new Vector2(swingX, swingY);
    }

    public bool IsAttacking()
    {
        return attack;
    }

    public Vector3 GetMoveDirection()
    {
        return moveDirection;
    }
}
