using System.Globalization;
using UnityEngine;

public static class OnlineLeaderboardRuntimeDiagnostics
{
    public static void LogRequestFailure(
        string operation,
        int attemptNumber,
        int maxAttempts,
        long responseCode,
        string error,
        float durationSeconds,
        bool willRetry
    )
    {
        string codeLabel = responseCode > 0
            ? responseCode.ToString(CultureInfo.InvariantCulture)
            : "n/a";
        string action = willRetry ? "retrying" : "giving_up";
        string durationMs = (durationSeconds * 1000f).ToString("0", CultureInfo.InvariantCulture);
        Debug.LogWarning(
            $"[Leaderboard] Request failed. op={Sanitize(operation)} attempt={attemptNumber}/{maxAttempts} "
            + $"code={codeLabel} duration_ms={durationMs} action={action} error={Sanitize(error)}"
        );
    }

    public static void LogRetryScheduled(
        string operation,
        int nextAttemptNumber,
        float delaySeconds,
        float retryBudgetSeconds
    )
    {
        Debug.LogWarning(
            $"[Leaderboard] Scheduling retry. op={Sanitize(operation)} next_attempt={nextAttemptNumber} "
            + $"delay_sec={delaySeconds.ToString("0.##", CultureInfo.InvariantCulture)} "
            + $"budget_sec={retryBudgetSeconds.ToString("0.##", CultureInfo.InvariantCulture)}"
        );
    }

    public static void LogGracefulDegradation(string context, string fallbackMode, string reason)
    {
        Debug.LogWarning(
            $"[Leaderboard] Graceful degradation active. context={Sanitize(context)} "
            + $"fallback={Sanitize(fallbackMode)} reason={Sanitize(reason)}"
        );
    }

    public static void LogSubmitValidation(string validationState, string validationReason)
    {
        if (string.IsNullOrWhiteSpace(validationState)
            || string.Equals(validationState, "accepted", System.StringComparison.Ordinal))
        {
            return;
        }

        Debug.LogWarning(
            $"[Leaderboard] Submit flagged. state={Sanitize(validationState)} "
            + $"reason={Sanitize(validationReason)}"
        );
    }

    private static string Sanitize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "unknown";

        const int maxLength = 160;
        string trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed.Substring(0, maxLength);
    }
}
