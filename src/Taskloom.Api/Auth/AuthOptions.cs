namespace Taskloom.Api.Auth;

/// <summary>Bound from the "Auth" config section (env vars like Auth__Providers__google__ClientId).</summary>
public class AuthOptions
{
    public DevLoginOptions DevLogin { get; set; } = new();

    /// <summary>
    /// OpenID Connect providers by key. Any OIDC provider works the same way:
    /// Google (Authority https://accounts.google.com) now,
    /// Microsoft Entra ID (Authority https://login.microsoftonline.com/{tenant}/v2.0) for the org rollout.
    /// A provider without a ClientId is ignored.
    /// </summary>
    public Dictionary<string, OidcProviderOptions> Providers { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>If either list is non-empty, only these emails / domains may sign in.</summary>
    public List<string> AllowedEmails { get; set; } = [];
    public List<string> AllowedEmailDomains { get; set; } = [];

    public bool IsEmailAllowed(string email)
    {
        if (AllowedEmails.Count == 0 && AllowedEmailDomains.Count == 0) return true;
        if (AllowedEmails.Any(e => string.Equals(e, email, StringComparison.OrdinalIgnoreCase))) return true;
        var at = email.LastIndexOf('@');
        return at > 0 && AllowedEmailDomains.Any(d => string.Equals(d.TrimStart('@'), email[(at + 1)..], StringComparison.OrdinalIgnoreCase));
    }
}

public class DevLoginOptions
{
    /// <summary>Lets anyone sign in by typing an email. Only for local development.</summary>
    public bool Enabled { get; set; }
}

public class OidcProviderOptions
{
    public string DisplayName { get; set; } = "";
    public string Authority { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    /// <summary>Entra multi-tenant authorities need issuer validation turned off; single-tenant leave true.</summary>
    public bool ValidateIssuer { get; set; } = true;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(Authority);
}
