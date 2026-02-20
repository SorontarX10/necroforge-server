using UnityEngine;
using GrassSim.Core;
using GrassSim.Telemetry;
using GrassSim.UI;

public class ChestRelicTrigger : MonoBehaviour
{
    [Header("Config")]
    public int relicChoices = 3;

    [Header("Data")]
    public RelicLibrary relicLibrary;

    private RelicSelectionUI relicUI;
    private bool used;
    private PlayerRelicController currentPlayer;

    private void Awake()
    {
        relicUI = FindFirstObjectByType<RelicSelectionUI>();

        if (relicUI == null)
        {
            Debug.LogError(
                "[ChestRelicTrigger] RelicSelectionUI not found in scene!",
                this
            );
        }

        if (relicLibrary == null)
        {
            Debug.LogError(
                "[ChestRelicTrigger] RelicLibrary not assigned!",
                this
            );
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (used)
            return;

        if (!other.CompareTag("Player"))
            return;

        if (relicUI == null || relicLibrary == null)
            return;

        currentPlayer = ResolvePlayer(other);
        used = true;
        OpenChest();
    }

    private void OnEnable()
    {
        MapCollectibleRegistry.RegisterChest(this);
    }

    private void OnDisable()
    {
        MapCollectibleRegistry.UnregisterChest(this);
    }

    private void OpenChest()
    {
        ResolvePlayer();

        RelicLibrary.RollResult roll = relicLibrary.RollWithDiagnostics(relicChoices, currentPlayer);
        ReportRelicRollTelemetry(roll);
        relicUI.Show(roll.offered);

        gameObject.SetActive(false);
    }

    private void ReportRelicRollTelemetry(RelicLibrary.RollResult roll)
    {
        if (roll == null)
            return;

        GameplayTelemetryHub.RelicOptionSample[] offered = BuildOfferedSamples(roll.offered);
        GameplayTelemetryHub.RejectedRelicOptionSample[] rejected = BuildRejectedSamples(roll.rejected);

        GameplayTelemetryHub.ReportRelicOptionsRolled(
            new GameplayTelemetryHub.RelicOptionsRolledSample(
                GetRunTimeSeconds(),
                "chest",
                offered,
                rejected
            )
        );
    }

    private GameplayTelemetryHub.RelicOptionSample[] BuildOfferedSamples(System.Collections.Generic.List<RelicDefinition> offered)
    {
        if (offered == null || offered.Count == 0)
            return System.Array.Empty<GameplayTelemetryHub.RelicOptionSample>();

        GameplayTelemetryHub.RelicOptionSample[] samples = new GameplayTelemetryHub.RelicOptionSample[offered.Count];
        for (int i = 0; i < offered.Count; i++)
        {
            RelicDefinition relic = offered[i];
            string id = relic != null ? relic.id : "unknown";
            string displayName = relic != null && !string.IsNullOrWhiteSpace(relic.displayName)
                ? relic.displayName
                : (relic != null ? relic.name : "Unknown");
            string rarity = relic != null ? relic.rarity.ToString() : "Unknown";
            int stacks = currentPlayer != null && relic != null ? currentPlayer.GetStacks(id) : 0;
            int maxStacks = currentPlayer != null && relic != null
                ? currentPlayer.GetEffectiveMaxStacks(relic)
                : (relic != null ? Mathf.Max(1, relic.maxStacks) : 1);

            samples[i] = new GameplayTelemetryHub.RelicOptionSample(
                id,
                displayName,
                rarity,
                stacks,
                maxStacks
            );
        }

        return samples;
    }

    private GameplayTelemetryHub.RejectedRelicOptionSample[] BuildRejectedSamples(System.Collections.Generic.List<RelicLibrary.RejectedRelicOption> rejected)
    {
        if (rejected == null || rejected.Count == 0)
            return System.Array.Empty<GameplayTelemetryHub.RejectedRelicOptionSample>();

        GameplayTelemetryHub.RejectedRelicOptionSample[] samples = new GameplayTelemetryHub.RejectedRelicOptionSample[rejected.Count];
        for (int i = 0; i < rejected.Count; i++)
        {
            RelicLibrary.RejectedRelicOption entry = rejected[i];
            RelicDefinition relic = entry != null ? entry.relic : null;
            string id = relic != null ? relic.id : "unknown";
            string displayName = relic != null && !string.IsNullOrWhiteSpace(relic.displayName)
                ? relic.displayName
                : (relic != null ? relic.name : "Unknown");
            string rarity = relic != null ? relic.rarity.ToString() : "Unknown";
            int stacks = currentPlayer != null && relic != null ? currentPlayer.GetStacks(id) : 0;
            int maxStacks = currentPlayer != null && relic != null
                ? currentPlayer.GetEffectiveMaxStacks(relic)
                : (relic != null ? Mathf.Max(1, relic.maxStacks) : 1);
            string reason = entry != null && !string.IsNullOrWhiteSpace(entry.reason)
                ? entry.reason
                : "unknown";

            samples[i] = new GameplayTelemetryHub.RejectedRelicOptionSample(
                id,
                displayName,
                rarity,
                reason,
                stacks,
                maxStacks
            );
        }

        return samples;
    }

    private PlayerRelicController ResolvePlayer(Collider source = null)
    {
        if (currentPlayer != null)
            return currentPlayer;

        if (source != null)
        {
            currentPlayer = source.GetComponent<PlayerRelicController>();
            if (currentPlayer == null)
                currentPlayer = source.GetComponentInParent<PlayerRelicController>();
        }

        if (currentPlayer == null)
        {
            var progression = PlayerLocator.GetProgression();
            if (progression != null)
                currentPlayer = progression.GetComponent<PlayerRelicController>();
        }

        if (currentPlayer == null)
            currentPlayer = FindFirstObjectByType<PlayerRelicController>();

        return currentPlayer;
    }

    private static float GetRunTimeSeconds()
    {
        if (GameTimerController.Instance != null)
            return Mathf.Max(0f, GameTimerController.Instance.elapsedTime);

        return 0f;
    }
}
