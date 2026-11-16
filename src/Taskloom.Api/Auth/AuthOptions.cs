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

    /// <summary>
    /// If either list has an entry, only these emails / domains may sign in. Blank entries are ignored here and in
    /// <see cref="SiteAdmins"/>: docker compose passes an empty string for a variable that isn't set.
    /// </summary>
    public List<string> AllowedEmails { get; set; } = [];
    public List<string> AllowedEmailDomains { get; set; } = [];

    /// <summary>
    /// Google signs in any Google account, so outside Development the app refuses to start with Google on and no
    /// allowlist (see <see cref="GoogleNeedsAllowlist"/>) unless this opts out on purpose.
    /// </summary>
    public bool AllowAnyAccount { get; set; }

    /// <summary>Emails of the people who may see every user, turn accounts off and on, and sign people out everywhere.</summary>
    public List<string> SiteAdmins { get; set; } = [];

    public SessionOptions Session { get; set; } = new();

    public const string NoAllowlistMessage =
        "Google sign-in is on, but no allowlist is set, so any Google account could sign in. Set Auth:AllowedEmails or " +
        "Auth:AllowedEmailDomains (for example Auth__AllowedEmails__0=you@gmail.com), or set Auth:AllowAnyAccount=true to allow any Google account.";

    /// <summary>The entries of a list setting that aren't blank, trimmed.</summary>
    private static IEnumerable<string> Entries(List<string> list) =>
        list.Where(e => !string.IsNullOrWhiteSpace(e)).Select(e => e.Trim());

    /// <summary>True when either allowlist has a (non-blank) entry.</summary>
    public bool HasAllowlist => Entries(AllowedEmails).Any() || Entries(AllowedEmailDomains).Any();

    public bool IsEmailAllowed(string email)
    {
        if (!HasAllowlist) return true;
        if (Entries(AllowedEmails).Any(e => string.Equals(e, email, StringComparison.OrdinalIgnoreCase))) return true;
        var at = email.LastIndexOf('@');
        return at > 0 && Entries(AllowedEmailDomains).Any(d => string.Equals(d.TrimStart('@'), email[(at + 1)..], StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The email is one of <see cref="SiteAdmins"/>, ignoring case.</summary>
    public bool IsSiteAdmin(string email) =>
        Entries(SiteAdmins).Any(e => string.Equals(e, email.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Google is on with neither allowlist nor <see cref="AllowAnyAccount"/>, so any Google account could sign in and get
    /// a workspace. Microsoft is pinned to one tenant and other providers only sign in identities that are already linked,
    /// so they don't count.
    /// </summary>
    public bool GoogleNeedsAllowlist =>
        !AllowAnyAccount && !HasAllowlist && EnabledProviders().Any(p => p.Key == SignInProvider.GoogleKey);

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

/// <summary>Auth:Session: how long a sign-in lasts and how often it is checked against the database.</summary>
public class SessionOptions
{
    /// <summary>
    /// A session is checked against its database row at most this often (0 = every request). In between, a cookie that
    /// was signed out elsewhere or belongs to a turned-off account still works, for up to this long.
    /// </summary>
    public int RecheckSeconds { get; set; } = 60;

    /// <summary>The cookie ends after this many days without use; every check moves the end along.</summary>
    public int IdleDays { get; set; } = 14;

    /// <summary>A session ends this many days after sign-in, however active it is.</summary>
    public int MaxDays { get; set; } = 30;
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
