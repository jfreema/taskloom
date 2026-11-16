using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Taskloom.Api.Tests.Support;

namespace Taskloom.Api.Tests;

/// <summary>
/// Security 1, spec F: sign-in routes are limited per client IP and /api per user, in one-minute windows. Each test
/// uses its own addresses (X-Forwarded-For, which the API trusts as configured), so their windows don't mix.
/// </summary>
public class RateLimitTests(RateLimitTests.App factory) : IClassFixture<RateLimitTests.App>
{
    private const string TooManySignIns = "Too many sign-in attempts. Wait a minute and try again.";
    private const string TooMany = "Too many requests. Wait a minute and try again.";

    /// <summary>The providers of <see cref="SignInFactory"/> with low limits, serving a stub web app.</summary>
    public sealed class App : SignInFactory
    {
        public const int SignInPerMinute = 3;
        public const int ApiPerMinute = 5;

        public App()
        {
            Settings["RateLimits:Enabled"] = "true";
            Settings["RateLimits:SignInPerMinute"] = SignInPerMinute.ToString();
            Settings["RateLimits:ApiPerMinute"] = ApiPerMinute.ToString();
            WebRoot = Directory.CreateTempSubdirectory("taskloom-limits-").FullName;
            File.WriteAllText(Path.Combine(WebRoot, "index.html"), "<!doctype html><html><body><div id=\"app\"></div></body></html>");
            File.WriteAllText(Path.Combine(WebRoot, "app.js"), "console.log('app');");
        }

        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
            Directory.Delete(WebRoot!, recursive: true);
        }
    }

    /// <summary>A client that doesn't follow redirects, coming from <paramref name="ip"/>.</summary>
    private HttpClient From(string ip, HttpClient? client = null)
    {
        client ??= factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("X-Forwarded-For", ip);
        return client;
    }

    private static async Task AssertTooManyAsync(HttpResponseMessage res)
    {
        Assert.Equal(TooMany, await res.ProblemAsync(HttpStatusCode.TooManyRequests));
        Assert.Equal("application/problem+json", res.Content.Headers.ContentType?.MediaType);
        var retryAfter = res.Headers.RetryAfter?.Delta;
        Assert.NotNull(retryAfter);
        Assert.InRange(retryAfter.Value.TotalSeconds, 1, 60);
        Assert.Equal(0, retryAfter.Value.Milliseconds);
    }

    [Fact]
    public async Task Sign_in_routes_are_limited_per_client_address()
    {
        var browser = From("198.51.100.10");
        for (var i = 0; i < App.SignInPerMinute; i++)
        {
            var challenge = await browser.GetAsync("/auth/login/google");
            Assert.Equal(HttpStatusCode.Redirect, challenge.StatusCode);
            Assert.StartsWith("https://google.example/authorize?", challenge.Headers.Location!.AbsoluteUri);
        }

        // A page load goes back to the sign-in page with the message, and so does a provider's callback (a form post).
        var limited = await browser.GetAsync("/auth/login/google");
        Assert.Equal(HttpStatusCode.Redirect, limited.StatusCode);
        Assert.Equal("/login?error=" + Uri.EscapeDataString(TooManySignIns), limited.Headers.Location!.OriginalString);
        var callback = await browser.PostAsync("/auth/callback/google", new FormUrlEncodedContent([new("code", "x"), new("state", "y")]));
        Assert.Equal("/login?error=" + Uri.EscapeDataString(TooManySignIns), callback.Headers.Location!.OriginalString);

        // Anything else gets the 429.
        await AssertTooManyAsync(await browser.PostAsync("/auth/login/google", null));

        // Another address is unaffected, and so are the provider list and dev login from this one.
        var neighbour = From("198.51.100.11");
        Assert.StartsWith("https://google.example/", (await neighbour.GetAsync("/auth/login/google")).Headers.Location!.AbsoluteUri);
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/auth/providers")).StatusCode);
    }

    [Fact]
    public async Task Signing_out_is_never_limited_so_it_always_ends_the_session()
    {
        // Spec F listed POST /auth/logout among the sign-in routes. It isn't one: anyone at the same address (a whole
        // office behind one NAT, or a page making the browser load /auth/login/x) could spend the address's permits,
        // and a refused sign-out would leave the session and its cookie working while the app showed the sign-in page.
        const string office = "198.51.100.40";
        var cookie = await factory.DevLoginCookieAsync(Builders.NewEmail());
        var someoneElse = From(office);
        for (var i = 0; i < App.SignInPerMinute; i++)
            Assert.Equal(HttpStatusCode.NotFound, (await someoneElse.GetAsync("/auth/login/nope")).StatusCode);
        Assert.Equal("/login?error=" + Uri.EscapeDataString(TooManySignIns), (await someoneElse.GetAsync("/auth/login/google")).Headers.Location!.OriginalString);

        using var browser = From(office, factory.CookieClient(cookie));
        var logout = await browser.PostAsync("/auth/logout", null);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Equal("", Cookies.SetCookie(logout));
        var sessionId = factory.SessionId(cookie);
        Assert.NotNull((await factory.DbAsync(db => db.UserSessions.AsNoTracking().SingleAsync(s => s.Id == sessionId))).RevokedAt);
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.GetAsync("/api/me")).StatusCode);
    }

    [Fact]
    public async Task Api_calls_are_limited_per_user()
    {
        const string office = "198.51.100.20";
        var ana = From(office, await factory.SignInAsync(Builders.NewEmail()));
        var ben = From(office, await factory.SignInAsync(Builders.NewEmail()));
        for (var i = 0; i < App.ApiPerMinute; i++)
            Assert.Equal(HttpStatusCode.OK, (await ana.GetAsync("/api/me")).StatusCode);
        await AssertTooManyAsync(await ana.GetAsync("/api/me"));
        await AssertTooManyAsync(await ana.PostAsJsonAsync("/api/workspaces", new { name = "Too many" }));

        // Someone else at the same address has their own allowance.
        Assert.Equal(HttpStatusCode.OK, (await ben.GetAsync("/api/me")).StatusCode);
        // The same person from another address has none left: it counts per user, not per address.
        ana.DefaultRequestHeaders.Remove("X-Forwarded-For");
        From("198.51.100.21", ana);
        await AssertTooManyAsync(await ana.GetAsync("/api/workspaces"));

        // Signed out, calls count per address.
        var anonymous = From("198.51.100.22");
        for (var i = 0; i < App.ApiPerMinute; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/me")).StatusCode);
        await AssertTooManyAsync(await anonymous.GetAsync("/api/me"));
        Assert.Equal(HttpStatusCode.Unauthorized, (await From("198.51.100.23").GetAsync("/api/me")).StatusCode);
    }

    [Fact]
    public async Task Health_checks_static_files_the_app_and_dev_login_are_never_limited()
    {
        const string ip = "198.51.100.30";
        var client = From(ip);
        var times = 3 * Math.Max(App.SignInPerMinute, App.ApiPerMinute);
        for (var i = 0; i < times; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/healthz")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/app.js")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/settings/people")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/auth/providers")).StatusCode);
            using var devLogin = new HttpRequestMessage(HttpMethod.Post, "/auth/dev-login")
            {
                Content = JsonContent.Create(new { email = Builders.NewEmail() }), Headers = { { "X-Taskloom", "1" } },
            };
            Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(devLogin)).StatusCode);
        }
    }
}
