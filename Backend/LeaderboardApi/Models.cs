using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;

namespace LeaderboardApi;

public sealed class StartRunRequest
{
    [JsonPropertyName("player_id")]
    public string? PlayerId { get; init; }

    [JsonPropertyName("display_name")]
    public string? DisplayName { get; init; }

    [JsonPropertyName("season")]
    public string? Season { get; init; }

    [JsonPropertyName("build_version")]
    public string? BuildVersion { get; init; }

    [JsonPropertyName("account_id")]
    public string? AccountId { get; init; }

    [JsonPropertyName("account_provider")]
    public string? AccountProvider { get; init; }

    [JsonPropertyName("account_provider_user_id")]
    public string? AccountProviderUserId { get; init; }
}

public sealed class SubmitRunRequest
{
    [JsonPropertyName("run_id")]
    public string? RunId { get; init; }

    [JsonPropertyName("player_id")]
    public string? PlayerId { get; init; }

    [JsonPropertyName("display_name")]
    public string? DisplayName { get; init; }

    [JsonPropertyName("score")]
    public int Score { get; init; }

    [JsonPropertyName("run_duration_sec")]
    public float RunDurationSec { get; init; }

    [JsonPropertyName("kills")]
    public int Kills { get; init; }

    [JsonPropertyName("build_version")]
    public string? BuildVersion { get; init; }

    [JsonPropertyName("is_cheat_session")]
    public bool IsCheatSession { get; init; }

    [JsonPropertyName("event_chain")]
    public string? EventChain { get; init; }

    [JsonPropertyName("event_chain_hash")]
    public string? EventChainHash { get; init; }

    [JsonPropertyName("event_count")]
    public int EventCount { get; init; }

    [JsonPropertyName("signature")]
    public string? Signature { get; init; }

    [JsonPropertyName("account_id")]
    public string? AccountId { get; init; }

    [JsonPropertyName("account_provider")]
    public string? AccountProvider { get; init; }

    [JsonPropertyName("account_provider_user_id")]
    public string? AccountProviderUserId { get; init; }
}

public sealed class GetLeaderboardQuery
{
    [FromQuery(Name = "season")]
    public string? Season { get; init; }

    [FromQuery(Name = "page")]
    public int? Page { get; init; }

    [FromQuery(Name = "page_size")]
    public int? PageSize { get; init; }
}

public sealed class GetMyRankQuery
{
    [FromQuery(Name = "season")]
    public string? Season { get; init; }

    [FromQuery(Name = "player_id")]
    public string? PlayerId { get; init; }
}

public sealed class AdminGetFlaggedRunsQuery
{
    [FromQuery(Name = "state")]
    public string? State { get; init; }

    [FromQuery(Name = "page")]
    public int? Page { get; init; }

    [FromQuery(Name = "page_size")]
    public int? PageSize { get; init; }
}

public sealed class AdminReviewRunRequest
{
    [JsonPropertyName("run_id")]
    public string? RunId { get; init; }

    [JsonPropertyName("action")]
    public string? Action { get; init; }

    [JsonPropertyName("note")]
    public string? Note { get; init; }
}

public sealed class ExchangeExternalAuthCodeRequest
{
    [JsonPropertyName("provider")]
    public string? Provider { get; init; }

    [JsonPropertyName("code")]
    public string? Code { get; init; }
}

public sealed class RefreshExternalAuthSessionRequest
{
    [JsonPropertyName("account_id")]
    public string? AccountId { get; init; }

    [JsonPropertyName("provider")]
    public string? Provider { get; init; }

    [JsonPropertyName("provider_user_id")]
    public string? ProviderUserId { get; init; }

    [JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; init; }
}

public sealed class LogoutExternalAuthSessionRequest
{
    [JsonPropertyName("account_id")]
    public string? AccountId { get; init; }

    [JsonPropertyName("provider")]
    public string? Provider { get; init; }

    [JsonPropertyName("provider_user_id")]
    public string? ProviderUserId { get; init; }

    [JsonPropertyName("access_token")]
    public string? AccessToken { get; init; }
}

public sealed class ExchangeSteamExternalAuthRequest
{
    [JsonPropertyName("steam_id")]
    public string? SteamId { get; init; }

    [JsonPropertyName("session_ticket")]
    public string? SessionTicket { get; init; }

    [JsonPropertyName("display_name")]
    public string? DisplayName { get; init; }
}

public sealed record StartExternalAuthFlowResponse(
    [property: JsonPropertyName("provider")] string Provider,
    [property: JsonPropertyName("flow_id")] string FlowId,
    [property: JsonPropertyName("auth_url")] string AuthUrl,
    [property: JsonPropertyName("expires_at_unix")] long ExpiresAtUnix
);

public sealed record ExternalAuthFlowStatusResponse(
    [property: JsonPropertyName("flow_id")] string FlowId,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("message")] string Message
);

public sealed record StartRunResponse(
    [property: JsonPropertyName("run_id")] string RunId,
    [property: JsonPropertyName("nonce")] string Nonce,
    [property: JsonPropertyName("session_key")] string SessionKey,
    [property: JsonPropertyName("expires_at_utc")] DateTimeOffset ExpiresAtUtc
);

public sealed record SubmitRunResponse(
    [property: JsonPropertyName("run_id")] string RunId,
    [property: JsonPropertyName("validation_state")] string ValidationState,
    [property: JsonPropertyName("validation_reason")] string ValidationReason
);

public sealed record LeaderboardEntryResponse(
    [property: JsonPropertyName("rank")] int Rank,
    [property: JsonPropertyName("player_id")] string PlayerId,
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("score")] int Score,
    [property: JsonPropertyName("run_duration_sec")] float RunDurationSec,
    [property: JsonPropertyName("kills")] int Kills,
    [property: JsonPropertyName("build_version")] string BuildVersion,
    [property: JsonPropertyName("created_at_utc")] DateTime CreatedAtUtc
);

public sealed record GetLeaderboardResponse(
    [property: JsonPropertyName("season")] string Season,
    [property: JsonPropertyName("page")] int Page,
    [property: JsonPropertyName("page_size")] int PageSize,
    [property: JsonPropertyName("total_count")] int TotalCount,
    [property: JsonPropertyName("entries")] IReadOnlyList<LeaderboardEntryResponse> Entries
);

public sealed record GetMyRankResponse(
    [property: JsonPropertyName("season")] string Season,
    [property: JsonPropertyName("found")] bool Found,
    [property: JsonPropertyName("entry")] LeaderboardEntryResponse? Entry
);

public sealed record AdminFlaggedRunResponse(
    [property: JsonPropertyName("run_id")] string RunId,
    [property: JsonPropertyName("player_id")] string PlayerId,
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("season")] string Season,
    [property: JsonPropertyName("score")] int Score,
    [property: JsonPropertyName("run_duration_sec")] float RunDurationSec,
    [property: JsonPropertyName("kills")] int Kills,
    [property: JsonPropertyName("build_version")] string BuildVersion,
    [property: JsonPropertyName("validation_state")] string ValidationState,
    [property: JsonPropertyName("validation_reason")] string ValidationReason,
    [property: JsonPropertyName("submitted_at_utc")] DateTime SubmittedAtUtc
);

public sealed record AdminGetFlaggedRunsResponse(
    [property: JsonPropertyName("page")] int Page,
    [property: JsonPropertyName("page_size")] int PageSize,
    [property: JsonPropertyName("total_count")] int TotalCount,
    [property: JsonPropertyName("entries")] IReadOnlyList<AdminFlaggedRunResponse> Entries
);

public sealed record AdminReviewRunResponse(
    [property: JsonPropertyName("run_id")] string RunId,
    [property: JsonPropertyName("validation_state")] string ValidationState,
    [property: JsonPropertyName("validation_reason")] string ValidationReason
);

public sealed record ExternalAuthSessionResponse(
    [property: JsonPropertyName("account_id")] string AccountId,
    [property: JsonPropertyName("provider")] string Provider,
    [property: JsonPropertyName("provider_user_id")] string ProviderUserId,
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("refresh_token")] string RefreshToken,
    [property: JsonPropertyName("expires_at_unix")] long ExpiresAtUnix
);

public sealed record ErrorResponse(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("message")] string Message
);

public sealed class ServiceResult<T>
{
    public bool Ok { get; }
    public int StatusCode { get; }
    public T? Payload { get; }
    public ErrorResponse? Error { get; }

    private ServiceResult(bool ok, int statusCode, T? payload, ErrorResponse? error)
    {
        Ok = ok;
        StatusCode = statusCode;
        Payload = payload;
        Error = error;
    }

    public static ServiceResult<T> Success(T payload, int statusCode = StatusCodes.Status200OK)
        => new(true, statusCode, payload, null);

    public static ServiceResult<T> Failure(int statusCode, string code, string message)
        => new(false, statusCode, default, new ErrorResponse(code, message));
}

public static class ServiceResultExtensions
{
    public static IResult ToIResult<T>(this ServiceResult<T> result)
    {
        if (result.Ok && result.Payload != null)
            return Results.Json(result.Payload, statusCode: result.StatusCode);

        ErrorResponse error = result.Error ?? new ErrorResponse("unknown_error", "Unknown error.");
        return Results.Json(error, statusCode: result.StatusCode);
    }
}
