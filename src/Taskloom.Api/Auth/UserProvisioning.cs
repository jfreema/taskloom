using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Taskloom.Application;
using Taskloom.Domain;
using Taskloom.Infrastructure;

namespace Taskloom.Api.Auth;

/// <summary>
/// What a provider says about the person signing in, read from its validated token (spec 5.8). Only Microsoft
/// (within the one tenant) and Google (with <c>email_verified</c>) vouch for an email; any other provider doesn't.
/// </summary>
/// <param name="InTenant">False for a Microsoft token from another tenant, which is refused whatever else it says.</param>
public sealed record ExternalIdentity(string? Subject, string? Email, bool EmailVerified, string? Name, bool InTenant)
{
    public static ExternalIdentity Read(SignInProvider provider, ClaimsPrincipal principal)
    {
        string? Claim(string type) => principal.FindFirst(type)?.Value is { } v && !string.IsNullOrWhiteSpace(v) ? v.Trim() : null;

        var subject = Claim("sub") ?? Claim("oid");
        var name = Claim("name");
        switch (provider.Key)
        {
            case SignInProvider.MicrosoftKey:
                // Within the one tenant the email claim (else preferred_username) counts as verified.
                var inTenant = provider.Tenant is { } tenant && Guid.TryParse(Claim("tid"), out var tid) && tid == tenant;
                return new(subject, Claim("email") ?? Claim("preferred_username"), inTenant, name, inTenant);
            case SignInProvider.GoogleKey:
                var verified = bool.TryParse(Claim("email_verified"), out var v) && v;
                return new(subject, Claim("email"), verified, name, true);
            default:
                return new(subject, Claim("email"), false, name, true);
        }
    }
}

/// <summary>The outcome of a provider sign-in: the user to sign in as, or the message to show on the sign-in page.</summary>
public sealed record SignInResult(User? User, string? Error)
{
    public static SignInResult Success(User user) => new(user, null);
    public static SignInResult Refused(string error) => new(null, error);
}

/// <summary>Maps a sign-in onto a Taskloom user, creating one (and a starter workspace) on first sign-in.</summary>
public class UserProvisioning(TaskloomDbContext db, AuthOptions options)
{
    public const string DevProvider = "dev";
    public const string NotAllowedMessage = "This account is not allowed to sign in to Taskloom.";

    /// <summary>Subjects longer than this don't fit user_identities."Subject".</summary>
    private const int MaxSubject = 255;

    public static string UnverifiedMessage(SignInProvider provider) => $"We couldn't verify your email address with {provider.DisplayName}.";

    /// <summary>
    /// The decision table of spec 5.8, for a principal the provider's handler has validated:
    /// <list type="number">
    /// <item>an identity (provider, subject) that is already linked signs in as its user;</item>
    /// <item>otherwise a verified email of a pending (invited) user links to it, and the provider's name replaces the placeholder name;</item>
    /// <item>a verified email of a user with other identities links to that user;</item>
    /// <item>a verified email nobody has creates the user (the name claim, else a guess from the email), the starter workspace and the link;</item>
    /// <item>an email the provider doesn't vouch for is refused, and nothing is linked or created.</item>
    /// </list>
    /// <see cref="AuthOptions.IsEmailAllowed"/> only ever sees a verified email: the linked user's, or the one being linked.
    /// </summary>
    public async Task<SignInResult> ResolveAsync(SignInProvider provider, ClaimsPrincipal principal, CancellationToken ct = default)
    {
        var id = ExternalIdentity.Read(provider, principal);
        var refused = SignInResult.Refused(UnverifiedMessage(provider));
        if (!id.InTenant || id.Subject is null || id.Subject.Length > MaxSubject) return refused;

        var linked = await db.UserIdentities.AsNoTracking()
            .Where(i => i.Provider == provider.Key && i.Subject == id.Subject)
            .Join(db.Users, i => i.UserId, u => u.Id, (_, u) => u)
            .FirstOrDefaultAsync(ct);
        if (linked is not null)
            return options.IsEmailAllowed(linked.Email) ? SignInResult.Success(linked) : SignInResult.Refused(NotAllowedMessage);

        var email = id.EmailVerified ? Emails.Normalize(id.Email) : null;
        if (email is null) return refused;
        if (!options.IsEmailAllowed(email)) return SignInResult.Refused(NotAllowedMessage);

        var name = CleanName(id.Name);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);
        if (user is null)
        {
            user = new User { Email = email, DisplayName = name ?? NameGuess.FromEmail(email) };
            db.Users.Add(user);
            AddStarterWorkspace(user);
        }
        else
        {
            var pending = !await db.UserIdentities.AnyAsync(i => i.UserId == user.Id, ct);
            if (pending && name is not null) user.DisplayName = name;
            if (!await db.WorkspaceMembers.AnyAsync(m => m.UserId == user.Id, ct)) AddStarterWorkspace(user);
        }
        db.UserIdentities.Add(new UserIdentity { UserId = user.Id, Provider = provider.Key, Subject = id.Subject });
        await db.SaveChangesAsync(ct);
        return SignInResult.Success(user);
    }

    /// <summary>
    /// Dev login (development only), unchanged by spec 5.8 apart from the name guess (5.4 #3): the typed email signs in
    /// as the user with that address, linking a "dev" identity, or creates the user. An existing user keeps their name.
    /// </summary>
    public async Task<User> DevSignInAsync(string email, string? displayName)
    {
        email = email.Trim().ToLowerInvariant();
        var identity = await db.UserIdentities.FirstOrDefaultAsync(i => i.Provider == DevProvider && i.Subject == email);
        if (identity is not null)
            return await db.Users.FirstAsync(u => u.Id == identity.UserId);

        // Same email from another provider (or an invite placeholder) links to the existing user.
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email);
        var isNew = user is null;
        if (user is null)
        {
            // The typed name as it is (the endpoint trims it and caps it at 200), else one guessed from the email.
            var name = displayName?.Trim();
            user = new User { Email = email, DisplayName = string.IsNullOrEmpty(name) ? NameGuess.FromEmail(email) : name };
            db.Users.Add(user);
        }
        db.UserIdentities.Add(new UserIdentity { UserId = user.Id, Provider = DevProvider, Subject = email });

        var hasWorkspace = !isNew && await db.WorkspaceMembers.AnyAsync(m => m.UserId == user.Id);
        if (!hasWorkspace) AddStarterWorkspace(user);

        await db.SaveChangesAsync();
        return user;
    }

    /// <summary>A provider's (or typed) name without control characters, trimmed and at most 200 characters; null when blank.</summary>
    private static string? CleanName(string? raw)
    {
        if (raw is null) return null;
        var name = string.Concat(raw.Where(c => !char.IsControl(c))).Trim();
        if (name.Length > Limits.Name) name = Text.Truncate(name, Limits.Name).TrimEnd();
        return name.Length == 0 ? null : name;
    }

    private void AddStarterWorkspace(User user)
    {
        const string suffix = "'s Workspace";
        var firstName = Text.Truncate(user.DisplayName.Split(' ')[0], Limits.Name - suffix.Length);
        var workspace = new Workspace { Name = firstName + suffix };
        var space = new Space { WorkspaceId = workspace.Id, Name = "Personal", Position = Positions.Step };
        var list = new TaskList { SpaceId = space.Id, Name = "Tasks", Position = Positions.Step };
        db.Workspaces.Add(workspace);
        db.WorkspaceMembers.Add(new WorkspaceMember { WorkspaceId = workspace.Id, UserId = user.Id, Role = WorkspaceRole.Owner });
        db.Spaces.Add(space);
        db.Lists.Add(list);
        db.Statuses.AddRange(ListStatus.Defaults(list.Id));
    }

    public static ClaimsPrincipal PrincipalFor(User user, string provider) =>
        new(new ClaimsIdentity(
        [
            new Claim(ClaimsExtensions.UserIdClaim, user.Id.ToString()),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Name, user.DisplayName),
            new Claim("taskloom:provider", provider),
        ], CookieAuthenticationDefaults.AuthenticationScheme));
}
