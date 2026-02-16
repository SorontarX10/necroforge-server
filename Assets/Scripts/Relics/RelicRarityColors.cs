using UnityEngine;

public static class RelicRarityColors
{
    public static Color Get(RelicRarity rarity)
    {
        return rarity switch
        {
            RelicRarity.Common     => Hex("#808080"),
            RelicRarity.Uncommon   => Hex("#3CB371"),
            RelicRarity.Rare       => Hex("#3A7BD5"),
            RelicRarity.Legendary  => Hex("#FFD700"),
            RelicRarity.Mythic     => Hex("#8A2BE2"),
            _ => Color.white
        };
    }

    private static Color Hex(string hex)
    {
        if (ColorUtility.TryParseHtmlString(hex, out var c))
            return c;

        return Color.white;
    }
}
