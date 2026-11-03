using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Taskloom.Api.Tests;

/// <summary>
/// Runs the real API against a real PostgreSQL database (a fresh one per test run).
/// Point TASKLOOM_TEST_DB at a server you can create databases on; the default matches docker compose.
/// </summary>
public class TaskloomFactory : WebApplicationFactory<Program>
{
    private readonly string _db = $"taskloom_test_{Guid.NewGuid():N}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var server = Environment.GetEnvironmentVariable("TASKLOOM_TEST_DB")
                     ?? "Host=localhost;Port=5432;Username=taskloom;Password=taskloom";
        builder.UseEnvironment("Testing");
        // UseSetting (not ConfigureAppConfiguration) so values are visible while Program.cs builds services.
        builder.UseSetting("ConnectionStrings:Taskloom", $"{server};Database={_db}");
        builder.UseSetting("Auth:DevLogin:Enabled", "true");
        builder.UseSetting("FileStore:Provider", "Local");
        builder.UseSetting("FileStore:Local:Root", Path.Combine(Path.GetTempPath(), _db));
    }

    public override async ValueTask DisposeAsync()
    {
        using (var scope = Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<Infrastructure.TaskloomDbContext>().Database.EnsureDeletedAsync();
        await base.DisposeAsync();
    }

    /// <summary>A client signed in as the given email, sending the CSRF header on every call.</summary>
    public async Task<HttpClient> SignInAsync(string email)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        client.DefaultRequestHeaders.Add("X-Taskloom", "1");
        var res = await client.PostAsJsonAsync("/auth/dev-login", new { email, displayName = email.Split('@')[0] });
        res.EnsureSuccessStatusCode();
        return client;
    }
}
