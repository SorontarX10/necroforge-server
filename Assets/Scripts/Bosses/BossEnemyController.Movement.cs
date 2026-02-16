using UnityEngine;

public partial class BossEnemyController : MonoBehaviour
{
    private void HandleTeleport()
    {
        if (Time.time < nextTeleportCheckAt)
            return;

        nextTeleportCheckAt = Time.time + teleportCheckInterval;

        if (Time.time < nextTeleportAt)
            return;

        Vector3 toPlayer = player.position - transform.position;
        toPlayer.y = 0f;

        if (toPlayer.sqrMagnitude <= teleportTriggerDistance * teleportTriggerDistance)
            return;

        if (!TryFindTeleportPoint(out Vector3 teleportPoint))
            return;

        transform.position = teleportPoint;

        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        damageSuppressedUntil = Time.time + postTeleportGraceDuration;
        nextTeleportAt = Time.time + teleportCooldown;
        PlayTeleportPresentation(teleportPoint);
        BeginChargingPhase();
    }

    private void HandleAttackCycle()
    {
        if (Time.time < attackCycleStateEndsAt)
            return;

        if (attackCycleState == AttackCycleState.Charging)
            BeginAttackPhase();
        else
            BeginChargingPhase();
    }

    private void BeginChargingPhase()
    {
        attackCycleState = AttackCycleState.Charging;
        attackCycleStateEndsAt = Time.time + GetCurrentChargeDuration();
        SetMovementPaused(true);
        bossHealthBarUI?.SetChargingState(true);
    }

    private void BeginAttackPhase()
    {
        attackCycleState = AttackCycleState.Attacking;
        attackCycleStateEndsAt = Time.time + GetCurrentAttackDuration();
        SetMovementPaused(false);
        bossHealthBarUI?.SetChargingState(false);
    }

    private void HandleChargingRotation()
    {
        if (player == null)
            return;

        Vector3 toPlayer = player.position - transform.position;
        toPlayer.y = 0f;
        if (toPlayer.sqrMagnitude < 0.001f)
            return;

        Quaternion targetRotation = Quaternion.LookRotation(toPlayer.normalized, Vector3.up);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Mathf.Max(0f, chargeTurnSpeed) * Time.deltaTime);

        if (rb != null)
        {
            Vector3 velocity = rb.linearVelocity;
            velocity.x = 0f;
            velocity.z = 0f;
            rb.linearVelocity = velocity;
        }
    }

    private void SetMovementPaused(bool paused)
    {
        if (quickDashActive && paused)
            CancelQuickDash();

        if (!attackSpeedsCached)
            CacheAttackMovementSpeeds();

        float movementMultiplier = GetCurrentMoveSpeedMultiplier();

        if (zombieAI != null)
            zombieAI.moveSpeed = paused ? 0f : attackZombieMoveSpeed * movementMultiplier;

        if (enemyAI != null)
        {
            enemyAI.wanderSpeed = paused ? 0f : attackEnemyWanderSpeed * movementMultiplier;
            enemyAI.chaseSpeed = paused ? 0f : attackEnemyChaseSpeed * movementMultiplier;
        }
    }

    private void CacheAttackMovementSpeeds()
    {
        if (zombieAI != null)
            attackZombieMoveSpeed = Mathf.Max(0f, zombieAI.moveSpeed);

        if (enemyAI != null)
        {
            attackEnemyWanderSpeed = Mathf.Max(0f, enemyAI.wanderSpeed);
            attackEnemyChaseSpeed = Mathf.Max(0f, enemyAI.chaseSpeed);
        }

        attackSpeedsCached = true;
    }

    private bool TryFindTeleportPoint(out Vector3 point)
    {
        float minDist = Mathf.Max(1f, teleportMinDistanceFromPlayer);
        float maxDist = Mathf.Max(minDist + 0.1f, teleportMaxDistanceFromPlayer);

        for (int i = 0; i < 10; i++)
        {
            Vector2 dir2 = Random.insideUnitCircle;
            if (dir2.sqrMagnitude < 0.0001f)
                dir2 = Vector2.up;

            dir2.Normalize();
            float dist = Random.Range(minDist, maxDist);
            Vector3 candidate = player.position + new Vector3(dir2.x, 0f, dir2.y) * dist;

            if (TrySnapToGround(candidate, out point))
            {
                Vector3 delta = point - transform.position;
                delta.y = 0f;
                if (delta.sqrMagnitude > 1f)
                    return true;
            }
        }

        point = transform.position;
        return false;
    }

    private bool TrySnapToGround(Vector3 worldPos, out Vector3 snapped)
    {
        Vector3 rayStart = worldPos + Vector3.up * groundRayHeight;
        float rayDistance = groundRayHeight * 2f;

        if (Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit, rayDistance, groundMask))
        {
            snapped = hit.point + Vector3.up * groundSnapOffset;
            return true;
        }

        snapped = worldPos;
        return false;
    }
}
