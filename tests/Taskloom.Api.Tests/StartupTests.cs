using System.Net;
using System.Reflection;
using Microsoft.Extensions.Logging;
using Taskloom.Api.Auth;
using Taskloom.Api.Tests.Support;

namespace Taskloom.Api.Tests;

/// <summary>
/// Security 1, spec A and B: Google sign-in needs an allowlist (or an explicit opt-out) before the app starts, and blank
/// list entries (what docker compose passes for an unset variable) are ignored everywhere.
/// </summary>
public class StartupTests
{
    private const string NoAllowlist =
        "Google sign-in is on, but no allowlist is set, so any Google account could sign in. Set Auth:AllowedEmails or " +
        "Auth:AllowedEmailDomains (for example Auth__AllowedEmails__0=you@gmail.com), or set Auth:AllowAnyAccount=true to allow any Google account.";

    /// <summary>An app with Google set up and nothing else, that doesn't touch a database; <paramref name="settings"/> are added.</summary>
    private static TaskloomFactory GoogleApp(params (string Key, string? Value)[] settings)
    {
        var factory = new NoDevLoginFactory();
        factory.Settings["Database:MigrateOnStartup"] = "false";
        factory.Settings["Auth:Providers:google:ClientId"] = "google-client";
        foreach (var (key, value) in settings) factory.Settings[key] = value;
        return factory;
    }

    /// <summary>Starts the app and returns the exception that stopped it, or null when it started.</summary>
    private static Exception? StartupFailure(TaskloomFactory factory)
    {
        try
        {
            _ = factory.Services;
            return null;
        }
        catch (Exception e)
        {
            // The entry point runs through reflection, which may wrap what it threw.
            while (e is TargetInvocationException or AggregateException && e.InnerException is not null) e = e.InnerException;
            return e;
        }
    }

    [Fact]
    public async Task Google_without_an_allowlist_refuses_to_start_outside_development()
    {
        Assert.Equal(NoAllowlist, AuthOptions.NoAllowlistMessage);
        foreach (var settings in new[]
                 {
                     Array.Empty<(string, string?)>(),
                     // Blank entries, as docker compose passes them for unset variables, are no allowlist.
                     [("Auth:AllowedEmails:0", ""), ("Auth:AllowedEmailDomains:0", "  ")],
                     [("Auth:AllowAnyAccount", "false"), ("Auth:SiteAdmins:0", "admin@example.com")],
                 })
        {
            await using var factory = GoogleApp(settings);
            var failure = StartupFailure(factory);
            var refused = Assert.IsType<InvalidOperationException>(failure);
            Assert.Equal(NoAllowlist, refused.Message);
        }
    }

    [Fact]
    public async Task Google_starts_with_an_allowlist_or_the_opt_out_and_microsoft_alone_needs_neither()
    {
        foreach (var settings in new[]
                 {
                     new (string, string?)[] { ("Auth:AllowedEmails:0", ""), ("Auth:AllowedEmails:1", "you@gmail.com") },
                     [("Auth:AllowedEmailDomains:0", "example.com")],
                     [("Auth:AllowAnyAccount", "true")],
                 })
        {
            await using var factory = GoogleApp(settings);
            Assert.Null(StartupFailure(factory));
            var providers = await (await factory.CreateClient().GetAsync("/auth/providers")).ReadAsync<AuthProvidersDto>();
            Assert.Equal(["google"], providers.Providers.Select(p => p.Key));
        }

        // Microsoft is pinned to one tenant, so it starts without an allowlist; so does a Google set up without a client id.
        await using var microsoft = new NoDevLoginFactory();
        microsoft.Settings["Database:MigrateOnStartup"] = "false";
        microsoft.Settings["Auth:Microsoft:TenantId"] = SignInFactory.Tenant.ToString();
        microsoft.Settings["Auth:Providers:microsoft:ClientId"] = "microsoft-client";
        Assert.Null(StartupFailure(microsoft));
        var listed = await (await microsoft.CreateClient().GetAsync("/auth/providers")).ReadAsync<AuthProvidersDto>();
        Assert.Equal(["microsoft"], listed.Providers.Select(p => p.Key));
    }

    [Fact]
    public async Task In_development_google_without_an_allowlist_starts_with_a_warning()
    {
        var logs = new LogCapture();
        await using var factory = new TaskloomFactory { TestServices = s => s.AddSingleton<ILoggerProvider>(logs) };
        factory.Settings["environment"] = "Development";
        factory.Settings["Database:MigrateOnStartup"] = "false";
        factory.Settings["Auth:Providers:google:ClientId"] = "google-client";
        Assert.Null(StartupFailure(factory));
        Assert.Equal(HttpStatusCode.OK, (await factory.CreateClient().GetAsync("/auth/providers")).StatusCode);
        Assert.Contains(logs.Entries, e => e.Level == LogLevel.Warning && e.Message == NoAllowlist);
    }

    [Fact]
    public void Blank_list_entries_are_ignored()
    {
        // Nothing but blanks: no allowlist, so anyone may sign in (and Google refuses to start, above), and no site admin.
        var blanks = new AuthOptions { AllowedEmails = ["", "  "], AllowedEmailDomains = [" "], SiteAdmins = ["", " "] };
        Assert.False(blanks.HasAllowlist);
        Assert.True(blanks.IsEmailAllowed("anyone@example.org"));
        Assert.False(blanks.IsSiteAdmin("anyone@example.org"));
        Assert.False(blanks.IsSiteAdmin(""));

        // Blanks next to real entries change nothing; entries match ignoring case and surrounding spaces.
        var mixed = new AuthOptions
        {
            AllowedEmails = ["", " Ana@Example.org "], AllowedEmailDomains = ["", "@contoso.com"], SiteAdmins = ["", " ADMIN@example.org"],
        };
        Assert.True(mixed.HasAllowlist);
        Assert.True(mixed.IsEmailAllowed("ana@example.org"));
        Assert.True(mixed.IsEmailAllowed("lee@CONTOSO.com"));
        Assert.False(mixed.IsEmailAllowed("lee@example.org"));
        Assert.True(mixed.IsSiteAdmin("admin@example.org"));
        Assert.False(mixed.IsSiteAdmin("ana@example.org"));
    }

    [Fact]
    public async Task A_rate_limit_of_zero_refuses_to_start_unless_rate_limiting_is_off()
    {
        foreach (var (key, message) in new[]
                 {
                     ("RateLimits:SignInPerMinute", "Set RateLimits:SignInPerMinute to 1 or more, or RateLimits:Enabled=false to turn rate limiting off."),
                     ("RateLimits:ApiPerMinute", "Set RateLimits:ApiPerMinute to 1 or more, or RateLimits:Enabled=false to turn rate limiting off."),
                 })
        {
            await using var on = new NoDevLoginFactory();
            on.Settings["Database:MigrateOnStartup"] = "false";
            on.Settings["RateLimits:Enabled"] = "true";
            on.Settings[key] = "0";
            Assert.Equal(message, Assert.IsType<InvalidOperationException>(StartupFailure(on)).Message);

            await using var off = new NoDevLoginFactory();
            off.Settings["Database:MigrateOnStartup"] = "false";
            off.Settings[key] = "0";
            Assert.Null(StartupFailure(off));
        }
    }

    [Fact]
    public async Task Settings_have_the_documented_defaults()
    {
        await using var factory = new NoDevLoginFactory();
        factory.Settings["Database:MigrateOnStartup"] = "false";
        // The test factory changes these two; without its settings they are the app's defaults.
        factory.Settings.Remove("Auth:Session:RecheckSeconds");
        factory.Settings.Remove("RateLimits:Enabled");
        var auth = factory.Services.GetRequiredService<AuthOptions>();
        Assert.Equal((false, 0, 60, 14, 30), (auth.AllowAnyAccount, auth.SiteAdmins.Count, auth.Session.RecheckSeconds, auth.Session.IdleDays, auth.Session.MaxDays));
        var limits = factory.Services.GetRequiredService<RateLimitOptions>();
        Assert.Equal((true, 20, 600), (limits.Enabled, limits.SignInPerMinute, limits.ApiPerMinute));
    }
}
