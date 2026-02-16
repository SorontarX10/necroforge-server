using GrassSim.Stats;

namespace GrassSim.Upgrades
{
    // UWAGA:
    // UpgradeRarity ma być zdefiniowane TYLKO w jednym miejscu w projekcie,
    // np. w osobnym pliku: Assets/Scripts/Upgrades/UpgradeRarity.cs

    public class UpgradeOption
    {
        public StatType stat;
        public float value;
        public UpgradeRarity rarity;

        public string displayName;
        public string description;

        public UpgradeOption(
            StatType stat,
            float value,
            UpgradeRarity rarity,
            string displayName,
            string description
        )
        {
            this.stat = stat;
            this.value = value;
            this.rarity = rarity;
            this.displayName = displayName;
            this.description = description;
        }

        public string GetValueText()
        {
            return stat switch
            {
                StatType.MaxHealth        => $"+{value:0.0} HP",
                StatType.HealthRegen      => $"+{value:0.0}/s HP",
                StatType.MaxStamina       => $"+{value:0.0} Stamina",
                StatType.StaminaRegen     => $"+{value:0.0}/s Stamina",
                StatType.Damage           => $"+{value:0.0} Damage",
                StatType.CritChance       => $"+{value * 100f:0.0}% Crit",
                StatType.CritMultiplier   => $"+{value * 100f:0.0}% Crit DMG",
                StatType.LifeSteal        => $"+{value * 100f:0.0}% Lifesteal",
                StatType.DamageReduction  => $"+{value * 100f:0.0}% DR",
                StatType.DodgeChance      => $"+{value * 100f:0.0}% Dodge",
                _ => $"+{value}"
            };
        }

        public override string ToString()
        {
            return $"{rarity} {displayName} ({GetValueText()})";
        }
    }
}
