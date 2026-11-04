using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;

namespace Taskloom.Api.Tests;

/// <summary>
/// Runs the real API against a real PostgreSQL database (a fresh one per test run).
/// Point TASKLOOM_TEST_DB at a server you can create databases on; the default matches docker compose.
/// </summary>
public class TaskloomFactory : WebApplicationFactory<Program>
{
    private readonly string _db = $"taskloom_test_{Guid.NewGuid():N}";

    /// <summary>The server part of the connection string, without a database.</summary>
    public static string DatabaseServer => Environment.GetEnvironmentVariable("TASKLOOM_TEST_DB")
                                   ?? "Host=localhost;Port=5432;Username=taskloom;Password=taskloom";

    /// <summary>Where the run's uploads go; deleted with the database.</summary>
    public string FilesRoot => Path.Combine(Path.GetTempPath(), _db);

    /// <summary>Dev login (and with it POST /api/dev/seed) is on unless a subclass turns it off.</summary>
    protected virtual bool DevLoginEnabled => true;

    /// <summary>Extra configuration ("Auth:Microsoft:TenantId" and the like), read when the app starts.</summary>
    public Dictionary<string, string?> Settings { get; } = [];

    /// <summary>Service registrations applied after the app's own, for example a static OpenID Connect configuration.</summary>
    public Action<IServiceCollection>? TestServices { get; init; }

    /// <summary>A folder to serve as wwwroot instead of the project's (which may hold no built index.html).</summary>
    public string? WebRoot { get; init; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        // UseSetting (not ConfigureAppConfiguration) so values are visible while Program.cs builds services.
        builder.UseSetting("ConnectionStrings:Taskloom", $"{DatabaseServer};Database={_db}");
        builder.UseSetting("Auth:DevLogin:Enabled", DevLoginEnabled ? "true" : "false");
        builder.UseSetting("FileStore:Provider", "Local");
        builder.UseSetting("FileStore:Local:Root", FilesRoot);
        foreach (var (key, value) in Settings) builder.UseSetting(key, value);
        if (WebRoot is not null) builder.UseWebRoot(WebRoot);
        if (TestServices is not null) builder.ConfigureTestServices(TestServices);
    }

    public override async ValueTask DisposeAsync()
    {
        using (var scope = Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<Infrastructure.TaskloomDbContext>().Database.EnsureDeletedAsync();
        await base.DisposeAsync();
        if (Directory.Exists(FilesRoot)) Directory.Delete(FilesRoot, recursive: true);
    }

    /// <summary>A client signed in as the given email, sending the CSRF header on every call.</summary>
    public async Task<HttpClient> SignInAsync(string email, string? displayName = null)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true, AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("X-Taskloom", "1");
        var res = await client.PostAsJsonAsync("/auth/dev-login", new { email, displayName = displayName ?? email.Split('@')[0] });
        res.EnsureSuccessStatusCode();
        return client;
    }
}

/// <summary>The API as deployed anywhere real: no dev login, so no dev endpoints.</summary>
public class NoDevLoginFactory : TaskloomFactory
{
    protected override bool DevLoginEnabled => false;
}
