using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using GrassSim.Auth;
using UnityEngine;
using UnityEngine.Networking;

public static class OnlineLeaderboardApiClient
{
    [Serializable]
    private sealed class StartRunRequestDto
    {
        public string player_id;
        public string display_name;
        public string season;
        public string build_version;
        public string account_id;
        public string account_provider;
        public string account_provider_user_id;
    }

    [Serializable]
    private sealed class StartRunResponseDto
    {
        public string run_id;
        public string nonce;
        public string session_key;
    }

    [Serializable]
    private sealed class SubmitRunRequestDto
    {
        public string run_id;
        public string player_id;
        public string display_name;
        public int score;
        public float run_duration_sec;
        public int kills;
        public string build_version;
        public bool is_cheat_session;
        public string event_chain;
        public string event_chain_hash;
        public int event_count;
        public string signature;
        public string account_id;
        public string account_provider;
        public string account_provider_user_id;
    }

    [Serializable]
    private sealed class SubmitRunResponseDto
    {
        public string validation_state;
        public string validation_reason;
    }

    [Serializable]
    private sealed class LeaderboardResponseDto
    {
        public LeaderboardEntryDto[] entries;
    }

    [Serializable]
    private sealed class MyRankResponseDto
    {
        public bool found;
        public LeaderboardEntryDto entry;
    }

    [Serializable]
    private sealed class LeaderboardEntryDto
    {
        public int rank;
        public string player_id;
        public string display_name;
        public int score;
        public float run_duration_sec;
        public int kills;
        public string created_at_utc;
    }

    private readonly struct RequestPolicy
    {
        public readonly int maxRetries;
        public readonly float retryBudgetSeconds;
        public readonly float retryBackoffSeconds;

        public RequestPolicy(int maxRetries, float retryBudgetSeconds, float retryBackoffSeconds)
        {
            this.maxRetries = maxRetries;
            this.retryBudgetSeconds = retryBudgetSeconds;
            this.retryBackoffSeconds = retryBackoffSeconds;
        }
    }

    private sealed class HttpAttemptResult
    {
        public bool success;
        public string text;
        public string error;
        public long responseCode;
        public float durationSeconds;
    }

    public sealed class LeaderboardEntry
    {
        public int rank;
        public string playerId;
        public string displayName;
        public int score;
        public float runDurationSec;
        public int kills;
        public string createdAtUtc;
    }

    public sealed class FetchTopResult
    {
        public bool success;
        public bool isStale;
        public string error;
        public List<LeaderboardEntry> entries = new();
    }

    public sealed class SubmitRunResult
    {
        public bool success;
        public string validationState;
        public string validationReason;
        public string error;
    }

    public sealed class FetchMyRankResult
    {
        public bool success;
        public bool isStale;
        public bool found;
        public string error;
        public LeaderboardEntry entry;
    }

    public static IEnumerator FetchTopEntries(int count, Action<FetchTopResult> onComplete)
    {
        FetchTopResult result = new();
        if (!OnlineLeaderboardSettings.IsOnlineEnabled)
        {
            result.success = false;
            result.error = "online_disabled";
            onComplete?.Invoke(result);
            yield break;
        }

        int pageSize = Mathf.Clamp(count, 1, 50);
        string season = OnlineLeaderboardSettings.GetSeason();
        string url = $"{OnlineLeaderboardSettings.GetBaseUrl()}/leaderboard?season={season}&page=1&page_size={pageSize}";

        yield return SendGet(
            operation: "fetch_top",
            url: url,
            retryCount: OnlineLeaderboardSettings.GetReadRetryCount(),
            onSuccess: json =>
            {
                LeaderboardResponseDto payload = JsonUtility.FromJson<LeaderboardResponseDto>(json);
                if (payload?.entries == null)
                {
                    result.success = false;
                    result.error = "invalid_payload";
                    return;
                }

                result.success = true;
                for (int i = 0; i < payload.entries.Length; i++)
                    result.entries.Add(ToEntry(payload.entries[i]));

                OnlineLeaderboardCache.StoreTopEntries(season, result.entries);
            },
            onError: error =>
            {
                result.success = false;
                result.error = error;
            }
        );

        if (!result.success
            && OnlineLeaderboardCache.TryGetTopEntries(season, out List<LeaderboardEntry> cachedEntries, out _))
        {
            result.success = true;
            result.isStale = true;
            result.entries = cachedEntries ?? new List<LeaderboardEntry>();
            OnlineLeaderboardRuntimeDiagnostics.LogGracefulDegradation(
                context: "fetch_top",
                fallbackMode: "last_synced_leaderboard",
                reason: result.error
            );
        }

        onComplete?.Invoke(result);
    }

    public static IEnumerator FetchMyRank(Action<FetchMyRankResult> onComplete)
    {
        FetchMyRankResult result = new();
        if (!OnlineLeaderboardSettings.IsOnlineEnabled)
        {
            result.success = false;
            result.error = "online_disabled";
            onComplete?.Invoke(result);
            yield break;
        }

        string season = OnlineLeaderboardSettings.GetSeason();
        string playerId = PlayerIdentityService.GetPlayerId();
        string escapedSeason = UnityWebRequest.EscapeURL(season);
        string escapedPlayerId = UnityWebRequest.EscapeURL(playerId);
        string url = $"{OnlineLeaderboardSettings.GetBaseUrl()}/leaderboard/me?season={escapedSeason}&player_id={escapedPlayerId}";

        yield return SendGet(
            operation: "fetch_my_rank",
            url: url,
            retryCount: OnlineLeaderboardSettings.GetReadRetryCount(),
            onSuccess: json =>
            {
                MyRankResponseDto payload = JsonUtility.FromJson<MyRankResponseDto>(json);
                if (payload == null)
                {
                    result.success = false;
                    result.error = "invalid_payload";
                    return;
                }

                result.success = true;
                result.found = payload.found;
                if (payload.found && payload.entry != null)
                    result.entry = ToEntry(payload.entry);

                OnlineLeaderboardCache.StoreMyRank(season, playerId, result.found, result.entry);
            },
            onError: error =>
            {
                result.success = false;
                result.error = error;
            }
        );

        if (!result.success
            && OnlineLeaderboardCache.TryGetMyRank(
                season,
                playerId,
                out bool cachedFound,
                out LeaderboardEntry cachedEntry,
                out _))
        {
            result.success = true;
            result.isStale = true;
            result.found = cachedFound;
            result.entry = cachedEntry;
            OnlineLeaderboardRuntimeDiagnostics.LogGracefulDegradation(
                context: "fetch_my_rank",
                fallbackMode: "last_synced_rank",
                reason: result.error
            );
        }

        onComplete?.Invoke(result);
    }

    public static IEnumerator SubmitRun(GameRunStats stats, Action<SubmitRunResult> onComplete)
    {
        SubmitRunResult result = new();
        if (!OnlineLeaderboardSettings.IsOnlineEnabled)
        {
            result.success = false;
            result.error = "online_disabled";
            onComplete?.Invoke(result);
            yield break;
        }

        string playerId = PlayerIdentityService.GetPlayerId();
        string displayName = PlayerIdentityService.GetDisplayName();
        string buildVersion = OnlineLeaderboardSettings.GetBuildVersionForLeaderboard();
        bool isCheatSession = GameSettings.GodMode;

        string accountId = string.Empty;
        string accountProvider = string.Empty;
        string accountProviderUserId = string.Empty;
        if (ExternalAuthSessionStore.TryGetActiveSession(out ExternalAuthSession authSession))
        {
            accountId = authSession.account_id ?? string.Empty;
            accountProvider = authSession.provider ?? string.Empty;
            accountProviderUserId = authSession.provider_user_id ?? string.Empty;
        }

        StartRunResponseDto startResponse = null;
        StartRunRequestDto startRequest = new()
        {
            player_id = playerId,
            display_name = displayName,
            season = OnlineLeaderboardSettings.GetSeason(),
            build_version = buildVersion,
            account_id = accountId,
            account_provider = accountProvider,
            account_provider_user_id = accountProviderUserId
        };

        yield return SendPostJson(
            operation: "start_run",
            url: $"{OnlineLeaderboardSettings.GetBaseUrl()}/runs/start",
            json: JsonUtility.ToJson(startRequest),
            retryCount: OnlineLeaderboardSettings.GetReadRetryCount(),
            onSuccess: json => { startResponse = JsonUtility.FromJson<StartRunResponseDto>(json); },
            onError: error =>
            {
                result.success = false;
                result.error = error;
            }
        );

        if (startResponse == null
            || string.IsNullOrWhiteSpace(startResponse.run_id)
            || string.IsNullOrWhiteSpace(startResponse.session_key))
        {
            if (string.IsNullOrWhiteSpace(result.error))
                result.error = "start_failed";
            onComplete?.Invoke(result);
            yield break;
        }

        RunEventHashChain.Payload chainPayload = RunEventHashChain.BuildPayload(
            startResponse.run_id,
            startResponse.nonce,
            stats
        );

        string canonical = BuildCanonicalPayload(
            runId: startResponse.run_id,
            playerId: playerId,
            score: stats.finalScore,
            runDurationSec: stats.timeSurvived,
            kills: stats.kills,
            buildVersion: buildVersion,
            isCheatSession: isCheatSession,
            eventChainHash: chainPayload.eventChainHash,
            eventCount: chainPayload.eventCount
        );
        string signature = ComputeSignature(startResponse.session_key, canonical);

        SubmitRunResponseDto submitResponse = null;
        SubmitRunRequestDto submitRequest = new()
        {
            run_id = startResponse.run_id,
            player_id = playerId,
            display_name = displayName,
            score = Mathf.Max(0, stats.finalScore),
            run_duration_sec = Mathf.Max(0f, stats.timeSurvived),
            kills = Mathf.Max(0, stats.kills),
            build_version = buildVersion,
            is_cheat_session = isCheatSession,
            event_chain = chainPayload.eventChain,
            event_chain_hash = chainPayload.eventChainHash,
            event_count = chainPayload.eventCount,
            signature = signature,
            account_id = accountId,
            account_provider = accountProvider,
            account_provider_user_id = accountProviderUserId
        };

        yield return SendPostJson(
            operation: "submit_run",
            url: $"{OnlineLeaderboardSettings.GetBaseUrl()}/runs/submit",
            json: JsonUtility.ToJson(submitRequest),
            retryCount: OnlineLeaderboardSettings.GetSubmitRetryCount(),
            onSuccess: json => { submitResponse = JsonUtility.FromJson<SubmitRunResponseDto>(json); },
            onError: error =>
            {
                result.success = false;
                result.error = error;
            }
        );

        if (submitResponse == null)
        {
            if (string.IsNullOrWhiteSpace(result.error))
                result.error = "submit_failed";
            onComplete?.Invoke(result);
            yield break;
        }

        result.success = true;
        result.validationState = submitResponse.validation_state;
        result.validationReason = submitResponse.validation_reason;
        OnlineLeaderboardRuntimeDiagnostics.LogSubmitValidation(result.validationState, result.validationReason);
        onComplete?.Invoke(result);
    }

    private static IEnumerator SendGet(
        string operation,
        string url,
        int retryCount,
        Action<string> onSuccess,
        Action<string> onError
    )
    {
        yield return SendWithPolicy(
            operation,
            retryCount,
            () =>
            {
                UnityWebRequest request = UnityWebRequest.Get(url);
                request.timeout = Mathf.CeilToInt(OnlineLeaderboardSettings.GetTimeoutSeconds());
                return request;
            },
            onSuccess,
            onError
        );
    }

    private static IEnumerator SendPostJson(
        string operation,
        string url,
        string json,
        int retryCount,
        Action<string> onSuccess,
        Action<string> onError
    )
    {
        byte[] body = Encoding.UTF8.GetBytes(json);
        yield return SendWithPolicy(
            operation,
            retryCount,
            () =>
            {
                UnityWebRequest request = new(url, UnityWebRequest.kHttpVerbPOST);
                request.uploadHandler = new UploadHandlerRaw(body);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.timeout = Mathf.CeilToInt(OnlineLeaderboardSettings.GetTimeoutSeconds());
                request.SetRequestHeader("Content-Type", "application/json");
                return request;
            },
            onSuccess,
            onError
        );
    }

    private static IEnumerator SendWithPolicy(
        string operation,
        int retryCount,
        Func<UnityWebRequest> requestFactory,
        Action<string> onSuccess,
        Action<string> onError
    )
    {
        RequestPolicy policy = BuildRequestPolicy(retryCount);
        int maxAttempts = Mathf.Max(1, policy.maxRetries + 1);
        float operationStartedAt = Time.realtimeSinceStartup;

        for (int attemptNumber = 1; attemptNumber <= maxAttempts; attemptNumber++)
        {
            HttpAttemptResult attemptResult = null;
            yield return SendSingleRequest(requestFactory, outcome => { attemptResult = outcome; });

            if (attemptResult != null && attemptResult.success)
            {
                onSuccess?.Invoke(attemptResult.text);
                yield break;
            }

            string attemptError = attemptResult?.error ?? "request_failed";
            long responseCode = attemptResult?.responseCode ?? 0L;
            float durationSeconds = attemptResult?.durationSeconds ?? 0f;
            float elapsedSeconds = Time.realtimeSinceStartup - operationStartedAt;
            bool willRetry = OnlineLeaderboardRetryPolicy.ShouldRetry(
                attemptNumber,
                maxAttempts,
                elapsedSeconds,
                policy.retryBudgetSeconds,
                responseCode,
                attemptError
            );

            OnlineLeaderboardRuntimeDiagnostics.LogRequestFailure(
                operation,
                attemptNumber,
                maxAttempts,
                responseCode,
                attemptError,
                durationSeconds,
                willRetry
            );

            if (!willRetry)
            {
                onError?.Invoke(attemptError);
                yield break;
            }

            float delaySeconds = OnlineLeaderboardRetryPolicy.GetRetryDelaySeconds(
                attemptNumber,
                policy.retryBackoffSeconds,
                elapsedSeconds,
                policy.retryBudgetSeconds
            );
            if (delaySeconds <= 0f)
            {
                onError?.Invoke(attemptError);
                yield break;
            }

            OnlineLeaderboardRuntimeDiagnostics.LogRetryScheduled(
                operation,
                attemptNumber + 1,
                delaySeconds,
                policy.retryBudgetSeconds
            );

            yield return new WaitForSecondsRealtime(delaySeconds);
        }

        onError?.Invoke("retry_budget_exhausted");
    }

    private static IEnumerator SendSingleRequest(
        Func<UnityWebRequest> requestFactory,
        Action<HttpAttemptResult> onComplete
    )
    {
        HttpAttemptResult result = new();
        float startedAt = Time.realtimeSinceStartup;

        using UnityWebRequest request = requestFactory();
        yield return request.SendWebRequest();

        result.durationSeconds = Time.realtimeSinceStartup - startedAt;
        result.responseCode = request.responseCode;

        if (request.result != UnityWebRequest.Result.Success)
        {
            result.success = false;
            result.error = BuildRequestError(request);
            onComplete?.Invoke(result);
            yield break;
        }

        result.success = true;
        result.text = request.downloadHandler?.text ?? string.Empty;
        onComplete?.Invoke(result);
    }

    private static RequestPolicy BuildRequestPolicy(int retryCount)
    {
        return new RequestPolicy(
            maxRetries: Mathf.Max(0, retryCount),
            retryBudgetSeconds: OnlineLeaderboardSettings.GetRetryBudgetSeconds(),
            retryBackoffSeconds: OnlineLeaderboardSettings.GetRetryBackoffSeconds()
        );
    }

    private static string BuildRequestError(UnityWebRequest request)
    {
        string rawError = string.IsNullOrWhiteSpace(request.error)
            ? request.result.ToString()
            : request.error;
        return $"http_error:{request.responseCode}:{rawError}";
    }

    private static string BuildCanonicalPayload(
        string runId,
        string playerId,
        int score,
        float runDurationSec,
        int kills,
        string buildVersion,
        bool isCheatSession,
        string eventChainHash,
        int eventCount
    )
    {
        string duration = Mathf.Max(0f, runDurationSec).ToString("0.###", CultureInfo.InvariantCulture);
        string cheat = isCheatSession ? "1" : "0";
        int safeEventCount = Mathf.Max(0, eventCount);
        string safeEventChainHash = string.IsNullOrWhiteSpace(eventChainHash) ? "none" : eventChainHash.Trim();
        return $"{runId}|{playerId}|{score}|{duration}|{kills}|{buildVersion}|{cheat}|{safeEventCount}|{safeEventChainHash}";
    }

    private static string ComputeSignature(string sessionKey, string canonicalPayload)
    {
        byte[] keyBytes = Encoding.UTF8.GetBytes(sessionKey);
        byte[] payloadBytes = Encoding.UTF8.GetBytes(canonicalPayload);
        using HMACSHA256 hmac = new(keyBytes);
        byte[] hash = hmac.ComputeHash(payloadBytes);
        return Convert.ToBase64String(hash);
    }

    private static LeaderboardEntry ToEntry(LeaderboardEntryDto src)
    {
        return new LeaderboardEntry
        {
            rank = src.rank,
            playerId = src.player_id,
            displayName = src.display_name,
            score = src.score,
            runDurationSec = src.run_duration_sec,
            kills = src.kills,
            createdAtUtc = src.created_at_utc
        };
    }
}
