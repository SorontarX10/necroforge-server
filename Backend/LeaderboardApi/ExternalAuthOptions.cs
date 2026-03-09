using System.Globalization;

namespace LeaderboardApi;

public sealed class ExternalAuthOptions
{
    public bool Enabled { get; init; }
    public string PublicBaseUrl { get; init; } = string.Empty;
    public string StateSecret { get; init; } = string.Empty;
    public int StateTtlSeconds { get; init; }
    public ExternalAuthProviderOptions Google { get; init; } = new("google");
    public ExternalAuthProviderOptions Microsoft { get; init; } = new("microsoft");
    public ExternalAuthProviderOptions Facebook { get; init; } = new("facebook");
    public ExternalAuthSteamOptions Steam { get; init; } = new();

    public static ExternalAuthOptions FromConfiguration(IConfiguration configuration)
    {
        return new ExternalAuthOptions
        {
            Enabled = GetBool(configuration, "Leaderboard:Auth:Enabled", "LEADERBOARD_AUTH_BROKER_ENABLED", false),
            PublicBaseUrl = GetString(
                configuration,
                "Leaderboard:Auth:PublicBaseUrl",
                "LEADERBOARD_AUTH_PUBLIC_BASE_URL",
                string.Empty
            ).Trim().TrimEnd('/'),
            StateSecret = GetString(
                configuration,
                "Leaderboard:Auth:StateSecret",
                "LEADERBOARD_AUTH_STATE_SECRET",
                string.Empty
            ),
            StateTtlSeconds = GetInt(
                configuration,
                "Leaderboard:Auth:StateTtlSeconds",
                "LEADERBOARD_AUTH_STATE_TTL_SECONDS",
                600,
                60,
                3600
            ),
            Google = BuildProvider(
                configuration,
                providerKey: "Google",
                envPrefix: "LEADERBOARD_AUTH_GOOGLE",
                providerName: "google",
                defaultScopes: "openid profile email"
            ),
            Microsoft = BuildProvider(
                configuration,
                providerKey: "Microsoft",
                envPrefix: "LEADERBOARD_AUTH_MICROSOFT",
                providerName: "microsoft",
                defaultScopes: "openid profile email offline_access",
                defaultTenant: "common"
            ),
            Facebook = BuildProvider(
                configuration,
                providerKey: "Facebook",
                envPrefix: "LEADERBOARD_AUTH_FACEBOOK",
                providerName: "facebook",
                defaultScopes: "public_profile,email"
            ),
            Steam = new ExternalAuthSteamOptions
            {
                Enabled = GetBool(
                    configuration,
                    "Leaderboard:Auth:Steam:Enabled",
                    "LEADERBOARD_AUTH_STEAM_ENABLED",
                    false
                ),
                AppId = GetLong(
                    configuration,
                    "Leaderboard:Auth:Steam:AppId",
                    "LEADERBOARD_AUTH_STEAM_APP_ID",
                    0L,
                    0L,
                    long.MaxValue
                ),
                WebApiKey = GetString(
                    configuration,
                    "Leaderboard:Auth:Steam:WebApiKey",
                    "LEADERBOARD_AUTH_STEAM_WEB_API_KEY",
                    string.Empty
                )
            }
        };
    }

    public bool TryGetProvider(string? rawProvider, out ExternalAuthProviderOptions provider)
    {
        string normalized = NormalizeProvider(rawProvider);
        provider = normalized switch
        {
            "google" => Google,
            "microsoft" => Microsoft,
            "facebook" => Facebook,
            _ => new ExternalAuthProviderOptions(normalized)
        };
        return provider.IsConfigured;
    }

    public string BuildCallbackUrl(HttpRequest request, string provider)
    {
        string baseUrl = PublicBaseUrl;
        if (string.IsNullOrWhiteSpace(baseUrl))
            baseUrl = $"{request.Scheme}://{request.Host.Value}".TrimEnd('/');

        return $"{baseUrl}/auth/external/{NormalizeProvider(provider)}/callback";
    }

    public static string NormalizeProvider(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        string trimmed = value.Trim().ToLowerInvariant();
        return trimmed.Length <= 32 ? trimmed : trimmed[..32];
    }

    private static ExternalAuthProviderOptions BuildProvider(
        IConfiguration configuration,
        string providerKey,
        string envPrefix,
        string providerName,
        string defaultScopes,
        string defaultTenant = "")
    {
        return new ExternalAuthProviderOptions(providerName)
        {
            Enabled = GetBool(
                configuration,
                $"Leaderboard:Auth:{providerKey}:Enabled",
                $"{envPrefix}_ENABLED",
                false
            ),
            ClientId = GetString(
                configuration,
                $"Leaderboard:Auth:{providerKey}:ClientId",
                $"{envPrefix}_CLIENT_ID",
                string.Empty
            ),
            ClientSecret = GetString(
                configuration,
                $"Leaderboard:Auth:{providerKey}:ClientSecret",
                $"{envPrefix}_CLIENT_SECRET",
                string.Empty
            ),
            Scopes = GetString(
                configuration,
                $"Leaderboard:Auth:{providerKey}:Scopes",
                $"{envPrefix}_SCOPES",
                defaultScopes
            ),
            Tenant = GetString(
                configuration,
                $"Leaderboard:Auth:{providerKey}:Tenant",
                $"{envPrefix}_TENANT",
                defaultTenant
            )
        };
    }

    private static string GetString(
        IConfiguration configuration,
        string configKey,
        string envKey,
        string defaultValue)
    {
        string? value = Environment.GetEnvironmentVariable(envKey);
        if (!string.IsNullOrWhiteSpace(value))
            return value;

        value = configuration[configKey];
        if (!string.IsNullOrWhiteSpace(value))
            return value;

        return defaultValue;
    }

    private static bool GetBool(
        IConfiguration configuration,
        string configKey,
        string envKey,
        bool defaultValue)
    {
        string raw = GetString(configuration, configKey, envKey, defaultValue ? "true" : "false");
        return bool.TryParse(raw, out bool parsed) ? parsed : defaultValue;
    }

    private static int GetInt(
        IConfiguration configuration,
        string configKey,
        string envKey,
        int defaultValue,
        int minValue,
        int maxValue)
    {
        string raw = GetString(configuration, configKey, envKey, defaultValue.ToString(CultureInfo.InvariantCulture));
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
            ? Math.Clamp(parsed, minValue, maxValue)
            : defaultValue;
    }

    private static long GetLong(
        IConfiguration configuration,
        string configKey,
        string envKey,
        long defaultValue,
        long minValue,
        long maxValue)
    {
        string raw = GetString(configuration, configKey, envKey, defaultValue.ToString(CultureInfo.InvariantCulture));
        return long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed)
            ? Math.Clamp(parsed, minValue, maxValue)
            : defaultValue;
    }
}

public sealed class ExternalAuthProviderOptions(string name)
{
    public string Name { get; } = name;
    public bool Enabled { get; init; }
    public string ClientId { get; init; } = string.Empty;
    public string ClientSecret { get; init; } = string.Empty;
    public string Scopes { get; init; } = string.Empty;
    public string Tenant { get; init; } = string.Empty;

    public bool IsConfigured =>
        Enabled
        && !string.IsNullOrWhiteSpace(ClientId)
        && !string.IsNullOrWhiteSpace(ClientSecret);
}

public sealed class ExternalAuthSteamOptions
{
    public bool Enabled { get; init; }
    public long AppId { get; init; }
    public string WebApiKey { get; init; } = string.Empty;

    public bool IsConfigured =>
        Enabled
        && AppId > 0
        && !string.IsNullOrWhiteSpace(WebApiKey);
}
