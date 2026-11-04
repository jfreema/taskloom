using System.Net;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.WebUtilities;
using Taskloom.Api.Auth;
using Taskloom.Api.Tests.Support;

namespace Taskloom.Api.Tests;

/// <summary>The public auth endpoints and the headers of the pages the API serves (spec 5.4 #1–#4, 5.8).</summary>
public class AuthTests(AuthTests.Site factory) : IClassFixture<AuthTests.Site>
{
    /// <summary>The three providers of <see cref="SignInFactory"/>, serving a stub build of the web app from a temporary folder.</summary>
    public sealed class Site : SignInFactory
    {
        public Site()
        {
            WebRoot = Directory.CreateTempSubdirectory("taskloom-site-").FullName;
            File.WriteAllText(Path.Combine(WebRoot, "index.html"),
                "<!doctype html><html><head><script src=\"/theme-init.js\"></script></head><body><div id=\"app\"></div></body></html>");
            File.WriteAllText(Path.Combine(WebRoot, "theme-init.js"), "document.documentElement.dataset.theme = 'light';");
        }

        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
            Directory.Delete(WebRoot!, recursive: true);
        }
    }

    [Fact]
    public async Task Providers_and_logout()
    {
        // Only the providers that are set up are listed: Microsoft (it has a tenant id), Google, then the others.
        var anonymous = factory.CreateClient(new() { AllowAutoRedirect = false });
        var listed = await (await anonymous.GetAsync("/auth/providers")).ReadAsync<AuthProvidersDto>();
        Assert.True(listed.DevLogin);
        Assert.Equal([("microsoft", "Microsoft"), ("google", "Google"), ("github", "GitHub")], listed.Providers.Select(p => (p.Key, p.DisplayName)));

        // Microsoft accepts tokens of the one tenant only, and every provider checks the issuer.
        var microsoft = factory.OidcOptions("microsoft");
        Assert.Equal($"https://login.microsoftonline.com/{SignInFactory.Tenant}/v2.0", microsoft.Authority);
        Assert.Equal(microsoft.Authority, microsoft.TokenValidationParameters.ValidIssuer);
        Assert.Equal("https://accounts.google.com", factory.OidcOptions("google").Authority);
        Assert.All(new[] { "microsoft", "google", "github" }, key => Assert.True(factory.OidcOptions(key).TokenValidationParameters.ValidateIssuer));

        // A listed provider's login challenges it (the key ignores case); an unknown one is 404.
        foreach (var key in new[] { "microsoft", "Google", "github" })
        {
            var challenge = await anonymous.GetAsync($"/auth/login/{key}");
            Assert.Equal(HttpStatusCode.Redirect, challenge.StatusCode);
            Assert.StartsWith($"https://{key.ToLowerInvariant()}.example/authorize?", challenge.Headers.Location!.AbsoluteUri);
        }
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync("/auth/login/okta")).StatusCode);

        // Microsoft without a tenant id, or with one that isn't a GUID, is not set up at all, and "dev" is never a provider.
        foreach (var tenant in new[] { "", "contoso.onmicrosoft.com", "organizations" })
        {
            await using var noTenant = new TaskloomFactory();
            noTenant.Settings["Database:MigrateOnStartup"] = "false";
            noTenant.Settings["Auth:Microsoft:TenantId"] = tenant;
            noTenant.Settings["Auth:Providers:microsoft:ClientId"] = "microsoft-client";
            noTenant.Settings["Auth:Providers:google:ClientId"] = "google-client";
            noTenant.Settings["Auth:Providers:dev:Authority"] = "https://dev.example";
            noTenant.Settings["Auth:Providers:dev:ClientId"] = "dev-client";
            var client = noTenant.CreateClient(new() { AllowAutoRedirect = false });
            var providers = await (await client.GetAsync("/auth/providers")).ReadAsync<AuthProvidersDto>();
            Assert.Equal(["google"], providers.Providers.Select(p => p.Key));
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/auth/login/microsoft")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/auth/login/dev")).StatusCode);
            var schemes = (await noTenant.Services.GetRequiredService<IAuthenticationSchemeProvider>().GetAllSchemesAsync()).Select(s => s.Name);
            Assert.Equal([AuthSetup.OidcSchemePrefix + "google"], schemes.Where(s => s.StartsWith(AuthSetup.OidcSchemePrefix, StringComparison.Ordinal)));
        }

        // As deployed: no dev login, and no provider until one has a client id.
        await using (var deployed = new NoDevLoginFactory())
        {
            deployed.Settings["Database:MigrateOnStartup"] = "false";
            var providers = await (await deployed.CreateClient().GetAsync("/auth/providers")).ReadAsync<AuthProvidersDto>();
            Assert.False(providers.DevLogin);
            Assert.Empty(providers.Providers);
        }

        // Logout needs the CSRF header, and ends the session.
        var signedIn = await factory.SignInAsync(Builders.NewEmail());
        signedIn.DefaultRequestHeaders.Remove(RequireTaskloomHeader.Header);
        Assert.Equal(HttpStatusCode.BadRequest, (await signedIn.PostAsync("/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await signedIn.GetAsync("/api/me")).StatusCode);
        var logout = await signedIn.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/auth/logout") { Headers = { { RequireTaskloomHeader.Header, "1" } } });
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Contains(logout.Headers.GetValues("Set-Cookie"), c => c.StartsWith("taskloom.auth=;", StringComparison.Ordinal));
        Assert.Equal(HttpStatusCode.Unauthorized, (await signedIn.GetAsync("/api/me")).StatusCode);
    }

    [Fact]
    public async Task Return_url_accepts_only_local_paths()
    {
        (string? Sent, string Kept)[] cases =
        [
            ("/l/3f1c/list?view=board", "/l/3f1c/list?view=board"),
            ("/", "/"),
            ("/t/abc#comments", "/t/abc#comments"),
            ("/search?next=//evil.example", "/search?next=//evil.example"),
            ("/%2F%2Fevil.example", "/%2F%2Fevil.example"),
            (null, "/"),
            ("", "/"),
            ("//evil.example", "/"),
            ("/\\evil.example", "/"),
            ("/a\\b", "/"),
            ("https://evil.example/", "/"),
            ("evil.example", "/"),
            (" /x", "/"),
            ("/x\ty", "/"),
            ("/x\r\nLocation: https://evil.example", "/"),
            ("/x\u007f", "/"),
            ("/x\u0085", "/"),
        ];
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        foreach (var (sent, kept) in cases)
        {
            Assert.Equal(kept, ReturnUrls.Safe(sent));
            // End to end: the challenge keeps the path in its protected state, and the callback redirects there.
            var res = await client.GetAsync("/auth/login/google" + (sent is null ? "" : "?returnUrl=" + Uri.EscapeDataString(sent)));
            Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);
            var authorize = res.Headers.Location!;
            Assert.StartsWith("https://google.example/authorize?", authorize.AbsoluteUri);
            var state = QueryHelpers.ParseQuery(authorize.Query)["state"].ToString();
            Assert.Equal(kept, factory.OidcOptions("google").StateDataFormat.Unprotect(state)!.RedirectUri);
        }
    }

    [Fact]
    public async Task Html_responses_carry_the_content_security_policy()
    {
        Assert.Equal("default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob:; " +
                     "connect-src 'self'; font-src 'self' data:; frame-ancestors 'none'; base-uri 'self'; form-action 'self'", ContentSecurityPolicy.Value);
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });

        // index.html and the SPA fallback, signed out and signed in, for GET and HEAD.
        var signedIn = await factory.SignInAsync(Builders.NewEmail());
        foreach (var c in new[] { client, signedIn })
        foreach (var path in new[] { "/", "/index.html", "/login?error=Nope", "/l/abc/list", $"/t/{Guid.NewGuid()}", "/settings/members" })
        foreach (var method in new[] { HttpMethod.Get, HttpMethod.Head })
        {
            var res = await c.SendAsync(new HttpRequestMessage(method, path));
            Assert.True(res.StatusCode == HttpStatusCode.OK, $"{method} {path}: {(int)res.StatusCode}");
            Assert.Equal("text/html", res.Content.Headers.ContentType?.MediaType);
            Assert.Equal(ContentSecurityPolicy.Value, Csp(res));
        }

        // Scripts, JSON and plain text don't carry it.
        foreach (var path in new[] { "/theme-init.js", "/healthz", "/auth/providers", "/api/me", "/api/no-such-thing", "/assets/missing.js" })
        {
            var res = await client.GetAsync(path);
            Assert.False(res.Content.Headers.ContentType?.MediaType == "text/html", $"{path} is HTML");
            Assert.Null(Csp(res));
        }
    }

    private static string? Csp(HttpResponseMessage res) =>
        res.Headers.TryGetValues("Content-Security-Policy", out var values) ? string.Join(", ", values) : null;
}
