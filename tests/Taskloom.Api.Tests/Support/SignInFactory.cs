using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Taskloom.Api.Auth;

namespace Taskloom.Api.Tests.Support;

/// <summary>
/// The API with three OpenID Connect providers set up: Microsoft (one tenant), Google, and GitHub as "any other
/// provider". Each has a static configuration, so challenges never fetch metadata, and tests hand the handlers'
/// events fake principals instead of real tokens. Only addresses at example.com and contoso.com may sign in.
/// </summary>
public class SignInFactory : TaskloomFactory
{
    public static readonly Guid Tenant = Guid.Parse("6f2a9d3e-1c4b-4e8f-9a7d-2b5c8e1f0a34");
    public static readonly Guid OtherTenant = Guid.Parse("0b1c2d3e-4f50-4617-8293-a4b5c6d7e8f9");
    public const string AllowedDomain = "example.com";

    public SignInFactory()
    {
        Settings["Auth:Microsoft:TenantId"] = Tenant.ToString();
        Settings["Auth:Providers:microsoft:ClientId"] = "microsoft-client";
        Settings["Auth:Providers:microsoft:ClientSecret"] = "microsoft-secret";
        Settings["Auth:Providers:google:ClientId"] = "google-client";
        Settings["Auth:Providers:google:ClientSecret"] = "google-secret";
        Settings["Auth:Providers:github:DisplayName"] = "GitHub";
        Settings["Auth:Providers:github:Authority"] = "https://github.example";
        Settings["Auth:Providers:github:ClientId"] = "github-client";
        Settings["Auth:AllowedEmailDomains:0"] = AllowedDomain;
        Settings["Auth:AllowedEmailDomains:1"] = "contoso.com";
        TestServices = services =>
        {
            foreach (var key in new[] { "microsoft", "google", "github" })
                services.Configure<OpenIdConnectOptions>(AuthSetup.OidcSchemePrefix + key, o => o.Configuration = new OpenIdConnectConfiguration
                {
                    Issuer = $"https://{key}.example/issuer",
                    AuthorizationEndpoint = $"https://{key}.example/authorize",
                    TokenEndpoint = $"https://{key}.example/token",
                });
        };
    }

    public OpenIdConnectOptions OidcOptions(string providerKey) =>
        Services.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>().Get(AuthSetup.OidcSchemePrefix + providerKey);

    public SignInProvider Provider(string providerKey) =>
        Services.GetRequiredService<AuthOptions>().EnabledProviders().Single(p => p.Key == providerKey);

    /// <summary>
    /// Runs the provider's real OnTokenValidated event with a principal carrying <paramref name="claims"/>, as the
    /// handler does after validating a token. Returns the principal that would be signed in, or the redirect it sent.
    /// </summary>
    public async Task<TokenOutcome> ValidateTokenAsync(string providerKey, params (string Type, string Value)[] claims)
    {
        using var scope = Services.CreateScope();
        var schemes = scope.ServiceProvider.GetRequiredService<IAuthenticationSchemeProvider>();
        var scheme = await schemes.GetSchemeAsync(AuthSetup.OidcSchemePrefix + providerKey) ?? throw new InvalidOperationException($"no scheme for {providerKey}");
        var options = OidcOptions(providerKey);
        var http = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims.Select(c => new Claim(c.Type, c.Value)), "oidc"));
        var context = new TokenValidatedContext(http, scheme, options, principal, new AuthenticationProperties());
        await options.Events.TokenValidated(context);
        if (context.Result?.Handled == true)
            return new TokenOutcome(null, http.Response.Headers.Location.ToString());
        return new TokenOutcome(context.Principal, null);
    }
}

/// <param name="Principal">The cookie principal to sign in with, when the sign-in was accepted.</param>
/// <param name="Redirect">Where the browser was sent instead, when it was refused.</param>
public record TokenOutcome(ClaimsPrincipal? Principal, string? Redirect)
{
    public Guid? UserId => Principal?.FindFirst(ClaimsExtensions.UserIdClaim)?.Value is { } id ? Guid.Parse(id) : null;

    /// <summary>The ?error= message of a refusal redirect to the sign-in page.</summary>
    public string? Error => Redirect is { } r && r.StartsWith("/login?error=", StringComparison.Ordinal)
        ? Uri.UnescapeDataString(r["/login?error=".Length..])
        : null;
}
