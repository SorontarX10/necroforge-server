using System;
using System.Collections.Generic;

public static class OnlineLeaderboardCache
{
    private static readonly List<OnlineLeaderboardApiClient.LeaderboardEntry> cachedTopEntries = new();

    private static bool hasTopSnapshot;
    private static string cachedTopSeason = string.Empty;
    private static DateTime cachedTopUpdatedAtUtc;

    private static bool hasMyRankSnapshot;
    private static string cachedMyRankSeason = string.Empty;
    private static string cachedMyRankPlayerId = string.Empty;
    private static bool cachedMyRankFound;
    private static OnlineLeaderboardApiClient.LeaderboardEntry cachedMyRankEntry;
    private static DateTime cachedMyRankUpdatedAtUtc;

    public static void StoreTopEntries(string season, List<OnlineLeaderboardApiClient.LeaderboardEntry> entries)
    {
        cachedTopEntries.Clear();
        if (entries != null)
        {
            for (int i = 0; i < entries.Count; i++)
                cachedTopEntries.Add(CloneEntry(entries[i]));
        }

        hasTopSnapshot = true;
        cachedTopSeason = NormalizeSeason(season);
        cachedTopUpdatedAtUtc = DateTime.UtcNow;
    }

    public static bool TryGetTopEntries(
        string season,
        out List<OnlineLeaderboardApiClient.LeaderboardEntry> entries,
        out DateTime updatedAtUtc
    )
    {
        entries = null;
        updatedAtUtc = default;

        if (!hasTopSnapshot || !string.Equals(cachedTopSeason, NormalizeSeason(season), StringComparison.Ordinal))
            return false;

        updatedAtUtc = cachedTopUpdatedAtUtc;
        entries = new List<OnlineLeaderboardApiClient.LeaderboardEntry>(cachedTopEntries.Count);
        for (int i = 0; i < cachedTopEntries.Count; i++)
            entries.Add(CloneEntry(cachedTopEntries[i]));

        return true;
    }

    public static void StoreMyRank(
        string season,
        string playerId,
        bool found,
        OnlineLeaderboardApiClient.LeaderboardEntry entry
    )
    {
        hasMyRankSnapshot = true;
        cachedMyRankSeason = NormalizeSeason(season);
        cachedMyRankPlayerId = NormalizePlayerId(playerId);
        cachedMyRankFound = found;
        cachedMyRankEntry = found ? CloneEntry(entry) : null;
        cachedMyRankUpdatedAtUtc = DateTime.UtcNow;
    }

    public static bool TryGetMyRank(
        string season,
        string playerId,
        out bool found,
        out OnlineLeaderboardApiClient.LeaderboardEntry entry,
        out DateTime updatedAtUtc
    )
    {
        found = false;
        entry = null;
        updatedAtUtc = default;

        if (!hasMyRankSnapshot
            || !string.Equals(cachedMyRankSeason, NormalizeSeason(season), StringComparison.Ordinal)
            || !string.Equals(cachedMyRankPlayerId, NormalizePlayerId(playerId), StringComparison.Ordinal))
        {
            return false;
        }

        found = cachedMyRankFound;
        entry = cachedMyRankFound ? CloneEntry(cachedMyRankEntry) : null;
        updatedAtUtc = cachedMyRankUpdatedAtUtc;
        return true;
    }

    private static OnlineLeaderboardApiClient.LeaderboardEntry CloneEntry(OnlineLeaderboardApiClient.LeaderboardEntry source)
    {
        if (source == null)
            return null;

        return new OnlineLeaderboardApiClient.LeaderboardEntry
        {
            rank = source.rank,
            playerId = source.playerId,
            displayName = source.displayName,
            score = source.score,
            runDurationSec = source.runDurationSec,
            kills = source.kills,
            createdAtUtc = source.createdAtUtc
        };
    }

    private static string NormalizeSeason(string season)
    {
        if (string.IsNullOrWhiteSpace(season))
            return OnlineLeaderboardSettings.DefaultSeason;

        return season.Trim().ToLowerInvariant();
    }

    private static string NormalizePlayerId(string playerId)
    {
        return string.IsNullOrWhiteSpace(playerId) ? string.Empty : playerId.Trim();
    }
}
