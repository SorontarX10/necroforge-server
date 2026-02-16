using GrassSim.Combat;

public interface IIncomingHitModifier
{
    float ModifyIncomingDamage(PlayerRelicController player, Combatant attacker, float damage, int stacks);
}
