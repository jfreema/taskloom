using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Options;
using Taskloom.Api.Auth;

namespace Taskloom.Api.Tests.Support;

/// <summary>
/// The sign-in cookie as a value tests can hold on to: copy it to another client, read the ticket inside, or forge one
/// with the API's own data protection keys.
/// </summary>
public static class Cookies
{
    public const string Name = "taskloom.auth";

    private static CookieAuthenticationOptions Options(TaskloomFactory factory) =>
        factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(CookieAuthenticationDefaults.AuthenticationScheme);

    /// <summary>The cookie value the API would set for this principal and properties.</summary>
    public static string Protect(this TaskloomFactory factory, ClaimsPrincipal principal, AuthenticationProperties? properties = null) =>
        Options(factory).TicketDataFormat.Protect(new AuthenticationTicket(principal, properties ?? new AuthenticationProperties(),
            CookieAuthenticationDefaults.AuthenticationScheme));

    /// <summary>The ticket inside a cookie value.</summary>
    public static AuthenticationTicket Unprotect(this TaskloomFactory factory, string cookie) =>
        Options(factory).TicketDataFormat.Unprotect(cookie) ?? throw new InvalidOperationException("not a ticket of this API");

    /// <summary>
    /// A client that sends this cookie on every request and ignores the cookies the API sets back, like a copy of the
    /// cookie in another browser would. It sends the CSRF header and doesn't follow redirects.
    /// </summary>
    public static HttpClient CookieClient(this TaskloomFactory factory, string cookie)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false, AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("Cookie", $"{Name}={cookie}");
        client.DefaultRequestHeaders.Add(RequireTaskloomHeader.Header, "1");
        return client;
    }

    /// <summary>Dev login, returning the cookie it set instead of a client that keeps it.</summary>
    public static async Task<string> DevLoginCookieAsync(this TaskloomFactory factory, string email, string? userAgent = null)
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false, AllowAutoRedirect = false });
        using var req = new HttpRequestMessage(HttpMethod.Post, "/auth/dev-login") { Content = JsonContent.Create(new { email }) };
        req.Headers.Add(RequireTaskloomHeader.Header, "1");
        if (userAgent is not null) req.Headers.TryAddWithoutValidation("User-Agent", userAgent);
        var res = await client.SendAsync(req);
        res.EnsureSuccessStatusCode();
        return SetCookie(res) ?? throw new InvalidOperationException("dev login set no cookie");
    }

    /// <summary>The auth cookie a response set: null when it set none, "" when it deleted it.</summary>
    public static string? SetCookie(HttpResponseMessage res) =>
        res.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.Where(v => v.StartsWith(Name + "=", StringComparison.Ordinal)).Select(v => v[(Name.Length + 1)..].Split(';')[0]).LastOrDefault()
            : null;

    /// <summary>The session id a cookie carries.</summary>
    public static Guid SessionId(this TaskloomFactory factory, string cookie) =>
        Guid.Parse(factory.Unprotect(cookie).Principal.FindFirstValue(SessionService.SessionIdClaim)!);
}
