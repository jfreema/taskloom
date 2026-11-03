using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace Taskloom.Api.Auth;

public static class AuthSetup
{
    public const string OidcSchemePrefix = "oidc-";

    public static IServiceCollection AddTaskloomAuth(this IServiceCollection services, IConfiguration config)
    {
        var options = config.GetSection("Auth").Get<AuthOptions>() ?? new AuthOptions();
        services.AddSingleton(options);
        services.AddScoped<UserProvisioning>();

        var auth = services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(o =>
            {
                o.Cookie.Name = "taskloom.auth";
                o.Cookie.HttpOnly = true;
                o.Cookie.SameSite = SameSiteMode.Lax;
                o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                o.ExpireTimeSpan = TimeSpan.FromDays(14);
                o.SlidingExpiration = true;
                // The SPA handles navigation; API calls get status codes, not redirects.
                o.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = 401; return Task.CompletedTask; };
                o.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = 403; return Task.CompletedTask; };
            });

        foreach (var (key, p) in options.Providers.Where(p => p.Value.IsConfigured))
        {
            var providerKey = key.ToLowerInvariant();
            auth.AddOpenIdConnect(OidcSchemePrefix + providerKey, string.IsNullOrEmpty(p.DisplayName) ? key : p.DisplayName, o =>
            {
                o.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                o.Authority = p.Authority;
                o.ClientId = p.ClientId;
                o.ClientSecret = p.ClientSecret;
                o.ResponseType = OpenIdConnectResponseType.Code;
                o.UsePkce = true;
                o.CallbackPath = $"/auth/callback/{providerKey}";
                o.Scope.Clear();
                o.Scope.Add("openid");
                o.Scope.Add("profile");
                o.Scope.Add("email");
                o.MapInboundClaims = false;
                o.SaveTokens = false;
                o.TokenValidationParameters.ValidateIssuer = p.ValidateIssuer;
                o.Events.OnTokenValidated = async ctx =>
                {
                    var principal = ctx.Principal!;
                    var subject = principal.FindFirstValue("sub") ?? principal.FindFirstValue("oid");
                    var email = principal.FindFirstValue("email") ?? principal.FindFirstValue("preferred_username");
                    var name = principal.FindFirstValue("name");
                    if (subject is null || email is null || !options.IsEmailAllowed(email))
                    {
                        ctx.Fail("This account is not allowed to sign in to Taskloom.");
                        return;
                    }
                    var provisioning = ctx.HttpContext.RequestServices.GetRequiredService<UserProvisioning>();
                    var user = await provisioning.FindOrCreateAsync(providerKey, subject, email, name);
                    ctx.Principal = UserProvisioning.PrincipalFor(user, providerKey);
                };
                o.Events.OnRemoteFailure = ctx =>
                {
                    ctx.Response.Redirect("/login?error=" + Uri.EscapeDataString(ctx.Failure?.Message ?? "Sign-in failed"));
                    ctx.HandleResponse();
                    return Task.CompletedTask;
                };
            });
        }

        services.AddAuthorization();
        return services;
    }

    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/auth");

        g.MapGet("/providers", (AuthOptions o) => new
        {
            devLogin = o.DevLogin.Enabled,
            providers = o.Providers.Where(p => p.Value.IsConfigured)
                .Select(p => new { key = p.Key.ToLowerInvariant(), displayName = string.IsNullOrEmpty(p.Value.DisplayName) ? p.Key : p.Value.DisplayName }),
        });

        g.MapGet("/login/{provider}", (string provider, string? returnUrl, AuthOptions o) =>
        {
            if (!o.Providers.TryGetValue(provider, out var p) || !p.IsConfigured) return Results.NotFound();
            var target = returnUrl is not null && returnUrl.StartsWith('/') && !returnUrl.StartsWith("//") ? returnUrl : "/";
            return Results.Challenge(new AuthenticationProperties { RedirectUri = target }, [OidcSchemePrefix + provider.ToLowerInvariant()]);
        });

        g.MapPost("/dev-login", async (DevLoginRequest req, AuthOptions o, UserProvisioning provisioning, HttpContext http) =>
        {
            if (!o.DevLogin.Enabled) return Results.NotFound();
            if (string.IsNullOrWhiteSpace(req.Email) || !req.Email.Contains('@'))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["email"] = ["Enter an email address."] });
            var email = req.Email.Trim().ToLowerInvariant();
            var user = await provisioning.FindOrCreateAsync("dev", email, email, req.DisplayName);
            await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, UserProvisioning.PrincipalFor(user, "dev"));
            return Results.Ok(new UserDto(user.Id, user.Email, user.DisplayName));
        }).AddEndpointFilter<RequireTaskloomHeader>();

        g.MapPost("/logout", async (HttpContext http) =>
        {
            await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.NoContent();
        }).AddEndpointFilter<RequireTaskloomHeader>();
    }
}

public record DevLoginRequest(string Email, string? DisplayName);

/// <summary>
/// CSRF guard: state-changing calls must carry an "X-Taskloom: 1" header. A cross-site form
/// can't set custom headers, and a cross-site fetch that tries triggers a CORS preflight we never allow.
/// </summary>
public class RequireTaskloomHeader : IEndpointFilter
{
    public const string Header = "X-Taskloom";

    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        var req = ctx.HttpContext.Request;
        if (!HttpMethods.IsGet(req.Method) && !HttpMethods.IsHead(req.Method) && req.Headers[Header] != "1")
            return ValueTask.FromResult<object?>(Results.Problem("Missing X-Taskloom header.", statusCode: 400));
        return next(ctx);
    }
}
