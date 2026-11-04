using System.Net;
using Taskloom.Api.Tests.Support;
using Taskloom.Domain;

namespace Taskloom.Api.Tests;

/// <summary>Workspace members (spec 5.4 #11–#14, 5.5 Members): invite names, the role guards, and unassigning on removal.</summary>
public class MemberTests(TaskloomFactory factory) : IClassFixture<TaskloomFactory>
{
    [Fact]
    public async Task Invites_use_the_given_name_or_a_guess_from_the_email()
    {
        var owner = await factory.NewUserAsync();
        var url = $"/api/workspaces/{owner.Workspace.Id}/members";
        var domain = $"{Guid.NewGuid():N}.example";
        async Task<MemberDto> Invite(object body) => await (await owner.Client.PostAsJsonAsync(url, body)).ReadAsync<MemberDto>();

        // A new address becomes a pending user named as the invite says (trimmed), with the email trimmed and in lower case.
        var nia = await Invite(new { email = $"  Nia.Okafor@{domain.ToUpperInvariant()} ", role = "Member", displayName = "  Nia O.  " });
        Assert.Equal(($"nia.okafor@{domain}", "Nia O.", WorkspaceRole.Member, true), (nia.Email, nia.DisplayName, nia.Role, nia.Pending));
        // Without a name (or with a blank one), the name is guessed from the address.
        Assert.Equal("Ana Moreno", (await Invite(new { email = $"ana.moreno@{domain}", role = "Member" })).DisplayName);
        Assert.Equal("Jsmith", (await Invite(new { email = $"jsmith@{domain}", role = "Guest", displayName = "   " })).DisplayName);
        Assert.Equal("J Doe", (await Invite(new { email = $"j_doe+test@{domain}", role = "Admin", displayName = (string?)null })).DisplayName);
        Assert.Equal("Kim Lee Park", (await Invite(new { email = $"kim-lee.park@{domain}", role = "Member" })).DisplayName);

        // Someone who already has an account keeps their name, and isn't pending once they have signed in.
        var known = await factory.NewUserAsync();
        var invitedKnown = await Invite(new { email = known.Me.Email, role = "Guest", displayName = "Someone Else" });
        Assert.Equal((known.Me.Id, known.Me.DisplayName, false), (invitedKnown.UserId, invitedKnown.DisplayName, invitedKnown.Pending));
        // A pending user invited to a second workspace keeps the first invite's name and stays pending.
        var other = await factory.NewUserAsync();
        var again = await (await other.Client.PostAsJsonAsync($"/api/workspaces/{other.Workspace.Id}/members",
            new { email = nia.Email, role = "Guest", displayName = "Another Name" })).ReadAsync<MemberDto>();
        Assert.Equal((nia.UserId, "Nia O.", true, WorkspaceRole.Guest), (again.UserId, again.DisplayName, again.Pending, again.Role));

        // The list is ordered by name, ignoring case, and says who is pending.
        var members = await owner.Client.MembersAsync(owner.Workspace.Id);
        Assert.Equal(members.Select(m => m.DisplayName).Order(StringComparer.OrdinalIgnoreCase), members.Select(m => m.DisplayName));
        Assert.Equal(7, members.Count);
        Assert.Equal(new[] { owner.Me.Id, known.Me.Id }.Order(), members.Where(m => !m.Pending).Select(m => m.UserId).Order());

        // Refusals: bad addresses, roles, names, and someone already in the workspace.
        foreach (var email in new[] { "ana@", "@example.com", "not an email", "a@b@example.com", "", "  " })
            Assert.Equal("Enter a valid email address.", await (await owner.Client.PostAsJsonAsync(url, new { email, role = "Member" })).ProblemAsync(HttpStatusCode.BadRequest));
        Assert.Equal("Enter a valid email address.", await (await owner.Client.PostAsJsonAsync(url, new { role = "Member" })).ProblemAsync(HttpStatusCode.BadRequest));
        Assert.Equal("Choose Admin, Member or Guest.", await (await owner.Client.PostAsJsonAsync(url, new { email = $"owner@{domain}", role = "Owner" })).ProblemAsync(HttpStatusCode.BadRequest));
        Assert.NotNull(await (await owner.Client.PostAsJsonAsync(url, new { email = $"boss@{domain}", role = "Boss" })).ProblemAsync(HttpStatusCode.BadRequest));
        Assert.Equal("Display names can be up to 200 characters.", await (await owner.Client.PostAsJsonAsync(url,
            new { email = $"long@{domain}", role = "Member", displayName = new string('n', 201) })).ProblemAsync(HttpStatusCode.BadRequest));
        Assert.Equal($"ana.moreno@{domain} is already a member of this workspace.", await (await owner.Client.PostAsJsonAsync(url,
            new { email = $"ANA.MORENO@{domain}", role = "Guest" })).ProblemAsync(HttpStatusCode.Conflict));
        Assert.Equal(members, await owner.Client.MembersAsync(owner.Workspace.Id));

        // Dev login guesses a blank display name the same way (5.4 #3), and keeps a typed one.
        var guessed = await (await (await factory.SignInAsync($"lee.chan+dev@{domain}", "  ")).GetAsync("/api/me")).ReadAsync<UserDto>();
        Assert.Equal("Lee Chan", guessed.DisplayName);
        var typed = await (await (await factory.SignInAsync($"typed@{domain}", "  Tara Typed ")).GetAsync("/api/me")).ReadAsync<UserDto>();
        Assert.Equal("Tara Typed", typed.DisplayName);
    }

    [Fact]
    public async Task Nobody_changes_their_own_role_or_the_owners()
    {
        var owner = await factory.NewUserAsync();
        var (ada, adaInfo) = await factory.JoinAsync(owner, WorkspaceRole.Admin, "Ada Admin");
        var (_, benInfo) = await factory.JoinAsync(owner, WorkspaceRole.Admin, "Ben Admin");
        var (max, maxInfo) = await factory.JoinAsync(owner, WorkspaceRole.Member, "Max Member");
        string Url(Guid userId) => $"/api/workspaces/{owner.Workspace.Id}/members/{userId}";
        async Task<Dictionary<Guid, WorkspaceRole>> RolesAsync() => (await owner.Client.MembersAsync(owner.Workspace.Id)).ToDictionary(m => m.UserId, m => m.Role);
        var roles = await RolesAsync();

        // Nobody changes their own role, the Owner included.
        Assert.Equal("You can't change your own role.", await (await ada.PatchAsJsonAsync(Url(adaInfo.UserId), new { role = "Member" })).ProblemAsync(HttpStatusCode.BadRequest));
        Assert.Equal("You can't change your own role.", await (await owner.Client.PatchAsJsonAsync(Url(owner.Me.Id), new { role = "Admin" })).ProblemAsync(HttpStatusCode.BadRequest));
        // Nobody changes the Owner, and nobody is made Owner.
        Assert.Equal("The workspace owner can't be changed.", await (await ada.PatchAsJsonAsync(Url(owner.Me.Id), new { role = "Member" })).ProblemAsync(HttpStatusCode.BadRequest));
        Assert.Equal("Choose Admin, Member or Guest.", await (await owner.Client.PatchAsJsonAsync(Url(maxInfo.UserId), new { role = "Owner" })).ProblemAsync(HttpStatusCode.BadRequest));
        Assert.Equal("Choose Admin, Member or Guest.", await (await ada.PatchAsJsonAsync(Url(benInfo.UserId), new { role = "Owner" })).ProblemAsync(HttpStatusCode.BadRequest));
        // Members can't change roles at all; someone who isn't a member is 404.
        Assert.Equal(HttpStatusCode.Forbidden, (await max.PatchAsJsonAsync(Url(benInfo.UserId), new { role = "Guest" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ada.PatchAsJsonAsync(Url(Guid.NewGuid()), new { role = "Member" })).StatusCode);
        Assert.Equal(roles, await RolesAsync());

        // Admins may change other Admins, and anyone below them.
        Assert.Equal(HttpStatusCode.NoContent, (await ada.PatchAsJsonAsync(Url(benInfo.UserId), new { role = "Member" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await ada.PatchAsJsonAsync(Url(maxInfo.UserId), new { role = "Guest" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.Client.PatchAsJsonAsync(Url(maxInfo.UserId), new { role = "Admin" })).StatusCode);
        roles = await RolesAsync();
        Assert.Equal((WorkspaceRole.Owner, WorkspaceRole.Admin, WorkspaceRole.Member, WorkspaceRole.Admin),
            (roles[owner.Me.Id], roles[adaInfo.UserId], roles[benInfo.UserId], roles[maxInfo.UserId]));

        // Nobody removes the Owner or themselves.
        Assert.Equal("The workspace owner can't be removed.", await (await ada.DeleteAsync(Url(owner.Me.Id))).ProblemAsync(HttpStatusCode.BadRequest));
        Assert.Equal("The workspace owner can't be removed.", await (await owner.Client.DeleteAsync(Url(owner.Me.Id))).ProblemAsync(HttpStatusCode.BadRequest));
        Assert.Equal("You can't remove yourself.", await (await ada.DeleteAsync(Url(adaInfo.UserId))).ProblemAsync(HttpStatusCode.BadRequest));
        Assert.Equal(HttpStatusCode.NotFound, (await ada.DeleteAsync(Url(Guid.NewGuid()))).StatusCode);
        Assert.Equal(roles, await RolesAsync());
        // An Admin removes another Admin.
        Assert.Equal(HttpStatusCode.NoContent, (await ada.DeleteAsync(Url(maxInfo.UserId))).StatusCode);
        Assert.DoesNotContain(maxInfo.UserId, (await RolesAsync()).Keys);
        Assert.Equal(HttpStatusCode.NotFound, (await max.GetAsync($"/api/workspaces/{owner.Workspace.Id}/members")).StatusCode);
    }

    [Fact]
    public async Task Removing_a_member_unassigns_them_in_that_workspace()
    {
        var owner = await factory.NewUserAsync();
        var (mia, miaInfo) = await factory.JoinAsync(owner, WorkspaceRole.Member, "Mia Member");
        var elsewhere = await factory.NewUserAsync();
        await elsewhere.Client.InviteAsync(elsewhere.Workspace.Id, miaInfo.Email, WorkspaceRole.Member);

        var here = await owner.Client.CreateTaskAsync(owner.List.Id, new { title = "Here", assigneeIds = new[] { miaInfo.UserId, owner.Me.Id } });
        var sub = await owner.Client.CreateTaskAsync(owner.List.Id, new { title = "Here too", parentTaskId = here.Id, assigneeIds = new[] { miaInfo.UserId } });
        var otherList = await owner.Client.CreateListAsync(owner.Space.Id, "Second list");
        var alsoHere = await owner.Client.CreateTaskAsync(otherList.Id, new { title = "Also here", assigneeIds = new[] { miaInfo.UserId } });
        var there = await elsewhere.Client.CreateTaskAsync(elsewhere.List.Id, new { title = "There", assigneeIds = new[] { miaInfo.UserId } });
        // Mia's comment stays after she leaves.
        await mia.PostAsJsonAsync($"/api/tasks/{here.Id}/comments", new { body = "On it" });

        Assert.Equal(HttpStatusCode.NoContent, (await owner.Client.DeleteAsync($"/api/workspaces/{owner.Workspace.Id}/members/{miaInfo.UserId}")).StatusCode);

        // Every assignment in this workspace is gone, the other assignee stays, and the other workspace is untouched.
        Assert.Equal([owner.Me.Id], (await owner.Client.TaskDetailAsync(here.Id)).Task.AssigneeIds);
        Assert.Empty((await owner.Client.TaskDetailAsync(sub.Id)).Task.AssigneeIds);
        Assert.Empty((await owner.Client.TaskDetailAsync(alsoHere.Id)).Task.AssigneeIds);
        Assert.Equal([miaInfo.UserId], (await elsewhere.Client.TaskDetailAsync(there.Id)).Task.AssigneeIds);
        Assert.Equal("Mia Member", Assert.Single((await owner.Client.TaskDetailAsync(here.Id)).Comments).Author.DisplayName);
        // Her Home in this workspace is empty for her now: she isn't a member.
        Assert.DoesNotContain(miaInfo.UserId, (await owner.Client.MembersAsync(owner.Workspace.Id)).Select(m => m.UserId));
        Assert.Equal(HttpStatusCode.NotFound, (await mia.GetAsync($"/api/tasks/{here.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await mia.GetAsync($"/api/workspaces/{owner.Workspace.Id}/tasks")).StatusCode);
        Assert.Equal([elsewhere.Workspace.Id], (await mia.TreeAsync()).Select(w => w.Id));
        Assert.Equal([there.Id], (await mia.WorkspaceTasksAsync(elsewhere.Workspace.Id)).Tasks.Select(t => t.Id));

        // Invited back, she can be assigned again.
        await owner.Client.InviteAsync(owner.Workspace.Id, miaInfo.Email, WorkspaceRole.Member);
        Assert.Equal([owner.Me.Id, miaInfo.UserId], (await owner.Client.PatchTaskAsync(here.Id, new { assigneeIds = new[] { owner.Me.Id, miaInfo.UserId } })).AssigneeIds);
    }
}
