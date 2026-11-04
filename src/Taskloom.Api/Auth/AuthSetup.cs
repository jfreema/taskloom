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
                o.Events.OnRedirectToLogin = ctx => { ctx.HttpContext.RespondUnauthorized(); return Task.CompletedTask; };
                o.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = 403; return Task.CompletedTask; };
            });

        // Spec 5.8: Microsoft only with a tenant id (its authority is that tenant's), and every issuer validated.
        foreach (var provider in options.EnabledProviders())
        {
            auth.AddOpenIdConnect(provider.Scheme, provider.DisplayName, o =>
            {
                o.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                o.Authority = provider.Authority;
                o.ClientId = provider.Options.ClientId;
                o.ClientSecret = provider.Options.ClientSecret;
                o.ResponseType = OpenIdConnectResponseType.Code;
                o.UsePkce = true;
                o.CallbackPath = $"/auth/callback/{provider.Key}";
                o.Scope.Clear();
                o.Scope.Add("openid");
                o.Scope.Add("profile");
                o.Scope.Add("email");
                o.MapInboundClaims = false;
                o.SaveTokens = false;
                o.TokenValidationParameters.ValidateIssuer = true;
                // Only the tenant's own issuer, whatever the metadata says.
                if (provider.Tenant is not null) o.TokenValidationParameters.ValidIssuer = provider.Authority;
                o.Events.OnTokenValidated = async ctx =>
                {
                    var provisioning = ctx.HttpContext.RequestServices.GetRequiredService<UserProvisioning>();
                    var result = await provisioning.ResolveAsync(provider, ctx.Principal!, ctx.HttpContext.RequestAborted);
                    if (result.User is null)
                    {
                        // Refused (spec 5.8): back to the sign-in page, which shows the message. No cookie is set.
                        ctx.Response.Redirect(LoginErrorPath(result.Error!));
                        ctx.HandleResponse();
                        return;
                    }
                    ctx.Principal = UserProvisioning.PrincipalFor(result.User, provider.Key);
                };
                o.Events.OnRemoteFailure = ctx =>
                {
                    // The handler's own message can be technical (or name tenants and issuers), so it goes to the log only.
                    ctx.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(AuthSetup))
                        .LogWarning(ctx.Failure, "Sign-in with {Provider} failed", provider.DisplayName);
                    ctx.Response.Redirect(LoginErrorPath(FailedMessage(provider)));
                    ctx.HandleResponse();
                    return Task.CompletedTask;
                };
            });
        }

        services.AddAuthorization();
        return services;
    }

    /// <summary>The sign-in page showing <paramref name="message"/> in its "Couldn't sign you in" alert (spec 8.14).</summary>
    public static string LoginErrorPath(string message) => "/login?error=" + Uri.EscapeDataString(message);

    /// <summary>Shown when a provider sign-in fails for any reason other than the refusals of the decision table.</summary>
    public static string FailedMessage(SignInProvider provider) => $"Sign-in with {provider.DisplayName} didn't work. Try again.";

    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/auth");

        g.MapGet("/providers", (AuthOptions o) => new AuthProvidersDto(
            o.DevLogin.Enabled,
            o.EnabledProviders().Select(p => new AuthProviderDto(p.Key, p.DisplayName)).ToList()));

        g.MapGet("/login/{provider}", (string provider, string? returnUrl, AuthOptions o) =>
        {
            var p = o.EnabledProviders().FirstOrDefault(x => string.Equals(x.Key, provider, StringComparison.OrdinalIgnoreCase));
            if (p is null) return Results.NotFound();
            // Only local paths (spec 5.8); anything else returns to "/".
            return Results.Challenge(new AuthenticationProperties { RedirectUri = ReturnUrls.Safe(returnUrl) }, [p.Scheme]);
        });

        g.MapPost("/dev-login", async (DevLoginRequest req, AuthOptions o, UserProvisioning provisioning, HttpContext http) =>
        {
            if (!o.DevLogin.Enabled) return Results.NotFound();
            var email = Emails.Normalize(req.Email);
            if (email is null) return Problems.BadRequest(Emails.InvalidMessage);
            // A blank name is guessed from the email (spec 5.5, NameGuess).
            var displayName = req.DisplayName?.Trim();
            if (displayName is { Length: > Limits.Name }) return Problems.BadRequest("Display names can be up to 200 characters.");
            var user = await provisioning.DevSignInAsync(email, displayName);
            await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, UserProvisioning.PrincipalFor(user, UserProvisioning.DevProvider));
            return Results.Ok(new UserDto(user.Id, user.Email, user.DisplayName));
        }).AddEndpointFilter<RequireTaskloomHeader>();

        g.MapPost("/logout", async (HttpContext http) =>
        {
            await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.NoContent();
        }).AddEndpointFilter<RequireTaskloomHeader>();
    }
}

public record DevLoginRequest(string? Email, string? DisplayName);

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

/// <summary>
/// The Content-Security-Policy of spec 5.8, on every HTML response the API serves (the SPA's index.html and its
/// fallback). In development Vite serves the page instead. A response that already has a policy keeps it
/// (attachments are sent with "sandbox").
/// </summary>
public static class ContentSecurityPolicy
{
    public const string Value =
        "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob:; " +
        "connect-src 'self'; font-src 'self' data:; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";

    public static IApplicationBuilder UseContentSecurityPolicyOnHtml(this IApplicationBuilder app) =>
        app.Use((ctx, next) =>
        {
            ctx.Response.OnStarting(() =>
            {
                var response = ctx.Response;
                if (IsHtml(response.ContentType) && !response.Headers.ContainsKey(Microsoft.Net.Http.Headers.HeaderNames.ContentSecurityPolicy))
                    response.Headers.ContentSecurityPolicy = Value;
                return Task.CompletedTask;
            });
            return next(ctx);
        });

    private static bool IsHtml(string? contentType) =>
        contentType is not null && contentType.TrimStart().StartsWith("text/html", StringComparison.OrdinalIgnoreCase);
}
