using UnityEngine;
using GrassSim.Combat;

[DisallowMultipleComponent]
public class BossTankDamageGate : MonoBehaviour, IIncomingDamageGate
{
    [SerializeField] private float shieldedUntil;

    public bool IsShieldActive => Time.time < shieldedUntil;

    public void Activate(float duration)
    {
        shieldedUntil = Mathf.Max(shieldedUntil, Time.time + Mathf.Max(0.05f, duration));
    }

    public bool ShouldBlockIncomingDamage(Combatant attacker, float incomingDamage)
    {
        return IsShieldActive;
    }
}
