using UnityEngine;
using System;
using System.Linq;
using System.Collections.Generic;

public static class LocalLeaderboardService
{
    private const string KEY = "LOCAL_SCORES";

    private const int MAX_ENTRIES = 10;
    private const string DateFormat = "yyyy-MM-dd HH:mm:ss";

    private static int lastSavedScore = int.MinValue;
    private static string lastSavedDate = string.Empty;

    [Serializable]
    public struct Entry
    {
        public int score;
        public string date;
    }

    public static void SaveScore(int score)
    {
        if (score <= 0)
            return;

        string now = DateTime.Now.ToString(DateFormat);
        if (lastSavedScore == score && lastSavedDate == now)
            return;

        var entries = GetAllEntries().ToList();

        entries.Add(new Entry
        {
            score = score,
            date = now
        });

        entries = entries
            .GroupBy(e => $"{e.score}|{e.date}")
            .Select(g => g.First())
            .OrderByDescending(e => e.score)
            .Take(MAX_ENTRIES)
            .ToList();

        // format: score|date,score|date,...
        string raw = string.Join(",",
            entries.Select(e => $"{e.score}|{e.date}")
        );

        PlayerPrefs.SetString(KEY, raw);
        PlayerPrefs.Save();

        lastSavedScore = score;
        lastSavedDate = now;
    }

    public static Entry[] GetTopEntries(int count)
    {
        return GetAllEntries()
            .Take(count)
            .ToArray();
    }

    private static Entry[] GetAllEntries()
    {
        if (!PlayerPrefs.HasKey(KEY))
            return Array.Empty<Entry>();

        return PlayerPrefs.GetString(KEY)
            .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(ParseEntry)
            .Where(e => e.score > 0)
            .GroupBy(e => $"{e.score}|{e.date}")
            .Select(g => g.First())
            .OrderByDescending(e => e.score)
            .ToArray();
    }

    private static Entry ParseEntry(string raw)
    {
        var parts = raw.Split('|');

        if (parts.Length != 2)
            return default;

        if (!int.TryParse(parts[0], out int parsedScore))
            return default;

        return new Entry
        {
            score = parsedScore,
            date = parts[1]
        };
    }
}
