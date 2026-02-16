using GrassSim.Combat;

public interface IIncomingDamageGate
{
    bool ShouldBlockIncomingDamage(Combatant attacker, float incomingDamage);
}
