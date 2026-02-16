using UnityEngine;
public interface ICombatInput
{
    Vector2 GetSwingInput();   // np. (-1..1, -1..1)
    bool IsAttacking();
    Vector3 GetMoveDirection();
}
