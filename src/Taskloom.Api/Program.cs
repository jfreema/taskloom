using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Taskloom.Api;
using Taskloom.Api.Auth;
using Taskloom.Api.Endpoints;
using Taskloom.Application;
using Taskloom.Cloud.Azure;
using Taskloom.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

// ---- Data ----
var connectionString = config.GetConnectionString("Taskloom")
    ?? throw new InvalidOperationException("Set ConnectionStrings__Taskloom.");
// Single-task queries load assignees and tags in one query; queries returning several tasks opt into AsSplitQuery().
builder.Services.AddDbContext<TaskloomDbContext>(o => o.UseNpgsql(connectionString,
    npgsql => npgsql.UseQuerySplittingBehavior(QuerySplittingBehavior.SingleQuery)));
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
builder.Services.AddSingleton<IEmailSender, LoggingEmailSender>();
builder.Services.AddAzureMonitorIfConfigured(config);

// ---- Auth ----
builder.Services.AddTaskloomAuth(config);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<Access>();

// Enums are their C# names on the wire (spec 5.1); numbers are refused so undefined values can't be stored.
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false)));
// Unreadable bodies throw in every environment, so the middleware below answers with the same ProblemDetails 400.
builder.Services.Configure<RouteHandlerOptions>(o => o.ThrowOnBadRequest = true);
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();

// Azure Container Apps (and most hosts) terminate TLS at an ingress proxy.
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
});

var app = builder.Build();

if (config.GetValue("Database:MigrateOnStartup", true))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<TaskloomDbContext>().Database.MigrateAsync();
}

app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseStatusCodePages();

// Map access failures and unreadable requests to status codes.
app.Use(async (ctx, next) =>
{
    try { await next(); }
    catch (BadHttpRequestException e) when (!ctx.Response.HasStarted)
    {
        var detail = e.StatusCode == StatusCodes.Status413PayloadTooLarge
            ? "The request is too large. Files can be up to 25 MB."
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

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/healthz");
app.MapAuthEndpoints();

var api = app.MapGroup("/api").RequireAuthorization().AddEndpointFilter<RequireTaskloomHeader>();
api.MapWorkspaceEndpoints();
api.MapTagEndpoints();
api.MapHierarchyEndpoints();
api.MapTaskEndpoints();
api.MapBulkEndpoints();
api.MapCommentEndpoints();
api.MapAttachmentEndpoints();
// Development only: the path does not exist unless dev login is enabled.
if (app.Services.GetRequiredService<AuthOptions>().DevLogin.Enabled) api.MapDevEndpoints();
api.MapFallback(() => Results.NotFound());

// The Vue app handles every other route.
app.MapFallbackToFile("index.html");

app.Run();

public partial class Program;
