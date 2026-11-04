namespace Taskloom.Api.Auth;

/// <summary>Bound from the "Auth" config section (env vars like Auth__Providers__google__ClientId).</summary>
public class AuthOptions
{
    public DevLoginOptions DevLogin { get; set; } = new();

    /// <summary>Microsoft Entra ID sign-in is limited to one tenant (spec 5.8): Auth:Microsoft:TenantId.</summary>
    public MicrosoftOptions Microsoft { get; set; } = new();

    /// <summary>
    /// OpenID Connect providers by key. A provider without a ClientId is ignored.
    /// "microsoft" is Microsoft Entra ID: its authority is always the tenant's
    /// (https://login.microsoftonline.com/{Microsoft:TenantId}/v2.0), and without a tenant id it is off.
    /// "google" (Authority https://accounts.google.com) vouches for an email when email_verified is true.
    /// Any other provider only signs in identities that are already linked (spec 5.8).
    /// </summary>
    public Dictionary<string, OidcProviderOptions> Providers { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>If either list is non-empty, only these emails / domains may sign in.</summary>
    public List<string> AllowedEmails { get; set; } = [];
    public List<string> AllowedEmailDomains { get; set; } = [];

    public bool IsEmailAllowed(string email)
    {
        if (AllowedEmails.Count == 0 && AllowedEmailDomains.Count == 0) return true;
        if (AllowedEmails.Any(e => string.Equals(e.Trim(), email, StringComparison.OrdinalIgnoreCase))) return true;
        var at = email.LastIndexOf('@');
        return at > 0 && AllowedEmailDomains.Any(d => string.Equals(d.Trim().TrimStart('@'), email[(at + 1)..], StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The providers that are registered and listed by /auth/providers: Microsoft first (only with a valid tenant
    /// id), then Google, then the others by key.
    /// </summary>
    public IReadOnlyList<SignInProvider> EnabledProviders()
    {
        var enabled = new List<SignInProvider>();
        foreach (var (rawKey, p) in Providers)
        {
            var key = rawKey.Trim().ToLowerInvariant();
            // "dev" names dev login's identities (subject = email), so no provider may sign in under it.
            if (key.Length == 0 || key == UserProvisioning.DevProvider || string.IsNullOrWhiteSpace(p.ClientId)) continue;
            var displayName = string.IsNullOrWhiteSpace(p.DisplayName) ? rawKey.Trim() : p.DisplayName.Trim();
            if (key == SignInProvider.MicrosoftKey)
            {
                if (Microsoft.Tenant is not { } tenant) continue;
                enabled.Add(new SignInProvider(key, displayName, MicrosoftOptions.AuthorityFor(tenant), p, tenant));
            }
            else if (!string.IsNullOrWhiteSpace(p.Authority))
            {
                enabled.Add(new SignInProvider(key, displayName, p.Authority.Trim(), p, null));
            }
        }
        return enabled
            .OrderBy(p => p.Key switch { SignInProvider.MicrosoftKey => 0, SignInProvider.GoogleKey => 1, _ => 2 })
            .ThenBy(p => p.Key, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>True when Microsoft sign-in has a client id but no usable tenant id, so it stays off.</summary>
    public bool MicrosoftLacksTenant =>
        Providers.TryGetValue(SignInProvider.MicrosoftKey, out var p) && !string.IsNullOrWhiteSpace(p.ClientId) && Microsoft.Tenant is null;
}

public class DevLoginOptions
{
    /// <summary>Lets anyone sign in by typing an email. Only for local development.</summary>
    public bool Enabled { get; set; }
}

public class MicrosoftOptions
{
    /// <summary>The Entra directory (tenant) id, a GUID. Empty turns Microsoft sign-in off.</summary>
    public string TenantId { get; set; } = "";

    /// <summary>The tenant id when it is a GUID; a domain name or "organizations" would not pin sign-in to one tenant.</summary>
    public Guid? Tenant => Guid.TryParse(TenantId?.Trim(), out var id) && id != Guid.Empty ? id : null;

    public static string AuthorityFor(Guid tenant) => $"https://login.microsoftonline.com/{tenant:D}/v2.0";
}

public class OidcProviderOptions
{
    public string DisplayName { get; set; } = "";
    /// <summary>The OpenID Connect issuer. Ignored for "microsoft", whose authority is the tenant's.</summary>
    public string Authority { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
}

/// <summary>A sign-in provider that is set up: its key (lower case, also the callback path), button name and authority.</summary>
/// <param name="Tenant">For Microsoft: the one tenant whose tokens are accepted.</param>
public sealed record SignInProvider(string Key, string DisplayName, string Authority, OidcProviderOptions Options, Guid? Tenant)
{
    public const string MicrosoftKey = "microsoft";
    public const string GoogleKey = "google";
    public const string GoogleAuthority = "https://accounts.google.com";

    /// <summary>Google documents both spellings of the issuer in its ID tokens.</summary>
    public static readonly IReadOnlyList<string> GoogleIssuers = [GoogleAuthority, "accounts.google.com"];

    public string Scheme => AuthSetup.OidcSchemePrefix + Key;

    /// <summary>The "google" provider pointed at Google itself.</summary>
    public bool IsGoogleAuthority => Key == GoogleKey && string.Equals(Authority.TrimEnd('/'), GoogleAuthority, StringComparison.OrdinalIgnoreCase);
}
