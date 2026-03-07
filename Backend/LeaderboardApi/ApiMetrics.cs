namespace LeaderboardApi;

public sealed class ApiMetrics
{
    private long totalRequests;
    private long failedRequests;
    private long runStartAttempts;
    private long runSubmitAttempts;
    private long acceptedSubmits;
    private long shadowBannedSubmits;
    private long rejectedSubmits;
    private long manualReviewSubmits;
    private long totalLatencyMsScaled;

    public void RecordRequest(int statusCode, double elapsedMs)
    {
        Interlocked.Increment(ref totalRequests);
        if (statusCode >= 400)
            Interlocked.Increment(ref failedRequests);

        long scaled = (long)Math.Round(Math.Max(0d, elapsedMs) * 1000d, MidpointRounding.AwayFromZero);
        Interlocked.Add(ref totalLatencyMsScaled, scaled);
    }

    public void RecordRunStartAttempt()
    {
        Interlocked.Increment(ref runStartAttempts);
    }

    public void RecordRunSubmitAttempt()
    {
        Interlocked.Increment(ref runSubmitAttempts);
    }

    public void RecordValidationState(string validationState)
    {
        switch (validationState)
        {
            case "accepted":
                Interlocked.Increment(ref acceptedSubmits);
                break;
            case "shadow_banned":
                Interlocked.Increment(ref shadowBannedSubmits);
                break;
            case "rejected":
                Interlocked.Increment(ref rejectedSubmits);
                break;
            case "manual_review":
                Interlocked.Increment(ref manualReviewSubmits);
                break;
        }
    }

    public object GetSnapshot()
    {
        long requests = Interlocked.Read(ref totalRequests);
        long failures = Interlocked.Read(ref failedRequests);
        long latencyScaled = Interlocked.Read(ref totalLatencyMsScaled);
        double avgLatencyMs = requests > 0 ? (latencyScaled / 1000d) / requests : 0d;
        double errorRate = requests > 0 ? failures / (double)requests : 0d;

        return new
        {
            requests_total = requests,
            requests_failed = failures,
            error_rate = Math.Round(errorRate, 4),
            avg_response_ms = Math.Round(avgLatencyMs, 3),
            runs_start_attempts = Interlocked.Read(ref runStartAttempts),
            runs_submit_attempts = Interlocked.Read(ref runSubmitAttempts),
            submits_accepted = Interlocked.Read(ref acceptedSubmits),
            submits_shadow_banned = Interlocked.Read(ref shadowBannedSubmits),
            submits_rejected = Interlocked.Read(ref rejectedSubmits),
            submits_manual_review = Interlocked.Read(ref manualReviewSubmits),
            timestamp_utc = DateTimeOffset.UtcNow
        };
    }
}
