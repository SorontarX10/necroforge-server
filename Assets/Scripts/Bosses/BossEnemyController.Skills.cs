using System.Collections;
using UnityEngine;
using GrassSim.Combat;
public partial class BossEnemyController : MonoBehaviour
{
    private void HandleArchetypeSkill()
    {
        switch (archetype)
        {
            case BossArchetype.Quick:
                HandleQuickDash();
                break;
            case BossArchetype.Tank:
                HandleTankShield();
                break;
        }
    }

    private void HandleQuickDash()
    {
        if (quickDashActive || Time.time < nextQuickDashAt || player == null)
            return;

        Vector3 toPlayer = player.position - transform.position;
        toPlayer.y = 0f;
        float distance = toPlayer.magnitude;

        if (distance < quickDashMinDistance || distance > quickDashMaxDistance)
            return;

        nextQuickDashAt = Time.time + Mathf.Max(0.2f, quickDashCooldown * GetCurrentSkillCooldownMultiplier());

        if (quickDashRoutine != null)
            StopCoroutine(quickDashRoutine);

        quickDashRoutine = StartCoroutine(QuickDashRoutine());
    }

    private IEnumerator QuickDashRoutine()
    {
        quickDashActive = true;

        float tunedQuickDashTelegraphDuration = GetReadabilityAdjustedTelegraphDuration(quickDashTelegraphDuration);
        if (tunedQuickDashTelegraphDuration > 0f)
        {
            Vector3 telegraphPos = player != null ? player.position : transform.position + transform.forward * quickDashMinDistance;
            SpawnGroundTelegraph(telegraphPos, quickDashTelegraphRadius, tunedQuickDashTelegraphDuration, quickDashTelegraphColor, quickDashWarningSfx);
            yield return new WaitForSeconds(tunedQuickDashTelegraphDuration);
        }

        if (player == null || combatant == null || combatant.IsDead || attackCycleState != AttackCycleState.Attacking || Time.time < damageSuppressedUntil)
        {
            quickDashActive = false;
            quickDashRoutine = null;
            yield break;
        }

        Vector3 toPlayer = player.position - transform.position;
        toPlayer.y = 0f;
        if (toPlayer.sqrMagnitude < 0.0001f)
            toPlayer = transform.forward;

        Vector3 dashDir = toPlayer.normalized;
        float duration = Mathf.Max(0.05f, quickDashDuration);
        float endTime = Time.time + duration;

        while (Time.time < endTime)
        {
            if (combatant == null || combatant.IsDead || attackCycleState != AttackCycleState.Attacking || Time.time < damageSuppressedUntil)
                break;

            if (rb != null)
            {
                Vector3 velocity = rb.linearVelocity;
                velocity.x = dashDir.x * quickDashSpeed;
                velocity.z = dashDir.z * quickDashSpeed;
                rb.linearVelocity = velocity;
            }
            else
            {
                transform.position += dashDir * quickDashSpeed * Time.deltaTime;
            }

            yield return null;
        }

        if (rb != null)
        {
            Vector3 velocity = rb.linearVelocity;
            velocity.x = 0f;
            velocity.z = 0f;
            rb.linearVelocity = velocity;
        }

        TryApplyQuickDashImpactDamage();
        quickDashActive = false;
        quickDashRoutine = null;
    }

    private void TryApplyQuickDashImpactDamage()
    {
        if (player == null || combatant == null || enemyCombatant == null || enemyCombatant.stats == null)
            return;

        Vector3 toPlayer = player.position - transform.position;
        toPlayer.y = 0f;
        if (toPlayer.sqrMagnitude > quickDashAttackRange * quickDashAttackRange)
            return;

        if (playerCombatant == null || playerCombatant.IsDead)
        {
            CachePlayer(force: false);
            if (playerCombatant == null || playerCombatant.IsDead)
                return;
        }

        if (playerRelics == null)
            playerRelics = playerCombatant.GetComponent<PlayerRelicController>();

        if (playerCombatant.IsDead)
            return;

        float damage = enemyCombatant.stats.damage * Mathf.Max(1f, quickDashAttackDamageMultiplier);
        if (playerRelics != null)
            damage = playerRelics.ModifyIncomingDamage(combatant, damage);

        if (damage > 0f)
            playerCombatant.TakeDamage(damage, transform);
    }

    private void HandleTankShield()
    {
        if (tankDamageGate == null || Time.time < nextTankShieldAt)
            return;

        tankDamageGate.Activate(tankShieldDuration);
        nextTankShieldAt = Time.time + Mathf.Max(0.2f, tankShieldCooldown * GetCurrentSkillCooldownMultiplier());
    }

    private void CancelQuickDash()
    {
        if (!quickDashActive && quickDashRoutine == null)
            return;

        if (quickDashRoutine != null)
        {
            StopCoroutine(quickDashRoutine);
            quickDashRoutine = null;
        }

        quickDashActive = false;

        if (rb != null)
        {
            Vector3 velocity = rb.linearVelocity;
            velocity.x = 0f;
            velocity.z = 0f;
            rb.linearVelocity = velocity;
        }
    }

    public bool TryTelegraphPoison(Combatant target, float duration, float dps, float tickInterval)
    {
        if (!initialized || specialEffects == null || target == null || target.IsDead)
            return false;

        StartCoroutine(PoisonTelegraphRoutine(target, duration, dps, tickInterval));
        return true;
    }

    public bool TryTelegraphRoot(Combatant target, float duration)
    {
        if (!initialized || specialEffects == null || target == null || target.IsDead)
            return false;

        StartCoroutine(RootTelegraphRoutine(target, duration));
        return true;
    }

    private IEnumerator PoisonTelegraphRoutine(Combatant target, float duration, float dps, float tickInterval)
    {
        float tunedPoisonTelegraphDuration = GetReadabilityAdjustedTelegraphDuration(poisonTelegraphDuration);
        if (tunedPoisonTelegraphDuration > 0f)
        {
            SpawnGroundTelegraph(target.transform.position, poisonTelegraphRadius, tunedPoisonTelegraphDuration, poisonTelegraphColor, poisonWarningSfx);
            yield return new WaitForSeconds(tunedPoisonTelegraphDuration);
        }

        if (target == null || target.IsDead || specialEffects == null)
            yield break;

        specialEffects.ForceApplyPoison(target, duration, dps, tickInterval);
    }

    private IEnumerator RootTelegraphRoutine(Combatant target, float duration)
    {
        float tunedRootTelegraphDuration = GetReadabilityAdjustedTelegraphDuration(rootTelegraphDuration);
        if (tunedRootTelegraphDuration > 0f)
        {
            SpawnGroundTelegraph(target.transform.position, rootTelegraphRadius, tunedRootTelegraphDuration, rootTelegraphColor, rootWarningSfx);
            yield return new WaitForSeconds(tunedRootTelegraphDuration);
        }

        if (target == null || target.IsDead || specialEffects == null)
            yield break;

        specialEffects.ForceApplyRoot(target, duration);
    }
}
