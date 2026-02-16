using UnityEngine;
using GrassSim.Combat;

[RequireComponent(typeof(Collider))]
public class MeleeHitbox : MonoBehaviour
{
    [Header("Damage Settings")]
    public float damageAmount = 10f;
    public float hitCooldown = 0.5f;

    private float lastHitTime = -Mathf.Infinity;

    private void Reset()
    {
        var col = GetComponent<Collider>();
        col.isTrigger = true;
    }

    private void OnTriggerStay(Collider other)
    {
        if (Time.time < lastHitTime + hitCooldown)
            return;

        Combatant target =
            other.GetComponent<Combatant>() ??
            other.GetComponentInParent<Combatant>() ??
            other.GetComponentInChildren<Combatant>();

        if (target == null)
            return;

        Combatant ownerCombatant = GetComponentInParent<Combatant>();
        if (ownerCombatant != null && target == ownerCombatant)
            return;

        target.TakeDamage(damageAmount);
        lastHitTime = Time.time;
    }
}
