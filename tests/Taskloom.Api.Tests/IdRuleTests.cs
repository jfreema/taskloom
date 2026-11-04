using System.Net;
using Taskloom.Api.Tests.Support;
using Taskloom.Domain;

namespace Taskloom.Api.Tests;

/// <summary>
/// The id rules of spec 5.1 and 5.5: a client-generated id that is taken is a 409, and every id in a body (or the
/// <c>moveTo</c> query) must belong to the workspace of the row being changed, else a 400 with the rule's message.
/// Nothing changes either way.
/// </summary>
public class IdRuleTests(TaskloomFactory factory) : IClassFixture<TaskloomFactory>
{
    private const string Taken = "An item with this id already exists.";

    /// <summary>A create endpoint that takes an optional client id, as the user's own call with that id and a name.</summary>
    public sealed record CreateRow(Func<TestUser, Guid, string, Task<HttpResponseMessage>> Create, HttpStatusCode Success);

    public static readonly Dictionary<string, CreateRow> Creates = new()
    {
        ["workspaces"] = new((u, id, name) => u.Client.PostAsJsonAsync("/api/workspaces", new { id, name }), HttpStatusCode.Created),
        ["spaces"] = new((u, id, name) => u.Client.PostAsJsonAsync($"/api/workspaces/{u.Workspace.Id}/spaces", new { id, name }), HttpStatusCode.Created),
        ["lists"] = new((u, id, name) => u.Client.PostAsJsonAsync($"/api/spaces/{u.Space.Id}/lists", new { id, name, statusPreset = "Simple" }), HttpStatusCode.Created),
        ["statuses"] = new((u, id, name) => u.Client.PostAsJsonAsync($"/api/lists/{u.List.Id}/statuses", new { id, name, category = "Active" }), HttpStatusCode.OK),
        ["tasks"] = new((u, id, name) => u.Client.PostAsJsonAsync($"/api/lists/{u.List.Id}/tasks", new { id, title = name }), HttpStatusCode.Created),
        // On the user's first task ("Discussed", made before the first call).
        ["comments"] = new(async (u, id, name) =>
        {
            var task = (await u.Client.ListTasksAsync(u.List.Id)).First();
            return await u.Client.PostAsJsonAsync($"/api/tasks/{task.Id}/comments", new { id, body = name });
        }, HttpStatusCode.OK),
        ["tags"] = new((u, id, name) => u.Client.PostAsJsonAsync($"/api/workspaces/{u.Workspace.Id}/tags", new { id, name }), HttpStatusCode.Created),
    };

    public static TheoryData<string> CreateEndpoints => new(Creates.Keys);

    [Theory]
    [MemberData(nameof(CreateEndpoints))]
    public async Task Duplicate_client_ids_return_409(string endpoint)
    {
        var row = Creates[endpoint];
        var user = await factory.NewUserAsync();
        var stranger = await factory.NewUserAsync();
        foreach (var u in new[] { user, stranger }) await u.Client.CreateTaskAsync(u.List.Id, new { title = "Discussed" });
        var id = Guid.NewGuid();

        var first = await row.Create(user, id, "First");
        Assert.Equal(row.Success, first.StatusCode);
        Assert.Contains(id.ToString(), await first.Content.ReadAsStringAsync());
        var mine = await SnapshotAsync(user);
        var theirs = await SnapshotAsync(stranger);

        // The same id again, from the same user or from anyone else in their own workspace: 409, and nothing changes.
        Assert.Equal(Taken, await (await row.Create(user, id, "Second")).ProblemAsync(HttpStatusCode.Conflict));
        Assert.Equal(Taken, await (await row.Create(user, id, "First")).ProblemAsync(HttpStatusCode.Conflict));
        Assert.Equal(Taken, await (await row.Create(stranger, id, "Theirs")).ProblemAsync(HttpStatusCode.Conflict));
        Assert.Equal(mine, await SnapshotAsync(user));
        Assert.Equal(theirs, await SnapshotAsync(stranger));

        // A fresh id still works afterwards.
        Assert.Equal(row.Success, (await row.Create(user, Guid.NewGuid(), "Third")).StatusCode);
    }

    /// <summary>A call in workspace A using ids from workspace B, which the caller also owns, and the 400 it must get.</summary>
    public sealed record ForeignRow(Func<Two, Task<HttpResponseMessage>> Send, string Message);

    private const string Status = "Choose a status of this list.";
    private const string List = "Choose a list in this workspace.";
    private const string Members = "Everyone assigned must be a member of this workspace.";
    private const string Tags = "Tags must belong to this workspace.";

    public static readonly Dictionary<string, ForeignRow> Foreign = new()
    {
        ["create: statusId"] = new(t => t.Create(new { title = "New", statusId = t.B.StatusId }), Status),
        ["create: parentTaskId"] = new(t => t.Create(new { title = "New", parentTaskId = t.B.TaskId }), "Choose a parent task in this list."),
        ["create: assigneeIds"] = new(t => t.Create(new { title = "New", assigneeIds = new[] { t.A.Me.Id, t.B.OnlyMemberId } }), Members),
        ["create: tagIds"] = new(t => t.Create(new { title = "New", tagIds = new[] { t.A.TagId, t.B.TagId } }), Tags),
        ["patch: statusId"] = new(t => t.Patch(new { statusId = t.B.StatusId }), Status),
        ["patch: listId"] = new(t => t.Patch(new { listId = t.B.ListId }), List),
        ["patch: listId with its statusId"] = new(t => t.Patch(new { listId = t.B.ListId, statusId = t.B.StatusId }), List),
        ["patch: assigneeIds"] = new(t => t.Patch(new { assigneeIds = new[] { t.B.OnlyMemberId } }), Members),
        ["patch: tagIds"] = new(t => t.Patch(new { tagIds = new[] { t.B.TagId } }), Tags),
        ["move: statusId"] = new(t => t.A.Client.PostAsJsonAsync($"/api/tasks/{t.A.TaskId}/move", new { statusId = t.B.StatusId }), Status),
        ["move: beforeTaskId"] = new(t => t.A.Client.PostAsJsonAsync($"/api/tasks/{t.A.TaskId}/move", new { statusId = t.A.StatusId, beforeTaskId = t.B.TaskId }),
            "The task next to the drop point isn't in that status any more."),
        ["bulk: statusId"] = new(t => t.Bulk(new { id = t.A.TaskId, statusId = t.B.StatusId }), Status),
        ["bulk: listId"] = new(t => t.Bulk(new { id = t.A.TaskId, listId = t.B.ListId }), List),
        ["bulk: assigneeIds"] = new(t => t.Bulk(new { id = t.A.TaskId, assigneeIds = new[] { t.B.OnlyMemberId } }), Members),
        ["bulk: addAssigneeIds"] = new(t => t.Bulk(new { id = t.A.TaskId, addAssigneeIds = new[] { t.B.OnlyMemberId } }), Members),
        ["bulk: removeAssigneeIds"] = new(t => t.Bulk(new { id = t.A.TaskId, removeAssigneeIds = new[] { t.B.OnlyMemberId } }), Members),
        ["bulk: tagIds"] = new(t => t.Bulk(new { id = t.A.TaskId, tagIds = new[] { t.B.TagId } }), Tags),
        ["bulk: addTagIds"] = new(t => t.Bulk(new { id = t.A.TaskId, addTagIds = new[] { t.B.TagId } }), Tags),
        ["bulk: removeTagIds"] = new(t => t.Bulk(new { id = t.A.TaskId, removeTagIds = new[] { t.B.TagId } }), Tags),
        ["create list: statusesFromListId"] = new(t => t.A.Client.PostAsJsonAsync($"/api/spaces/{t.A.SpaceId}/lists",
            new { name = "Copied", statusesFromListId = t.B.ListId }), "Choose a list in this workspace to copy statuses from."),
        ["delete status: moveTo"] = new(t => t.A.Client.DeleteAsync($"/api/statuses/{t.A.StatusId}?moveTo={t.B.StatusId}"),
            "Choose another status of this list to move its tasks to."),
        ["patch list: spaceId"] = new(t => t.A.Client.PatchAsJsonAsync($"/api/lists/{t.A.ListId}", new { spaceId = t.B.SpaceId }), "Choose a space in this workspace."),
    };

    public static TheoryData<string> ForeignIds => new(Foreign.Keys);

    [Theory]
    [MemberData(nameof(ForeignIds))]
    public async Task Ids_from_another_workspace_are_rejected(string rule)
    {
        var two = await Two.CreateAsync(factory);
        var before = await two.SnapshotAsync();

        var res = await Foreign[rule].Send(two);

        Assert.Equal(Foreign[rule].Message, await res.ProblemAsync(HttpStatusCode.BadRequest));
        Assert.Equal(before, await two.SnapshotAsync());
    }

    /// <summary>
    /// Two workspaces owned by the same user: A (the starter workspace) and B ("Elsewhere"). Each has a space, a list
    /// with the default statuses, a task (in "Review") and a tag; B also has a member who isn't in A, and that member is
    /// assigned to B's task.
    /// </summary>
    public sealed class Two
    {
        public required Side A { get; init; }
        public required Side B { get; init; }

        public sealed record Side(HttpClient Client, UserDto Me, Guid WorkspaceId, Guid SpaceId, Guid ListId, Guid StatusId, Guid TaskId, Guid TagId, Guid OnlyMemberId);

        public Task<HttpResponseMessage> Create(object body) => A.Client.PostAsJsonAsync($"/api/lists/{A.ListId}/tasks", body);
        public Task<HttpResponseMessage> Patch(object body) => A.Client.PatchAsJsonAsync($"/api/tasks/{A.TaskId}", body);
        public Task<HttpResponseMessage> Bulk(object update) => A.Client.BulkAsync(update);

        public static async Task<Two> CreateAsync(TaskloomFactory factory)
        {
            var user = await factory.NewUserAsync();
            var aTag = await user.Client.CreateTagAsync(user.Workspace.Id, "here");
            var review = user.Status("Review").Id;
            var aTask = await user.Client.CreateTaskAsync(user.List.Id, new { title = "Here", statusId = review, tagIds = new[] { aTag.Id }, assigneeIds = new[] { user.Me.Id } });

            var b = await (await user.Client.PostAsJsonAsync("/api/workspaces", new { name = "Elsewhere" })).ReadAsync<WorkspaceTreeDto>();
            var bSpace = await (await user.Client.PostAsJsonAsync($"/api/workspaces/{b.Id}/spaces", new { name = "Far" })).ReadAsync<SpaceDto>();
            var bList = await user.Client.CreateListAsync(bSpace.Id, "Far list");
            var bReview = bList.Statuses.Single(s => s.Name == "Review").Id;
            var bTag = await user.Client.CreateTagAsync(b.Id, "there");
            var bMember = await user.Client.InviteAsync(b.Id, Builders.NewEmail(), WorkspaceRole.Member);
            var bTask = await user.Client.CreateTaskAsync(bList.Id, new { title = "There", statusId = bReview, tagIds = new[] { bTag.Id }, assigneeIds = new[] { bMember.UserId } });

            return new Two
            {
                A = new Side(user.Client, user.Me, user.Workspace.Id, user.Space.Id, user.List.Id, review, aTask.Id, aTag.Id, user.Me.Id),
                B = new Side(user.Client, user.Me, b.Id, bSpace.Id, bList.Id, bReview, bTask.Id, bTag.Id, bMember.UserId),
            };
        }

        /// <summary>Both workspaces as the API returns them: the tree, members, tags, tasks and both tasks' details.</summary>
        public async Task<string> SnapshotAsync()
        {
            var paths = new List<string> { "/api/workspaces" };
            foreach (var side in new[] { A, B })
                paths.AddRange([$"/api/workspaces/{side.WorkspaceId}/members", $"/api/workspaces/{side.WorkspaceId}/tags",
                    $"/api/lists/{side.ListId}/tasks", $"/api/tasks/{side.TaskId}"]);
            var parts = new List<string>();
            foreach (var path in paths) parts.Add(await (await A.Client.GetAsync(path)).Content.ReadAsStringAsync());
            return string.Join("\n", parts);
        }
    }

    /// <summary>What a user can see: their tree, and in each workspace the members, tags, and every list's tasks with comments.</summary>
    private static async Task<string> SnapshotAsync(TestUser user)
    {
        var parts = new List<string>();
        var tree = await user.Client.TreeAsync();
        parts.Add(await (await user.Client.GetAsync("/api/workspaces")).Content.ReadAsStringAsync());
        foreach (var w in tree)
        {
            parts.Add(await (await user.Client.GetAsync($"/api/workspaces/{w.Id}/tags")).Content.ReadAsStringAsync());
            foreach (var list in w.Spaces.SelectMany(s => s.Lists))
            foreach (var task in await user.Client.ListTasksAsync(list.Id))
                parts.Add(await (await user.Client.GetAsync($"/api/tasks/{task.Id}")).Content.ReadAsStringAsync());
        }
        return string.Join("\n", parts);
    }
}
