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

        PlayerRelicController rewardRelics = ResolveRewardRelics();
        PlayerProgressionController progression = rewardRelics != null
            ? rewardRelics.Progression
            : PlayerLocator.GetProgression();

        List<RelicDefinition> rolled = RollLegendaryOrMythicFromContext(
            relicLibrary,
            rewardRelics,
            progression,
            rewardChoices
        );
        if (rolled.Count == 0)
            return;

        ReportBossRewardRollTelemetry(rolled);

        RelicLibrary rerollLibrary = relicLibrary;
        PlayerRelicController rerollRelics = rewardRelics;
        PlayerProgressionController rerollProgression = progression;
        int rerollChoices = rewardChoices;
        relicSelectionUI.Show(
            rolled,
            () => RollLegendaryOrMythicFromContext(
                rerollLibrary,
                rerollRelics,
                rerollProgression,
                rerollChoices
            )
        );
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
        PlayerRelicController rewardRelics = ResolveRewardRelics();
        PlayerProgressionController progression = rewardRelics != null
            ? rewardRelics.Progression
            : PlayerLocator.GetProgression();

        return RollLegendaryOrMythicFromContext(
            relicLibrary,
            rewardRelics,
            progression,
            count
        );
    }

    private static List<RelicDefinition> RollLegendaryOrMythicFromContext(
        RelicLibrary library,
        PlayerRelicController rewardRelics,
        PlayerProgressionController progression,
        int count
    )
    {
        if (library == null || library.relics == null || count <= 0)
            return new List<RelicDefinition>();

        List<RelicDefinition> pool = new();
        List<RelicDefinition> all = library.relics;

        for (int i = 0; i < all.Count; i++)
        {
            RelicDefinition relic = all[i];
            if (relic == null)
                continue;

            if (relic.rarity != RelicRarity.Legendary && relic.rarity != RelicRarity.Mythic)
                continue;

            if (progression != null && progression.IsRelicBanished(relic.id))
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
