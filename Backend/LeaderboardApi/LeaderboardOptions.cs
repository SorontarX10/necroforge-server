using System.Globalization;
using Npgsql;

namespace LeaderboardApi;

public sealed record LeaderboardOptions(
    string DbConnectionString,
    string VersionLock,
    string AdminApiKey,
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
            DbConnectionString: BuildDbConnectionString(configuration),
            VersionLock: GetString(
                configuration,
                "Leaderboard:VersionLock",
                "LEADERBOARD_VERSION_LOCK",
                string.Empty
            ),
            AdminApiKey: GetString(
                configuration,
                "Leaderboard:AdminApiKey",
                "LEADERBOARD_ADMIN_API_KEY",
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
        string? value = Environment.GetEnvironmentVariable(envKey);
        if (!string.IsNullOrWhiteSpace(value))
            return value;

        value = configuration[configKey];
        if (!string.IsNullOrWhiteSpace(value))
            return value;

        return defaultValue;
    }

    private static string BuildDbConnectionString(IConfiguration configuration)
    {
        string? explicitConnectionString = Environment.GetEnvironmentVariable("LEADERBOARD_DB_CONNECTION");
        if (!string.IsNullOrWhiteSpace(explicitConnectionString))
            return explicitConnectionString;

        if (!HasDbComponentOverrides())
        {
            explicitConnectionString = configuration["Leaderboard:DbConnectionString"];
            if (!string.IsNullOrWhiteSpace(explicitConnectionString))
                return explicitConnectionString;
        }

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = GetString(configuration, "Leaderboard:DbHost", "LEADERBOARD_DB_HOST", "localhost"),
            Port = GetInt(configuration, "Leaderboard:DbPort", "LEADERBOARD_DB_PORT", 5432, 1, 65535),
            Database = GetFirstNonEmpty(
                Environment.GetEnvironmentVariable("LEADERBOARD_DB_NAME"),
                Environment.GetEnvironmentVariable("POSTGRES_DB"),
                configuration["Leaderboard:DbName"],
                "leaderboard"
            ),
            Username = GetFirstNonEmpty(
                Environment.GetEnvironmentVariable("LEADERBOARD_DB_USER"),
                Environment.GetEnvironmentVariable("POSTGRES_USER"),
                configuration["Leaderboard:DbUser"],
                "leaderboard"
            ),
            Password = GetFirstNonEmpty(
                Environment.GetEnvironmentVariable("LEADERBOARD_DB_PASSWORD"),
                Environment.GetEnvironmentVariable("POSTGRES_PASSWORD"),
                configuration["Leaderboard:DbPassword"],
                "leaderboard"
            )
        };

        return builder.ConnectionString;
    }

    private static bool HasDbComponentOverrides()
    {
        return HasEnvironmentVariable("LEADERBOARD_DB_HOST")
            || HasEnvironmentVariable("LEADERBOARD_DB_PORT")
            || HasEnvironmentVariable("LEADERBOARD_DB_NAME")
            || HasEnvironmentVariable("LEADERBOARD_DB_USER")
            || HasEnvironmentVariable("LEADERBOARD_DB_PASSWORD")
            || HasEnvironmentVariable("POSTGRES_DB")
            || HasEnvironmentVariable("POSTGRES_USER")
            || HasEnvironmentVariable("POSTGRES_PASSWORD");
    }

    private static string GetFirstNonEmpty(params string?[] values)
    {
        foreach (string? value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return string.Empty;
    }

    private static bool HasEnvironmentVariable(string name)
    {
        return !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name));
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
