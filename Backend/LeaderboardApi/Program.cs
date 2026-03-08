using System.Threading.RateLimiting;
using LeaderboardApi;
using Microsoft.AspNetCore.HttpOverrides;
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
ExternalAuthOptions authOptions = ExternalAuthOptions.FromConfiguration(builder.Configuration);
NpgsqlDataSource dataSource = new NpgsqlDataSourceBuilder(options.DbConnectionString).Build();
await LeaderboardDb.EnsureSchemaAsync(dataSource);

builder.Services.AddSingleton(options);
builder.Services.AddSingleton(authOptions);
builder.Services.AddSingleton(dataSource);
builder.Services.AddSingleton<ApiMetrics>();
builder.Services.AddHttpClient(nameof(ExternalAuthBroker));
builder.Services.AddSingleton<ExternalAuthBroker>();
builder.Services.Configure<ForwardedHeadersOptions>(forwarded =>
{
    forwarded.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
    forwarded.KnownNetworks.Clear();
    forwarded.KnownProxies.Clear();
});

var app = builder.Build();
app.UseForwardedHeaders();
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

app.MapGet(
    "/auth/external/{provider}/start",
    (string provider, HttpContext httpContext, ExternalAuthBroker broker) =>
    {
        ServiceResult<string> result = broker.BuildStartRedirectUrl(provider, httpContext.Request);
        if (!result.Ok || string.IsNullOrWhiteSpace(result.Payload))
            return result.ToIResult();

        return Results.Redirect(result.Payload);
    }
);

app.MapGet(
    "/auth/external/{provider}/callback",
    (string provider, HttpContext httpContext, ExternalAuthBroker broker) =>
    {
        string html = broker.RenderCallbackPage(provider, httpContext.Request);
        return Results.Content(html, "text/html; charset=utf-8");
    }
);

app.MapPost(
    "/auth/external/exchange",
    async (ExchangeExternalAuthCodeRequest request, HttpContext httpContext, ExternalAuthBroker broker) =>
    {
        ServiceResult<ExternalAuthSessionResponse> result = await broker.ExchangeCodeAsync(
            request,
            httpContext.Request,
            httpContext.RequestAborted
        );
        return result.ToIResult();
    }
);

app.MapPost(
    "/auth/external/refresh",
    async (RefreshExternalAuthSessionRequest request, HttpContext httpContext, ExternalAuthBroker broker) =>
    {
        ServiceResult<ExternalAuthSessionResponse> result = await broker.RefreshAsync(
            request,
            httpContext.Request,
            httpContext.RequestAborted
        );
        return result.ToIResult();
    }
);

app.MapPost(
    "/auth/external/logout",
    async (LogoutExternalAuthSessionRequest request, HttpContext httpContext, ExternalAuthBroker broker) =>
    {
        await broker.RevokeAccessTokenAsync(request, httpContext.RequestAborted);
        return Results.NoContent();
    }
);

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

RouteGroupBuilder admin = app.MapGroup("/admin");
admin.AddEndpointFilter(async (context, next) =>
{
    HttpContext http = context.HttpContext;
    LeaderboardOptions opts = http.RequestServices.GetRequiredService<LeaderboardOptions>();
    if (string.IsNullOrWhiteSpace(opts.AdminApiKey))
    {
        return Results.Json(
            new ErrorResponse("admin_not_configured", "Admin API key is not configured."),
            statusCode: StatusCodes.Status503ServiceUnavailable
        );
    }

    string provided = http.Request.Headers["X-Admin-Token"].ToString();
    if (!string.Equals(provided, opts.AdminApiKey, StringComparison.Ordinal))
    {
        return Results.Json(
            new ErrorResponse("admin_unauthorized", "Missing or invalid X-Admin-Token."),
            statusCode: StatusCodes.Status401Unauthorized
        );
    }

    return await next(context);
});

admin.MapGet(
    "/runs/flagged",
    async ([AsParameters] AdminGetFlaggedRunsQuery query, NpgsqlDataSource db, LeaderboardOptions opts) =>
    {
        ServiceResult<AdminGetFlaggedRunsResponse> result = await LeaderboardDb.GetFlaggedRunsAsync(db, opts, query);
        return result.ToIResult();
    }
);

admin.MapPost(
    "/runs/review",
    async (AdminReviewRunRequest request, NpgsqlDataSource db, LeaderboardOptions opts) =>
    {
        ServiceResult<AdminReviewRunResponse> result = await LeaderboardDb.ReviewRunAsync(db, opts, request);
        return result.ToIResult();
    }
);

app.Run();
