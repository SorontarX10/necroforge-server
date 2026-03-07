using System;
using UnityEngine;

public static class PlayerIdentityService
{
    private const string PlayerIdKey = "leaderboard_player_id";
    private const string DisplayNameKey = "leaderboard_display_name";

    public static string GetPlayerId()
    {
        string current = PlayerPrefs.GetString(PlayerIdKey, string.Empty);
        if (!string.IsNullOrWhiteSpace(current))
            return current;

        current = Guid.NewGuid().ToString("N");
        PlayerPrefs.SetString(PlayerIdKey, current);
        PlayerPrefs.Save();
        return current;
    }

    public static string GetDisplayName()
    {
        string current = PlayerPrefs.GetString(DisplayNameKey, string.Empty);
        if (!string.IsNullOrWhiteSpace(current))
            return current;

        string playerId = GetPlayerId();
        string suffix = playerId.Length >= 4 ? playerId.Substring(playerId.Length - 4) : playerId;
        current = $"Player-{suffix}";
        PlayerPrefs.SetString(DisplayNameKey, current);
        PlayerPrefs.Save();
        return current;
    }

    public static void SetDisplayName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        string normalized = value.Trim();
        if (normalized.Length > 48)
            normalized = normalized.Substring(0, 48);

        PlayerPrefs.SetString(DisplayNameKey, normalized);
        PlayerPrefs.Save();
    }
}
