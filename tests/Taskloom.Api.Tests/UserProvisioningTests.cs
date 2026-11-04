using Microsoft.EntityFrameworkCore;
using Taskloom.Api.Auth;
using Taskloom.Api.Tests.Support;
using Taskloom.Domain;

namespace Taskloom.Api.Tests;

/// <summary>
/// Provider sign-in (spec 5.8): the decision table of <c>UserProvisioning.ResolveAsync</c>, row by row, through each
/// provider's real token-validated event with a fake OpenID Connect principal.
/// </summary>
public class UserProvisioningTests(SignInFactory factory) : IClassFixture<SignInFactory>
{
    private const string Refused = "We couldn't verify your email address with {0}.";
    private const string NotAllowed = "This account is not allowed to sign in to Taskloom.";

    /// <summary>Who already has the email the provider sends.</summary>
    public enum Holder { Nobody, Pending, SignedIn }

    /// <summary>What the row expects: the existing user, a new one, or a refusal.</summary>
    public enum Outcome { ExistingUser, NewUser, RefusedUnverified, RefusedNotAllowed }

    /// <param name="Linked">The identity (provider, subject) is already linked to the holder (a signed-in user).</param>
    /// <param name="Claims">The token's claims besides <c>sub</c>, with {email} standing for the address.</param>
    /// <param name="Renamed">For an existing user: whether the provider's name replaces theirs.</param>
    public sealed record Row(string Provider, bool Linked, Holder Holder, (string Type, string Value)[] Claims, Outcome Expect,
        bool Renamed = false, string Domain = SignInFactory.AllowedDomain);

    private static readonly string Tid = SignInFactory.Tenant.ToString();
    private static readonly string OtherTid = SignInFactory.OtherTenant.ToString();

    public static readonly Dictionary<string, Row> Rows = new()
    {
        // Row 1: an identity already linked signs in as its user, whatever the email says.
        ["linked google identity with a verified email"] = new("google", true, Holder.SignedIn,
            [("email", "{email}"), ("email_verified", "true"), ("name", "Not Used")], Outcome.ExistingUser),
        ["linked google identity with an unverified email"] = new("google", true, Holder.SignedIn,
            [("email", "someone-else@example.com"), ("email_verified", "false")], Outcome.ExistingUser),
        ["linked identity of a provider that never vouches for emails"] = new("github", true, Holder.SignedIn,
            [("email", "{email}")], Outcome.ExistingUser),
        ["linked microsoft identity in the tenant"] = new("microsoft", true, Holder.SignedIn,
            [("tid", Tid), ("preferred_username", "{email}")], Outcome.ExistingUser),
        // Row 2: a verified email of a pending (invited) user links, and the provider's name replaces the placeholder.
        ["verified google email of a pending user"] = new("google", false, Holder.Pending,
            [("email", "{email}"), ("email_verified", "true"), ("name", "Provider Name")], Outcome.ExistingUser, Renamed: true),
        ["verified email of a pending user without a name claim"] = new("google", false, Holder.Pending,
            [("email", "{email}"), ("email_verified", "True")], Outcome.ExistingUser),
        ["microsoft preferred_username of a pending user"] = new("microsoft", false, Holder.Pending,
            [("tid", Tid), ("preferred_username", "{email}"), ("name", "Provider Name")], Outcome.ExistingUser, Renamed: true),
        // Row 3: a verified email of a user with other identities links to them; their name stays.
        ["verified google email of a signed-in user"] = new("google", false, Holder.SignedIn,
            [("email", "{email}"), ("email_verified", "true"), ("name", "Provider Name")], Outcome.ExistingUser),
        ["microsoft email of a signed-in user"] = new("microsoft", false, Holder.SignedIn,
            [("tid", Tid), ("email", "{email}"), ("preferred_username", "upn@contoso.com"), ("name", "Provider Name")], Outcome.ExistingUser),
        // Row 4: a verified email nobody has creates the user, the starter workspace and the link.
        ["verified google email of nobody"] = new("google", false, Holder.Nobody,
            [("email", "{email}"), ("email_verified", "true"), ("name", "Provider Name")], Outcome.NewUser),
        ["microsoft email of nobody without a name claim"] = new("microsoft", false, Holder.Nobody,
            [("tid", Tid), ("email", "{email}")], Outcome.NewUser),
        // Row 5: an email the provider doesn't vouch for is refused, and nothing is linked or created.
        ["unverified google email of nobody"] = new("google", false, Holder.Nobody,
            [("email", "{email}"), ("email_verified", "false"), ("name", "Provider Name")], Outcome.RefusedUnverified),
        ["google email without email_verified"] = new("google", false, Holder.Nobody,
            [("email", "{email}")], Outcome.RefusedUnverified),
        ["unverified google email of a pending user"] = new("google", false, Holder.Pending,
            [("email", "{email}"), ("email_verified", "false"), ("name", "Provider Name")], Outcome.RefusedUnverified),
        ["unverified google email of a signed-in user"] = new("google", false, Holder.SignedIn,
            [("email", "{email}"), ("email_verified", "false")], Outcome.RefusedUnverified),
        ["any other provider's email of a signed-in user"] = new("github", false, Holder.SignedIn,
            [("email", "{email}"), ("email_verified", "true")], Outcome.RefusedUnverified),
        ["verified google token without an email"] = new("google", false, Holder.Nobody,
            [("email_verified", "true"), ("name", "Provider Name")], Outcome.RefusedUnverified),
        // Microsoft sign-in is limited to one tenant.
        ["microsoft email of a signed-in user from another tenant"] = new("microsoft", false, Holder.SignedIn,
            [("tid", OtherTid), ("email", "{email}")], Outcome.RefusedUnverified),
        ["microsoft identity linked but from another tenant"] = new("microsoft", true, Holder.SignedIn,
            [("tid", OtherTid), ("email", "{email}")], Outcome.RefusedUnverified),
        ["microsoft email of a pending user without a tenant claim"] = new("microsoft", false, Holder.Pending,
            [("email", "{email}"), ("name", "Provider Name")], Outcome.RefusedUnverified),
        // IsEmailAllowed only ever sees a verified email: the one being linked, or the linked user's.
        ["verified email outside the allowed domains"] = new("google", false, Holder.Nobody,
            [("email", "{email}"), ("email_verified", "true")], Outcome.RefusedNotAllowed, Domain: "outsider.test"),
        ["linked user whose email is outside the allowed domains"] = new("google", true, Holder.SignedIn,
            [("email", "someone@example.com"), ("email_verified", "true")], Outcome.RefusedNotAllowed, Domain: "outsider.test"),
    };

    public static TheoryData<string> RowNames => new(Rows.Keys);

    [Theory]
    [MemberData(nameof(RowNames))]
    public async Task Sign_in_follows_the_decision_table(string name)
    {
        var row = Rows[name];
        var owner = await factory.NewUserAsync();
        var email = $"nia.okafor+{Guid.NewGuid():N}@{row.Domain}";
        var subject = $"sub-{Guid.NewGuid():N}";

        Guid? holderId = null;
        var holderName = "";
        switch (row.Holder)
        {
            case Holder.Pending:
                var invited = await owner.Client.InviteAsync(owner.Workspace.Id, email, WorkspaceRole.Member);
                (holderId, holderName) = (invited.UserId, invited.DisplayName);
                Assert.True(invited.Pending);
                break;
            case Holder.SignedIn:
                // Dev login checks no allow-list, so it can make a user the providers may not let in.
                var me = await (await (await factory.SignInAsync(email, "Dev Name")).GetAsync("/api/me")).ReadAsync<UserDto>();
                (holderId, holderName) = (me.Id, me.DisplayName);
                break;
        }
        if (row.Linked)
            await factory.DbAsync(async db =>
            {
                db.UserIdentities.Add(new UserIdentity { UserId = holderId!.Value, Provider = row.Provider, Subject = subject });
                await db.SaveChangesAsync();
            });

        var identitiesBefore = await IdentitiesAsync(email);
        var usersBefore = await factory.DbAsync(db => db.Users.CountAsync());
        var claims = row.Claims.Select(c => (c.Type, c.Value.Replace("{email}", email))).Prepend(("sub", subject)).ToArray();

        var outcome = await factory.ValidateTokenAsync(row.Provider, claims);

        var user = await factory.DbAsync(db => db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Email == email));
        switch (row.Expect)
        {
            case Outcome.ExistingUser:
                Assert.Null(outcome.Redirect);
                Assert.Equal(holderId, outcome.UserId);
                Assert.Equal(row.Renamed ? "Provider Name" : holderName, user!.DisplayName);
                Assert.Equal(user.DisplayName, outcome.Principal!.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value);
                // The identity is linked once: kept when it was, added when it wasn't.
                Assert.Equal(identitiesBefore.Append((row.Provider, subject)).Distinct().Order(), (await IdentitiesAsync(email)).Order());
                Assert.Equal(usersBefore, await factory.DbAsync(db => db.Users.CountAsync()));
                // A pending user already belongs to the inviting workspace, so no starter workspace is added.
                if (row.Holder == Holder.Pending)
                    Assert.Equal([owner.Workspace.Id], await WorkspacesOfAsync(user.Id));
                break;
            case Outcome.NewUser:
                Assert.Null(outcome.Redirect);
                Assert.Equal(user!.Id, outcome.UserId);
                var expectedName = row.Claims.Any(c => c.Type == "name") ? "Provider Name" : "Nia Okafor";
                Assert.Equal(expectedName, user.DisplayName);
                Assert.Equal([(row.Provider, subject)], await IdentitiesAsync(email));
                // The starter workspace: "<first name>'s Workspace" with Personal / Tasks and the default statuses.
                var workspaceId = Assert.Single(await WorkspacesOfAsync(user.Id));
                var starter = await factory.DbAsync(db => db.Workspaces.AsNoTracking()
                    .Where(w => w.Id == workspaceId)
                    .Select(w => new { w.Name, Spaces = w.Spaces.Select(s => s.Name + "/" + string.Join(",", s.Lists.Select(l => l.Name))).ToList(),
                        Role = w.Members.Single().Role, Statuses = w.Spaces.SelectMany(s => s.Lists).SelectMany(l => l.Statuses).Count() })
                    .SingleAsync());
                Assert.Equal((expectedName.Split(' ')[0] + "'s Workspace", WorkspaceRole.Owner, 4), (starter.Name, starter.Role, starter.Statuses));
                Assert.Equal(["Personal/Tasks"], starter.Spaces);
                break;
            default:
                var message = row.Expect == Outcome.RefusedNotAllowed ? NotAllowed : string.Format(Refused, factory.Provider(row.Provider).DisplayName);
                Assert.Null(outcome.Principal);
                Assert.Equal("/login?error=" + Uri.EscapeDataString(message), outcome.Redirect);
                Assert.Equal(message, outcome.Error);
                // Nothing is created, linked or renamed.
                Assert.Equal(usersBefore, await factory.DbAsync(db => db.Users.CountAsync()));
                Assert.Equal(identitiesBefore, await IdentitiesAsync(email));
                if (holderId is not null) Assert.Equal(holderName, user!.DisplayName);
                else Assert.Null(user);
                break;
        }
    }

    [Fact]
    public async Task Invited_member_takes_the_provider_name_on_first_sign_in()
    {
        var owner = await factory.NewUserAsync();
        var anaEmail = $"ana.moreno+{Guid.NewGuid():N}@example.com";
        var samEmail = $"sam+{Guid.NewGuid():N}@example.com";
        var ana = await owner.Client.InviteAsync(owner.Workspace.Id, anaEmail, WorkspaceRole.Member);
        var sam = await (await owner.Client.PostAsJsonAsync($"/api/workspaces/{owner.Workspace.Id}/members",
            new { email = samEmail, role = "Guest", displayName = "Sam Lee" })).ReadAsync<MemberDto>();
        Assert.Equal(("Ana Moreno", true), (ana.DisplayName, ana.Pending));
        Assert.Equal(("Sam Lee", true), (sam.DisplayName, sam.Pending));

        // Ana's first sign-in (Microsoft, in the tenant) links her invite and takes the provider's name.
        var first = await factory.ValidateTokenAsync("microsoft",
            ("sub", "ana-ms"), ("tid", SignInFactory.Tenant.ToString()), ("preferred_username", anaEmail.ToUpperInvariant()), ("name", "  Ana María Moreno  "));
        Assert.Equal(ana.UserId, first.UserId);
        async Task<MemberDto> Member(Guid id) => (await owner.Client.MembersAsync(owner.Workspace.Id)).Single(m => m.UserId == id);
        Assert.Equal(("Ana María Moreno", false, WorkspaceRole.Member), ((await Member(ana.UserId)).DisplayName, (await Member(ana.UserId)).Pending, (await Member(ana.UserId)).Role));

        // Later sign-ins, even a first one with another provider, keep the name she has now.
        var google = await factory.ValidateTokenAsync("google", ("sub", "ana-g"), ("email", anaEmail), ("email_verified", "true"), ("name", "Ana M."));
        Assert.Equal(ana.UserId, google.UserId);
        var again = await factory.ValidateTokenAsync("microsoft",
            ("sub", "ana-ms"), ("tid", SignInFactory.Tenant.ToString()), ("preferred_username", anaEmail), ("name", "Ana Changed"));
        Assert.Equal(ana.UserId, again.UserId);
        Assert.Equal("Ana María Moreno", (await Member(ana.UserId)).DisplayName);

        // Without a name claim the name from the invite stays.
        var samSignIn = await factory.ValidateTokenAsync("google", ("sub", "sam-g"), ("email", samEmail), ("email_verified", "true"));
        Assert.Equal(sam.UserId, samSignIn.UserId);
        Assert.Equal(("Sam Lee", false), ((await Member(sam.UserId)).DisplayName, (await Member(sam.UserId)).Pending));
        // Neither got a starter workspace: they already belong to the inviting one.
        Assert.Equal([owner.Workspace.Id], await WorkspacesOfAsync(ana.UserId));
        Assert.Equal([owner.Workspace.Id], await WorkspacesOfAsync(sam.UserId));

        // A name claim with control characters or over 200 characters is cleaned up rather than refused.
        var kimEmail = $"kim+{Guid.NewGuid():N}@example.com";
        var kim = await owner.Client.InviteAsync(owner.Workspace.Id, kimEmail, WorkspaceRole.Member);
        var longName = "Kim\u0000 " + new string('k', 300);
        Assert.Equal(kim.UserId, (await factory.ValidateTokenAsync("google", ("sub", "kim-g"), ("email", kimEmail), ("email_verified", "true"), ("name", longName))).UserId);
        Assert.Equal(("Kim " + new string('k', 196)), (await Member(kim.UserId)).DisplayName);
    }

    private Task<List<(string, string)>> IdentitiesAsync(string email) =>
        factory.DbAsync(async db => (await db.UserIdentities.AsNoTracking()
                .Where(i => db.Users.Any(u => u.Id == i.UserId && u.Email == email))
                .Select(i => new { i.Provider, i.Subject }).ToListAsync())
            .Select(i => (i.Provider, i.Subject)).Order().ToList());

    private Task<List<Guid>> WorkspacesOfAsync(Guid userId) =>
        factory.DbAsync(db => db.WorkspaceMembers.AsNoTracking().Where(m => m.UserId == userId).Select(m => m.WorkspaceId).ToListAsync());
}
