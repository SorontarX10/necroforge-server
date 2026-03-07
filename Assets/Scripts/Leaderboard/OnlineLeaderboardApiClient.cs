using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
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
        public string signature;
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
        string url = $"{OnlineLeaderboardSettings.GetBaseUrl()}/leaderboard?season={OnlineLeaderboardSettings.GetSeason()}&page=1&page_size={pageSize}";
        yield return SendGet(url, json =>
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
            {
                LeaderboardEntryDto src = payload.entries[i];
                result.entries.Add(ToEntry(src));
            }
        }, error =>
        {
            result.success = false;
            result.error = error;
        });

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

        string escapedSeason = UnityWebRequest.EscapeURL(OnlineLeaderboardSettings.GetSeason());
        string escapedPlayerId = UnityWebRequest.EscapeURL(PlayerIdentityService.GetPlayerId());
        string url = $"{OnlineLeaderboardSettings.GetBaseUrl()}/leaderboard/me?season={escapedSeason}&player_id={escapedPlayerId}";
        yield return SendGet(url, json =>
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
        }, error =>
        {
            result.success = false;
            result.error = error;
        });

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
        string buildVersion = Application.version;
        bool isCheatSession = GameSettings.GodMode;

        StartRunResponseDto startResponse = null;
        StartRunRequestDto startRequest = new()
        {
            player_id = playerId,
            display_name = displayName,
            season = OnlineLeaderboardSettings.GetSeason(),
            build_version = buildVersion
        };

        yield return SendPostJson(
            $"{OnlineLeaderboardSettings.GetBaseUrl()}/runs/start",
            JsonUtility.ToJson(startRequest),
            json => { startResponse = JsonUtility.FromJson<StartRunResponseDto>(json); },
            error =>
            {
                result.success = false;
                result.error = error;
            }
        );

        if (startResponse == null || string.IsNullOrWhiteSpace(startResponse.run_id) || string.IsNullOrWhiteSpace(startResponse.session_key))
        {
            if (string.IsNullOrWhiteSpace(result.error))
                result.error = "start_failed";
            onComplete?.Invoke(result);
            yield break;
        }

        string canonical = BuildCanonicalPayload(
            runId: startResponse.run_id,
            playerId: playerId,
            score: stats.finalScore,
            runDurationSec: stats.timeSurvived,
            kills: stats.kills,
            buildVersion: buildVersion,
            isCheatSession: isCheatSession
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
            signature = signature
        };

        yield return SendPostJson(
            $"{OnlineLeaderboardSettings.GetBaseUrl()}/runs/submit",
            JsonUtility.ToJson(submitRequest),
            json => { submitResponse = JsonUtility.FromJson<SubmitRunResponseDto>(json); },
            error =>
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
        onComplete?.Invoke(result);
    }

    private static IEnumerator SendGet(string url, Action<string> onSuccess, Action<string> onError)
    {
        using UnityWebRequest request = UnityWebRequest.Get(url);
        request.timeout = Mathf.CeilToInt(OnlineLeaderboardSettings.GetTimeoutSeconds());
        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            onError?.Invoke($"http_error:{request.responseCode}:{request.error}");
            yield break;
        }

        onSuccess?.Invoke(request.downloadHandler.text);
    }

    private static IEnumerator SendPostJson(string url, string json, Action<string> onSuccess, Action<string> onError)
    {
        byte[] body = Encoding.UTF8.GetBytes(json);
        using UnityWebRequest request = new(url, UnityWebRequest.kHttpVerbPOST);
        request.uploadHandler = new UploadHandlerRaw(body);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.timeout = Mathf.CeilToInt(OnlineLeaderboardSettings.GetTimeoutSeconds());
        request.SetRequestHeader("Content-Type", "application/json");
        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            onError?.Invoke($"http_error:{request.responseCode}:{request.error}");
            yield break;
        }

        onSuccess?.Invoke(request.downloadHandler.text);
    }

    private static string BuildCanonicalPayload(
        string runId,
        string playerId,
        int score,
        float runDurationSec,
        int kills,
        string buildVersion,
        bool isCheatSession
    )
    {
        string duration = Mathf.Max(0f, runDurationSec).ToString("0.###", CultureInfo.InvariantCulture);
        string cheat = isCheatSession ? "1" : "0";
        return $"{runId}|{playerId}|{score}|{duration}|{kills}|{buildVersion}|{cheat}";
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
