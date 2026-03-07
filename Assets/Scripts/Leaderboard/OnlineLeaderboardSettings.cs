using System;
using System.IO;
using GrassSim.Core;
using UnityEngine;

public static class OnlineLeaderboardSettings
{
    private const string BaseUrlKey = "leaderboard_api_base_url";
    private const string SeasonKey = "leaderboard_season";
    private const string TimeoutSecondsKey = "leaderboard_timeout_seconds";
    private const string ConfigFileName = "leaderboard_config.json";

    [Serializable]
    private sealed class LeaderboardConfigFile
    {
        public string base_url;
        public string season = "global_all_time";
        public float timeout_seconds = 8f;
    }

    private static LeaderboardConfigFile cachedConfig;
    private static bool configLoaded;

    public static string DefaultSeason => "global_all_time";

    public static bool IsOnlineEnabled =>
        BuildProfileResolver.IsOnlineLeaderboardEnabled &&
        !string.IsNullOrWhiteSpace(GetBaseUrl());

    public static string GetBaseUrl()
    {
        string value = PlayerPrefs.GetString(BaseUrlKey, string.Empty);
        if (string.IsNullOrWhiteSpace(value))
            value = GetConfig().base_url;

        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        value = value.Trim();
        return value.EndsWith("/") ? value.TrimEnd('/') : value;
    }

    public static void SetBaseUrl(string baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            PlayerPrefs.DeleteKey(BaseUrlKey);
            PlayerPrefs.Save();
            return;
        }

        string normalized = baseUrl.Trim().TrimEnd('/');
        PlayerPrefs.SetString(BaseUrlKey, normalized);
        PlayerPrefs.Save();
    }

    public static string GetSeason()
    {
        string value = PlayerPrefs.GetString(SeasonKey, string.Empty);
        if (string.IsNullOrWhiteSpace(value))
            value = GetConfig().season;
        if (string.IsNullOrWhiteSpace(value))
            value = DefaultSeason;

        return string.IsNullOrWhiteSpace(value) ? DefaultSeason : value.Trim().ToLowerInvariant();
    }

    public static float GetTimeoutSeconds()
    {
        float defaultTimeout = Mathf.Clamp(GetConfig().timeout_seconds, 3f, 20f);
        if (defaultTimeout <= 0f)
            defaultTimeout = 8f;

        float value = PlayerPrefs.GetFloat(TimeoutSecondsKey, defaultTimeout);
        return Mathf.Clamp(value, 3f, 20f);
    }

    private static LeaderboardConfigFile GetConfig()
    {
        if (configLoaded)
            return cachedConfig ?? new LeaderboardConfigFile();

        configLoaded = true;
        cachedConfig = new LeaderboardConfigFile();
        try
        {
            string path = Path.Combine(Application.streamingAssetsPath, ConfigFileName);
            if (!File.Exists(path))
                return cachedConfig;

            string json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json))
                return cachedConfig;

            LeaderboardConfigFile parsed = JsonUtility.FromJson<LeaderboardConfigFile>(json);
            if (parsed != null)
                cachedConfig = parsed;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[LeaderboardSettings] Failed to load config file: {ex.Message}");
        }

        return cachedConfig;
    }
}
