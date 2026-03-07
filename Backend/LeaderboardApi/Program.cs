using System.Threading.RateLimiting;
using LeaderboardApi;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddCors(options =>
{
    options.AddPolicy(
        "open",
        policy => policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()
    );
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(
        "runs-start",
        context => RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 30,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }
        )
    );
    options.AddPolicy(
        "runs-submit",
        context => RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 30,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }
        )
    );
});

LeaderboardOptions options = LeaderboardOptions.FromConfiguration(builder.Configuration);
NpgsqlDataSource dataSource = new NpgsqlDataSourceBuilder(options.DbConnectionString).Build();
await LeaderboardDb.EnsureSchemaAsync(dataSource);

builder.Services.AddSingleton(options);
builder.Services.AddSingleton(dataSource);
builder.Services.AddSingleton<ApiMetrics>();

var app = builder.Build();
app.UseCors("open");
app.UseRateLimiter();
app.Use(async (ctx, next) =>
{
    ApiMetrics metrics = ctx.RequestServices.GetRequiredService<ApiMetrics>();
    var started = DateTime.UtcNow;
    await next();
    metrics.RecordRequest(ctx.Response.StatusCode, (DateTime.UtcNow - started).TotalMilliseconds);
});

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    timestamp_utc = DateTimeOffset.UtcNow
}));

app.MapGet("/metrics", (ApiMetrics metrics) => Results.Ok(metrics.GetSnapshot()));

app.MapPost(
        "/runs/start",
        async (StartRunRequest request, NpgsqlDataSource db, LeaderboardOptions opts, ApiMetrics metrics) =>
        {
            metrics.RecordRunStartAttempt();
            ServiceResult<StartRunResponse> result = await LeaderboardDb.StartRunAsync(db, opts, request);
            return result.ToIResult();
        }
    )
    .RequireRateLimiting("runs-start");

app.MapPost(
        "/runs/submit",
        async (SubmitRunRequest request, NpgsqlDataSource db, LeaderboardOptions opts, ApiMetrics metrics) =>
        {
            metrics.RecordRunSubmitAttempt();
            ServiceResult<SubmitRunResponse> result = await LeaderboardDb.SubmitRunAsync(db, opts, request);
            if (result.Ok && result.Payload != null)
                metrics.RecordValidationState(result.Payload.ValidationState);
            return result.ToIResult();
        }
    )
    .RequireRateLimiting("runs-submit");

app.MapGet(
    "/leaderboard",
    async ([AsParameters] GetLeaderboardQuery query, NpgsqlDataSource db, LeaderboardOptions opts) =>
    {
        ServiceResult<GetLeaderboardResponse> result = await LeaderboardDb.GetLeaderboardAsync(db, opts, query);
        return result.ToIResult();
    }
);

app.MapGet(
    "/leaderboard/me",
    async ([AsParameters] GetMyRankQuery query, NpgsqlDataSource db, LeaderboardOptions opts) =>
    {
        ServiceResult<GetMyRankResponse> result = await LeaderboardDb.GetMyRankAsync(db, opts, query);
        return result.ToIResult();
    }
);

app.Run();
