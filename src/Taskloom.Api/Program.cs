using System.Text.Json.Serialization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
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
builder.Services.AddDbContext<TaskloomDbContext>(o => o.UseNpgsql(connectionString));
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

builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
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

// Map access failures to status codes.
app.Use(async (ctx, next) =>
{
    try { await next(); }
    catch (NotFoundException) { ctx.Response.StatusCode = 404; }
    catch (ForbiddenException) { ctx.Response.StatusCode = 403; }
    catch (UnauthorizedAccessException) { ctx.Response.StatusCode = 401; }
});

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/healthz");
app.MapAuthEndpoints();

var api = app.MapGroup("/api").RequireAuthorization().AddEndpointFilter<RequireTaskloomHeader>();
api.MapWorkspaceEndpoints();
api.MapTaskEndpoints();
api.MapFallback(() => Results.NotFound());

// The Vue app handles every other route.
app.MapFallbackToFile("index.html");

app.Run();

public partial class Program;
