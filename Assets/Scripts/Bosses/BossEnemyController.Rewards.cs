using System.Collections.Generic;
using UnityEngine;
using GrassSim.Enemies;

public partial class BossEnemyController : MonoBehaviour
{
    private void HandleDeath(EnemyCombatant _)
    {
        if (deathHandled)
            return;

        deathHandled = true;
        OpenBossRewardSelection();
        owner?.NotifyBossDeath(this);
    }

    private void OpenBossRewardSelection()
    {
        if (relicSelectionUI == null)
            relicSelectionUI = FindFirstObjectByType<RelicSelectionUI>();

        if (relicLibrary == null)
            ResolveRelicLibraryFallback();

        if (relicSelectionUI == null || relicLibrary == null || relicLibrary.relics == null)
            return;

        List<RelicDefinition> rolled = RollLegendaryOrMythic(rewardChoices);
        if (rolled.Count == 0)
            return;

        relicSelectionUI.Show(rolled);
    }

    private void ResolveRelicLibraryFallback()
    {
        var chestSpawner = FindFirstObjectByType<RelicChestSpawner>();
        if (chestSpawner != null && chestSpawner.chestPrefab != null)
        {
            var chestTrigger = chestSpawner.chestPrefab.GetComponent<ChestRelicTrigger>();
            if (chestTrigger != null && chestTrigger.relicLibrary != null)
                relicLibrary = chestTrigger.relicLibrary;
        }

        if (relicLibrary == null)
        {
            var loadedLibraries = Resources.FindObjectsOfTypeAll<RelicLibrary>();
            if (loadedLibraries != null && loadedLibraries.Length > 0)
                relicLibrary = loadedLibraries[0];
        }
    }

    private List<RelicDefinition> RollLegendaryOrMythic(int count)
    {
        List<RelicDefinition> pool = new();
        var all = relicLibrary.relics;

        for (int i = 0; i < all.Count; i++)
        {
            RelicDefinition relic = all[i];
            if (relic == null)
                continue;

            if (relic.rarity == RelicRarity.Legendary || relic.rarity == RelicRarity.Mythic)
                pool.Add(relic);
        }

        if (pool.Count == 0)
            return new List<RelicDefinition>();

        List<RelicDefinition> result = new();
        List<RelicDefinition> uniquePool = new(pool);

        while (result.Count < count && uniquePool.Count > 0)
        {
            int idx = Random.Range(0, uniquePool.Count);
            result.Add(uniquePool[idx]);
            uniquePool.RemoveAt(idx);
        }

        while (result.Count < count)
        {
            int idx = Random.Range(0, pool.Count);
            result.Add(pool[idx]);
        }

        return result;
    }
}
