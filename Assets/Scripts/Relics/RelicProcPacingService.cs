using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Runtime pacing helper for kill-window relic procs.
/// Keeps proc frequency closer to target TTK bands.
/// </summary>
public static class RelicProcPacingService
{
    private sealed class PlayerProcState
    {
        public readonly List<float> killTimes = new(64);
        public float avgKillsPerSecond;
    }

    private const float SampleWindowSeconds = 12f;
    private static readonly Dictionary<int, PlayerProcState> stateByPlayerId = new(32);

    public static void NotifyMeleeKill(PlayerRelicController player)
    {
        if (player == null)
            return;

        PlayerProcState state = GetOrCreateState(player);
        float now = Time.time;
        state.killTimes.Add(now);
        PruneKills(state.killTimes, now);

        float elapsed = SampleWindowSeconds;
        int count = state.killTimes.Count;
        if (count > 1)
        {
            elapsed = Mathf.Max(0.1f, now - state.killTimes[0]);
        }

        float currentRate = count / Mathf.Max(0.1f, elapsed);
        state.avgKillsPerSecond = state.avgKillsPerSecond <= 0f
            ? currentRate
            : Mathf.Lerp(state.avgKillsPerSecond, currentRate, 0.18f);
    }

    public static float GetKillWindow(
        PlayerRelicController player,
        float baseWindowSeconds,
        float targetTtkSeconds,
        float minMultiplier = 0.72f,
        float maxMultiplier = 1.3f)
    {
        float rateScale = GetRateScale(player, targetTtkSeconds);

        // Fast kills -> tighter proc window, slow kills -> looser window.
        float mul = rateScale >= 1f
            ? Mathf.Lerp(1f, minMultiplier, Mathf.Clamp01((rateScale - 1f) * 0.85f))
            : Mathf.Lerp(1f, maxMultiplier, Mathf.Clamp01((1f - rateScale) * 0.9f));

        return Mathf.Max(0.1f, baseWindowSeconds * mul);
    }

    public static int GetKillsRequired(
        PlayerRelicController player,
        int baseKillsRequired,
        float targetTtkSeconds,
        int maxAdditionalKills = 3)
    {
        float rateScale = GetRateScale(player, targetTtkSeconds);
        if (rateScale <= 1.05f)
            return Mathf.Max(1, baseKillsRequired);

        int extra = Mathf.Clamp(Mathf.FloorToInt((rateScale - 1f) * 2f), 0, Mathf.Max(0, maxAdditionalKills));
        return Mathf.Max(1, baseKillsRequired + extra);
    }

    private static float GetRateScale(PlayerRelicController player, float targetTtkSeconds)
    {
        if (player == null || targetTtkSeconds <= 0f)
            return 1f;

        PlayerProcState state = GetOrCreateState(player);
        float targetKillsPerSecond = 1f / Mathf.Max(0.1f, targetTtkSeconds);
        float measuredRate = Mathf.Max(0.01f, state.avgKillsPerSecond);
        return measuredRate / targetKillsPerSecond;
    }

    private static PlayerProcState GetOrCreateState(PlayerRelicController player)
    {
        int id = player.GetInstanceID();
        if (stateByPlayerId.TryGetValue(id, out PlayerProcState state))
            return state;

        state = new PlayerProcState();
        stateByPlayerId.Add(id, state);
        return state;
    }

    private static void PruneKills(List<float> killTimes, float now)
    {
        if (killTimes == null || killTimes.Count == 0)
            return;

        float minTime = now - SampleWindowSeconds;
        for (int i = killTimes.Count - 1; i >= 0; i--)
        {
            if (killTimes[i] < minTime)
                killTimes.RemoveAt(i);
        }
    }
}
