using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Taskloom.Api.Auth;
using Taskloom.Api.Tests.Support;
using Taskloom.Domain;

namespace Taskloom.Api.Tests;

/// <summary>
/// Security 1, spec C: every sign-in creates a server-side session that its cookie names, and the cookie is checked
/// against it (here on every request, Auth:Session:RecheckSeconds=0), so signing out or a session's end stops even a
/// copied cookie.
/// </summary>
public class SessionTests(TaskloomFactory factory) : IClassFixture<TaskloomFactory>
{
    private Task<UserSession> SessionAsync(Guid id) => factory.DbAsync(db => db.UserSessions.AsNoTracking().SingleAsync(s => s.Id == id));

    private Task<List<UserSession>> SessionsOfAsync(string email) =>
        factory.DbAsync(db => db.UserSessions.AsNoTracking().Where(s => s.User!.Email == email).OrderBy(s => s.CreatedAt).ToListAsync());

    [Fact]
    public async Task Dev_login_creates_a_session_that_the_cookie_names()
    {
        var email = Builders.NewEmail();
        var agent = "Mozilla/5.0\t(X11; Linux)\u007f " + new string('a', 400);
        var cookie = await factory.DevLoginCookieAsync(email, agent);

        var session = await SessionAsync(factory.SessionId(cookie));
        var user = await factory.DbAsync(db => db.Users.AsNoTracking().SingleAsync(u => u.Email == email));
        Assert.Equal((user.Id, "dev"), (session.UserId, session.Provider));
        Assert.Equal(session.CreatedAt, session.LastSeenAt);
        Assert.Equal(session.CreatedAt.AddDays(30), session.ExpiresAt);
        Assert.Null(session.RevokedAt);
        Assert.InRange(session.CreatedAt, DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow);
        // The User-Agent without control characters, cut to 300 characters.
        Assert.Equal(("Mozilla/5.0(X11; Linux) " + new string('a', 400))[..300], session.UserAgent);

        // The cookie: who, how, which session; the sign-in counted as a check; it ends after 14 idle days, before the session.
        var ticket = factory.Unprotect(cookie);
        Assert.Equal(user.Id.ToString(), ticket.Principal.FindFirstValue(ClaimsExtensions.UserIdClaim));
        Assert.Equal("dev", ticket.Principal.FindFirstValue("taskloom:provider"));
        Assert.Equal(session.CreatedAt.ToUnixTimeSeconds().ToString(), ticket.Properties.Items[SessionService.CheckedAtItem]);
        Assert.Equal(session.ExpiresAt.ToUnixTimeSeconds().ToString(), ticket.Properties.Items[SessionService.EndsAtItem]);
        Assert.InRange(ticket.Properties.ExpiresUtc!.Value, session.CreatedAt.AddDays(14).AddSeconds(-1), session.CreatedAt.AddDays(14).AddSeconds(5));

        // Each check moves LastSeenAt and renews the cookie.
        using var client = factory.CookieClient(cookie);
        var res = await client.GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.True((await SessionAsync(session.Id)).LastSeenAt > session.LastSeenAt);
        var renewed = Cookies.SetCookie(res);
        Assert.False(string.IsNullOrEmpty(renewed));
        Assert.Equal(session.Id, factory.SessionId(renewed!));

        // A blank or missing User-Agent is stored as null.
        Assert.Null((await SessionAsync(factory.SessionId(await factory.DevLoginCookieAsync(email, " \t ")))).UserAgent);
    }

    [Fact]
    public async Task Each_sign_in_is_its_own_session_and_old_ended_ones_are_deleted()
    {
        var email = Builders.NewEmail();
        var first = factory.SessionId(await factory.DevLoginCookieAsync(email));
        var userId = (await SessionAsync(first)).UserId;

        // Ended (expired or revoked) more than seven days ago, ended recently, and live.
        var now = DateTimeOffset.UtcNow;
        UserSession Row(DateTimeOffset expires, DateTimeOffset? revoked) => new()
        {
            UserId = userId, Provider = "dev", CreatedAt = expires.AddDays(-30), LastSeenAt = expires.AddDays(-29), ExpiresAt = expires, RevokedAt = revoked,
        };
        var oldExpired = Row(now.AddDays(-8), null);
        var oldRevoked = Row(now.AddDays(20), now.AddDays(-8));
        var recentlyExpired = Row(now.AddDays(-6), null);
        var recentlyRevoked = Row(now.AddDays(20), now.AddDays(-6));
        await factory.DbAsync(async db =>
        {
            db.UserSessions.AddRange(oldExpired, oldRevoked, recentlyExpired, recentlyRevoked);
            await db.SaveChangesAsync();
        });

        var second = factory.SessionId(await factory.DevLoginCookieAsync(email));
        Assert.NotEqual(first, second);
        Assert.Equal(new[] { first, second, recentlyExpired.Id, recentlyRevoked.Id }.Order(), (await SessionsOfAsync(email)).Select(s => s.Id).Order());
    }

    [Fact]
    public async Task Signing_out_ends_the_session_so_a_copied_cookie_gets_401()
    {
        var cookie = await factory.DevLoginCookieAsync(Builders.NewEmail());
        using var browser = factory.CookieClient(cookie);
        using var copy = factory.CookieClient(cookie);
        Assert.Equal(HttpStatusCode.OK, (await copy.GetAsync("/api/me")).StatusCode);

        var logout = await browser.PostAsync("/auth/logout", null);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Equal("", Cookies.SetCookie(logout));
        Assert.NotNull((await SessionAsync(factory.SessionId(cookie))).RevokedAt);

        // The copy is refused (401 without a body, as for anyone signed out), and told to delete the cookie.
        var refused = await copy.GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        Assert.Equal("", await refused.Content.ReadAsStringAsync());
        Assert.Equal("", Cookies.SetCookie(refused));
        Assert.Equal(HttpStatusCode.Unauthorized, (await copy.PostAsJsonAsync("/api/workspaces", new { name = "After sign-out" })).StatusCode);

        // Signing out again with the dead cookie still answers 204.
        Assert.Equal(HttpStatusCode.NoContent, (await copy.PostAsync("/auth/logout", null)).StatusCode);
    }

    [Fact]
    public async Task A_cookie_from_before_sessions_gets_401()
    {
        var user = await factory.NewUserAsync();
        // What a cookie of the previous version holds: no session id.
        var legacy = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimsExtensions.UserIdClaim, user.Me.Id.ToString()),
            new Claim(ClaimTypes.Email, user.Me.Email),
            new Claim(ClaimTypes.Name, user.Me.DisplayName),
            new Claim("taskloom:provider", "dev"),
        ], CookieAuthenticationDefaults.AuthenticationScheme));
        using var client = factory.CookieClient(factory.Protect(legacy));
        var res = await client.GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
        Assert.Equal("", Cookies.SetCookie(res));
        // The app's page still loads (signed out), so it can show the sign-in page.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/auth/providers")).StatusCode);
    }

    [Fact]
    public async Task A_cookie_naming_someone_elses_or_an_unknown_session_gets_401()
    {
        var mine = await factory.NewUserAsync();
        var theirs = factory.Unprotect(await factory.DevLoginCookieAsync(Builders.NewEmail()));
        foreach (var sessionId in new[] { theirs.Principal.FindFirstValue(SessionService.SessionIdClaim)!, Guid.NewGuid().ToString(), "not-a-guid" })
        {
            var forged = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimsExtensions.UserIdClaim, mine.Me.Id.ToString()),
                new Claim(SessionService.SessionIdClaim, sessionId),
            ], CookieAuthenticationDefaults.AuthenticationScheme));
            using var client = factory.CookieClient(factory.Protect(forged));
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/me")).StatusCode);
        }
    }

    [Fact]
    public async Task Turning_dev_login_off_ends_dev_login_sessions()
    {
        // The same database, and so the same cookie keys, behind a server with dev login off: as when dev login is turned
        // off, or the data moves to a real deployment. A cookie from dev login is refused there; one from a provider isn't.
        await using var deployed = new NoDevLoginFactory();
        var user = new User { Email = Builders.NewEmail(), DisplayName = "Dee Dev" };
        var now = DateTimeOffset.UtcNow;
        UserSession Via(string provider) => new()
        {
            UserId = user.Id, Provider = provider, CreatedAt = now, LastSeenAt = now, ExpiresAt = now.AddDays(30),
        };
        var dev = Via(UserProvisioning.DevProvider);
        var google = Via("google");
        await deployed.DbAsync(async db =>
        {
            db.Users.Add(user);
            db.UserSessions.AddRange(dev, google);
            await db.SaveChangesAsync();
        });

        using var devBrowser = deployed.CookieClient(deployed.Protect(UserProvisioning.PrincipalFor(user, UserProvisioning.DevProvider, dev.Id)));
        var refused = await devBrowser.GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        Assert.Equal("", Cookies.SetCookie(refused));
        using var googleBrowser = deployed.CookieClient(deployed.Protect(UserProvisioning.PrincipalFor(user, "google", google.Id)));
        Assert.Equal(HttpStatusCode.OK, (await googleBrowser.GetAsync("/api/me")).StatusCode);
    }

    [Fact]
    public async Task A_session_past_its_end_gets_401_however_active()
    {
        var cookie = await factory.DevLoginCookieAsync(Builders.NewEmail());
        var sessionId = factory.SessionId(cookie);
        using var client = factory.CookieClient(cookie);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/me")).StatusCode);

        await factory.DbAsync(db => db.UserSessions.Where(s => s.Id == sessionId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ExpiresAt, DateTimeOffset.UtcNow.AddSeconds(-1))));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/me")).StatusCode);
    }

    [Fact]
    public async Task A_renewed_cookie_never_outlives_its_session()
    {
        var cookie = await factory.DevLoginCookieAsync(Builders.NewEmail());
        var sessionId = factory.SessionId(cookie);
        // Two days left: less than the 14 idle days a renewal would give.
        var ends = DateTimeOffset.UtcNow.AddDays(2).AddMilliseconds(750);
        await factory.DbAsync(db => db.UserSessions.Where(s => s.Id == sessionId).ExecuteUpdateAsync(s => s.SetProperty(x => x.ExpiresAt, ends)));

        using var client = factory.CookieClient(cookie);
        var res = await client.GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var renewed = factory.Unprotect(Cookies.SetCookie(res)!);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(ends.ToUnixTimeSeconds()), renewed.Properties.ExpiresUtc);
        Assert.True(renewed.Properties.ExpiresUtc <= ends);

        // A session with longer to go gets the full 14 idle days from now.
        var fresh = await factory.DevLoginCookieAsync(Builders.NewEmail());
        using var other = factory.CookieClient(fresh);
        var expires = factory.Unprotect(Cookies.SetCookie(await other.GetAsync("/api/me"))!).Properties.ExpiresUtc!.Value;
        Assert.InRange(expires, DateTimeOffset.UtcNow.AddDays(14).AddSeconds(-30), DateTimeOffset.UtcNow.AddDays(14));
    }

    [Fact]
    public async Task Attachment_downloads_check_the_session_but_keep_their_cache_headers()
    {
        var user = await factory.NewUserAsync();
        var task = await user.Client.CreateTaskAsync(user.List.Id, new { title = "Files" });
        var file = await user.Client.UploadAsync(task.Id, "a.txt", "hello"u8.ToArray());
        var cookie = await factory.DevLoginCookieAsync(user.Me.Email);
        using var client = factory.CookieClient(cookie);

        var res = await client.GetAsync($"/api/attachments/{file.Id}");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("private, max-age=31536000, immutable", res.Headers.NonValidated["Cache-Control"].ToString());
        Assert.Null(Cookies.SetCookie(res));

        var sessionId = factory.SessionId(cookie);
        await factory.DbAsync(db => db.UserSessions.Where(s => s.Id == sessionId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, DateTimeOffset.UtcNow)));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/api/attachments/{file.Id}")).StatusCode);
    }

    [Fact]
    public async Task Signing_out_other_devices_keeps_only_this_session()
    {
        var email = Builders.NewEmail();
        var here = await factory.DevLoginCookieAsync(email);
        var laptop = await factory.DevLoginCookieAsync(email);
        var phone = await factory.DevLoginCookieAsync(email);
        var someoneElse = await factory.DevLoginCookieAsync(Builders.NewEmail());
        using var client = factory.CookieClient(here);

        var res = await client.PostAsync("/api/me/sign-out-others", null);
        Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/me")).StatusCode);
        foreach (var other in new[] { laptop, phone })
        {
            using var otherClient = factory.CookieClient(other);
            Assert.Equal(HttpStatusCode.Unauthorized, (await otherClient.GetAsync("/api/me")).StatusCode);
        }
        using (var stranger = factory.CookieClient(someoneElse))
            Assert.Equal(HttpStatusCode.OK, (await stranger.GetAsync("/api/me")).StatusCode);
        Assert.Equal([factory.SessionId(here)], (await SessionsOfAsync(email)).Where(s => s.RevokedAt is null).Select(s => s.Id));

        // It needs a signed-in caller and the CSRF header, like every /api call.
        Assert.Equal(HttpStatusCode.Unauthorized, (await factory.CreateClient().PostAsync("/api/me/sign-out-others", null)).StatusCode);
        client.DefaultRequestHeaders.Remove(RequireTaskloomHeader.Header);
        Assert.Equal("Missing X-Taskloom header.", await (await client.PostAsync("/api/me/sign-out-others", null)).ProblemAsync(HttpStatusCode.BadRequest));
    }

    [Fact]
    public async Task With_a_recheck_interval_the_database_is_skipped_between_checks()
    {
        var clock = new TestClock();
        await using var app = new TaskloomFactory { TestServices = s => s.AddSingleton<TimeProvider>(clock) };
        app.Settings["Auth:Session:RecheckSeconds"] = "60";
        var cookie = await app.DevLoginCookieAsync(Builders.NewEmail());
        var sessionId = app.SessionId(cookie);
        using var client = app.CookieClient(cookie);
        var signedIn = await app.DbAsync(db => db.UserSessions.AsNoTracking().SingleAsync(s => s.Id == sessionId));

        // Within a minute of the sign-in (itself a check) the cookie is accepted without reading the session, even revoked.
        clock.Advance(TimeSpan.FromSeconds(30));
        var res = await client.GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Null(Cookies.SetCookie(res));
        await app.DbAsync(db => db.UserSessions.Where(s => s.Id == sessionId).ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, DateTimeOffset.UtcNow)));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/me")).StatusCode);
        Assert.Equal(signedIn.LastSeenAt, (await app.DbAsync(db => db.UserSessions.AsNoTracking().SingleAsync(s => s.Id == sessionId))).LastSeenAt);

        // Once the minute is up, the next request checks and is refused.
        clock.Advance(TimeSpan.FromSeconds(31));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/me")).StatusCode);

        // A live session's check renews the cookie with the new check time, which starts the next minute.
        var live = await app.DevLoginCookieAsync(Builders.NewEmail());
        using var liveClient = app.CookieClient(live);
        clock.Advance(TimeSpan.FromSeconds(61));
        var checkedRes = await liveClient.GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.OK, checkedRes.StatusCode);
        var renewed = app.Unprotect(Cookies.SetCookie(checkedRes)!);
        var now = clock.GetUtcNow().ToUnixTimeSeconds();
        Assert.InRange(long.Parse(renewed.Properties.Items[SessionService.CheckedAtItem]!), now - 2, now);
    }
}
