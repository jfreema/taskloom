using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Taskloom.Api;
using Taskloom.Api.Auth;
using Taskloom.Api.Endpoints;
using Taskloom.Api.Jobs;
using Taskloom.Api.Notifications;
using Taskloom.Application;
using Taskloom.Cloud.Azure;
using Taskloom.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

// ---- Data ----
var connectionString = config.GetConnectionString("Taskloom")
    ?? throw new InvalidOperationException("Set ConnectionStrings__Taskloom.");
// Single-task queries load assignees and tags in one query; queries returning several tasks opt into AsSplitQuery().
// A failed command is logged as a warning, not an error: the middleware below turns the expected ones (a taken id,
// text PostgreSQL can't store) into 409 and 400, and any other failure still reaches the exception handler.
builder.Services.AddDbContext<TaskloomDbContext>(o => o
    .UseNpgsql(connectionString, npgsql => npgsql.UseQuerySplittingBehavior(QuerySplittingBehavior.SingleQuery))
    .ConfigureWarnings(w => w.Log((RelationalEventId.CommandError, LogLevel.Warning), (CoreEventId.SaveChangesFailed, LogLevel.Warning))));
builder.Services.AddDataProtection().PersistKeysToDbContext<TaskloomDbContext>().SetApplicationName("Taskloom");

// ---- Cloud adapters, picked by config so the code never changes between clouds ----
switch ((config["FileStore:Provider"] ?? "Local").ToLowerInvariant())
{
    case "azure":
        builder.Services.AddAzureBlobFileStore(config);
        break;
    default:
        var root = config["FileStore:Local:Root"];
        if (string.IsNullOrWhiteSpace(root)) root = Path.Combine(builder.Environment.ContentRootPath, "data", "files");
        builder.Services.AddSingleton<IFileStore>(new LocalDiskFileStore(root));
        break;
}
// Features 2: the Inbox, its emails (Email:Provider "Log" or "Smtp") and the jobs that send them and the reminders.
builder.Services.AddTaskloomEmail(config);
builder.Services.AddSingleton(NotificationOptions.From(config));
builder.Services.AddScoped<NotifierFactory>();
builder.Services.AddTaskloomJobs();
builder.Services.AddAzureMonitorIfConfigured(config);

// ---- Auth ----
builder.Services.AddTaskloomAuth(config);
builder.Services.AddTaskloomRateLimits(config);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<Access>();

// Enums are their C# names on the wire (spec 5.1); numbers are refused so undefined values can't be stored.
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false)));
// Unreadable bodies throw in every environment, so the middleware below answers with the same ProblemDetails 400.
builder.Services.Configure<RouteHandlerOptions>(o => o.ThrowOnBadRequest = true);
// A 413 the server gives without a message (an over-long body cut off while it is read) gets the one RequestLimits gives.
builder.Services.AddProblemDetails(o => o.CustomizeProblemDetails = c =>
{
    if (c.ProblemDetails.Status == StatusCodes.Status413PayloadTooLarge) c.ProblemDetails.Detail ??= RequestLimits.MessageFor(c.HttpContext);
});
builder.Services.AddHealthChecks();
builder.Services.AddTaskloomCompression();

// Azure Container Apps (and most hosts) terminate TLS at an ingress proxy.
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
});

var app = builder.Build();

var authOptions = app.Services.GetRequiredService<AuthOptions>();
// Security 1: with Google on and no allowlist, any Google account could sign in and get a workspace.
if (authOptions.GoogleNeedsAllowlist)
{
    if (!app.Environment.IsDevelopment()) throw new InvalidOperationException(AuthOptions.NoAllowlistMessage);
    app.Logger.LogWarning(AuthOptions.NoAllowlistMessage);
}
if (authOptions.MicrosoftLacksTenant)
    app.Logger.LogWarning("Microsoft sign-in is off: set Auth:Microsoft:TenantId (ENTRA_TENANT_ID) to the Entra tenant id, a GUID.");
if (authOptions.DevLogin.Enabled && !app.Environment.IsDevelopment())
    app.Logger.LogWarning("Dev login is on outside Development: anyone who can reach this server can sign in as any email address. Set Auth:DevLogin:Enabled to false anywhere real.");

var notificationOptions = app.Services.GetRequiredService<NotificationOptions>();
if (notificationOptions.EmailConfigured && notificationOptions.PublicUrl.Length == 0)
    app.Logger.LogWarning("Emails go out without links: set App:PublicUrl to the address people open Taskloom at, like https://tasks.example.com.");

if (config.GetValue("Database:MigrateOnStartup", true))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<TaskloomDbContext>().Database.MigrateAsync();
}

app.UseForwardedHeaders();
// Efficiency 1: compresses what the build hasn't already (AppFiles); outermost, so error pages are compressed too.
app.UseResponseCompression();
// Spec 5.8: every HTML response (index.html and the SPA fallback) carries the Content-Security-Policy.
app.UseContentSecurityPolicyOnHtml();
// No response is ever to be sniffed into another type than its Content-Type says (attachments set this themselves too).
app.Use((ctx, next) =>
{
    ctx.Response.OnStarting(() =>
    {
        ctx.Response.Headers.XContentTypeOptions = "nosniff";
        return Task.CompletedTask;
    });
    return next(ctx);
});
app.UseExceptionHandler();
app.UseStatusCodePages();

// Map access failures and unreadable requests to status codes.
app.Use(async (ctx, next) =>
{
    try { await next(); }
    catch (BadHttpRequestException e) when (!ctx.Response.HasStarted)
    {
        var detail = e.StatusCode == StatusCodes.Status413PayloadTooLarge
            ? RequestLimits.MessageFor(ctx)
            : Problems.UnreadableMessage;
        await Results.Problem(detail: detail, statusCode: e.StatusCode).ExecuteAsync(ctx);
    }
    // A rule of spec 5.5 refused the request; nothing was saved.
    catch (ProblemException e) when (!ctx.Response.HasStarted)
    {
        await Results.Problem(detail: e.Message, statusCode: e.Status).ExecuteAsync(ctx);
    }
    catch (NotFoundException) { ctx.Response.StatusCode = 404; }
    catch (ForbiddenException) { ctx.Response.StatusCode = 403; }
    catch (UnauthorizedAccessException) { ctx.RespondUnauthorized(); }
    // A create with a client-generated id that is already taken (spec 5.1).
    catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } && !ctx.Response.HasStarted)
    {
        await Problems.Conflict("An item with this id already exists.").ExecuteAsync(ctx);
    }
    // Text PostgreSQL can't store, sent in any field: a NUL character, or half a surrogate pair. Nothing was saved.
    catch (Exception e) when (IsUnstorableText(e) && !ctx.Response.HasStarted)
    {
        await Problems.BadRequest(Problems.UnreadableMessage).ExecuteAsync(ctx);
    }
});

static bool IsUnstorableText(Exception? e)
{
    for (; e is not null; e = e.InnerException)
    {
        if (e is EncoderFallbackException or PostgresException
            { SqlState: PostgresErrorCodes.CharacterNotInRepertoire or PostgresErrorCodes.UntranslatableCharacter })
            return true;
    }
    return false;
}

app.UseRequestBodyLimits();
app.UseDefaultFiles();
// Efficiency 1: the build's compressed copies, and cache headers that let the browser keep /assets for a year.
var appFiles = AppFiles.StaticFileOptions();
app.UsePrecompressedAppFiles();
app.UseStaticFiles(appFiles);
// Static files are served above, so they are never rate limited. The sign-in limit covers the providers' callbacks,
// which UseAuthentication answers, so it comes first; the /api limit counts per user, so it needs the signed-in user.
app.UseSignInRateLimit();
app.UseAuthentication();
app.UseApiRateLimit();
app.UseAuthorization();

app.MapHealthChecks("/healthz");
app.MapAuthEndpoints();
// POST /jobs/run, for a scheduler; only there when Jobs:TriggerKey is set.
app.MapJobEndpoints();

var api = app.MapGroup("/api").RequireAuthorization().AddEndpointFilter<RequireTaskloomHeader>();
api.MapWorkspaceEndpoints();
api.MapAdminEndpoints();
api.MapTagEndpoints();
api.MapHierarchyEndpoints();
api.MapTaskEndpoints();
api.MapBulkEndpoints();
api.MapCommentEndpoints();
api.MapAttachmentEndpoints();
api.MapPhotoEndpoints();
api.MapNotificationEndpoints();
api.MapDashboardEndpoints();
// Development only: the path does not exist unless dev login is enabled.
if (authOptions.DevLogin.Enabled) api.MapDevEndpoints();
// Unknown /api paths are 404, never the SPA; known paths with the wrong method or content type are 405 or 415.
api.MapApiFallback();

// The Vue app handles every other route.
app.MapFallbackToFile("index.html", appFiles);

app.Run();

public partial class Program;
