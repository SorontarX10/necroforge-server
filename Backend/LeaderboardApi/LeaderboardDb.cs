using System.Globalization;
using Npgsql;

namespace LeaderboardApi;

public static class LeaderboardDb
{
    private sealed record ExternalAccountIdentity(
        string AccountId,
        string Provider,
        string ProviderUserId
    );

    private sealed record RunRow(
        Guid RunId,
        string PlayerId,
        string Season,
        string Nonce,
        string SessionKey,
        DateTime ExpiresAtUtc,
        DateTime? SubmittedAtUtc
    );

    public static async Task EnsureSchemaAsync(NpgsqlDataSource dataSource)
    {
        await using NpgsqlConnection conn = await dataSource.OpenConnectionAsync();
        await using NpgsqlCommand cmd = new(
            """
            CREATE TABLE IF NOT EXISTS players (
                player_id TEXT PRIMARY KEY,
                display_name TEXT NOT NULL,
                account_id TEXT NULL,
                auth_provider TEXT NULL,
                provider_user_id TEXT NULL,
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );

            CREATE TABLE IF NOT EXISTS user_accounts (
                account_id TEXT PRIMARY KEY,
                provider TEXT NOT NULL,
                provider_user_id TEXT NOT NULL,
                display_name TEXT NOT NULL,
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                last_seen_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                UNIQUE (provider, provider_user_id)
            );

            CREATE TABLE IF NOT EXISTS runs (
                run_id UUID PRIMARY KEY,
                player_id TEXT NOT NULL REFERENCES players(player_id),
                season TEXT NOT NULL,
                build_version TEXT NOT NULL,
                nonce TEXT NOT NULL,
                session_key TEXT NOT NULL,
                status TEXT NOT NULL DEFAULT 'started',
                display_name TEXT NOT NULL DEFAULT '',
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                expires_at TIMESTAMPTZ NOT NULL,
                submitted_at TIMESTAMPTZ NULL,
                score INTEGER NULL,
                run_duration_sec REAL NULL,
                kills INTEGER NULL,
                is_cheat_session BOOLEAN NULL,
                signature TEXT NULL,
                event_chain TEXT NULL,
                event_chain_hash TEXT NULL,
                event_chain_count INTEGER NULL,
                validation_state TEXT NULL,
                validation_reason TEXT NULL
            );

            CREATE TABLE IF NOT EXISTS leaderboard_entries (
                id BIGSERIAL PRIMARY KEY,
                run_id UUID NOT NULL UNIQUE REFERENCES runs(run_id) ON DELETE CASCADE,
                player_id TEXT NOT NULL REFERENCES players(player_id),
                display_name TEXT NOT NULL,
                season TEXT NOT NULL,
                score INTEGER NOT NULL,
                run_duration_sec REAL NOT NULL,
                kills INTEGER NOT NULL,
                build_version TEXT NOT NULL,
                validation_state TEXT NOT NULL,
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );

            CREATE TABLE IF NOT EXISTS moderation_flags (
                id BIGSERIAL PRIMARY KEY,
                run_id UUID NOT NULL REFERENCES runs(run_id) ON DELETE CASCADE,
                flag_code TEXT NOT NULL,
                details TEXT NULL,
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );

            ALTER TABLE runs ADD COLUMN IF NOT EXISTS event_chain TEXT NULL;
            ALTER TABLE runs ADD COLUMN IF NOT EXISTS event_chain_hash TEXT NULL;
            ALTER TABLE runs ADD COLUMN IF NOT EXISTS event_chain_count INTEGER NULL;
            ALTER TABLE players ADD COLUMN IF NOT EXISTS account_id TEXT NULL;
            ALTER TABLE players ADD COLUMN IF NOT EXISTS auth_provider TEXT NULL;
            ALTER TABLE players ADD COLUMN IF NOT EXISTS provider_user_id TEXT NULL;

            CREATE INDEX IF NOT EXISTS idx_runs_player_created
                ON runs (player_id, created_at DESC);

            CREATE INDEX IF NOT EXISTS idx_runs_player_submitted
                ON runs (player_id, submitted_at DESC);

            CREATE INDEX IF NOT EXISTS idx_leaderboard_season_score
                ON leaderboard_entries (season, score DESC, created_at ASC);

            CREATE INDEX IF NOT EXISTS idx_leaderboard_player_season
                ON leaderboard_entries (player_id, season, score DESC, created_at ASC);

            CREATE INDEX IF NOT EXISTS idx_moderation_flags_run_id
                ON moderation_flags (run_id, created_at DESC);

            CREATE INDEX IF NOT EXISTS idx_players_account_id
                ON players (account_id);

            CREATE INDEX IF NOT EXISTS idx_user_accounts_provider_user
                ON user_accounts (provider, provider_user_id);
            """,
            conn
        );
        await cmd.ExecuteNonQueryAsync();
    }

    public static async Task<ServiceResult<StartRunResponse>> StartRunAsync(
        NpgsqlDataSource db,
        LeaderboardOptions opts,
        StartRunRequest request
    )
    {
        if (request is null)
            return ServiceResult<StartRunResponse>.Failure(StatusCodes.Status400BadRequest, "invalid_request", "Body is required.");

        string playerId = NormalizePlayerId(request.PlayerId);
        if (string.IsNullOrWhiteSpace(playerId))
            return ServiceResult<StartRunResponse>.Failure(StatusCodes.Status400BadRequest, "invalid_player_id", "player_id is required.");

        string season = opts.NormalizeSeason(request.Season);
        if (string.IsNullOrWhiteSpace(season))
            return ServiceResult<StartRunResponse>.Failure(StatusCodes.Status400BadRequest, "invalid_season", "Unsupported season.");

        string displayName = NormalizeDisplayName(request.DisplayName, playerId);
        string buildVersion = NormalizeBuildVersion(request.BuildVersion);
        ExternalAccountIdentity? externalAccount = NormalizeExternalAccountIdentity(
            request.AccountId,
            request.AccountProvider,
            request.AccountProviderUserId
        );
        Guid runId = Guid.NewGuid();
        string nonce = LeaderboardSecurity.CreateRandomToken();
        string sessionKey = LeaderboardSecurity.CreateRandomToken();
        DateTimeOffset expiresAt = DateTimeOffset.UtcNow.AddSeconds(opts.SessionTtlSeconds);

        await using NpgsqlConnection conn = await db.OpenConnectionAsync();
        await using NpgsqlTransaction tx = await conn.BeginTransactionAsync();
        try
        {
            bool startRateLimited = await IsOverStartRateLimitAsync(
                conn,
                tx,
                playerId,
                opts.MaxStartPerMinutePerPlayer
            );
            if (startRateLimited)
            {
                await tx.RollbackAsync();
                return ServiceResult<StartRunResponse>.Failure(
                    StatusCodes.Status429TooManyRequests,
                    "player_rate_limited",
                    "Too many run starts for this player. Please wait a moment."
                );
            }

            if (externalAccount != null)
                await UpsertUserAccountAsync(conn, tx, externalAccount, displayName);

            await UpsertPlayerAsync(conn, tx, playerId, displayName, externalAccount);

            await using NpgsqlCommand insertRun = new(
                """
                INSERT INTO runs (
                    run_id,
                    player_id,
                    season,
                    build_version,
                    nonce,
                    session_key,
                    status,
                    display_name,
                    expires_at
                )
                VALUES (
                    @run_id,
                    @player_id,
                    @season,
                    @build_version,
                    @nonce,
                    @session_key,
                    'started',
                    @display_name,
                    @expires_at
                );
                """,
                conn,
                tx
            );
            insertRun.Parameters.AddWithValue("run_id", runId);
            insertRun.Parameters.AddWithValue("player_id", playerId);
            insertRun.Parameters.AddWithValue("season", season);
            insertRun.Parameters.AddWithValue("build_version", buildVersion);
            insertRun.Parameters.AddWithValue("nonce", nonce);
            insertRun.Parameters.AddWithValue("session_key", sessionKey);
            insertRun.Parameters.AddWithValue("display_name", displayName);
            insertRun.Parameters.AddWithValue("expires_at", expiresAt.UtcDateTime);
            await insertRun.ExecuteNonQueryAsync();

            await tx.CommitAsync();
            return ServiceResult<StartRunResponse>.Success(
                new StartRunResponse(
                    RunId: runId.ToString("D"),
                    Nonce: nonce,
                    SessionKey: sessionKey,
                    ExpiresAtUtc: expiresAt
                )
            );
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            return ServiceResult<StartRunResponse>.Failure(StatusCodes.Status500InternalServerError, "db_error", ex.Message);
        }
    }

    public static async Task<ServiceResult<SubmitRunResponse>> SubmitRunAsync(
        NpgsqlDataSource db,
        LeaderboardOptions opts,
        SubmitRunRequest request
    )
    {
        if (request is null)
            return ServiceResult<SubmitRunResponse>.Failure(StatusCodes.Status400BadRequest, "invalid_request", "Body is required.");

        if (!Guid.TryParse(request.RunId, out Guid runId))
            return ServiceResult<SubmitRunResponse>.Failure(StatusCodes.Status400BadRequest, "invalid_run_id", "run_id must be UUID.");

        string playerId = NormalizePlayerId(request.PlayerId);
        if (string.IsNullOrWhiteSpace(playerId))
            return ServiceResult<SubmitRunResponse>.Failure(StatusCodes.Status400BadRequest, "invalid_player_id", "player_id is required.");

        string displayName = NormalizeDisplayName(request.DisplayName, playerId);
        string buildVersion = NormalizeBuildVersion(request.BuildVersion);
        ExternalAccountIdentity? externalAccount = NormalizeExternalAccountIdentity(
            request.AccountId,
            request.AccountProvider,
            request.AccountProviderUserId
        );
        int score = Math.Max(0, request.Score);
        int kills = Math.Max(0, request.Kills);
        float runDurationSec = MathF.Max(0f, request.RunDurationSec);
        string eventChain = NormalizeEventChain(request.EventChain);
        string eventChainHash = NormalizeEventChainHash(request.EventChainHash);
        int eventCount = Math.Max(0, request.EventCount);
        bool hasEventChainPayload = !string.IsNullOrWhiteSpace(eventChain)
                                    && !string.IsNullOrWhiteSpace(eventChainHash)
                                    && eventCount > 0;
        bool hasPartialEventChainPayload = !hasEventChainPayload
                                           && (!string.IsNullOrWhiteSpace(request.EventChain)
                                               || !string.IsNullOrWhiteSpace(request.EventChainHash)
                                               || request.EventCount > 0);
        string providedSignature = request.Signature?.Trim() ?? string.Empty;

        await using NpgsqlConnection conn = await db.OpenConnectionAsync();
        await using NpgsqlTransaction tx = await conn.BeginTransactionAsync();
        try
        {
            bool submitRateLimited = await IsOverSubmitRateLimitAsync(
                conn,
                tx,
                playerId,
                opts.MaxSubmitPerMinutePerPlayer
            );
            if (submitRateLimited)
            {
                await tx.RollbackAsync();
                return ServiceResult<SubmitRunResponse>.Failure(
                    StatusCodes.Status429TooManyRequests,
                    "player_rate_limited",
                    "Too many run submits for this player. Please wait a moment."
                );
            }

            RunRow? run = await LoadRunForUpdateAsync(conn, tx, runId);
            if (run is null)
            {
                await tx.RollbackAsync();
                return ServiceResult<SubmitRunResponse>.Failure(StatusCodes.Status404NotFound, "run_not_found", "Run session not found.");
            }

            if (run.SubmittedAtUtc.HasValue)
            {
                await tx.RollbackAsync();
                return ServiceResult<SubmitRunResponse>.Failure(StatusCodes.Status409Conflict, "duplicate_submit", "Run was already submitted.");
            }

            if (!string.Equals(run.PlayerId, playerId, StringComparison.Ordinal))
            {
                await tx.RollbackAsync();
                return ServiceResult<SubmitRunResponse>.Failure(StatusCodes.Status400BadRequest, "player_mismatch", "player_id does not match run session.");
            }

            if (DateTimeOffset.UtcNow > run.ExpiresAtUtc)
            {
                await tx.RollbackAsync();
                return ServiceResult<SubmitRunResponse>.Failure(StatusCodes.Status400BadRequest, "run_expired", "Run session expired.");
            }

            string canonicalPayload = hasEventChainPayload
                ? LeaderboardSecurity.BuildCanonicalPayload(
                    runId,
                    playerId,
                    score,
                    runDurationSec,
                    kills,
                    buildVersion,
                    request.IsCheatSession,
                    eventChainHash,
                    eventCount
                )
                : LeaderboardSecurity.BuildCanonicalPayload(
                    runId,
                    playerId,
                    score,
                    runDurationSec,
                    kills,
                    buildVersion,
                    request.IsCheatSession
                );
            string expectedSignature = LeaderboardSecurity.ComputeSignatureBase64(run.SessionKey, canonicalPayload);
            bool signatureValid = LeaderboardSecurity.FixedTimeEqualsBase64(providedSignature, expectedSignature);

            List<string> flags = [];
            if (hasPartialEventChainPayload)
                flags.Add("event_chain_partial");

            if (hasEventChainPayload && !IsValidEventCount(eventCount))
                flags.Add("event_chain_count_invalid");

            if (hasEventChainPayload &&
                !TryValidateEventChain(
                    runId,
                    run.Nonce,
                    eventChain,
                    eventChainHash,
                    eventCount,
                    score,
                    kills,
                    runDurationSec,
                    out string chainValidationError))
            {
                flags.Add(chainValidationError);
            }

            string validationState;
            if (!signatureValid)
            {
                validationState = "rejected";
                flags.Add("invalid_signature");
            }
            else
            {
                flags.AddRange(EvaluateValidationFlags(request.IsCheatSession, score, kills, runDurationSec, buildVersion, opts));
                validationState = DecideValidationState(flags);
            }

            string validationReason = flags.Count > 0 ? string.Join(",", flags) : "ok";

            if (externalAccount != null)
                await UpsertUserAccountAsync(conn, tx, externalAccount, displayName);

            await UpsertPlayerAsync(conn, tx, playerId, displayName, externalAccount);
            await UpdateRunSubmissionAsync(
                conn,
                tx,
                runId,
                displayName,
                buildVersion,
                score,
                kills,
                runDurationSec,
                request.IsCheatSession,
                providedSignature,
                eventChain,
                eventChainHash,
                eventCount,
                validationState,
                validationReason
            );

            if (!string.Equals(validationState, "rejected", StringComparison.Ordinal))
                await InsertLeaderboardEntryAsync(conn, tx, runId, playerId, displayName, run.Season, score, kills, runDurationSec, buildVersion, validationState);

            foreach (string flag in flags)
                await InsertModerationFlagAsync(conn, tx, runId, flag, $"score={score},kills={kills},duration={runDurationSec:0.###},version={buildVersion}");

            await tx.CommitAsync();
            return ServiceResult<SubmitRunResponse>.Success(
                new SubmitRunResponse(
                    RunId: runId.ToString("D"),
                    ValidationState: validationState,
                    ValidationReason: validationReason
                )
            );
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            return ServiceResult<SubmitRunResponse>.Failure(StatusCodes.Status500InternalServerError, "db_error", ex.Message);
        }
    }

    public static async Task<ServiceResult<GetLeaderboardResponse>> GetLeaderboardAsync(
        NpgsqlDataSource db,
        LeaderboardOptions opts,
        GetLeaderboardQuery query
    )
    {
        string season = opts.NormalizeSeason(query.Season);
        if (string.IsNullOrWhiteSpace(season))
            return ServiceResult<GetLeaderboardResponse>.Failure(StatusCodes.Status400BadRequest, "invalid_season", "Unsupported season.");

        int page = Math.Max(1, query.Page ?? 1);
        int pageSize = Math.Clamp(query.PageSize ?? 20, 1, opts.MaxPageSize);
        int offset = (page - 1) * pageSize;
        List<LeaderboardEntryResponse> entries = [];

        await using NpgsqlConnection conn = await db.OpenConnectionAsync();
        int totalCount;
        await using (NpgsqlCommand countCmd = new(
            """
            SELECT COUNT(*) FROM leaderboard_entries
            WHERE season = @season
              AND validation_state = 'accepted';
            """,
            conn
        ))
        {
            countCmd.Parameters.AddWithValue("season", season);
            object? raw = await countCmd.ExecuteScalarAsync();
            totalCount = raw is null ? 0 : Convert.ToInt32(raw, CultureInfo.InvariantCulture);
        }

        await using (NpgsqlCommand pageCmd = new(
            """
            WITH ranked AS (
                SELECT
                    ROW_NUMBER() OVER (ORDER BY score DESC, run_duration_sec DESC, created_at ASC) AS rank,
                    player_id, display_name, score, run_duration_sec, kills, build_version, created_at
                FROM leaderboard_entries
                WHERE season = @season
                  AND validation_state = 'accepted'
            )
            SELECT rank, player_id, display_name, score, run_duration_sec, kills, build_version, created_at
            FROM ranked
            ORDER BY rank
            OFFSET @offset
            LIMIT @limit;
            """,
            conn
        ))
        {
            pageCmd.Parameters.AddWithValue("season", season);
            pageCmd.Parameters.AddWithValue("offset", offset);
            pageCmd.Parameters.AddWithValue("limit", pageSize);

            await using NpgsqlDataReader reader = await pageCmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                entries.Add(new LeaderboardEntryResponse(
                    Rank: reader.GetInt32(0),
                    PlayerId: reader.GetString(1),
                    DisplayName: reader.GetString(2),
                    Score: reader.GetInt32(3),
                    RunDurationSec: reader.GetFloat(4),
                    Kills: reader.GetInt32(5),
                    BuildVersion: reader.GetString(6),
                    CreatedAtUtc: DateTime.SpecifyKind(reader.GetDateTime(7), DateTimeKind.Utc)
                ));
            }
        }

        return ServiceResult<GetLeaderboardResponse>.Success(
            new GetLeaderboardResponse(
                Season: season,
                Page: page,
                PageSize: pageSize,
                TotalCount: totalCount,
                Entries: entries
            )
        );
    }

    public static async Task<ServiceResult<GetMyRankResponse>> GetMyRankAsync(
        NpgsqlDataSource db,
        LeaderboardOptions opts,
        GetMyRankQuery query
    )
    {
        string season = opts.NormalizeSeason(query.Season);
        if (string.IsNullOrWhiteSpace(season))
            return ServiceResult<GetMyRankResponse>.Failure(StatusCodes.Status400BadRequest, "invalid_season", "Unsupported season.");

        string playerId = NormalizePlayerId(query.PlayerId);
        if (string.IsNullOrWhiteSpace(playerId))
            return ServiceResult<GetMyRankResponse>.Failure(StatusCodes.Status400BadRequest, "invalid_player_id", "player_id is required.");

        await using NpgsqlConnection conn = await db.OpenConnectionAsync();
        await using NpgsqlCommand cmd = new(
            """
            WITH ranked AS (
                SELECT
                    ROW_NUMBER() OVER (ORDER BY score DESC, run_duration_sec DESC, created_at ASC) AS rank,
                    player_id, display_name, score, run_duration_sec, kills, build_version, created_at
                FROM leaderboard_entries
                WHERE season = @season
                  AND validation_state = 'accepted'
            )
            SELECT rank, player_id, display_name, score, run_duration_sec, kills, build_version, created_at
            FROM ranked
            WHERE player_id = @player_id
            ORDER BY rank
            LIMIT 1;
            """,
            conn
        );
        cmd.Parameters.AddWithValue("season", season);
        cmd.Parameters.AddWithValue("player_id", playerId);

        await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return ServiceResult<GetMyRankResponse>.Success(new GetMyRankResponse(season, Found: false, Entry: null));

        LeaderboardEntryResponse entry = new(
            Rank: reader.GetInt32(0),
            PlayerId: reader.GetString(1),
            DisplayName: reader.GetString(2),
            Score: reader.GetInt32(3),
            RunDurationSec: reader.GetFloat(4),
            Kills: reader.GetInt32(5),
            BuildVersion: reader.GetString(6),
            CreatedAtUtc: DateTime.SpecifyKind(reader.GetDateTime(7), DateTimeKind.Utc)
        );
        return ServiceResult<GetMyRankResponse>.Success(new GetMyRankResponse(season, Found: true, Entry: entry));
    }

    public static async Task<ServiceResult<AdminGetFlaggedRunsResponse>> GetFlaggedRunsAsync(
        NpgsqlDataSource db,
        LeaderboardOptions opts,
        AdminGetFlaggedRunsQuery query
    )
    {
        string state = NormalizeAdminStateFilter(query.State);
        if (state == "invalid")
        {
            return ServiceResult<AdminGetFlaggedRunsResponse>.Failure(
                StatusCodes.Status400BadRequest,
                "invalid_state",
                "Allowed state filters: manual_review, shadow_banned, rejected, accepted, all_flagged."
            );
        }

        int page = Math.Max(1, query.Page ?? 1);
        int pageSize = Math.Clamp(query.PageSize ?? 20, 1, opts.MaxPageSize);
        int offset = (page - 1) * pageSize;
        List<AdminFlaggedRunResponse> entries = [];

        await using NpgsqlConnection conn = await db.OpenConnectionAsync();

        int totalCount;
        if (state == "all_flagged")
        {
            await using NpgsqlCommand countCmd = new(
                """
                SELECT COUNT(*) FROM runs
                WHERE submitted_at IS NOT NULL
                  AND validation_state <> 'accepted';
                """,
                conn
            );
            object? raw = await countCmd.ExecuteScalarAsync();
            totalCount = raw is null ? 0 : Convert.ToInt32(raw, CultureInfo.InvariantCulture);
        }
        else
        {
            await using NpgsqlCommand countCmd = new(
                """
                SELECT COUNT(*) FROM runs
                WHERE submitted_at IS NOT NULL
                  AND validation_state = @validation_state;
                """,
                conn
            );
            countCmd.Parameters.AddWithValue("validation_state", state);
            object? raw = await countCmd.ExecuteScalarAsync();
            totalCount = raw is null ? 0 : Convert.ToInt32(raw, CultureInfo.InvariantCulture);
        }

        if (state == "all_flagged")
        {
            await using NpgsqlCommand listCmd = new(
                """
                SELECT run_id, player_id, display_name, season,
                       COALESCE(score, 0), COALESCE(run_duration_sec, 0), COALESCE(kills, 0),
                       build_version, COALESCE(validation_state, ''), COALESCE(validation_reason, ''),
                       submitted_at
                FROM runs
                WHERE submitted_at IS NOT NULL
                  AND validation_state <> 'accepted'
                ORDER BY submitted_at DESC
                OFFSET @offset
                LIMIT @limit;
                """,
                conn
            );
            listCmd.Parameters.AddWithValue("offset", offset);
            listCmd.Parameters.AddWithValue("limit", pageSize);

            await using NpgsqlDataReader reader = await listCmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                entries.Add(new AdminFlaggedRunResponse(
                    RunId: reader.GetGuid(0).ToString("D"),
                    PlayerId: reader.GetString(1),
                    DisplayName: reader.GetString(2),
                    Season: reader.GetString(3),
                    Score: reader.GetInt32(4),
                    RunDurationSec: reader.GetFloat(5),
                    Kills: reader.GetInt32(6),
                    BuildVersion: reader.GetString(7),
                    ValidationState: reader.GetString(8),
                    ValidationReason: reader.GetString(9),
                    SubmittedAtUtc: DateTime.SpecifyKind(reader.GetDateTime(10), DateTimeKind.Utc)
                ));
            }
        }
        else
        {
            await using NpgsqlCommand listCmd = new(
                """
                SELECT run_id, player_id, display_name, season,
                       COALESCE(score, 0), COALESCE(run_duration_sec, 0), COALESCE(kills, 0),
                       build_version, COALESCE(validation_state, ''), COALESCE(validation_reason, ''),
                       submitted_at
                FROM runs
                WHERE submitted_at IS NOT NULL
                  AND validation_state = @validation_state
                ORDER BY submitted_at DESC
                OFFSET @offset
                LIMIT @limit;
                """,
                conn
            );
            listCmd.Parameters.AddWithValue("validation_state", state);
            listCmd.Parameters.AddWithValue("offset", offset);
            listCmd.Parameters.AddWithValue("limit", pageSize);

            await using NpgsqlDataReader reader = await listCmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                entries.Add(new AdminFlaggedRunResponse(
                    RunId: reader.GetGuid(0).ToString("D"),
                    PlayerId: reader.GetString(1),
                    DisplayName: reader.GetString(2),
                    Season: reader.GetString(3),
                    Score: reader.GetInt32(4),
                    RunDurationSec: reader.GetFloat(5),
                    Kills: reader.GetInt32(6),
                    BuildVersion: reader.GetString(7),
                    ValidationState: reader.GetString(8),
                    ValidationReason: reader.GetString(9),
                    SubmittedAtUtc: DateTime.SpecifyKind(reader.GetDateTime(10), DateTimeKind.Utc)
                ));
            }
        }

        return ServiceResult<AdminGetFlaggedRunsResponse>.Success(
            new AdminGetFlaggedRunsResponse(page, pageSize, totalCount, entries)
        );
    }

    public static async Task<ServiceResult<AdminReviewRunResponse>> ReviewRunAsync(
        NpgsqlDataSource db,
        LeaderboardOptions opts,
        AdminReviewRunRequest request
    )
    {
        if (request is null)
        {
            return ServiceResult<AdminReviewRunResponse>.Failure(
                StatusCodes.Status400BadRequest,
                "invalid_request",
                "Body is required."
            );
        }

        if (!Guid.TryParse(request.RunId, out Guid runId))
        {
            return ServiceResult<AdminReviewRunResponse>.Failure(
                StatusCodes.Status400BadRequest,
                "invalid_run_id",
                "run_id must be UUID."
            );
        }

        string targetState = NormalizeAdminAction(request.Action);
        if (targetState == "invalid")
        {
            return ServiceResult<AdminReviewRunResponse>.Failure(
                StatusCodes.Status400BadRequest,
                "invalid_action",
                "Allowed actions: accept, reject, shadow_ban, manual_review."
            );
        }

        string note = NormalizeAdminNote(request.Note);
        string validationReason = string.IsNullOrWhiteSpace(note)
            ? $"admin_review:{targetState}"
            : $"admin_review:{targetState}:{note}";

        await using NpgsqlConnection conn = await db.OpenConnectionAsync();
        await using NpgsqlTransaction tx = await conn.BeginTransactionAsync();
        try
        {
            await using NpgsqlCommand loadCmd = new(
                """
                SELECT player_id, display_name, season,
                       COALESCE(score, 0), COALESCE(run_duration_sec, 0), COALESCE(kills, 0),
                       build_version, submitted_at
                FROM runs
                WHERE run_id = @run_id
                FOR UPDATE;
                """,
                conn,
                tx
            );
            loadCmd.Parameters.AddWithValue("run_id", runId);

            string playerId;
            string displayName;
            string season;
            int score;
            float runDurationSec;
            int kills;
            string buildVersion;
            DateTime? submittedAtUtc;

            await using (NpgsqlDataReader reader = await loadCmd.ExecuteReaderAsync())
            {
                if (!await reader.ReadAsync())
                {
                    await tx.RollbackAsync();
                    return ServiceResult<AdminReviewRunResponse>.Failure(
                        StatusCodes.Status404NotFound,
                        "run_not_found",
                        "Run session not found."
                    );
                }

                playerId = reader.GetString(0);
                displayName = reader.GetString(1);
                season = reader.GetString(2);
                score = reader.GetInt32(3);
                runDurationSec = reader.GetFloat(4);
                kills = reader.GetInt32(5);
                buildVersion = reader.GetString(6);
                submittedAtUtc = reader.IsDBNull(7) ? null : DateTime.SpecifyKind(reader.GetDateTime(7), DateTimeKind.Utc);
            }

            if (!submittedAtUtc.HasValue)
            {
                await tx.RollbackAsync();
                return ServiceResult<AdminReviewRunResponse>.Failure(
                    StatusCodes.Status409Conflict,
                    "run_not_submitted",
                    "Run has not been submitted yet."
                );
            }

            await using (NpgsqlCommand updateRun = new(
                """
                UPDATE runs
                SET validation_state = @validation_state,
                    validation_reason = @validation_reason
                WHERE run_id = @run_id;
                """,
                conn,
                tx
            ))
            {
                updateRun.Parameters.AddWithValue("validation_state", targetState);
                updateRun.Parameters.AddWithValue("validation_reason", validationReason);
                updateRun.Parameters.AddWithValue("run_id", runId);
                await updateRun.ExecuteNonQueryAsync();
            }

            if (string.Equals(targetState, "rejected", StringComparison.Ordinal))
            {
                await using NpgsqlCommand updateEntry = new(
                    """
                    UPDATE leaderboard_entries
                    SET validation_state = 'rejected'
                    WHERE run_id = @run_id;
                    """,
                    conn,
                    tx
                );
                updateEntry.Parameters.AddWithValue("run_id", runId);
                await updateEntry.ExecuteNonQueryAsync();
            }
            else
            {
                await using NpgsqlCommand upsertEntry = new(
                    """
                    INSERT INTO leaderboard_entries (
                        run_id, player_id, display_name, season, score, run_duration_sec, kills, build_version, validation_state
                    )
                    VALUES (
                        @run_id, @player_id, @display_name, @season, @score, @run_duration_sec, @kills, @build_version, @validation_state
                    )
                    ON CONFLICT (run_id) DO UPDATE
                    SET display_name = EXCLUDED.display_name,
                        score = EXCLUDED.score,
                        run_duration_sec = EXCLUDED.run_duration_sec,
                        kills = EXCLUDED.kills,
                        build_version = EXCLUDED.build_version,
                        validation_state = EXCLUDED.validation_state;
                    """,
                    conn,
                    tx
                );
                upsertEntry.Parameters.AddWithValue("run_id", runId);
                upsertEntry.Parameters.AddWithValue("player_id", playerId);
                upsertEntry.Parameters.AddWithValue("display_name", displayName);
                upsertEntry.Parameters.AddWithValue("season", season);
                upsertEntry.Parameters.AddWithValue("score", score);
                upsertEntry.Parameters.AddWithValue("run_duration_sec", runDurationSec);
                upsertEntry.Parameters.AddWithValue("kills", kills);
                upsertEntry.Parameters.AddWithValue("build_version", buildVersion);
                upsertEntry.Parameters.AddWithValue("validation_state", targetState);
                await upsertEntry.ExecuteNonQueryAsync();
            }

            await InsertModerationFlagAsync(conn, tx, runId, $"admin_action_{targetState}", note);
            await tx.CommitAsync();

            return ServiceResult<AdminReviewRunResponse>.Success(
                new AdminReviewRunResponse(
                    RunId: runId.ToString("D"),
                    ValidationState: targetState,
                    ValidationReason: validationReason
                )
            );
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            return ServiceResult<AdminReviewRunResponse>.Failure(
                StatusCodes.Status500InternalServerError,
                "db_error",
                ex.Message
            );
        }
    }

    private static async Task UpsertPlayerAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        string playerId,
        string displayName,
        ExternalAccountIdentity? externalAccount
    )
    {
        await using NpgsqlCommand cmd = new(
            """
            INSERT INTO players (player_id, display_name, account_id, auth_provider, provider_user_id)
            VALUES (@player_id, @display_name, @account_id, @auth_provider, @provider_user_id)
            ON CONFLICT (player_id) DO UPDATE
            SET display_name = EXCLUDED.display_name,
                account_id = COALESCE(EXCLUDED.account_id, players.account_id),
                auth_provider = COALESCE(EXCLUDED.auth_provider, players.auth_provider),
                provider_user_id = COALESCE(EXCLUDED.provider_user_id, players.provider_user_id),
                updated_at = NOW();
            """,
            conn,
            tx
        );
        cmd.Parameters.AddWithValue("player_id", playerId);
        cmd.Parameters.AddWithValue("display_name", displayName);
        cmd.Parameters.AddWithValue("account_id", (object?)externalAccount?.AccountId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("auth_provider", (object?)externalAccount?.Provider ?? DBNull.Value);
        cmd.Parameters.AddWithValue("provider_user_id", (object?)externalAccount?.ProviderUserId ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task UpsertUserAccountAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        ExternalAccountIdentity identity,
        string displayName
    )
    {
        await using NpgsqlCommand cmd = new(
            """
            INSERT INTO user_accounts (account_id, provider, provider_user_id, display_name)
            VALUES (@account_id, @provider, @provider_user_id, @display_name)
            ON CONFLICT (account_id) DO UPDATE
            SET provider = EXCLUDED.provider,
                provider_user_id = EXCLUDED.provider_user_id,
                display_name = EXCLUDED.display_name,
                last_seen_at = NOW();
            """,
            conn,
            tx
        );
        cmd.Parameters.AddWithValue("account_id", identity.AccountId);
        cmd.Parameters.AddWithValue("provider", identity.Provider);
        cmd.Parameters.AddWithValue("provider_user_id", identity.ProviderUserId);
        cmd.Parameters.AddWithValue("display_name", displayName);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<RunRow?> LoadRunForUpdateAsync(NpgsqlConnection conn, NpgsqlTransaction tx, Guid runId)
    {
        await using NpgsqlCommand cmd = new(
            """
            SELECT run_id, player_id, season, nonce, session_key, expires_at, submitted_at
            FROM runs
            WHERE run_id = @run_id
            FOR UPDATE;
            """,
            conn,
            tx
        );
        cmd.Parameters.AddWithValue("run_id", runId);

        await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return null;

        return new RunRow(
            RunId: reader.GetGuid(0),
            PlayerId: reader.GetString(1),
            Season: reader.GetString(2),
            Nonce: reader.GetString(3),
            SessionKey: reader.GetString(4),
            ExpiresAtUtc: DateTime.SpecifyKind(reader.GetDateTime(5), DateTimeKind.Utc),
            SubmittedAtUtc: reader.IsDBNull(6) ? null : DateTime.SpecifyKind(reader.GetDateTime(6), DateTimeKind.Utc)
        );
    }

    private static async Task UpdateRunSubmissionAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        Guid runId,
        string displayName,
        string buildVersion,
        int score,
        int kills,
        float runDurationSec,
        bool isCheatSession,
        string signature,
        string eventChain,
        string eventChainHash,
        int eventCount,
        string validationState,
        string validationReason
    )
    {
        await using NpgsqlCommand cmd = new(
            """
            UPDATE runs
            SET status = 'submitted',
                submitted_at = NOW(),
                display_name = @display_name,
                build_version = @build_version,
                score = @score,
                run_duration_sec = @run_duration_sec,
                kills = @kills,
                is_cheat_session = @is_cheat_session,
                signature = @signature,
                event_chain = @event_chain,
                event_chain_hash = @event_chain_hash,
                event_chain_count = @event_chain_count,
                validation_state = @validation_state,
                validation_reason = @validation_reason
            WHERE run_id = @run_id;
            """,
            conn,
            tx
        );
        cmd.Parameters.AddWithValue("display_name", displayName);
        cmd.Parameters.AddWithValue("build_version", buildVersion);
        cmd.Parameters.AddWithValue("score", score);
        cmd.Parameters.AddWithValue("run_duration_sec", runDurationSec);
        cmd.Parameters.AddWithValue("kills", kills);
        cmd.Parameters.AddWithValue("is_cheat_session", isCheatSession);
        cmd.Parameters.AddWithValue("signature", signature);
        cmd.Parameters.AddWithValue("event_chain", eventChain);
        cmd.Parameters.AddWithValue("event_chain_hash", eventChainHash);
        cmd.Parameters.AddWithValue("event_chain_count", eventCount);
        cmd.Parameters.AddWithValue("validation_state", validationState);
        cmd.Parameters.AddWithValue("validation_reason", validationReason);
        cmd.Parameters.AddWithValue("run_id", runId);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task InsertLeaderboardEntryAsync(
        NpgsqlConnection conn,
        NpgsqlTransaction tx,
        Guid runId,
        string playerId,
        string displayName,
        string season,
        int score,
        int kills,
        float runDurationSec,
        string buildVersion,
        string validationState
    )
    {
        await using NpgsqlCommand cmd = new(
            """
            INSERT INTO leaderboard_entries (
                run_id, player_id, display_name, season, score, run_duration_sec, kills, build_version, validation_state
            )
            VALUES (
                @run_id, @player_id, @display_name, @season, @score, @run_duration_sec, @kills, @build_version, @validation_state
            )
            ON CONFLICT (run_id) DO NOTHING;
            """,
            conn,
            tx
        );
        cmd.Parameters.AddWithValue("run_id", runId);
        cmd.Parameters.AddWithValue("player_id", playerId);
        cmd.Parameters.AddWithValue("display_name", displayName);
        cmd.Parameters.AddWithValue("season", season);
        cmd.Parameters.AddWithValue("score", score);
        cmd.Parameters.AddWithValue("run_duration_sec", runDurationSec);
        cmd.Parameters.AddWithValue("kills", kills);
        cmd.Parameters.AddWithValue("build_version", buildVersion);
        cmd.Parameters.AddWithValue("validation_state", validationState);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task InsertModerationFlagAsync(NpgsqlConnection conn, NpgsqlTransaction tx, Guid runId, string code, string details)
    {
        await using NpgsqlCommand cmd = new(
            """
            INSERT INTO moderation_flags (run_id, flag_code, details)
            VALUES (@run_id, @flag_code, @details);
            """,
            conn,
            tx
        );
        cmd.Parameters.AddWithValue("run_id", runId);
        cmd.Parameters.AddWithValue("flag_code", code);
        cmd.Parameters.AddWithValue("details", details);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<bool> IsOverStartRateLimitAsync(NpgsqlConnection conn, NpgsqlTransaction tx, string playerId, int limit)
    {
        await using NpgsqlCommand cmd = new(
            """
            SELECT COUNT(*)
            FROM runs
            WHERE player_id = @player_id
              AND created_at > NOW() - INTERVAL '1 minute';
            """,
            conn,
            tx
        );
        cmd.Parameters.AddWithValue("player_id", playerId);
        object? raw = await cmd.ExecuteScalarAsync();
        int count = raw is null ? 0 : Convert.ToInt32(raw, CultureInfo.InvariantCulture);
        return count >= limit;
    }

    private static async Task<bool> IsOverSubmitRateLimitAsync(NpgsqlConnection conn, NpgsqlTransaction tx, string playerId, int limit)
    {
        await using NpgsqlCommand cmd = new(
            """
            SELECT COUNT(*)
            FROM runs
            WHERE player_id = @player_id
              AND submitted_at IS NOT NULL
              AND submitted_at > NOW() - INTERVAL '1 minute';
            """,
            conn,
            tx
        );
        cmd.Parameters.AddWithValue("player_id", playerId);
        object? raw = await cmd.ExecuteScalarAsync();
        int count = raw is null ? 0 : Convert.ToInt32(raw, CultureInfo.InvariantCulture);
        return count >= limit;
    }

    private static bool IsValidEventCount(int eventCount)
    {
        return eventCount >= 2 && eventCount <= 512;
    }

    private static bool TryValidateEventChain(
        Guid runId,
        string nonce,
        string eventChain,
        string eventChainHash,
        int eventCount,
        int expectedScore,
        int expectedKills,
        float expectedRunDurationSec,
        out string errorCode
    )
    {
        errorCode = "event_chain_invalid";

        if (string.IsNullOrWhiteSpace(eventChain))
        {
            errorCode = "event_chain_missing";
            return false;
        }

        string[] checkpoints = eventChain.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (checkpoints.Length != eventCount)
        {
            errorCode = "event_chain_count_mismatch";
            return false;
        }

        if (!IsValidEventCount(checkpoints.Length))
        {
            errorCode = "event_chain_count_invalid";
            return false;
        }

        int previousSecond = -1;
        int previousKills = -1;
        int previousScore = -1;
        int finalSecond = 0;
        int finalKills = 0;
        int finalScore = 0;
        string rollingHash = LeaderboardSecurity.ComputeSha256Hex($"seed|{runId:D}|{nonce}");

        for (int i = 0; i < checkpoints.Length; i++)
        {
            if (!TryParseEventCheckpoint(checkpoints[i], out int second, out int kills, out int score))
            {
                errorCode = "event_chain_parse_failed";
                return false;
            }

            if (second < previousSecond || kills < previousKills || score < previousScore)
            {
                errorCode = "event_chain_not_monotonic";
                return false;
            }

            rollingHash = LeaderboardSecurity.ComputeSha256Hex($"{rollingHash}|{second}|{kills}|{score}");
            previousSecond = second;
            previousKills = kills;
            previousScore = score;
            finalSecond = second;
            finalKills = kills;
            finalScore = score;
        }

        if (!string.Equals(rollingHash, eventChainHash, StringComparison.OrdinalIgnoreCase))
        {
            errorCode = "event_chain_hash_mismatch";
            return false;
        }

        int expectedFinalSecond = (int)MathF.Floor(MathF.Max(0f, expectedRunDurationSec));
        if (Math.Abs(finalSecond - expectedFinalSecond) > 1)
        {
            errorCode = "event_chain_duration_mismatch";
            return false;
        }

        if (finalKills != expectedKills)
        {
            errorCode = "event_chain_kills_mismatch";
            return false;
        }

        if (finalScore != expectedScore)
        {
            errorCode = "event_chain_score_mismatch";
            return false;
        }

        errorCode = string.Empty;
        return true;
    }

    private static bool TryParseEventCheckpoint(string raw, out int second, out int kills, out int score)
    {
        second = 0;
        kills = 0;
        score = 0;

        string[] parts = raw.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length != 3)
            return false;

        if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out second))
            return false;
        if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out kills))
            return false;
        if (!int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out score))
            return false;

        if (second < 0 || kills < 0 || score < 0)
            return false;

        return true;
    }

    private static List<string> EvaluateValidationFlags(
        bool isCheatSession,
        int score,
        int kills,
        float runDurationSec,
        string buildVersion,
        LeaderboardOptions opts
    )
    {
        List<string> flags = [];
        if (isCheatSession)
            flags.Add("cheat_session");
        if (runDurationSec < opts.MinRunDurationSeconds)
            flags.Add("run_too_short");

        float minutes = MathF.Max(0.1f, runDurationSec / 60f);
        if (score / minutes > opts.MaxScorePerMinute)
            flags.Add("score_rate_exceeded");
        if (kills / minutes > opts.MaxKillsPerMinute)
            flags.Add("kill_rate_exceeded");

        if (!string.IsNullOrWhiteSpace(opts.VersionLock) &&
            !string.Equals(opts.VersionLock, buildVersion, StringComparison.Ordinal))
        {
            flags.Add("build_version_mismatch");
        }

        return flags;
    }

    private static string DecideValidationState(List<string> flags)
    {
        if (flags.Count == 0)
            return "accepted";
        if (flags.Contains("cheat_session"))
            return "shadow_banned";
        if (flags.Contains("run_too_short"))
            return "rejected";
        return "manual_review";
    }

    private static string NormalizePlayerId(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;
        string trimmed = raw.Trim();
        return trimmed.Length <= 128 ? trimmed : trimmed[..128];
    }

    private static ExternalAccountIdentity? NormalizeExternalAccountIdentity(
        string? rawAccountId,
        string? rawProvider,
        string? rawProviderUserId
    )
    {
        string provider = NormalizeExternalProvider(rawProvider);
        string providerUserId = NormalizeExternalProviderUserId(rawProviderUserId);

        if (string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(providerUserId))
            return null;

        string accountId = NormalizeExternalAccountId(rawAccountId, provider, providerUserId);
        if (string.IsNullOrWhiteSpace(accountId))
            return null;

        return new ExternalAccountIdentity(accountId, provider, providerUserId);
    }

    private static string NormalizeExternalProvider(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        string trimmed = raw.Trim().ToLowerInvariant();
        return trimmed.Length <= 32 ? trimmed : trimmed[..32];
    }

    private static string NormalizeExternalProviderUserId(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        string trimmed = raw.Trim();
        return trimmed.Length <= 128 ? trimmed : trimmed[..128];
    }

    private static string NormalizeExternalAccountId(string? raw, string provider, string providerUserId)
    {
        if (!string.IsNullOrWhiteSpace(raw))
        {
            string trimmed = raw.Trim();
            return trimmed.Length <= 128 ? trimmed : trimmed[..128];
        }

        return $"{provider}:{providerUserId}";
    }

    private static string NormalizeDisplayName(string? raw, string playerId)
    {
        string fallback = $"Player-{Math.Abs(playerId.GetHashCode(StringComparison.Ordinal)) % 10000:0000}";
        if (string.IsNullOrWhiteSpace(raw))
            return fallback;
        string trimmed = raw.Trim();
        if (trimmed.Length > 48)
            trimmed = trimmed[..48];
        return string.IsNullOrWhiteSpace(trimmed) ? fallback : trimmed;
    }

    private static string NormalizeBuildVersion(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "unknown";
        string trimmed = raw.Trim();
        return trimmed.Length <= 32 ? trimmed : trimmed[..32];
    }

    private static string NormalizeEventChain(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        string trimmed = raw.Trim();
        return trimmed.Length <= 8192 ? trimmed : string.Empty;
    }

    private static string NormalizeEventChainHash(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        string trimmed = raw.Trim();
        return trimmed.Length <= 128 ? trimmed : string.Empty;
    }

    private static string NormalizeAdminStateFilter(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "manual_review";

        string value = raw.Trim().ToLowerInvariant();
        return value switch
        {
            "manual_review" => "manual_review",
            "shadow_banned" => "shadow_banned",
            "rejected" => "rejected",
            "accepted" => "accepted",
            "all_flagged" => "all_flagged",
            _ => "invalid"
        };
    }

    private static string NormalizeAdminAction(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "invalid";

        string value = raw.Trim().ToLowerInvariant();
        return value switch
        {
            "accept" => "accepted",
            "accepted" => "accepted",
            "reject" => "rejected",
            "rejected" => "rejected",
            "shadow_ban" => "shadow_banned",
            "shadow_banned" => "shadow_banned",
            "manual_review" => "manual_review",
            _ => "invalid"
        };
    }

    private static string NormalizeAdminNote(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;
        string trimmed = raw.Trim();
        return trimmed.Length <= 256 ? trimmed : trimmed[..256];
    }
}
