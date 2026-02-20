using System.Collections.Generic;
using UnityEngine;
using GrassSim.Core;
using GrassSim.Enemies;
using GrassSim.Telemetry;

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

        ReportBossRewardRollTelemetry(rolled);
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
        PlayerRelicController rewardRelics = ResolveRewardRelics();

        for (int i = 0; i < all.Count; i++)
        {
            RelicDefinition relic = all[i];
            if (relic == null)
                continue;

            if (relic.rarity != RelicRarity.Legendary && relic.rarity != RelicRarity.Mythic)
                continue;

            if (rewardRelics != null && !rewardRelics.CanAcceptRelic(relic))
                continue;

            if (relic.effect == null || string.IsNullOrWhiteSpace(relic.id))
                continue;

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

        return result;
    }

    private PlayerRelicController ResolveRewardRelics()
    {
        if (playerRelics != null)
            return playerRelics;

        if (playerCombatant != null)
        {
            playerRelics = playerCombatant.GetComponent<PlayerRelicController>();
            if (playerRelics != null)
                return playerRelics;
        }

        var progression = PlayerLocator.GetProgression();
        if (progression != null)
            playerRelics = progression.GetComponent<PlayerRelicController>();

        if (playerRelics == null)
            playerRelics = FindFirstObjectByType<PlayerRelicController>();

        return playerRelics;
    }

    private void ReportBossRewardRollTelemetry(List<RelicDefinition> rolled)
    {
        if (rolled == null || rolled.Count == 0)
            return;

        PlayerRelicController rewardRelics = ResolveRewardRelics();
        GameplayTelemetryHub.RelicOptionSample[] offered = new GameplayTelemetryHub.RelicOptionSample[rolled.Count];
        for (int i = 0; i < rolled.Count; i++)
        {
            RelicDefinition relic = rolled[i];
            string id = relic != null ? relic.id : "unknown";
            string displayName = relic != null && !string.IsNullOrWhiteSpace(relic.displayName)
                ? relic.displayName
                : (relic != null ? relic.name : "Unknown");
            string rarity = relic != null ? relic.rarity.ToString() : "Unknown";
            int currentStacks = rewardRelics != null && relic != null ? rewardRelics.GetStacks(id) : 0;
            int maxStacks = rewardRelics != null && relic != null
                ? rewardRelics.GetEffectiveMaxStacks(relic)
                : (relic != null ? Mathf.Max(1, relic.maxStacks) : 1);

            offered[i] = new GameplayTelemetryHub.RelicOptionSample(
                id,
                displayName,
                rarity,
                currentStacks,
                maxStacks
            );
        }

        GameplayTelemetryHub.ReportRelicOptionsRolled(
            new GameplayTelemetryHub.RelicOptionsRolledSample(
                GetRunTimeSeconds(),
                "boss_reward",
                offered,
                System.Array.Empty<GameplayTelemetryHub.RejectedRelicOptionSample>()
            )
        );
    }

    private static float GetRunTimeSeconds()
    {
        if (GameTimerController.Instance != null)
            return Mathf.Max(0f, GameTimerController.Instance.elapsedTime);

        return 0f;
    }
}
