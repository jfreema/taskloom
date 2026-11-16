using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Taskloom.Api.Tests.Support;
using Taskloom.Domain;

namespace Taskloom.Api.Tests;

/// <summary>
/// Security 1, spec D: site admins (Auth:SiteAdmins) list everyone, turn accounts off and on, and sign people out
/// everywhere; to anyone else the routes don't exist.
/// </summary>
public class SiteAdminTests(SiteAdminTests.App factory) : IClassFixture<SiteAdminTests.App>
{
    private const string TurnedOff = "This account has been turned off. Ask your Taskloom admin to turn it back on.";

    /// <summary>The API with two site admins (one written in capitals, next to a blank entry) and its log kept.</summary>
    public sealed class App : TaskloomFactory
    {
        public const string AdminEmail = "site.admin@example.com";
        public const string OtherAdminEmail = "other.admin@example.com";

        public LogCapture Logs { get; } = new();

        public App()
        {
            Settings["Auth:SiteAdmins:0"] = AdminEmail;
            Settings["Auth:SiteAdmins:1"] = "";
            Settings["Auth:SiteAdmins:2"] = " Other.Admin@Example.COM ";
            TestServices = s => s.AddSingleton<ILoggerProvider>(Logs);
        }
    }

    private Task<HttpClient> AdminAsync() => factory.SignInAsync(App.AdminEmail, "Site Admin");

    private static async Task<List<AdminUserDto>> UsersAsync(HttpClient admin) =>
        await (await admin.GetAsync("/api/admin/users")).ReadAsync<List<AdminUserDto>>();

    private static async Task<AdminUserDto> UserAsync(HttpClient admin, Guid id) => (await UsersAsync(admin)).Single(u => u.Id == id);

    [Fact]
    public async Task Me_says_whether_the_caller_is_a_site_admin()
    {
        var admin = await AdminAsync();
        var me = await (await admin.GetAsync("/api/me")).ReadAsync<MeDto>();
        Assert.Equal((App.AdminEmail, true), (me.Email, me.IsSiteAdmin));
        Assert.True((await (await admin.PatchAsJsonAsync("/api/me", new { displayName = "Site Admin" })).ReadAsync<MeDto>()).IsSiteAdmin);
        // Matched ignoring case and spaces.
        var other = await factory.SignInAsync(App.OtherAdminEmail);
        Assert.True((await (await other.GetAsync("/api/me")).ReadAsync<MeDto>()).IsSiteAdmin);

        var user = await factory.SignInAsync(Builders.NewEmail());
        Assert.False((await (await user.GetAsync("/api/me")).ReadAsync<MeDto>()).IsSiteAdmin);
        Assert.False((await (await user.PatchAsJsonAsync("/api/me", new { displayName = "Not Admin" })).ReadAsync<MeDto>()).IsSiteAdmin);
    }

    [Fact]
    public async Task The_admin_routes_are_404_to_everyone_else_and_change_nothing()
    {
        var admin = await AdminAsync();
        var target = await factory.NewUserAsync();
        var user = await factory.NewUserAsync();
        var before = await UserAsync(admin, target.Me.Id);

        foreach (var (method, path) in new[]
                 {
                     ("GET", "/api/admin/users"), ("POST", $"/api/admin/users/{target.Me.Id}/disable"),
                     ("POST", $"/api/admin/users/{target.Me.Id}/enable"), ("POST", $"/api/admin/users/{target.Me.Id}/sign-out"),
                 })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await user.Client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path))).StatusCode);
            // Signed out: 401, as everywhere under /api.
            var anonymous = await factory.CreateClient().SendAsync(new HttpRequestMessage(new HttpMethod(method), path) { Headers = { { "X-Taskloom", "1" } } });
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        }
        Assert.Equal(before with { LastSeenAt = null }, (await UserAsync(admin, target.Me.Id)) with { LastSeenAt = null });
        Assert.Equal(HttpStatusCode.OK, (await target.Client.GetAsync("/api/me")).StatusCode);
    }

    [Fact]
    public async Task The_list_shows_everyone_with_their_status_sessions_and_workspaces()
    {
        var admin = await AdminAsync();
        var zed = await factory.NewUserAsync();
        await (await zed.Client.PatchAsJsonAsync("/api/me", new { displayName = "Zed Admin-List" })).ReadAsync<MeDto>();
        // Zed: a second session, a third one signed out, and a second workspace.
        var zedLaptop = await factory.SignInAsync(zed.Me.Email);
        var zedOld = await factory.SignInAsync(zed.Me.Email);
        Assert.Equal(HttpStatusCode.NoContent, (await zedOld.PostAsync("/auth/logout", null)).StatusCode);
        await (await zedLaptop.PostAsJsonAsync("/api/workspaces", new { name = "Second" })).ReadAsync<WorkspaceTreeDto>();
        var invited = await (await zed.Client.PostAsJsonAsync($"/api/workspaces/{zed.Workspace.Id}/members",
            new { email = Builders.NewEmail(), role = "Guest", displayName = "Mia Admin-List" })).ReadAsync<MemberDto>();
        // Two names that are the same ignoring case.
        var ann = await (await (await factory.SignInAsync(Builders.NewEmail(), "ann Admin-List")).GetAsync("/api/me")).ReadAsync<MeDto>();
        var annUpper = await (await (await factory.SignInAsync(Builders.NewEmail(), "ANN Admin-List")).GetAsync("/api/me")).ReadAsync<MeDto>();

        var rows = await UsersAsync(admin);
        Assert.Equal(await factory.DbAsync(db => db.Users.CountAsync()), rows.Count);
        // By display name ignoring case, then by id.
        var anns = new[] { ann.Id, annUpper.Id }.OrderBy(id => id.ToString()).ToList();
        Guid[] expected = [anns[0], anns[1], invited.UserId, zed.Me.Id];
        Assert.Equal(expected, rows.Select(r => r.Id).Where(expected.Contains));

        var zedRow = rows.Single(r => r.Id == zed.Me.Id);
        Assert.Equal((zed.Me.Email, "Zed Admin-List", false, false, false, 2, 2),
            (zedRow.Email, zedRow.DisplayName, zedRow.Pending, zedRow.Disabled, zedRow.IsSiteAdmin, zedRow.ActiveSessions, zedRow.Workspaces));
        // "Last active" is the latest check of any of their sessions, the signed-out one included.
        Assert.Equal(await factory.DbAsync(db => db.UserSessions.Where(s => s.UserId == zed.Me.Id).MaxAsync(s => s.LastSeenAt)), zedRow.LastSeenAt);
        Assert.Equal(await factory.DbAsync(db => db.Users.Where(u => u.Id == zed.Me.Id).Select(u => u.CreatedAt).SingleAsync()), zedRow.CreatedAt);

        // Invited, never signed in: pending, never seen, in one workspace (the inviting one).
        var invitedRow = rows.Single(r => r.Id == invited.UserId);
        Assert.Equal((true, false, (DateTimeOffset?)null, 0, 1), (invitedRow.Pending, invitedRow.Disabled, invitedRow.LastSeenAt, invitedRow.ActiveSessions, invitedRow.Workspaces));

        var adminRow = rows.Single(r => r.Email == App.AdminEmail);
        Assert.True(adminRow.IsSiteAdmin);
        Assert.True(adminRow.ActiveSessions >= 1);
        Assert.True(rows.Single(r => r.Id == ann.Id).ActiveSessions == 1);
    }

    [Fact]
    public async Task Turning_an_account_off_signs_the_person_out_everywhere_until_it_is_turned_back_on()
    {
        var admin = await AdminAsync();
        var adminMe = await (await admin.GetAsync("/api/me")).ReadAsync<MeDto>();
        var owner = await factory.NewUserAsync();
        var (target, member) = await factory.JoinAsync(owner, WorkspaceRole.Member, "Tia Target");
        var targetLaptop = await factory.SignInAsync(member.Email);
        var task = await owner.Client.CreateTaskAsync(owner.List.Id, new { title = "Assigned", assigneeIds = new[] { member.UserId } });
        var comment = await (await target.PostAsJsonAsync($"/api/tasks/{task.Id}/comments", new { body = "Mine" })).ReadAsync<CommentDto>();

        var res = await admin.PostAsync($"/api/admin/users/{member.UserId}/disable", null);
        var off = await res.ReadAsync<AdminUserDto>();
        Assert.Equal((member.UserId, true, 0, false), (off.Id, off.Disabled, off.ActiveSessions, off.Pending));
        var disabledAt = await factory.DbAsync(db => db.Users.Where(u => u.Id == member.UserId).Select(u => u.DisabledAt).SingleAsync());
        Assert.NotNull(disabledAt);
        Assert.Equal(0, await factory.DbAsync(db => db.UserSessions.CountAsync(s => s.UserId == member.UserId && s.RevokedAt == null)));

        // Signed out on every browser, and dev login refuses them.
        Assert.Equal(HttpStatusCode.Unauthorized, (await target.GetAsync("/api/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await targetLaptop.GetAsync("/api/me")).StatusCode);
        var refused = await factory.CreateClient().SendAsync(new HttpRequestMessage(HttpMethod.Post, "/auth/dev-login")
        {
            Content = JsonContent.Create(new { email = member.Email }), Headers = { { "X-Taskloom", "1" } },
        });
        Assert.Equal(TurnedOff, await refused.ProblemAsync(HttpStatusCode.Forbidden));

        // They stay a member (shown as turned off), an assignee and the author of their comments.
        var members = await owner.Client.MembersAsync(owner.Workspace.Id);
        Assert.Equal((true, false), (members.Single(m => m.UserId == member.UserId).Disabled, members.Single(m => m.UserId == owner.Me.Id).Disabled));
        var detail = await owner.Client.TaskDetailAsync(task.Id);
        Assert.Equal([member.UserId], detail.Task.AssigneeIds);
        Assert.Equal(comment.Id, Assert.Single(detail.Comments).Id);

        // Turning it off again keeps the first time.
        Assert.True((await (await admin.PostAsync($"/api/admin/users/{member.UserId}/disable", null)).ReadAsync<AdminUserDto>()).Disabled);
        Assert.Equal(disabledAt, await factory.DbAsync(db => db.Users.Where(u => u.Id == member.UserId).Select(u => u.DisabledAt).SingleAsync()));

        // Turned back on: they can sign in again.
        var on = await (await admin.PostAsync($"/api/admin/users/{member.UserId}/enable", null)).ReadAsync<AdminUserDto>();
        Assert.False(on.Disabled);
        var back = await factory.SignInAsync(member.Email);
        Assert.Equal(HttpStatusCode.OK, (await back.GetAsync("/api/me")).StatusCode);
        Assert.False((await owner.Client.MembersAsync(owner.Workspace.Id)).Single(m => m.UserId == member.UserId).Disabled);
        Assert.Equal(1, (await UserAsync(admin, member.UserId)).ActiveSessions);

        // Each action is logged with who did what to whom.
        var logged = factory.Logs.Entries.Where(e => e.Level == LogLevel.Information && e.Message.Contains(member.UserId.ToString())).Select(e => e.Message).ToList();
        Assert.Contains(logged, m => m.Contains("turned off") && m.Contains(adminMe.Id.ToString()) && m.Contains(App.AdminEmail) && m.Contains(member.Email));
        Assert.Contains(logged, m => m.Contains("turned on") && m.Contains(adminMe.Id.ToString()) && m.Contains(App.AdminEmail) && m.Contains(member.Email));
    }

    [Fact]
    public async Task Site_admins_can_not_turn_off_themselves_or_each_other_and_unknown_people_are_404()
    {
        var admin = await AdminAsync();
        var adminMe = await (await admin.GetAsync("/api/me")).ReadAsync<MeDto>();
        var other = await factory.SignInAsync(App.OtherAdminEmail);
        var otherMe = await (await other.GetAsync("/api/me")).ReadAsync<MeDto>();

        Assert.Equal("You can't turn off your own account.",
            await (await admin.PostAsync($"/api/admin/users/{adminMe.Id}/disable", null)).ProblemAsync(HttpStatusCode.BadRequest));
        Assert.Equal("Site admins can't be turned off here. Remove them from Auth:SiteAdmins first.",
            await (await admin.PostAsync($"/api/admin/users/{otherMe.Id}/disable", null)).ProblemAsync(HttpStatusCode.BadRequest));
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await other.GetAsync("/api/me")).StatusCode);
        Assert.False((await UserAsync(admin, otherMe.Id)).Disabled);

        foreach (var action in new[] { "disable", "enable", "sign-out" })
            Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsync($"/api/admin/users/{Guid.NewGuid()}/{action}", null)).StatusCode);
    }

    [Fact]
    public async Task Signing_someone_out_everywhere_ends_all_their_sessions_including_your_own()
    {
        var admin = await AdminAsync();
        var target = await factory.NewUserAsync();
        var targetPhone = await factory.SignInAsync(target.Me.Email);

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/admin/users/{target.Me.Id}/sign-out", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await target.Client.GetAsync("/api/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await targetPhone.GetAsync("/api/me")).StatusCode);
        // Not turned off: they can sign in again.
        var row = await UserAsync(admin, target.Me.Id);
        Assert.Equal((false, 0), (row.Disabled, row.ActiveSessions));
        Assert.Equal(HttpStatusCode.OK, (await (await factory.SignInAsync(target.Me.Email)).GetAsync("/api/me")).StatusCode);
        Assert.Contains(factory.Logs.Entries, e => e.Level == LogLevel.Information && e.Message.Contains("out everywhere") &&
                                                   e.Message.Contains(target.Me.Id.ToString()) && e.Message.Contains(target.Me.Email));

        // Yourself: every session ends, this one included.
        var adminTablet = await AdminAsync();
        var adminId = (await (await admin.GetAsync("/api/me")).ReadAsync<MeDto>()).Id;
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/admin/users/{adminId}/sign-out", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await admin.GetAsync("/api/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await adminTablet.GetAsync("/api/me")).StatusCode);
    }
}
