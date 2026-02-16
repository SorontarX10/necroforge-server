using UnityEngine;

public class EnemyCombatInput : MonoBehaviour, ICombatInput
{
    public Transform target;
    public float swingSpeed = 1.2f;
    public float attackDistance = 2.5f;

    public bool IsAttacking()
    {
        if (!target) return false;

        float dist = Vector3.Distance(transform.position, target.position);
        return dist <= attackDistance;
    }

    public Vector2 GetSwingInput()
    {
        // prosty, deterministyczny swing (idealny pod ML)
        float x = Mathf.Sin(Time.time * swingSpeed);
        float y = Mathf.Cos(Time.time * swingSpeed * 0.7f);

        return new Vector2(x, y);
    }

    public Vector3 GetMoveDirection()
    {
        if (!target)
            return Vector3.zero;

        Vector3 dir = target.position - transform.position;
        dir.y = 0f;

        return dir.normalized;
    }
}
