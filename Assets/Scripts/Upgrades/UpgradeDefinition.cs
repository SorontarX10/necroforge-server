using UnityEngine;
using GrassSim.Stats;

namespace GrassSim.Upgrades
{
    [CreateAssetMenu(
        menuName = "GrassSim/Upgrades/Upgrade Definition",
        fileName = "UpgradeDefinition"
    )]
    public class UpgradeDefinition : ScriptableObject
    {
        public string id;
        public string displayName;
        [TextArea] public string description;

        public StatType stat;

        public float baseValue;
        public int maxLevel = 5;
    }
}
