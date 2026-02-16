using UnityEngine;
using GrassSim.Combat;

public class HeadHitbox : MonoBehaviour
{
    public Combatant owner;

    private void Awake()
    {
        owner = GetComponentInParent<Combatant>();
    }

    public void ApplyDamage(float dmg)
    {
        if (owner == null) return;
        owner.TakeDamage(dmg);
    }
}
