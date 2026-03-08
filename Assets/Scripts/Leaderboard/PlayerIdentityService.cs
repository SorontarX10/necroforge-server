using System;
using GrassSim.Auth;
using UnityEngine;

public static class PlayerIdentityService
{
    private const string PlayerIdKey = "leaderboard_player_id";
    private const string DisplayNameKey = "leaderboard_display_name";

    public static string GetPlayerId()
    {
        if (ExternalAuthSessionStore.TryGetActiveSession(out ExternalAuthSession sessionFromAuth)
            && !string.IsNullOrWhiteSpace(sessionFromAuth.account_id))
        {
            return sessionFromAuth.account_id;
        }

        string platformPlayerId = PlatformServices.GetPlayerId();
        if (!string.IsNullOrWhiteSpace(platformPlayerId))
            return platformPlayerId;

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
        if (ExternalAuthSessionStore.TryGetActiveSession(out ExternalAuthSession sessionFromAuth)
            && !string.IsNullOrWhiteSpace(sessionFromAuth.display_name))
        {
            return NormalizeDisplayName(sessionFromAuth.display_name);
        }

        string platformName = PlatformServices.GetPlayerName();
        if (!string.IsNullOrWhiteSpace(platformName))
            return NormalizeDisplayName(platformName);

        string current = PlayerPrefs.GetString(DisplayNameKey, string.Empty);
        if (!string.IsNullOrWhiteSpace(current))
            return NormalizeDisplayName(current);

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

        if (ExternalAuthSessionStore.TryGetActiveSession(out _))
            return;

        string platformPlayerId = PlatformServices.GetPlayerId();
        if (!string.IsNullOrWhiteSpace(platformPlayerId))
            return;

        string normalized = NormalizeDisplayName(value);

        PlayerPrefs.SetString(DisplayNameKey, normalized);
        PlayerPrefs.Save();
    }

    private static string NormalizeDisplayName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "Player";

        string normalized = value.Trim();
        if (normalized.Length > 48)
            normalized = normalized.Substring(0, 48);

        return string.IsNullOrWhiteSpace(normalized) ? "Player" : normalized;
    }
}
