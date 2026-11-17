using System.Net;
using Taskloom.Api.Endpoints;
using Taskloom.Api.Tests.Support;
using Taskloom.Domain;

namespace Taskloom.Api.Tests;

/// <summary>
/// Private spaces (Security 3): only a private space's people see it or anything in it, whatever their workspace role;
/// to everyone else every route answers 404, as if it didn't exist. Their role still decides what they can change.
/// </summary>
public class PrivateSpaceTests(TaskloomFactory factory) : IClassFixture<TaskloomFactory>
{
    /// <summary>
    /// The owner's workspace with an Admin, a Member (Max), a Guest (Gus), and two people who are in the private space
    /// "HR": Pia (Member, who created it) and Gil (Guest). HR has a list with a task, a comment and an attachment.
    /// </summary>
    private sealed record World(
        TestUser Owner, HttpClient Admin, Guid AdminId, HttpClient Max, Guid MaxId, HttpClient Gus,
        HttpClient Pia, Guid PiaId, HttpClient Gil, Guid GilId,
        SpaceDto Hr, ListSummaryDto HrList, TaskDto HrTask, CommentDto HrComment, AttachmentDto HrFile)
    {
        public Guid WorkspaceId => Owner.Workspace.Id;
        public HttpClient[] Outsiders => [Owner.Client, Admin, Max, Gus];
    }

    private async Task<World> CreateWorldAsync()
    {
        var owner = await factory.NewUserAsync();
        var (admin, adminInfo) = await factory.JoinAsync(owner, WorkspaceRole.Admin, "Ada Admin");
        var (max, maxInfo) = await factory.JoinAsync(owner, WorkspaceRole.Member, "Max Member");
        var (gus, _) = await factory.JoinAsync(owner, WorkspaceRole.Guest, "Gus Guest");
        var (pia, piaInfo) = await factory.JoinAsync(owner, WorkspaceRole.Member, "Pia People");
        var (gil, gilInfo) = await factory.JoinAsync(owner, WorkspaceRole.Guest, "Gil Guest");

        var hr = await CreateSpaceAsync(pia, owner.Workspace.Id, new { name = "HR", isPrivate = true, memberIds = new[] { gilInfo.UserId } });
        var hrList = await pia.CreateListAsync(hr.Id, "Reviews");
        var task = await pia.CreateTaskAsync(hrList.Id, new { title = "Payroll review", assigneeIds = new[] { piaInfo.UserId } });
        var comment = await (await gil.PostAsJsonAsync($"/api/tasks/{task.Id}/comments", new { body = "Seen" })).ReadAsync<CommentDto>();
        var file = await pia.UploadAsync(task.Id, "salaries.txt", "secret"u8.ToArray());
        return new World(owner, admin, adminInfo.UserId, max, maxInfo.UserId, gus, pia, piaInfo.UserId, gil, gilInfo.UserId,
            hr, hrList, task, comment, file);
    }

    private static async Task<SpaceDto> CreateSpaceAsync(HttpClient client, Guid workspaceId, object body) =>
        await (await client.PostAsJsonAsync($"/api/workspaces/{workspaceId}/spaces", body)).ReadAsync<SpaceDto>();

    private static async Task<SpaceDto?> SpaceInTreeAsync(HttpClient client, Guid spaceId) =>
        (await client.TreeAsync()).SelectMany(w => w.Spaces).SingleOrDefault(s => s.Id == spaceId);

    private static Task<HttpResponseMessage> AddPeopleAsync(HttpClient client, Guid spaceId, params Guid[] userIds) =>
        client.PostAsJsonAsync($"/api/spaces/{spaceId}/members", new { userIds });

    [Fact]
    public async Task A_private_space_and_everything_in_it_is_hidden_from_everyone_not_in_it()
    {
        var w = await CreateWorldAsync();
        Assert.True(w.Hr.IsPrivate);
        Assert.Equal([w.PiaId, w.GilId], w.Hr.MemberIds);
        var before = await (await w.Pia.GetAsync($"/api/tasks/{w.HrTask.Id}")).Content.ReadAsStringAsync();

        var hrStatus = w.HrList.Statuses[0].Id;
        var reads = new[]
        {
            $"/api/lists/{w.HrList.Id}/tasks", $"/api/lists/{w.HrList.Id}", $"/api/tasks/{w.HrTask.Id}", $"/api/attachments/{w.HrFile.Id}",
        };
        foreach (var client in w.Outsiders)
        {
            // Not in the tree, not in Home, the palette search or "everything" lists.
            Assert.Null(await SpaceInTreeAsync(client, w.Hr.Id));
            Assert.Contains((await client.TreeAsync()).Single().Spaces, s => s.Id == w.Owner.Space.Id);
            foreach (var query in new[] { "?scope=all", "?scope=all&includeDone=true", "?scope=all&q=payroll", "?q=payroll" })
                Assert.DoesNotContain((await client.WorkspaceTasksAsync(w.WorkspaceId, query)).Tasks, t => t.Id == w.HrTask.Id);

            // Every route answers 404, Owner and Admin included.
            foreach (var path in reads) Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(path)).StatusCode);
            var writes = new Func<Task<HttpResponseMessage>>[]
            {
                () => client.PatchAsJsonAsync($"/api/spaces/{w.Hr.Id}", new { name = "Mine now" }),
                () => client.PatchAsJsonAsync($"/api/spaces/{w.Hr.Id}", new { isPrivate = false }),
                () => client.DeleteAsync($"/api/spaces/{w.Hr.Id}"),
                () => AddPeopleAsync(client, w.Hr.Id, w.MaxId),
                () => client.DeleteAsync($"/api/spaces/{w.Hr.Id}/members/{w.GilId}"),
                () => client.PostAsJsonAsync($"/api/spaces/{w.Hr.Id}/lists", new { name = "Sneaky" }),
                () => client.PatchAsJsonAsync($"/api/lists/{w.HrList.Id}", new { name = "Renamed" }),
                () => client.PatchAsJsonAsync($"/api/lists/{w.HrList.Id}", new { spaceId = w.Owner.Space.Id }),
                () => client.DeleteAsync($"/api/lists/{w.HrList.Id}"),
                () => client.PostAsJsonAsync($"/api/lists/{w.HrList.Id}/statuses", new { name = "Blocked", category = "Active" }),
                () => client.PatchAsJsonAsync($"/api/statuses/{hrStatus}", new { name = "Renamed" }),
                () => client.DeleteAsync($"/api/statuses/{hrStatus}"),
                () => client.PostAsJsonAsync($"/api/lists/{w.HrList.Id}/tasks", new { title = "Sneaky" }),
                () => client.PatchAsJsonAsync($"/api/tasks/{w.HrTask.Id}", new { title = "Renamed" }),
                () => client.PostAsJsonAsync($"/api/tasks/{w.HrTask.Id}/move", new { statusId = hrStatus }),
                () => client.DeleteAsync($"/api/tasks/{w.HrTask.Id}"),
                () => client.BulkAsync(new { id = w.HrTask.Id, priority = "Low" }),
                () => client.BulkDeleteAsync(w.HrTask.Id),
                () => client.PostAsJsonAsync($"/api/tasks/{w.HrTask.Id}/comments", new { body = "Hi" }),
                () => client.PatchAsJsonAsync($"/api/comments/{w.HrComment.Id}", new { body = "Edited" }),
                () => client.DeleteAsync($"/api/comments/{w.HrComment.Id}"),
                () => client.PostAsync($"/api/tasks/{w.HrTask.Id}/attachments",
                    new MultipartFormDataContent { { new ByteArrayContent("x"u8.ToArray()), "file", "x.txt" } }),
                () => client.DeleteAsync($"/api/attachments/{w.HrFile.Id}"),
            };
            foreach (var send in writes)
            {
                var res = await send();
                Assert.True(res.StatusCode == HttpStatusCode.NotFound,
                    $"{res.RequestMessage!.Method} {res.RequestMessage.RequestUri}: expected 404, got {(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");
            }
        }

        // A bulk call mixing a visible task with a hidden one changes nothing.
        var visible = await w.Owner.Client.CreateTaskAsync(w.Owner.List.Id, new { title = "Public task" });
        Assert.Equal(HttpStatusCode.NotFound, (await w.Owner.Client.BulkAsync(new { id = visible.Id, priority = "Low" }, new { id = w.HrTask.Id, priority = "Low" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await w.Owner.Client.BulkDeleteAsync(visible.Id, w.HrTask.Id)).StatusCode);
        Assert.Equal(TaskPriority.None, (await w.Owner.Client.TaskDetailAsync(visible.Id)).Task.Priority);

        // Nothing changed, and the space's people still see all of it.
        Assert.Equal(before, await (await w.Pia.GetAsync($"/api/tasks/{w.HrTask.Id}")).Content.ReadAsStringAsync());
        foreach (var insider in new[] { w.Pia, w.Gil })
        {
            var hr = await SpaceInTreeAsync(insider, w.Hr.Id);
            Assert.NotNull(hr);
            Assert.Equal((true, "Reviews"), (hr.IsPrivate, hr.Lists.Single().Name));
            Assert.Equal([w.PiaId, w.GilId], hr.MemberIds);
            Assert.Single(await insider.ListTasksAsync(w.HrList.Id));
            Assert.Contains((await insider.WorkspaceTasksAsync(w.WorkspaceId, "?scope=all&q=payroll")).Tasks, t => t.Id == w.HrTask.Id);
            Assert.Equal(HttpStatusCode.OK, (await insider.GetAsync($"/api/attachments/{w.HrFile.Id}")).StatusCode);
        }
        // GET /api/lists/{id} names only the people who can see the list.
        var detail = await (await w.Pia.GetAsync($"/api/lists/{w.HrList.Id}")).ReadAsync<ListDetailDto>();
        Assert.Equal(new[] { w.GilId, w.PiaId }.Order(), detail.Members.Select(m => m.Id).Order());

        // Gil is still a guest in there: he comments but can't edit.
        Assert.Equal(HttpStatusCode.OK, (await w.Gil.PostAsJsonAsync($"/api/tasks/{w.HrTask.Id}/comments", new { body = "Me too" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await w.Gil.PostAsJsonAsync($"/api/lists/{w.HrList.Id}/tasks", new { title = "Guest task" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await w.Gil.PatchAsJsonAsync($"/api/tasks/{w.HrTask.Id}", new { title = "Guest edit" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await AddPeopleAsync(w.Gil, w.Hr.Id, w.MaxId)).StatusCode);
    }

    [Fact]
    public async Task Tasks_and_lists_only_move_into_spaces_the_caller_can_see()
    {
        var w = await CreateWorldAsync();
        var maxTask = await w.Max.CreateTaskAsync(w.Owner.List.Id, new { title = "Max's task" });

        // To Max, HR's list and space don't exist.
        Assert.Equal(TaskChange.ListMessage, await (await w.Max.PatchAsJsonAsync($"/api/tasks/{maxTask.Id}", new { listId = w.HrList.Id })).ProblemAsync(HttpStatusCode.BadRequest));
        Assert.Equal(TaskChange.ListMessage, await (await w.Max.BulkAsync(new { id = maxTask.Id, listId = w.HrList.Id })).ProblemAsync(HttpStatusCode.BadRequest));
        Assert.Equal("Choose a space in this workspace.", await (await w.Max.PatchAsJsonAsync($"/api/lists/{w.Owner.List.Id}", new { spaceId = w.Hr.Id })).ProblemAsync(HttpStatusCode.BadRequest));
        Assert.Equal("Choose a list in this workspace to copy statuses from.", await (await w.Max.PostAsJsonAsync($"/api/spaces/{w.Owner.Space.Id}/lists",
            new { name = "Copy", statusesFromListId = w.HrList.Id })).ProblemAsync(HttpStatusCode.BadRequest));
        Assert.Equal(w.Owner.List.Id, (await w.Max.TaskDetailAsync(maxTask.Id)).Task.ListId);

        // Pia can see both, so she can move Max's task into HR: then Max can't see it any more.
        var moved = await w.Pia.PatchTaskAsync(maxTask.Id, new { listId = w.HrList.Id });
        Assert.Equal(w.HrList.Id, moved.ListId);
        Assert.Equal(HttpStatusCode.NotFound, (await w.Max.GetAsync($"/api/tasks/{maxTask.Id}")).StatusCode);

        // And a whole list: a list moved into HR disappears for Max, with its tasks.
        var list = await w.Pia.CreateListAsync(w.Owner.Space.Id, "Moving list");
        var inList = await w.Max.CreateTaskAsync(list.Id, new { title = "In the moving list" });
        Assert.Equal(HttpStatusCode.NoContent, (await w.Pia.PatchAsJsonAsync($"/api/lists/{list.Id}", new { spaceId = w.Hr.Id })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await w.Max.GetAsync($"/api/tasks/{inList.Id}")).StatusCode);
        Assert.DoesNotContain((await w.Max.TreeAsync()).Single().Spaces.SelectMany(s => s.Lists), l => l.Id == list.Id);
        // Statuses copy from a list the caller can see.
        var copy = await w.Pia.CreateListAsync(w.Owner.Space.Id, "Copy", new { name = "Copy", statusesFromListId = w.HrList.Id });
        Assert.Equal(w.HrList.Statuses.Select(s => s.Name), copy.Statuses.Select(s => s.Name));
    }

    [Fact]
    public async Task Only_people_in_a_private_space_can_be_assigned_to_its_tasks()
    {
        var w = await CreateWorldAsync();
        var stranger = await factory.NewUserAsync();

        // Max is in the workspace but not in HR; a stranger isn't even in the workspace.
        Assert.Equal(TaskEndpoints.AssigneesMustSeeSpace, await (await w.Pia.PostAsJsonAsync($"/api/lists/{w.HrList.Id}/tasks",
            new { title = "For Max", assigneeIds = new[] { w.MaxId } })).ProblemAsync(HttpStatusCode.BadRequest));
        Assert.Equal(TaskEndpoints.AssigneesMustBeMembers, await (await w.Pia.PostAsJsonAsync($"/api/lists/{w.HrList.Id}/tasks",
            new { title = "For a stranger", assigneeIds = new[] { w.MaxId, stranger.Me.Id } })).ProblemAsync(HttpStatusCode.BadRequest));
        Assert.Equal(TaskEndpoints.AssigneesMustSeeSpace, await (await w.Pia.PatchAsJsonAsync($"/api/tasks/{w.HrTask.Id}",
            new { assigneeIds = new[] { w.PiaId, w.MaxId } })).ProblemAsync(HttpStatusCode.BadRequest));
        Assert.Equal(TaskEndpoints.AssigneesMustSeeSpace, await (await w.Pia.BulkAsync(new { id = w.HrTask.Id, addAssigneeIds = new[] { w.MaxId } }))
            .ProblemAsync(HttpStatusCode.BadRequest));
        Assert.Equal([w.PiaId], (await w.Pia.TaskDetailAsync(w.HrTask.Id)).Task.AssigneeIds);

        // HR's people can be, guests included.
        var forGil = await w.Pia.CreateTaskAsync(w.HrList.Id, new { title = "For Gil", assigneeIds = new[] { w.GilId } });
        Assert.Equal([w.GilId], forGil.AssigneeIds);
        Assert.Equal([w.PiaId, w.GilId], (await w.Pia.PatchTaskAsync(w.HrTask.Id, new { assigneeIds = new[] { w.PiaId, w.GilId } })).AssigneeIds);

        // A task moving into HR is checked against HR: adding Max on the way in is refused...
        var maxTask = await w.Max.CreateTaskAsync(w.Owner.List.Id, new { title = "Max's task", assigneeIds = new[] { w.MaxId } });
        var other = await w.Pia.CreateTaskAsync(w.Owner.List.Id, new { title = "Unassigned" });
        Assert.Equal(TaskEndpoints.AssigneesMustSeeSpace, await (await w.Pia.PatchAsJsonAsync($"/api/tasks/{other.Id}",
            new { listId = w.HrList.Id, assigneeIds = new[] { w.MaxId } })).ProblemAsync(HttpStatusCode.BadRequest));
        // ...but someone already assigned keeps the assignment: access changes never touch assignments. Max just can't
        // see the task, in Home or anywhere, until he is added to HR.
        var moved = await w.Pia.PatchTaskAsync(maxTask.Id, new { listId = w.HrList.Id });
        Assert.Equal([w.MaxId], moved.AssigneeIds);
        Assert.DoesNotContain((await w.Max.WorkspaceTasksAsync(w.WorkspaceId)).Tasks, t => t.Id == maxTask.Id);
        Assert.Equal(HttpStatusCode.OK, (await AddPeopleAsync(w.Pia, w.Hr.Id, w.MaxId)).StatusCode);
        Assert.Contains((await w.Max.WorkspaceTasksAsync(w.WorkspaceId)).Tasks, t => t.Id == maxTask.Id);
        // Kept in the order they were assigned: Pia from the start, Max now.
        Assert.Equal([w.PiaId, w.MaxId], (await w.Pia.PatchTaskAsync(w.HrTask.Id, new { assigneeIds = new[] { w.MaxId, w.PiaId } })).AssigneeIds);
    }

    [Fact]
    public async Task Making_a_space_private_keeps_it_to_its_people_and_making_it_public_shows_it_to_everyone()
    {
        var owner = await factory.NewUserAsync();
        var (max, maxInfo) = await factory.JoinAsync(owner, WorkspaceRole.Member, "Max Member");
        var (gus, gusInfo) = await factory.JoinAsync(owner, WorkspaceRole.Guest, "Gus Guest");
        var pending = await owner.Client.InviteAsync(owner.Workspace.Id, Builders.NewEmail(), WorkspaceRole.Member);
        var space = owner.Space.Id;
        var task = await owner.Client.CreateTaskAsync(owner.List.Id, new { title = "Plans", assigneeIds = new[] { maxInfo.UserId } });

        // A public space has no people of its own.
        Assert.False(owner.Space.IsPrivate);
        Assert.Empty(owner.Space.MemberIds);
        Assert.Equal(HierarchyEndpoints.PeopleNeedPrivate, await (await AddPeopleAsync(owner.Client, space, maxInfo.UserId)).ProblemAsync(HttpStatusCode.BadRequest));
        Assert.Equal(HierarchyEndpoints.PeopleNeedPrivate, await (await owner.Client.PostAsJsonAsync($"/api/workspaces/{owner.Workspace.Id}/spaces",
            new { name = "Public", memberIds = new[] { maxInfo.UserId } })).ProblemAsync(HttpStatusCode.BadRequest));

        // Max (a Member) makes it private: he is its only person, so the owner loses it.
        Assert.Equal(HttpStatusCode.NoContent, (await max.PatchAsJsonAsync($"/api/spaces/{space}", new { isPrivate = true })).StatusCode);
        Assert.Equal([maxInfo.UserId], (await SpaceInTreeAsync(max, space))!.MemberIds);
        Assert.Null(await SpaceInTreeAsync(owner.Client, space));
        Assert.Null(await SpaceInTreeAsync(gus, space));
        Assert.Equal(HttpStatusCode.NotFound, (await owner.Client.GetAsync($"/api/tasks/{task.Id}")).StatusCode);
        // Sending isPrivate again changes nothing.
        Assert.Equal(HttpStatusCode.NoContent, (await max.PatchAsJsonAsync($"/api/spaces/{space}", new { isPrivate = true, name = "Plans" })).StatusCode);

        // He adds the owner, the guest and a pending invitee; repeats and people already in are ignored.
        var added = await (await AddPeopleAsync(max, space, owner.Me.Id, gusInfo.UserId, owner.Me.Id, maxInfo.UserId, pending.UserId)).ReadAsync<SpaceMembersDto>();
        Assert.Equal([maxInfo.UserId, owner.Me.Id, gusInfo.UserId, pending.UserId], added.MemberIds);
        Assert.NotNull(await SpaceInTreeAsync(owner.Client, space));
        Assert.Equal(HttpStatusCode.OK, (await gus.GetAsync($"/api/tasks/{task.Id}")).StatusCode);

        // Refusals: nobody to add, someone outside the workspace, too many.
        var stranger = await factory.NewUserAsync();
        Assert.Equal("Choose who to add.", await (await AddPeopleAsync(max, space)).ProblemAsync(HttpStatusCode.BadRequest));
        Assert.Equal(HierarchyEndpoints.PeopleMustBeMembers, await (await AddPeopleAsync(max, space, stranger.Me.Id)).ProblemAsync(HttpStatusCode.BadRequest));
        Assert.Equal(HierarchyEndpoints.PeopleMustBeMembers, await (await AddPeopleAsync(max, space, Guid.NewGuid())).ProblemAsync(HttpStatusCode.BadRequest));
        Assert.Equal("Add up to 500 people at a time.", await (await AddPeopleAsync(max, space, Enumerable.Range(0, 501).Select(_ => Guid.NewGuid()).ToArray()))
            .ProblemAsync(HttpStatusCode.BadRequest));
        Assert.Null(await SpaceInTreeAsync(stranger.Client, space));

        // Taking people out: a guest can't take others out but can leave; Members take anyone out.
        Assert.Equal(HttpStatusCode.Forbidden, (await gus.DeleteAsync($"/api/spaces/{space}/members/{pending.UserId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await gus.DeleteAsync($"/api/spaces/{space}/members/{gusInfo.UserId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await gus.GetAsync($"/api/tasks/{task.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await max.DeleteAsync($"/api/spaces/{space}/members/{pending.UserId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await max.DeleteAsync($"/api/spaces/{space}/members/{pending.UserId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.Client.DeleteAsync($"/api/spaces/{space}/members/{maxInfo.UserId}")).StatusCode);
        Assert.Null(await SpaceInTreeAsync(max, space));
        // The owner is the last one: someone has to stay.
        Assert.Equal(HierarchyEndpoints.LastPerson, await (await owner.Client.DeleteAsync($"/api/spaces/{space}/members/{owner.Me.Id}")).ProblemAsync(HttpStatusCode.BadRequest));
        // Max lost sight of the task but is still assigned to it.
        Assert.Equal([maxInfo.UserId], (await owner.Client.TaskDetailAsync(task.Id)).Task.AssigneeIds);

        // Public again: everyone sees it, and its people are forgotten.
        Assert.Equal(HttpStatusCode.NoContent, (await owner.Client.PatchAsJsonAsync($"/api/spaces/{space}", new { isPrivate = false })).StatusCode);
        foreach (var client in new[] { owner.Client, max, gus })
        {
            var tree = await SpaceInTreeAsync(client, space);
            Assert.NotNull(tree);
            Assert.Equal((false, 0), (tree.IsPrivate, tree.MemberIds.Count));
        }
        Assert.Equal(HttpStatusCode.OK, (await max.GetAsync($"/api/tasks/{task.Id}")).StatusCode);
        // Private once more: only the one who did it, not the people from before.
        Assert.Equal(HttpStatusCode.Forbidden, (await gus.PatchAsJsonAsync($"/api/spaces/{space}", new { isPrivate = true })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.Client.PatchAsJsonAsync($"/api/spaces/{space}", new { isPrivate = true })).StatusCode);
        Assert.Equal([owner.Me.Id], (await SpaceInTreeAsync(owner.Client, space))!.MemberIds);
        Assert.Null(await SpaceInTreeAsync(max, space));
    }

    [Fact]
    public async Task Admins_can_find_and_join_private_spaces_they_are_not_in()
    {
        var w = await CreateWorldAsync();
        var url = $"/api/workspaces/{w.WorkspaceId}/private-spaces";

        // Owner and Admins see every private space's name and size; Members and Guests can't list them.
        var listed = await (await w.Admin.GetAsync(url)).ReadAsync<List<PrivateSpaceDto>>();
        Assert.Equal([new PrivateSpaceDto(w.Hr.Id, "HR", w.Hr.Color, 2, false)], listed);
        Assert.Equal([new PrivateSpaceDto(w.Hr.Id, "HR", w.Hr.Color, 2, false)], await (await w.Owner.Client.GetAsync(url)).ReadAsync<List<PrivateSpaceDto>>());
        Assert.Equal(HttpStatusCode.Forbidden, (await w.Pia.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await w.Gil.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await (await factory.NewUserAsync()).Client.GetAsync(url)).StatusCode);

        // Joining: a Member or Guest who isn't in it gets 404 (it doesn't exist for them), one who is gets 403.
        Assert.Equal(HttpStatusCode.NotFound, (await w.Max.PostAsJsonAsync($"/api/spaces/{w.Hr.Id}/join", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await w.Gus.PostAsJsonAsync($"/api/spaces/{w.Hr.Id}/join", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await w.Pia.PostAsJsonAsync($"/api/spaces/{w.Hr.Id}/join", new { })).StatusCode);
        Assert.Equal("Everyone in the workspace can already see this space.",
            await (await w.Admin.PostAsJsonAsync($"/api/spaces/{w.Owner.Space.Id}/join", new { })).ProblemAsync(HttpStatusCode.BadRequest));

        // The Admin joins (twice is fine) and shows among the space's people.
        Assert.Equal(HttpStatusCode.NoContent, (await w.Admin.PostAsJsonAsync($"/api/spaces/{w.Hr.Id}/join", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await w.Admin.PostAsJsonAsync($"/api/spaces/{w.Hr.Id}/join", new { })).StatusCode);
        Assert.Equal([w.PiaId, w.GilId, w.AdminId], (await SpaceInTreeAsync(w.Pia, w.Hr.Id))!.MemberIds);
        Assert.Equal(HttpStatusCode.OK, (await w.Admin.GetAsync($"/api/tasks/{w.HrTask.Id}")).StatusCode);
        Assert.Equal([new PrivateSpaceDto(w.Hr.Id, "HR", w.Hr.Color, 3, true)], await (await w.Admin.GetAsync(url)).ReadAsync<List<PrivateSpaceDto>>());

        // Leaving the workspace takes people out of its private spaces, so an invite back doesn't restore them. With
        // everyone gone a space has nobody, and an admin can still join it.
        foreach (var id in new[] { w.PiaId, w.GilId, w.AdminId })
            Assert.Equal(HttpStatusCode.NoContent, (await w.Owner.Client.DeleteAsync($"/api/workspaces/{w.WorkspaceId}/members/{id}")).StatusCode);
        Assert.Equal([new PrivateSpaceDto(w.Hr.Id, "HR", w.Hr.Color, 0, false)], await (await w.Owner.Client.GetAsync(url)).ReadAsync<List<PrivateSpaceDto>>());
        var pia = await w.Owner.Client.InviteAsync(w.WorkspaceId, (await (await w.Pia.GetAsync("/api/me")).ReadAsync<UserDto>()).Email, WorkspaceRole.Member);
        Assert.Equal(w.PiaId, pia.UserId);
        Assert.Null(await SpaceInTreeAsync(w.Pia, w.Hr.Id));
        Assert.Equal(HttpStatusCode.NoContent, (await w.Owner.Client.PostAsJsonAsync($"/api/spaces/{w.Hr.Id}/join", new { })).StatusCode);
        Assert.Equal([w.Owner.Me.Id], (await SpaceInTreeAsync(w.Owner.Client, w.Hr.Id))!.MemberIds);
        Assert.Single(await w.Owner.Client.ListTasksAsync(w.HrList.Id));
    }

    [Fact]
    public async Task A_new_private_space_starts_with_its_creator_and_the_people_named()
    {
        var owner = await factory.NewUserAsync();
        var (max, maxInfo) = await factory.JoinAsync(owner, WorkspaceRole.Member, "Max Member");
        var (_, gusInfo) = await factory.JoinAsync(owner, WorkspaceRole.Guest, "Gus Guest");
        var stranger = await factory.NewUserAsync();
        var url = $"/api/workspaces/{owner.Workspace.Id}/spaces";

        // The creator comes first even when named later; repeats are dropped.
        var space = await CreateSpaceAsync(max, owner.Workspace.Id, new { name = "Finance", isPrivate = true, memberIds = new[] { gusInfo.UserId, maxInfo.UserId, gusInfo.UserId } });
        Assert.True(space.IsPrivate);
        Assert.Equal([maxInfo.UserId, gusInfo.UserId], space.MemberIds);
        Assert.Equal(space.MemberIds, (await SpaceInTreeAsync(max, space.Id))!.MemberIds);
        Assert.Null(await SpaceInTreeAsync(owner.Client, space.Id));

        // Without people it's just the creator; everyone named must be in the workspace; guests can't create spaces.
        Assert.Equal([owner.Me.Id], (await CreateSpaceAsync(owner.Client, owner.Workspace.Id, new { name = "Solo", isPrivate = true })).MemberIds);
        Assert.Equal(HierarchyEndpoints.PeopleMustBeMembers, await (await owner.Client.PostAsJsonAsync(url,
            new { name = "Bad", isPrivate = true, memberIds = new[] { stranger.Me.Id } })).ProblemAsync(HttpStatusCode.BadRequest));
        var spaceCount = (await owner.Client.TreeAsync()).Single().Spaces.Count;
        Assert.Equal(2, spaceCount);

        // A public space created the usual way is public, with no people.
        var plain = await CreateSpaceAsync(owner.Client, owner.Workspace.Id, new { name = "Plain" });
        Assert.Equal((false, 0), (plain.IsPrivate, plain.MemberIds.Count));
        Assert.NotNull(await SpaceInTreeAsync(max, plain.Id));
    }
}
