using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.WebUtilities;

namespace LeaderboardApi;

public sealed class ExternalAuthBroker(
    IHttpClientFactory httpClientFactory,
    ExternalAuthOptions options,
    ILogger<ExternalAuthBroker> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;
    private readonly ExternalAuthOptions _options = options;
    private readonly ILogger<ExternalAuthBroker> _logger = logger;

    public bool IsEnabled => _options.Enabled;

    public ServiceResult<string> BuildStartRedirectUrl(string rawProvider, HttpRequest request)
    {
        if (!_options.Enabled)
        {
            return ServiceResult<string>.Failure(
                StatusCodes.Status503ServiceUnavailable,
                "auth_broker_disabled",
                "External auth broker is disabled."
            );
        }

        if (!TryResolveProvider(rawProvider, out ExternalAuthProviderOptions provider, out string providerError))
        {
            return ServiceResult<string>.Failure(
                StatusCodes.Status400BadRequest,
                "auth_provider_not_supported",
                providerError
            );
        }

        string callbackUrl = _options.BuildCallbackUrl(request, provider.Name);
        string state = BuildState(provider.Name);

        var query = new Dictionary<string, string?>
        {
            ["client_id"] = provider.ClientId,
            ["redirect_uri"] = callbackUrl,
            ["response_type"] = "code",
            ["scope"] = provider.Scopes,
            ["state"] = state
        };

        switch (provider.Name)
        {
            case "google":
                query["access_type"] = "offline";
                query["prompt"] = "consent";
                break;
            case "microsoft":
                query["response_mode"] = "query";
                break;
        }

        string redirectUrl = QueryHelpers.AddQueryString(GetAuthorizationEndpoint(provider), query);
        return ServiceResult<string>.Success(redirectUrl);
    }

    public string RenderCallbackPage(string rawProvider, HttpRequest request)
    {
        if (!_options.Enabled)
            return BuildCallbackHtml(false, "Auth broker is disabled.", request.GetDisplayUrl());

        if (!TryResolveProvider(rawProvider, out ExternalAuthProviderOptions provider, out string providerError))
            return BuildCallbackHtml(false, providerError, request.GetDisplayUrl());

        string error = request.Query["error"].ToString();
        if (!string.IsNullOrWhiteSpace(error))
        {
            string description = request.Query["error_description"].ToString();
            string message = string.IsNullOrWhiteSpace(description)
                ? $"Provider returned error: {error}"
                : $"Provider returned error: {error} ({description})";
            return BuildCallbackHtml(false, message, request.GetDisplayUrl());
        }

        string code = request.Query["code"].ToString();
        if (string.IsNullOrWhiteSpace(code))
            return BuildCallbackHtml(false, "Missing 'code' query parameter in callback URL.", request.GetDisplayUrl());

        string state = request.Query["state"].ToString();
        if (!ValidateState(provider.Name, state))
            return BuildCallbackHtml(false, "Invalid or expired OAuth state.", request.GetDisplayUrl());

        string copyUrl = QueryHelpers.AddQueryString(
            $"{request.Scheme}://{request.Host.Value}{request.Path.Value}",
            new Dictionary<string, string?>
            {
                ["provider"] = provider.Name,
                ["code"] = code
            }
        );
        return BuildCallbackHtml(
            true,
            "Login approved. Copy the URL below and return to the game.",
            copyUrl
        );
    }

    public async Task<ServiceResult<ExternalAuthSessionResponse>> ExchangeCodeAsync(
        ExchangeExternalAuthCodeRequest request,
        HttpRequest httpRequest,
        CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return ServiceResult<ExternalAuthSessionResponse>.Failure(
                StatusCodes.Status503ServiceUnavailable,
                "auth_broker_disabled",
                "External auth broker is disabled."
            );
        }

        if (!TryResolveProvider(request.Provider, out ExternalAuthProviderOptions provider, out string providerError))
        {
            return ServiceResult<ExternalAuthSessionResponse>.Failure(
                StatusCodes.Status400BadRequest,
                "auth_provider_not_supported",
                providerError
            );
        }

        string code = request.Code?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(code))
        {
            return ServiceResult<ExternalAuthSessionResponse>.Failure(
                StatusCodes.Status400BadRequest,
                "auth_missing_code",
                "Field 'code' is required."
            );
        }

        string callbackUrl = _options.BuildCallbackUrl(httpRequest, provider.Name);
        ServiceResult<ExternalAuthTokenPayload> tokenResult = await ExchangeCodeForTokenAsync(
            provider,
            code,
            callbackUrl,
            cancellationToken
        );
        if (!tokenResult.Ok || tokenResult.Payload == null)
            return BuildErrorResult<ExternalAuthSessionResponse>(tokenResult.StatusCode, tokenResult.Error);

        ServiceResult<ExternalAuthIdentity> identityResult = await ResolveIdentityAsync(
            provider.Name,
            tokenResult.Payload.AccessToken,
            tokenResult.Payload.IdToken,
            cancellationToken
        );
        if (!identityResult.Ok || identityResult.Payload == null)
            return BuildErrorResult<ExternalAuthSessionResponse>(identityResult.StatusCode, identityResult.Error);

        ExternalAuthSessionResponse session = BuildSession(provider.Name, identityResult.Payload, tokenResult.Payload);
        return ServiceResult<ExternalAuthSessionResponse>.Success(session);
    }

    public async Task<ServiceResult<ExternalAuthSessionResponse>> RefreshAsync(
        RefreshExternalAuthSessionRequest request,
        HttpRequest httpRequest,
        CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return ServiceResult<ExternalAuthSessionResponse>.Failure(
                StatusCodes.Status503ServiceUnavailable,
                "auth_broker_disabled",
                "External auth broker is disabled."
            );
        }

        if (!TryResolveProvider(request.Provider, out ExternalAuthProviderOptions provider, out string providerError))
        {
            return ServiceResult<ExternalAuthSessionResponse>.Failure(
                StatusCodes.Status400BadRequest,
                "auth_provider_not_supported",
                providerError
            );
        }

        string refreshToken = request.RefreshToken?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return ServiceResult<ExternalAuthSessionResponse>.Failure(
                StatusCodes.Status400BadRequest,
                "auth_missing_refresh_token",
                "Field 'refresh_token' is required."
            );
        }

        if (string.Equals(provider.Name, "facebook", StringComparison.Ordinal))
        {
            return ServiceResult<ExternalAuthSessionResponse>.Failure(
                StatusCodes.Status400BadRequest,
                "auth_refresh_not_supported",
                "Facebook refresh flow is not supported by this broker. Sign in again."
            );
        }

        ServiceResult<ExternalAuthTokenPayload> tokenResult = await RefreshTokenAsync(
            provider,
            refreshToken,
            _options.BuildCallbackUrl(httpRequest, provider.Name),
            cancellationToken
        );
        if (!tokenResult.Ok || tokenResult.Payload == null)
            return BuildErrorResult<ExternalAuthSessionResponse>(tokenResult.StatusCode, tokenResult.Error);

        ServiceResult<ExternalAuthIdentity> identityResult = await ResolveIdentityAsync(
            provider.Name,
            tokenResult.Payload.AccessToken,
            tokenResult.Payload.IdToken,
            cancellationToken
        );
        if (!identityResult.Ok || identityResult.Payload == null)
            return BuildErrorResult<ExternalAuthSessionResponse>(identityResult.StatusCode, identityResult.Error);

        ExternalAuthTokenPayload payload = tokenResult.Payload with
        {
            RefreshToken = string.IsNullOrWhiteSpace(tokenResult.Payload.RefreshToken)
                ? refreshToken
                : tokenResult.Payload.RefreshToken
        };
        ExternalAuthSessionResponse session = BuildSession(provider.Name, identityResult.Payload, payload);
        return ServiceResult<ExternalAuthSessionResponse>.Success(session);
    }

    public async Task RevokeAccessTokenAsync(
        LogoutExternalAuthSessionRequest request,
        CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
            return;

        if (!TryResolveProvider(request.Provider, out ExternalAuthProviderOptions provider, out _))
            return;

        string accessToken = request.AccessToken?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(accessToken))
            return;

        try
        {
            if (string.Equals(provider.Name, "google", StringComparison.Ordinal))
            {
                using HttpClient client = CreateHttpClient();
                using var revokeRequest = new HttpRequestMessage(
                    HttpMethod.Post,
                    "https://oauth2.googleapis.com/revoke")
                {
                    Content = new FormUrlEncodedContent(new Dictionary<string, string>
                    {
                        ["token"] = accessToken
                    })
                };
                using HttpResponseMessage _ = await client.SendAsync(revokeRequest, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "External auth logout revoke request failed for provider {Provider}.", provider.Name);
        }
    }

    private static ExternalAuthSessionResponse BuildSession(
        string provider,
        ExternalAuthIdentity identity,
        ExternalAuthTokenPayload tokenPayload)
    {
        string providerUserId = TrimTo(identity.ProviderUserId, 128);
        string accountId = TrimTo($"{provider}:{providerUserId}", 128);
        string displayName = string.IsNullOrWhiteSpace(identity.DisplayName)
            ? BuildFallbackDisplayName(accountId)
            : TrimTo(identity.DisplayName, 48);

        int expiresIn = Math.Clamp(tokenPayload.ExpiresIn <= 0 ? 3600 : tokenPayload.ExpiresIn, 60, 2_592_000);
        long expiresAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + expiresIn;

        return new ExternalAuthSessionResponse(
            AccountId: accountId,
            Provider: provider,
            ProviderUserId: providerUserId,
            DisplayName: displayName,
            AccessToken: tokenPayload.AccessToken,
            RefreshToken: tokenPayload.RefreshToken,
            ExpiresAtUnix: expiresAtUnix
        );
    }

    private static string BuildFallbackDisplayName(string accountId)
    {
        string suffix = accountId.Length >= 6 ? accountId[^6..] : accountId;
        return $"Player-{suffix}";
    }

    private bool TryResolveProvider(
        string? rawProvider,
        out ExternalAuthProviderOptions provider,
        out string error)
    {
        provider = new ExternalAuthProviderOptions(string.Empty);
        error = "Unsupported provider.";

        string normalized = ExternalAuthOptions.NormalizeProvider(rawProvider);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            error = "Field 'provider' is required.";
            return false;
        }

        if (!_options.TryGetProvider(normalized, out provider))
        {
            error = $"Provider '{normalized}' is disabled or not configured on server.";
            return false;
        }

        return true;
    }

    private static string TrimTo(string? value, int maxLength)
    {
        string normalized = value?.Trim() ?? string.Empty;
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }

    private HttpClient CreateHttpClient()
    {
        HttpClient client = _httpClientFactory.CreateClient(nameof(ExternalAuthBroker));
        client.Timeout = TimeSpan.FromSeconds(12);
        return client;
    }

    private string GetAuthorizationEndpoint(ExternalAuthProviderOptions provider)
    {
        return provider.Name switch
        {
            "google" => "https://accounts.google.com/o/oauth2/v2/auth",
            "microsoft" => $"https://login.microsoftonline.com/{GetMicrosoftTenant(provider)}/oauth2/v2.0/authorize",
            "facebook" => "https://www.facebook.com/v20.0/dialog/oauth",
            _ => string.Empty
        };
    }

    private string GetTokenEndpoint(ExternalAuthProviderOptions provider)
    {
        return provider.Name switch
        {
            "google" => "https://oauth2.googleapis.com/token",
            "microsoft" => $"https://login.microsoftonline.com/{GetMicrosoftTenant(provider)}/oauth2/v2.0/token",
            "facebook" => "https://graph.facebook.com/v20.0/oauth/access_token",
            _ => string.Empty
        };
    }

    private static string GetMicrosoftTenant(ExternalAuthProviderOptions provider)
    {
        string tenant = provider.Tenant?.Trim() ?? string.Empty;
        return string.IsNullOrWhiteSpace(tenant) ? "common" : tenant;
    }

    private static ServiceResult<TTo> BuildErrorResult<TTo>(int statusCode, ErrorResponse? error)
    {
        ErrorResponse resolved = error ?? new ErrorResponse("unknown_error", "Unknown error.");
        return ServiceResult<TTo>.Failure(statusCode, resolved.Code, resolved.Message);
    }

    private static string BuildCallbackHtml(bool success, string message, string urlToCopy)
    {
        string status = success ? "Success" : "Error";
        string color = success ? "#0f7a0f" : "#a41010";
        string safeMessage = WebUtility.HtmlEncode(message ?? string.Empty);
        string safeUrl = WebUtility.HtmlEncode(urlToCopy ?? string.Empty);
        return $$"""
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1" />
  <title>External Auth Callback</title>
</head>
<body style="font-family:Segoe UI,Arial,sans-serif;background:#111;color:#eee;padding:24px;">
  <h1 style="color:{{color}};margin:0 0 8px 0;">{{status}}</h1>
  <p style="margin:0 0 16px 0;">{{safeMessage}}</p>
  <p style="margin:0 0 8px 0;">Copy this URL and paste it in game:</p>
  <textarea id="callbackUrl" style="width:100%;height:86px;">{{safeUrl}}</textarea>
  <div style="margin-top:12px;">
    <button type="button" onclick="copyUrl()" style="padding:8px 14px;">Copy URL</button>
  </div>
  <script>
    function copyUrl() {
      var el = document.getElementById('callbackUrl');
      el.focus();
      el.select();
      if (navigator.clipboard && navigator.clipboard.writeText) {
        navigator.clipboard.writeText(el.value).catch(function(){ document.execCommand('copy'); });
      } else {
        document.execCommand('copy');
      }
    }
  </script>
</body>
</html>
""";
    }

    private string BuildState(string provider)
    {
        string nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();
        string issuedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        string payload = $"{provider}|{issuedAt}|{nonce}";
        string payloadEncoded = Base64UrlTextEncoder.Encode(Encoding.UTF8.GetBytes(payload));

        if (string.IsNullOrWhiteSpace(_options.StateSecret))
            return payloadEncoded;

        string signature = ComputeStateSignature(payload);
        return $"{payloadEncoded}.{signature}";
    }

    private bool ValidateState(string provider, string state)
    {
        if (string.IsNullOrWhiteSpace(state))
            return false;

        if (string.IsNullOrWhiteSpace(_options.StateSecret))
            return true;

        string[] chunks = state.Split('.', 2, StringSplitOptions.RemoveEmptyEntries);
        if (chunks.Length != 2)
            return false;

        string payload;
        try
        {
            payload = Encoding.UTF8.GetString(Base64UrlTextEncoder.Decode(chunks[0]));
        }
        catch
        {
            return false;
        }

        string expectedSignature = ComputeStateSignature(payload);
        byte[] providedBytes = Encoding.UTF8.GetBytes(chunks[1]);
        byte[] expectedBytes = Encoding.UTF8.GetBytes(expectedSignature);
        if (!CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes))
            return false;

        string[] payloadParts = payload.Split('|', 3, StringSplitOptions.None);
        if (payloadParts.Length != 3)
            return false;
        if (!string.Equals(payloadParts[0], provider, StringComparison.Ordinal))
            return false;

        if (!long.TryParse(payloadParts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out long issuedAt))
            return false;

        long age = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - issuedAt;
        return age >= 0 && age <= _options.StateTtlSeconds;
    }

    private string ComputeStateSignature(string payload)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_options.StateSecret));
        byte[] hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return Base64UrlTextEncoder.Encode(hash);
    }

    private async Task<ServiceResult<ExternalAuthTokenPayload>> ExchangeCodeForTokenAsync(
        ExternalAuthProviderOptions provider,
        string code,
        string callbackUrl,
        CancellationToken cancellationToken)
    {
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["client_id"] = provider.ClientId,
            ["client_secret"] = provider.ClientSecret,
            ["redirect_uri"] = callbackUrl
        };
        if (string.Equals(provider.Name, "facebook", StringComparison.Ordinal))
            form.Remove("grant_type");

        return await SendTokenRequestAsync(provider, form, cancellationToken);
    }

    private async Task<ServiceResult<ExternalAuthTokenPayload>> RefreshTokenAsync(
        ExternalAuthProviderOptions provider,
        string refreshToken,
        string callbackUrl,
        CancellationToken cancellationToken)
    {
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = provider.ClientId,
            ["client_secret"] = provider.ClientSecret
        };

        if (string.Equals(provider.Name, "microsoft", StringComparison.Ordinal))
            form["scope"] = provider.Scopes;
        if (string.Equals(provider.Name, "google", StringComparison.Ordinal))
            form["redirect_uri"] = callbackUrl;

        return await SendTokenRequestAsync(provider, form, cancellationToken);
    }

    private async Task<ServiceResult<ExternalAuthTokenPayload>> SendTokenRequestAsync(
        ExternalAuthProviderOptions provider,
        Dictionary<string, string> form,
        CancellationToken cancellationToken)
    {
        using HttpClient client = CreateHttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, GetTokenEndpoint(provider))
        {
            Content = new FormUrlEncodedContent(form)
        };
        using HttpResponseMessage response = await client.SendAsync(request, cancellationToken);

        string rawResponse = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            string providerError = ExtractProviderError(rawResponse, response.ReasonPhrase);
            int statusCode = response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized
                ? StatusCodes.Status401Unauthorized
                : StatusCodes.Status502BadGateway;
            return ServiceResult<ExternalAuthTokenPayload>.Failure(
                statusCode,
                "auth_token_exchange_failed",
                providerError
            );
        }

        try
        {
            using JsonDocument doc = JsonDocument.Parse(rawResponse);
            JsonElement root = doc.RootElement;
            string accessToken = GetJsonString(root, "access_token");
            if (string.IsNullOrWhiteSpace(accessToken))
            {
                return ServiceResult<ExternalAuthTokenPayload>.Failure(
                    StatusCodes.Status502BadGateway,
                    "auth_token_missing_access_token",
                    "Provider response did not include access_token."
                );
            }

            string refreshToken = GetJsonString(root, "refresh_token");
            string idToken = GetJsonString(root, "id_token");
            int expiresIn = GetJsonInt(root, "expires_in", 3600);
            ExternalAuthTokenPayload payload = new(
                AccessToken: accessToken,
                RefreshToken: refreshToken,
                IdToken: idToken,
                ExpiresIn: expiresIn
            );
            return ServiceResult<ExternalAuthTokenPayload>.Success(payload);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Token response parse failed for provider {Provider}.", provider.Name);
            return ServiceResult<ExternalAuthTokenPayload>.Failure(
                StatusCodes.Status502BadGateway,
                "auth_token_invalid_response",
                "Provider token response could not be parsed."
            );
        }
    }

    private async Task<ServiceResult<ExternalAuthIdentity>> ResolveIdentityAsync(
        string provider,
        string accessToken,
        string? idToken,
        CancellationToken cancellationToken)
    {
        ExternalAuthIdentity? idTokenIdentity = BuildIdentityFromIdToken(provider, idToken);
        if (idTokenIdentity != null)
            return ServiceResult<ExternalAuthIdentity>.Success(idTokenIdentity);

        string userInfoUrl = provider switch
        {
            "google" => "https://openidconnect.googleapis.com/v1/userinfo",
            "microsoft" => "https://graph.microsoft.com/oidc/userinfo",
            "facebook" => QueryHelpers.AddQueryString(
                "https://graph.facebook.com/me",
                new Dictionary<string, string?>
                {
                    ["fields"] = "id,name,email",
                    ["access_token"] = accessToken
                }),
            _ => string.Empty
        };
        if (string.IsNullOrWhiteSpace(userInfoUrl))
        {
            return ServiceResult<ExternalAuthIdentity>.Failure(
                StatusCodes.Status400BadRequest,
                "auth_provider_not_supported",
                $"Provider '{provider}' is not supported."
            );
        }

        using HttpClient client = CreateHttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, userInfoUrl);
        if (!string.Equals(provider, "facebook", StringComparison.Ordinal))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using HttpResponseMessage response = await client.SendAsync(request, cancellationToken);
        string raw = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return ServiceResult<ExternalAuthIdentity>.Failure(
                StatusCodes.Status401Unauthorized,
                "auth_userinfo_failed",
                ExtractProviderError(raw, response.ReasonPhrase)
            );
        }

        try
        {
            using JsonDocument doc = JsonDocument.Parse(raw);
            JsonElement root = doc.RootElement;
            string providerUserId = provider switch
            {
                "google" => GetJsonString(root, "sub"),
                "microsoft" => FirstNonEmpty(
                    GetJsonString(root, "oid"),
                    GetJsonString(root, "sub"),
                    GetJsonString(root, "id")),
                "facebook" => GetJsonString(root, "id"),
                _ => string.Empty
            };
            string email = provider switch
            {
                "google" => GetJsonString(root, "email"),
                "microsoft" => FirstNonEmpty(
                    GetJsonString(root, "email"),
                    GetJsonString(root, "preferred_username")),
                "facebook" => GetJsonString(root, "email"),
                _ => string.Empty
            };
            string displayName = provider switch
            {
                "google" => FirstNonEmpty(GetJsonString(root, "name"), email),
                "microsoft" => FirstNonEmpty(
                    GetJsonString(root, "name"),
                    GetJsonString(root, "preferred_username"),
                    email),
                "facebook" => FirstNonEmpty(GetJsonString(root, "name"), email),
                _ => string.Empty
            };

            if (string.IsNullOrWhiteSpace(providerUserId))
            {
                return ServiceResult<ExternalAuthIdentity>.Failure(
                    StatusCodes.Status401Unauthorized,
                    "auth_missing_provider_user_id",
                    "Provider response did not include user identity."
                );
            }

            return ServiceResult<ExternalAuthIdentity>.Success(
                new ExternalAuthIdentity(
                    ProviderUserId: TrimTo(providerUserId, 128),
                    DisplayName: TrimTo(displayName, 96),
                    Email: TrimTo(email, 128)
                )
            );
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Userinfo parse failed for provider {Provider}.", provider);
            return ServiceResult<ExternalAuthIdentity>.Failure(
                StatusCodes.Status502BadGateway,
                "auth_userinfo_invalid_response",
                "Provider user info response could not be parsed."
            );
        }
    }

    private static ExternalAuthIdentity? BuildIdentityFromIdToken(string provider, string? idToken)
    {
        Dictionary<string, string> claims = ParseIdTokenClaims(idToken);
        if (claims.Count == 0)
            return null;

        string providerUserId = provider switch
        {
            "google" => GetClaim(claims, "sub"),
            "microsoft" => FirstNonEmpty(GetClaim(claims, "oid"), GetClaim(claims, "sub")),
            "facebook" => GetClaim(claims, "sub"),
            _ => GetClaim(claims, "sub")
        };
        if (string.IsNullOrWhiteSpace(providerUserId))
            return null;

        string email = FirstNonEmpty(GetClaim(claims, "email"), GetClaim(claims, "preferred_username"));
        string displayName = FirstNonEmpty(
            GetClaim(claims, "name"),
            GetClaim(claims, "given_name"),
            email
        );
        return new ExternalAuthIdentity(
            ProviderUserId: TrimTo(providerUserId, 128),
            DisplayName: TrimTo(displayName, 96),
            Email: TrimTo(email, 128)
        );
    }

    private static Dictionary<string, string> ParseIdTokenClaims(string? idToken)
    {
        var claims = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(idToken))
            return claims;

        string[] parts = idToken.Split('.');
        if (parts.Length < 2)
            return claims;

        try
        {
            byte[] payloadBytes = Base64UrlTextEncoder.Decode(parts[1]);
            using JsonDocument payload = JsonDocument.Parse(payloadBytes);
            foreach (JsonProperty property in payload.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.String)
                {
                    claims[property.Name] = property.Value.GetString() ?? string.Empty;
                }
                else if (property.Value.ValueKind is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False)
                {
                    claims[property.Name] = property.Value.ToString();
                }
            }
        }
        catch
        {
            return claims;
        }

        return claims;
    }

    private static string GetClaim(Dictionary<string, string> claims, string key)
    {
        return claims.TryGetValue(key, out string? value) ? value : string.Empty;
    }

    private static string GetJsonString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement property))
            return string.Empty;
        return property.ValueKind == JsonValueKind.String ? property.GetString() ?? string.Empty : property.ToString();
    }

    private static int GetJsonInt(JsonElement element, string propertyName, int fallback)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement property))
            return fallback;
        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out int parsed))
            return parsed;
        if (property.ValueKind == JsonValueKind.String
            && int.TryParse(property.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
        {
            return parsed;
        }

        return fallback;
    }

    private static string ExtractProviderError(string? responseBody, string? fallback)
    {
        if (!string.IsNullOrWhiteSpace(responseBody))
        {
            try
            {
                using JsonDocument errorDoc = JsonDocument.Parse(responseBody);
                JsonElement root = errorDoc.RootElement;
                string error = GetJsonString(root, "error");
                string description = FirstNonEmpty(
                    GetJsonString(root, "error_description"),
                    GetJsonString(root, "error_message"),
                    GetJsonString(root, "message")
                );
                string combined = string.IsNullOrWhiteSpace(description)
                    ? error
                    : $"{error}: {description}";
                if (!string.IsNullOrWhiteSpace(combined))
                    return TrimTo(combined, 240);
            }
            catch
            {
            }

            return TrimTo(responseBody.Replace('\n', ' ').Replace('\r', ' '), 240);
        }

        return string.IsNullOrWhiteSpace(fallback) ? "provider_error" : fallback;
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (string? value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return string.Empty;
    }
}

internal sealed record ExternalAuthTokenPayload(
    string AccessToken,
    string RefreshToken,
    string IdToken,
    int ExpiresIn
);

internal sealed record ExternalAuthIdentity(
    string ProviderUserId,
    string DisplayName,
    string Email
);
