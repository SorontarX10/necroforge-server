public interface ICritChanceModifier
{
    float GetCritChanceBonus(PlayerRelicController player, int stacks);
}

public interface ICritMultiplierModifier
{
    float GetCritMultiplierBonus(PlayerRelicController player, int stacks);
}

public interface ILifeStealModifier
{
    float GetLifeStealBonus(PlayerRelicController player, int stacks);
}

public interface ISwingSpeedModifier
{
    float GetSwingSpeedBonus(PlayerRelicController player, int stacks);
}

public interface ISpeedModifier
{
    float GetSpeedBonus(PlayerRelicController player, int stacks);
}

public interface IMaxHealthModifier
{
    float GetMaxHealthBonus(PlayerRelicController player, int stacks);
}

public interface IStaminaRegenModifier
{
    float GetStaminaRegenBonus(PlayerRelicController player, int stacks);
}

public interface IDamageReductionModifier
{
    float GetDamageReductionBonus(PlayerRelicController player, int stacks);
}

public interface IDodgeChanceModifier
{
    float GetDodgeChanceBonus(PlayerRelicController player, int stacks);
}

public interface ISwordLengthModifier
{
    float GetSwordLengthBonus(PlayerRelicController player, int stacks);
}

public interface IStaminaSwingOverrideModifier
{
    float GetStaminaSwingMultiplierOverride(PlayerRelicController player, int stacks);
}
