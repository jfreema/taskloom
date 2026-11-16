using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Taskloom.Domain;
using Taskloom.Infrastructure;

namespace Taskloom.Api.Auth;

/// <summary>
/// Server-side sessions (Security 1, spec C). Every sign-in creates a <see cref="UserSession"/> row whose id the cookie
/// carries, and the cookie is checked against that row at most every <see cref="SessionOptions.RecheckSeconds"/>, so
/// signing out, turning an account off or taking an email off the allowlist ends the cookie, even a copied one.
/// </summary>
public class SessionService(TaskloomDbContext db, AuthOptions options, TimeProvider time)
{
    public const string SessionIdClaim = "taskloom:sid";

    /// <summary>The cookie ticket's item holding when the session was last checked against the database, in Unix seconds.</summary>
    public const string CheckedAtItem = "taskloom:checked";

    /// <summary>The cookie ticket's item holding when the session ends (its ExpiresAt), in Unix seconds.</summary>
    public const string EndsAtItem = "taskloom:ends";

    /// <summary>Ended sessions are kept this long (for the admin's "Last active"), then deleted at the user's next sign-in.</summary>
    private static readonly TimeSpan KeepEnded = TimeSpan.FromDays(7);

    private const int MaxUserAgent = 300;

    /// <summary>
    /// Creates the session for a sign-in that was accepted. Also deletes the user's sessions that ended (expired or were
    /// revoked) more than a week ago.
    /// </summary>
    public async Task<UserSession> CreateAsync(Guid userId, string provider, string? userAgent, CancellationToken ct = default)
    {
        var now = Now();
        var cutoff = now - KeepEnded;
        await db.UserSessions.Where(s => s.UserId == userId && (s.ExpiresAt < cutoff || s.RevokedAt < cutoff)).ExecuteDeleteAsync(ct);
        var session = new UserSession
        {
            UserId = userId, Provider = provider, CreatedAt = now, LastSeenAt = now,
            ExpiresAt = now.AddDays(options.Session.MaxDays), UserAgent = CleanUserAgent(userAgent),
        };
        db.UserSessions.Add(session);
        await db.SaveChangesAsync(ct);
        return session;
    }

    /// <summary>
    /// Readies the properties of the cookie a sign-in sets: the sign-in counts as a check, and the cookie keeps the
    /// session's end so that it never outlives it (<see cref="SessionCappedTicketFormat"/>).
    /// </summary>
    public static void Stamp(AuthenticationProperties properties, UserSession session)
    {
        properties.Items[CheckedAtItem] = UnixSeconds(session.CreatedAt);
        properties.Items[EndsAtItem] = UnixSeconds(session.ExpiresAt);
    }

    /// <summary>
    /// The cookie handler's OnValidatePrincipal. A cookie without a session (issued before sessions existed) is refused;
    /// a cookie checked less than RecheckSeconds ago is accepted as it is; otherwise the session and its user are read in
    /// one query and the cookie is refused unless the session is live, the account is on and (except for dev login) the
    /// email is still allowed. A dev login session is refused once dev login is off: anyone who could reach dev login
    /// could sign in as anyone, so turning it off (or moving the database to a server without it) must end those
    /// sessions too. A refused cookie is deleted, and the API answers 401 as for anyone signed out.
    /// </summary>
    public async Task ValidateAsync(CookieValidatePrincipalContext ctx)
    {
        var principal = ctx.Principal;
        if (principal is null
            || !Guid.TryParse(principal.FindFirstValue(SessionIdClaim), out var sessionId)
            || !Guid.TryParse(principal.FindFirstValue(ClaimsExtensions.UserIdClaim), out var userId))
        {
            await RejectAsync(ctx);
            return;
        }

        var now = Now();
        var recheck = options.Session.RecheckSeconds;
        if (recheck > 0 && ctx.Properties.Items.TryGetValue(CheckedAtItem, out var raw)
            && long.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var checkedAt)
            && now.ToUnixTimeSeconds() - checkedAt < recheck)
            return;

        var session = await db.UserSessions.AsNoTracking()
            .Where(s => s.Id == sessionId)
            .Select(s => new { s.UserId, s.Provider, s.ExpiresAt, s.RevokedAt, s.User!.Email, s.User.DisabledAt })
            .FirstOrDefaultAsync(ctx.HttpContext.RequestAborted);
        if (session is null || session.UserId != userId || session.RevokedAt is not null || session.ExpiresAt <= now
            || session.DisabledAt is not null
            || (session.Provider == UserProvisioning.DevProvider ? !options.DevLogin.Enabled : !options.IsEmailAllowed(session.Email)))
        {
            await RejectAsync(ctx);
            return;
        }

        await db.UserSessions.Where(s => s.Id == sessionId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.LastSeenAt, now), ctx.HttpContext.RequestAborted);
        // The cookie handler marks a response that renews the cookie "no-cache, no-store", so a response that sets its
        // own caching keeps the cookie as it is: it was checked all the same, and the next request renews it.
        if (ctx.HttpContext.GetEndpoint()?.Metadata.GetMetadata<CacheableResponse>() is not null) return;
        ctx.Properties.Items[CheckedAtItem] = UnixSeconds(now);
        ctx.Properties.Items[EndsAtItem] = UnixSeconds(session.ExpiresAt);
        // A fresh IdleDays from now, capped at the session's end when the cookie is written.
        ctx.ShouldRenew = true;
    }

    /// <summary>Ends one session (signing out). A copied cookie stops working at its next check.</summary>
    public Task RevokeAsync(Guid sessionId, CancellationToken ct = default)
    {
        var now = Now();
        return db.UserSessions.Where(s => s.Id == sessionId && s.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, now), ct);
    }

    /// <summary>Ends every live session of the user, except <paramref name="keep"/> when given.</summary>
    public Task<int> RevokeAllAsync(Guid userId, Guid? keep = null, CancellationToken ct = default)
    {
        var now = Now();
        return db.UserSessions.Where(s => s.UserId == userId && s.RevokedAt == null && s.Id != keep)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, now), ct);
    }

    /// <summary>The session id the signed-in principal's cookie carries, if any.</summary>
    public static Guid? SessionIdOf(ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue(SessionIdClaim), out var id) ? id : null;

    private static async Task RejectAsync(CookieValidatePrincipalContext ctx)
    {
        ctx.RejectPrincipal();
        await ctx.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }

    /// <summary>
    /// Moves the cookie's expiry back to its session's end when it would be later. Unix seconds round down, so the
    /// cookie (which keeps its expiry to the second) never ends after the session.
    /// </summary>
    public static void CapAtSessionEnd(AuthenticationProperties properties)
    {
        if (!properties.Items.TryGetValue(EndsAtItem, out var raw)
            || !long.TryParse(raw, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var seconds))
            return;
        var ends = DateTimeOffset.FromUnixTimeSeconds(seconds);
        if (properties.ExpiresUtc is null || properties.ExpiresUtc > ends) properties.ExpiresUtc = ends;
    }

    private static string UnixSeconds(DateTimeOffset time) => time.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);

    /// <summary>Now, at the precision PostgreSQL keeps (microseconds), so a row reads back exactly as it was written.</summary>
    private DateTimeOffset Now()
    {
        var now = time.GetUtcNow();
        return now.AddTicks(-(now.Ticks % TimeSpan.TicksPerMicrosecond));
    }

    /// <summary>The User-Agent header without control characters, trimmed and at most 300 characters; null when blank.</summary>
    public static string? CleanUserAgent(string? raw)
    {
        if (raw is null) return null;
        var agent = Text.Truncate(string.Concat(raw.Where(c => !char.IsControl(c))).Trim(), MaxUserAgent).TrimEnd();
        return agent.Length == 0 ? null : agent;
    }
}

/// <summary>
/// Endpoint metadata: the response sets its own Cache-Control (attachment downloads), so a session check on it doesn't
/// renew the cookie, which would turn the response into "no-cache, no-store".
/// </summary>
public sealed class CacheableResponse;

/// <summary>
/// The cookie handler's ticket format, capping each ticket's expiry at its session's end however the handler writes it:
/// at sign-in, at a renewal after a check, or by sliding expiration (spec C: a renewed cookie never outlives its session).
/// </summary>
public sealed class SessionCappedTicketFormat(ISecureDataFormat<AuthenticationTicket> inner) : ISecureDataFormat<AuthenticationTicket>
{
    public string Protect(AuthenticationTicket data) => Protect(data, null);

    public string Protect(AuthenticationTicket data, string? purpose)
    {
        SessionService.CapAtSessionEnd(data.Properties);
        return inner.Protect(data, purpose);
    }

    public AuthenticationTicket? Unprotect(string? protectedText) => inner.Unprotect(protectedText);

    public AuthenticationTicket? Unprotect(string? protectedText, string? purpose) => inner.Unprotect(protectedText, purpose);
}
