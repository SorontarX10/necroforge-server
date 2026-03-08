using System;
using UnityEngine;

public static class OnlineLeaderboardRetryPolicy
{
    public static bool ShouldRetry(
        int attemptNumber,
        int maxAttempts,
        float elapsedSeconds,
        float retryBudgetSeconds,
        long responseCode,
        string error
    )
    {
        if (attemptNumber >= maxAttempts)
            return false;

        if (retryBudgetSeconds <= 0f || elapsedSeconds >= retryBudgetSeconds)
            return false;

        if (responseCode == 408 || responseCode == 429 || responseCode >= 500)
            return true;

        string normalized = (error ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
            return responseCode <= 0;

        return normalized.Contains("timeout", StringComparison.Ordinal)
            || normalized.Contains("timed out", StringComparison.Ordinal)
            || normalized.Contains("network", StringComparison.Ordinal)
            || normalized.Contains("connection", StringComparison.Ordinal)
            || normalized.Contains("resolve host", StringComparison.Ordinal)
            || normalized.Contains("cannot resolve", StringComparison.Ordinal)
            || normalized.Contains("no internet", StringComparison.Ordinal)
            || responseCode <= 0;
    }

    public static float GetRetryDelaySeconds(
        int retryNumber,
        float baseDelaySeconds,
        float elapsedSeconds,
        float retryBudgetSeconds
    )
    {
        float remainingBudget = Mathf.Max(0f, retryBudgetSeconds - elapsedSeconds);
        if (remainingBudget <= 0f)
            return 0f;

        float clampedBaseDelay = Mathf.Clamp(baseDelaySeconds, 0.05f, 2f);
        int exponent = Mathf.Clamp(retryNumber - 1, 0, 4);
        float proposedDelay = clampedBaseDelay * Mathf.Pow(2f, exponent);
        return Mathf.Min(proposedDelay, remainingBudget);
    }
}
