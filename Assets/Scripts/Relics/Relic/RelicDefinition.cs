using UnityEngine;
using GrassSim.Upgrades;

[CreateAssetMenu(menuName = "GrassSim/Relics/Relic")]
public class RelicDefinition : ScriptableObject
{
    [Header("ID")]
    public string id;

    [Header("UI")]
    public string displayName;
    [TextArea] public string description;
    public Sprite icon;
    public RelicRarity rarity;

    [Header("Stacking")]
    public bool stackable = true;
    public int maxStacks = 5;

    [Header("Effect")]
    public RelicEffect effect;
}
