using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace Taskloom.Api.Auth;

public static class AuthSetup
{
    public const string OidcSchemePrefix = "oidc-";

    public static IServiceCollection AddTaskloomAuth(this IServiceCollection services, IConfiguration config)
    {
        var options = config.GetSection("Auth").Get<AuthOptions>() ?? new AuthOptions();
        services.AddSingleton(options);
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<UserProvisioning>();
        services.AddScoped<SessionService>();
        services.AddScoped<ProviderPhotos>();
        // Photos come only from the providers' own hosts, which answer directly: no redirects are followed.
        services.AddHttpClient(ProviderPhotos.ClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false });

        var auth = services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(o =>
            {
                o.Cookie.Name = "taskloom.auth";
                o.Cookie.HttpOnly = true;
                o.Cookie.SameSite = SameSiteMode.Lax;
                o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                o.ExpireTimeSpan = TimeSpan.FromDays(options.Session.IdleDays);
                o.SlidingExpiration = true;
                // The SPA handles navigation; API calls get status codes, not redirects.
                o.Events.OnRedirectToLogin = ctx => { ctx.HttpContext.RespondUnauthorized(); return Task.CompletedTask; };
                o.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = 403; return Task.CompletedTask; };
                // Spec C: the cookie names a session, which is checked against the database every RecheckSeconds.
                o.Events.OnValidatePrincipal = ctx => ctx.HttpContext.RequestServices.GetRequiredService<SessionService>().ValidateAsync(ctx);
            });
        // After AddCookie's own post-configuration, which creates the ticket format this wraps.
        services.AddSingleton<IPostConfigureOptions<CookieAuthenticationOptions>, CapCookiesAtSessionEnd>();

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
                // Look 2: Graph's /me/photo needs User.Read; the access token for it comes with the ID token.
                var graphPhoto = provider.Key == SignInProvider.MicrosoftKey && options.Microsoft.Photos;
                if (graphPhoto) o.Scope.Add("User.Read");
                o.MapInboundClaims = false;
                o.SaveTokens = false;
                o.TokenValidationParameters.ValidateIssuer = true;
                // Only the tenant's own issuer, whatever the metadata says.
                if (provider.Tenant is not null) o.TokenValidationParameters.ValidIssuer = provider.Authority;
                else if (provider.IsGoogleAuthority) o.TokenValidationParameters.ValidIssuers = [.. SignInProvider.GoogleIssuers];
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
                    var services = ctx.HttpContext.RequestServices;
                    var session = await services.GetRequiredService<SessionService>()
                        .CreateAsync(result.User.Id, provider.Key, ctx.Request.Headers.UserAgent, ctx.HttpContext.RequestAborted);
                    // Look 2: the provider's photo for the avatar, unless the person chose their own. Never fails the sign-in.
                    await services.GetRequiredService<ProviderPhotos>().RefreshAsync(
                        services.GetRequiredService<Taskloom.Infrastructure.TaskloomDbContext>(), result.User.Id, provider, ctx.Principal!,
                        graphPhoto ? ctx.TokenEndpointResponse?.AccessToken : null,
                        Clock.Now(), ctx.HttpContext.RequestAborted);
                    ctx.Principal = UserProvisioning.PrincipalFor(result.User, provider.Key, session.Id);
                    SessionService.Stamp(ctx.Properties!, session);
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

        g.MapPost("/dev-login", async (DevLoginRequest req, AuthOptions o, UserProvisioning provisioning, SessionService sessions, HttpContext http) =>
        {
            if (!o.DevLogin.Enabled) return Results.NotFound();
            var email = Emails.Normalize(req.Email);
            if (email is null) return Problems.BadRequest(Emails.InvalidMessage);
            // A blank name is guessed from the email (spec 5.5, NameGuess).
            var displayName = req.DisplayName?.Trim();
            if (displayName is { Length: > Limits.Name }) return Problems.BadRequest("Display names can be up to 200 characters.");
            var result = await provisioning.DevSignInAsync(email, displayName);
            if (result.User is not { } user) return Results.Problem(detail: result.Error, statusCode: StatusCodes.Status403Forbidden);
            var session = await sessions.CreateAsync(user.Id, UserProvisioning.DevProvider, http.Request.Headers.UserAgent, http.RequestAborted);
            var properties = new AuthenticationProperties();
            SessionService.Stamp(properties, session);
            await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
                UserProvisioning.PrincipalFor(user, UserProvisioning.DevProvider, session.Id), properties);
            return Results.Ok(UserDto.Of(user));
        }).AddEndpointFilter<RequireTaskloomHeader>();

        // The session ends first, so a copy of the cookie stops working too (at its next check).
        g.MapPost("/logout", async (HttpContext http, SessionService sessions) =>
        {
            if (SessionService.SessionIdOf(http.User) is { } sessionId) await sessions.RevokeAsync(sessionId, http.RequestAborted);
            await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.NoContent();
        }).AddEndpointFilter<RequireTaskloomHeader>();
    }
}

public record DevLoginRequest(string? Email, string? DisplayName);

/// <summary>Wraps the cookie scheme's ticket format so that no cookie outlives its session (<see cref="SessionCappedTicketFormat"/>).</summary>
public sealed class CapCookiesAtSessionEnd : IPostConfigureOptions<CookieAuthenticationOptions>
{
    public void PostConfigure(string? name, CookieAuthenticationOptions options)
    {
        if (name == CookieAuthenticationDefaults.AuthenticationScheme && options.TicketDataFormat is not SessionCappedTicketFormat)
            options.TicketDataFormat = new SessionCappedTicketFormat(options.TicketDataFormat);
    }
}

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
