using System.Globalization;

namespace LeaderboardApi;

public sealed record LeaderboardOptions(
    string DbConnectionString,
    string VersionLock,
    int SessionTtlSeconds,
    int MaxStartPerMinutePerPlayer,
    int MaxSubmitPerMinutePerPlayer,
    float MinRunDurationSeconds,
    float MaxScorePerMinute,
    float MaxKillsPerMinute,
    int MaxPageSize,
    string[] AllowedSeasons
)
{
    public static LeaderboardOptions FromConfiguration(IConfiguration configuration)
    {
        return new LeaderboardOptions(
            DbConnectionString: GetString(
                configuration,
                "Leaderboard:DbConnectionString",
                "LEADERBOARD_DB_CONNECTION",
                "Host=localhost;Port=5432;Database=leaderboard;Username=leaderboard;Password=leaderboard"
            ),
            VersionLock: GetString(
                configuration,
                "Leaderboard:VersionLock",
                "LEADERBOARD_VERSION_LOCK",
                string.Empty
            ),
            SessionTtlSeconds: GetInt(
                configuration,
                "Leaderboard:SessionTtlSeconds",
                "LEADERBOARD_SESSION_TTL_SECONDS",
                300,
                30,
                3600
            ),
            MaxStartPerMinutePerPlayer: GetInt(
                configuration,
                "Leaderboard:MaxStartPerMinutePerPlayer",
                "LEADERBOARD_MAX_START_PER_MINUTE_PER_PLAYER",
                20,
                1,
                200
            ),
            MaxSubmitPerMinutePerPlayer: GetInt(
                configuration,
                "Leaderboard:MaxSubmitPerMinutePerPlayer",
                "LEADERBOARD_MAX_SUBMIT_PER_MINUTE_PER_PLAYER",
                20,
                1,
                200
            ),
            MinRunDurationSeconds: GetFloat(
                configuration,
                "Leaderboard:MinRunDurationSeconds",
                "LEADERBOARD_MIN_RUN_DURATION_SECONDS",
                30f,
                0f,
                7200f
            ),
            MaxScorePerMinute: GetFloat(
                configuration,
                "Leaderboard:MaxScorePerMinute",
                "LEADERBOARD_MAX_SCORE_PER_MINUTE",
                120000f,
                100f,
                10_000_000f
            ),
            MaxKillsPerMinute: GetFloat(
                configuration,
                "Leaderboard:MaxKillsPerMinute",
                "LEADERBOARD_MAX_KILLS_PER_MINUTE",
                3000f,
                1f,
                100_000f
            ),
            MaxPageSize: GetInt(
                configuration,
                "Leaderboard:MaxPageSize",
                "LEADERBOARD_MAX_PAGE_SIZE",
                50,
                10,
                100
            ),
            AllowedSeasons: ["global_all_time", "global_weekly"]
        );
    }

    public string NormalizeSeason(string? raw)
    {
        string candidate = string.IsNullOrWhiteSpace(raw)
            ? "global_all_time"
            : raw.Trim().ToLowerInvariant();
        return AllowedSeasons.Contains(candidate) ? candidate : string.Empty;
    }

    private static string GetString(
        IConfiguration configuration,
        string configKey,
        string envKey,
        string defaultValue
    )
    {
        string? value = configuration[configKey];
        if (!string.IsNullOrWhiteSpace(value))
            return value;

        value = Environment.GetEnvironmentVariable(envKey);
        if (!string.IsNullOrWhiteSpace(value))
            return value;

        return defaultValue;
    }

    private static int GetInt(
        IConfiguration configuration,
        string configKey,
        string envKey,
        int defaultValue,
        int minValue,
        int maxValue
    )
    {
        string raw = GetString(configuration, configKey, envKey, defaultValue.ToString(CultureInfo.InvariantCulture));
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
            ? Math.Clamp(parsed, minValue, maxValue)
            : defaultValue;
    }

    private static float GetFloat(
        IConfiguration configuration,
        string configKey,
        string envKey,
        float defaultValue,
        float minValue,
        float maxValue
    )
    {
        string raw = GetString(configuration, configKey, envKey, defaultValue.ToString(CultureInfo.InvariantCulture));
        return float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed)
            ? Math.Clamp(parsed, minValue, maxValue)
            : defaultValue;
    }
}
