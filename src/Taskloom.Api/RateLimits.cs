using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Taskloom.Api.Auth;

namespace Taskloom.Api;

/// <summary>Bound from the "RateLimits" config section (Security 1, spec F).</summary>
public class RateLimitOptions
{
    public bool Enabled { get; set; } = true;

    /// <summary>Per client IP, for starting a sign-in and the providers' callbacks (not signing out, see UseSignInRateLimit).</summary>
    public int SignInPerMinute { get; set; } = 20;

    /// <summary>Per signed-in user (per client IP when signed out), for every /api call.</summary>
    public int ApiPerMinute { get; set; } = 600;
}

/// <summary>
/// Fixed one-minute windows from ASP.NET Core's rate limiter, in two places in the pipeline. The sign-in limiter runs
/// before authentication, because the providers' callbacks are answered inside UseAuthentication; the API limiter runs
/// after it, so it can count per user. Static files, the SPA fallback, /healthz, /auth/providers, dev login and signing
/// out are never limited. Client IPs come from X-Forwarded-For as UseForwardedHeaders reads it (README).
/// </summary>
public static class RateLimits
{
    public const string SignInMessage = "Too many sign-in attempts. Wait a minute and try again.";
    public const string TooManyMessage = "Too many requests. Wait a minute and try again.";

    private static readonly TimeSpan OneMinute = TimeSpan.FromMinutes(1);

    public static IServiceCollection AddTaskloomRateLimits(this IServiceCollection services, IConfiguration config)
    {
        var options = config.GetSection("RateLimits").Get<RateLimitOptions>() ?? new RateLimitOptions();
        if (options.Enabled)
        {
            // A limit of 0 would fail every request it covers, so it is refused at startup instead.
            if (options.SignInPerMinute < 1)
                throw new InvalidOperationException("Set RateLimits:SignInPerMinute to 1 or more, or RateLimits:Enabled=false to turn rate limiting off.");
            if (options.ApiPerMinute < 1)
                throw new InvalidOperationException("Set RateLimits:ApiPerMinute to 1 or more, or RateLimits:Enabled=false to turn rate limiting off.");
        }
        services.AddSingleton(options);
        // The limiter's shared services; each of the two middlewares below brings its own options.
        services.AddRateLimiter(_ => { });
        return services;
    }

    /// <summary>
    /// GET /auth/login/{provider} and the callbacks /auth/callback/{provider}, per client IP. A refused browser navigation
    /// goes back to the sign-in page with the message; anything else gets the 429. POST /auth/logout is left out on
    /// purpose: it only ends the caller's own session and has nothing to guess, and a refused sign-out would leave the
    /// session and its cookie working, while anyone at the same address (a whole office behind one NAT) can spend the
    /// address's sign-in permits.
    /// </summary>
    public static IApplicationBuilder UseSignInRateLimit(this IApplicationBuilder app)
    {
        var options = app.ApplicationServices.GetRequiredService<RateLimitOptions>();
        if (!options.Enabled) return app;
        return app.UseRateLimiter(new RateLimiterOptions
        {
            GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx => IsSignInRoute(ctx.Request.Path)
                ? RateLimitPartition.GetFixedWindowLimiter("ip:" + ClientIp(ctx), _ => PerMinute(options.SignInPerMinute))
                : RateLimitPartition.GetNoLimiter("")),
            RejectionStatusCode = StatusCodes.Status429TooManyRequests,
            OnRejected = (rejected, _) =>
            {
                var http = rejected.HttpContext;
                if (!IsNavigation(http.Request)) return RejectAsync(rejected);
                http.Response.Redirect(AuthSetup.LoginErrorPath(SignInMessage));
                return ValueTask.CompletedTask;
            },
        });
    }

    /// <summary>Every /api call, per signed-in user (the taskloom:uid claim), or per client IP for a caller who isn't signed in.</summary>
    public static IApplicationBuilder UseApiRateLimit(this IApplicationBuilder app)
    {
        var options = app.ApplicationServices.GetRequiredService<RateLimitOptions>();
        if (!options.Enabled) return app;
        return app.UseRateLimiter(new RateLimiterOptions
        {
            GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx => ctx.Request.Path.StartsWithSegments("/api")
                ? RateLimitPartition.GetFixedWindowLimiter(
                    ctx.User.FindFirstValue(ClaimsExtensions.UserIdClaim) is { } userId ? "user:" + userId : "ip:" + ClientIp(ctx),
                    _ => PerMinute(options.ApiPerMinute))
                : RateLimitPartition.GetNoLimiter("")),
            RejectionStatusCode = StatusCodes.Status429TooManyRequests,
            OnRejected = (rejected, _) => RejectAsync(rejected),
        });
    }

    private static bool IsSignInRoute(PathString path) =>
        path.StartsWithSegments("/auth/login") || path.StartsWithSegments("/auth/callback");

    /// <summary>
    /// A page load in the browser: a GET, or a provider's callback, which arrives as the browser posting the provider's
    /// form (response_mode=form_post) and so is a navigation too.
    /// </summary>
    private static bool IsNavigation(HttpRequest request) =>
        HttpMethods.IsGet(request.Method) || request.Path.StartsWithSegments("/auth/callback");

    private static string ClientIp(HttpContext ctx) => ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    private static FixedWindowRateLimiterOptions PerMinute(int permits) =>
        new() { PermitLimit = permits, Window = OneMinute, QueueLimit = 0, AutoReplenishment = true };

    /// <summary>The 429: ProblemDetails with the message, and Retry-After in whole seconds.</summary>
    private static async ValueTask RejectAsync(OnRejectedContext rejected)
    {
        var http = rejected.HttpContext;
        var wait = rejected.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter) ? retryAfter : OneMinute;
        http.Response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling(wait.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
        await Results.Problem(detail: TooManyMessage, statusCode: StatusCodes.Status429TooManyRequests).ExecuteAsync(http);
    }
}
