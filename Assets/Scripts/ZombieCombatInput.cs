using UnityEngine;
using GrassSim.Core;

public class ZombieCombatInput : MonoBehaviour, ICombatInput
{
    public ZombiePerception perception;
    public float attackDistance = 2.2f;

    [SerializeField, Min(0.05f)] private float targetResolveInterval = 0.25f;

    private Transform fallbackTarget;
    private float nextTargetResolveAt;

    void Awake()
    {
        if (!perception)
            perception = GetComponent<ZombiePerception>();
    }

    // ======================
    // ICombatInput
    // ======================

    public bool IsAttacking()
    {
        Transform target = ResolveTarget();
        if (target == null)
            return false;

        Vector3 delta = target.position - transform.position;
        delta.y = 0f;
        return delta.sqrMagnitude <= attackDistance * attackDistance;
    }

    public Vector2 GetSwingInput()
    {
        // Zombie nie używa swingów
        return Vector2.zero;
    }

    public Vector3 GetMoveDirection()
    {
        // Zombie NIE steruje ruchem przez ICombatInput
        return Vector3.zero;
    }

    private Transform ResolveTarget()
    {
        if (perception != null && perception.VisibleEnemy != null)
            return perception.VisibleEnemy;

        if (fallbackTarget != null && fallbackTarget.gameObject.activeInHierarchy)
            return fallbackTarget;

        if (Time.time < nextTargetResolveAt)
            return null;

        nextTargetResolveAt = Time.time + Mathf.Max(0.05f, targetResolveInterval);
        fallbackTarget = PlayerLocator.GetTransform();
        return fallbackTarget;
    }
}
