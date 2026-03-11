using System;
using System.Collections.Generic;
using GrassSim.Auth;
using UnityEngine;

public static class PlayerIdentityService
{
    private const string PlayerIdKey = "leaderboard_player_id";
    private const string DisplayNameKey = "leaderboard_display_name";
    private const string ExternalDisplayNameOverridesKey = "leaderboard_external_display_name_overrides_v1";

    [Serializable]
    private sealed class ExternalDisplayNameOverrideEntry
    {
        public string account_id;
        public string display_name;
    }

    [Serializable]
    private sealed class ExternalDisplayNameOverridesEnvelope
    {
        public List<ExternalDisplayNameOverrideEntry> entries = new();
    }

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
            if (TryGetExternalDisplayNameOverride(sessionFromAuth.account_id, out string overrideDisplayName))
                return NormalizeDisplayName(overrideDisplayName);

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

        string normalized = NormalizeDisplayName(value);

        if (ExternalAuthSessionStore.TryGetActiveSession(out ExternalAuthSession sessionFromAuth)
            && !string.IsNullOrWhiteSpace(sessionFromAuth.account_id))
        {
            SetExternalDisplayNameOverride(sessionFromAuth.account_id, normalized);
            ExternalAuthService.TrySetDisplayNameOverride(normalized);
            return;
        }

        string platformPlayerId = PlatformServices.GetPlayerId();
        if (!string.IsNullOrWhiteSpace(platformPlayerId))
            return;

        PlayerPrefs.SetString(DisplayNameKey, normalized);
        PlayerPrefs.Save();
    }

    public static bool HasCustomExternalDisplayNameForActiveSession()
    {
        if (!ExternalAuthSessionStore.TryGetActiveSession(out ExternalAuthSession sessionFromAuth))
            return false;

        return TryGetExternalDisplayNameOverride(sessionFromAuth.account_id, out _);
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

    private static bool TryGetExternalDisplayNameOverride(string accountId, out string displayName)
    {
        displayName = string.Empty;
        if (string.IsNullOrWhiteSpace(accountId))
            return false;

        ExternalDisplayNameOverridesEnvelope envelope = LoadExternalDisplayNameOverrides();
        if (envelope.entries == null || envelope.entries.Count == 0)
            return false;

        for (int i = 0; i < envelope.entries.Count; i++)
        {
            ExternalDisplayNameOverrideEntry entry = envelope.entries[i];
            if (entry == null || string.IsNullOrWhiteSpace(entry.account_id) || string.IsNullOrWhiteSpace(entry.display_name))
                continue;

            if (string.Equals(entry.account_id.Trim(), accountId.Trim(), StringComparison.Ordinal))
            {
                displayName = entry.display_name.Trim();
                return !string.IsNullOrWhiteSpace(displayName);
            }
        }

        return false;
    }

    private static void SetExternalDisplayNameOverride(string accountId, string displayName)
    {
        if (string.IsNullOrWhiteSpace(accountId) || string.IsNullOrWhiteSpace(displayName))
            return;

        string normalizedAccountId = accountId.Trim();
        string normalizedDisplayName = NormalizeDisplayName(displayName);

        ExternalDisplayNameOverridesEnvelope envelope = LoadExternalDisplayNameOverrides();
        if (envelope.entries == null)
            envelope.entries = new List<ExternalDisplayNameOverrideEntry>();

        bool updated = false;
        for (int i = 0; i < envelope.entries.Count; i++)
        {
            ExternalDisplayNameOverrideEntry entry = envelope.entries[i];
            if (entry == null || string.IsNullOrWhiteSpace(entry.account_id))
                continue;

            if (string.Equals(entry.account_id.Trim(), normalizedAccountId, StringComparison.Ordinal))
            {
                entry.display_name = normalizedDisplayName;
                updated = true;
                break;
            }
        }

        if (!updated)
        {
            envelope.entries.Add(new ExternalDisplayNameOverrideEntry
            {
                account_id = normalizedAccountId,
                display_name = normalizedDisplayName
            });
        }

        // Keep only the newest entries to avoid unbounded PlayerPrefs growth.
        const int maxEntries = 64;
        if (envelope.entries.Count > maxEntries)
        {
            int removeCount = envelope.entries.Count - maxEntries;
            envelope.entries.RemoveRange(0, removeCount);
        }

        try
        {
            PlayerPrefs.SetString(ExternalDisplayNameOverridesKey, JsonUtility.ToJson(envelope));
            PlayerPrefs.Save();
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[PlayerIdentity] Failed to persist external display name override: {ex.Message}");
        }
    }

    private static ExternalDisplayNameOverridesEnvelope LoadExternalDisplayNameOverrides()
    {
        string raw = PlayerPrefs.GetString(ExternalDisplayNameOverridesKey, string.Empty);
        if (string.IsNullOrWhiteSpace(raw))
            return new ExternalDisplayNameOverridesEnvelope();

        try
        {
            ExternalDisplayNameOverridesEnvelope parsed = JsonUtility.FromJson<ExternalDisplayNameOverridesEnvelope>(raw);
            if (parsed?.entries == null)
                return new ExternalDisplayNameOverridesEnvelope();
            return parsed;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[PlayerIdentity] Failed to parse external display name overrides: {ex.Message}");
            return new ExternalDisplayNameOverridesEnvelope();
        }
    }
}
