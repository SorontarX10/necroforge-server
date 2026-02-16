using System;
using System.Collections.Generic;
using UnityEngine;
using GrassSim.Stats;

[CreateAssetMenu(
    menuName = "GrassSim/Upgrades/Upgrade Stat Icon Library",
    fileName = "UpgradeStatIconLibrary"
)]
public class UpgradeStatIconLibrary : ScriptableObject
{
    [Serializable]
    public class Entry
    {
        public StatType stat;
        public Sprite icon;
    }

    public List<Entry> entries = new();

    public Sprite GetIcon(StatType stat)
    {
        foreach (var e in entries)
        {
            if (e.stat == stat)
                return e.icon;
        }

        Debug.LogWarning($"[UpgradeStatIconLibrary] No icon for stat {stat}");
        return null;
    }
}
