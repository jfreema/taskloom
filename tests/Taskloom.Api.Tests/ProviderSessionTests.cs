using System.Net;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Taskloom.Api.Auth;
using Taskloom.Api.Tests.Support;
using Taskloom.Domain;

namespace Taskloom.Api.Tests;

/// <summary>
/// Security 1, spec C and D for provider sign-in: a session per sign-in, sessions that end when the email leaves the
/// allowlist (dev login's don't), and turned-off accounts refused whichever way they sign in.
/// </summary>
public class ProviderSessionTests(SignInFactory factory) : IClassFixture<SignInFactory>
{
    private const string Disabled = "This account has been turned off. Ask your Taskloom admin to turn it back on.";

    /// <summary>A verified Google sign-in for the email; the identity's subject is new unless given.</summary>
    private Task<TokenOutcome> GoogleAsync(string email, string? subject = null) =>
        factory.ValidateTokenAsync("google", ("sub", subject ?? $"sub-{Guid.NewGuid():N}"), ("email", email), ("email_verified", "true"));

    /// <summary>A client holding the cookie that the accepted provider sign-in would set.</summary>
    private HttpClient CookieClientFor(TokenOutcome outcome) => factory.CookieClient(factory.Protect(outcome.Principal!, outcome.Properties));

    [Fact]
    public async Task Provider_sign_in_creates_a_session_that_the_cookie_names()
    {
        var email = $"ana+{Guid.NewGuid():N}@{SignInFactory.AllowedDomain}";
        var outcome = await GoogleAsync(email);

        var sessionId = Guid.Parse(outcome.Principal!.FindFirstValue(SessionService.SessionIdClaim)!);
        var session = await factory.DbAsync(db => db.UserSessions.AsNoTracking().SingleAsync(s => s.Id == sessionId));
        Assert.Equal((outcome.UserId!.Value, "google", (string?)null), (session.UserId, session.Provider, session.UserAgent));
        Assert.Equal(session.CreatedAt.AddDays(30), session.ExpiresAt);
        Assert.Equal("google", outcome.Principal!.FindFirstValue("taskloom:provider"));
        Assert.Equal(session.CreatedAt.ToUnixTimeSeconds().ToString(), outcome.Properties!.Items[SessionService.CheckedAtItem]);

        using var client = CookieClientFor(outcome);
        Assert.Equal(email, (await (await client.GetAsync("/api/me")).ReadAsync<MeDto>()).Email);

        // A refused sign-in creates no session.
        var before = await factory.DbAsync(db => db.UserSessions.CountAsync());
        var refused = await factory.ValidateTokenAsync("google", ("sub", "x"), ("email", "nobody@outsider.test"), ("email_verified", "true"));
        Assert.NotNull(refused.Error);
        Assert.Equal(before, await factory.DbAsync(db => db.UserSessions.CountAsync()));
    }

    [Fact]
    public async Task Leaving_the_allowlist_ends_provider_sessions_at_the_next_check_but_not_dev_sessions()
    {
        var options = factory.Services.GetRequiredService<AuthOptions>();
        var email = $"kai+{Guid.NewGuid():N}@removed.test";
        options.AllowedEmails.Add(email);
        try
        {
            using var google = CookieClientFor(await GoogleAsync(email));
            // Dev login checks no allowlist, and its sessions are never ended by one.
            using var dev = factory.CookieClient(await factory.DevLoginCookieAsync(email));
            Assert.Equal(HttpStatusCode.OK, (await google.GetAsync("/api/me")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await dev.GetAsync("/api/me")).StatusCode);

            options.AllowedEmails.Remove(email);
            Assert.Equal(HttpStatusCode.Unauthorized, (await google.GetAsync("/api/me")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await dev.GetAsync("/api/me")).StatusCode);
            // Nor can they sign in with Google again.
            Assert.Equal(UserProvisioning.NotAllowedMessage, (await GoogleAsync(email)).Error);
        }
        finally
        {
            options.AllowedEmails.Remove(email);
        }
    }

    [Fact]
    public async Task A_turned_off_account_is_refused_by_every_provider_and_its_cookies_stop_working()
    {
        var email = $"lee+{Guid.NewGuid():N}@{SignInFactory.AllowedDomain}";
        const string subject = "lee-google";
        var signedIn = await GoogleAsync(email, subject);
        using var google = CookieClientFor(signedIn);
        using var dev = factory.CookieClient(await factory.DevLoginCookieAsync(email));
        var userId = signedIn.UserId!.Value;
        Assert.Equal(HttpStatusCode.OK, (await google.GetAsync("/api/me")).StatusCode);

        await factory.DbAsync(db => db.Users.Where(u => u.Id == userId).ExecuteUpdateAsync(s => s.SetProperty(u => u.DisabledAt, DateTimeOffset.UtcNow)));
        var identitiesBefore = await IdentitiesAsync(userId);
        var sessionsBefore = await factory.DbAsync(db => db.UserSessions.CountAsync(s => s.UserId == userId));

        // Their live cookies are refused at the next check.
        Assert.Equal(HttpStatusCode.Unauthorized, (await google.GetAsync("/api/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await dev.GetAsync("/api/me")).StatusCode);

        // Linked identity, verified email of another provider, and dev login: all refused with the message.
        var linked = await GoogleAsync(email, subject);
        Assert.Equal(Disabled, linked.Error);
        Assert.Equal("/login?error=" + Uri.EscapeDataString(Disabled), linked.Redirect);
        var byEmail = await factory.ValidateTokenAsync("microsoft",
            ("sub", "lee-microsoft"), ("tid", SignInFactory.Tenant.ToString()), ("email", email));
        Assert.Equal(Disabled, byEmail.Error);
        var devLogin = await factory.CreateClient().SendAsync(new HttpRequestMessage(HttpMethod.Post, "/auth/dev-login")
        {
            Content = JsonContent.Create(new { email }), Headers = { { RequireTaskloomHeader.Header, "1" } },
        });
        Assert.Equal(Disabled, await devLogin.ProblemAsync(HttpStatusCode.Forbidden));
        Assert.Null(Cookies.SetCookie(devLogin));

        // Nothing was linked, and no session was created.
        Assert.Equal(identitiesBefore, await IdentitiesAsync(userId));
        Assert.Equal(sessionsBefore, await factory.DbAsync(db => db.UserSessions.CountAsync(s => s.UserId == userId)));

        // A turned-off account that is only invited (never signed in) can't take its invite either.
        var owner = await factory.NewUserAsync($"owner+{Guid.NewGuid():N}@{SignInFactory.AllowedDomain}");
        var invitedEmail = $"ivy+{Guid.NewGuid():N}@{SignInFactory.AllowedDomain}";
        var invited = await owner.Client.InviteAsync(owner.Workspace.Id, invitedEmail, WorkspaceRole.Member);
        await factory.DbAsync(db => db.Users.Where(u => u.Id == invited.UserId).ExecuteUpdateAsync(s => s.SetProperty(u => u.DisabledAt, DateTimeOffset.UtcNow)));
        Assert.Equal(Disabled, (await GoogleAsync(invitedEmail)).Error);
        Assert.Empty(await IdentitiesAsync(invited.UserId));
    }

    private Task<List<string>> IdentitiesAsync(Guid userId) =>
        factory.DbAsync(db => db.UserIdentities.AsNoTracking().Where(i => i.UserId == userId).Select(i => i.Provider + ":" + i.Subject).OrderBy(x => x).ToListAsync());
}
