using System.Net;
using System.Text.Json;
using Taskloom.Api.Tests.Support;
using Taskloom.Domain;
using static Taskloom.Api.Tests.Support.TestJson;

namespace Taskloom.Api.Tests;

public class WorkspaceTests(TaskloomFactory factory) : IClassFixture<TaskloomFactory>
{
    [Fact]
    public async Task Workspace_rename_rejects_null_and_blank_names()
    {
        var user = await factory.NewUserAsync();
        var url = $"/api/workspaces/{user.Workspace.Id}";
        foreach (var body in new object[] { new { name = (string?)null }, new { }, new { name = "" }, new { name = "   " }, new { name = new string('w', 201) } })
            Assert.Equal("Enter a workspace name.", await (await user.Client.PatchAsJsonAsync(url, body)).ProblemAsync(HttpStatusCode.BadRequest));
        // A body that can't be read is a 400 problem too, not a server error.
        Assert.NotNull(await (await user.Client.PatchAsJsonAsync(url, new { name = 5 })).ProblemAsync(HttpStatusCode.BadRequest));
        Assert.Equal(user.Workspace.Name, (await user.Client.TreeAsync()).Single().Name);

        Assert.Equal(HttpStatusCode.NoContent, (await user.Client.PatchAsJsonAsync(url, new { name = "  Team space  " })).StatusCode);
        Assert.Equal("Team space", (await user.Client.TreeAsync()).Single().Name);
        Assert.Equal(HttpStatusCode.NoContent, (await user.Client.PatchAsJsonAsync(url, new { name = new string('w', 200) })).StatusCode);

        // Creating a workspace follows the same rule.
        Assert.Equal("Enter a workspace name.", await (await user.Client.PostAsJsonAsync("/api/workspaces", new { name = " " })).ProblemAsync(HttpStatusCode.BadRequest));
        var created = await (await user.Client.PostAsJsonAsync("/api/workspaces", new { name = " Second " })).ReadAsync<WorkspaceTreeDto>();
        Assert.Equal(("Second", WorkspaceRole.Owner), (created.Name, created.Role));
        Assert.Empty(created.Spaces);
        // Workspaces are ordered by creation.
        Assert.Equal([user.Workspace.Id, created.Id], (await user.Client.TreeAsync()).Select(w => w.Id));
    }

    [Fact]
    public async Task Patch_me_updates_the_display_name_and_rejects_blank()
    {
        var user = await factory.NewUserAsync();
        var me = await (await user.Client.PatchAsJsonAsync("/api/me", new { displayName = "  Jared Doe  " })).ReadAsync<UserDto>();
        Assert.Equal(new UserDto(user.Me.Id, user.Me.Email, "Jared Doe"), me);
        Assert.Equal("Jared Doe", (await (await user.Client.GetAsync("/api/me")).ReadAsync<UserDto>()).DisplayName);

        foreach (var body in new object[] { new { displayName = (string?)null }, new { }, new { displayName = "  " } })
            Assert.Equal("Enter a display name.", await (await user.Client.PatchAsJsonAsync("/api/me", body)).ProblemAsync(HttpStatusCode.BadRequest));
        Assert.NotNull(await (await user.Client.PatchAsJsonAsync("/api/me", new { displayName = new string('n', 201) })).ProblemAsync(HttpStatusCode.BadRequest));
        Assert.Equal("Jared Doe", (await (await user.Client.GetAsync("/api/me")).ReadAsync<UserDto>()).DisplayName);

        // Other members see the new name.
        var other = await factory.NewUserAsync();
        await other.Client.InviteAsync(other.Workspace.Id, user.Me.Email, WorkspaceRole.Member);
        var members = await (await other.Client.GetAsync($"/api/workspaces/{other.Workspace.Id}/members")).ReadAsync<List<MemberDto>>();
        Assert.Equal("Jared Doe", members.Single(m => m.UserId == user.Me.Id).DisplayName);

        var anon = factory.CreateClient();
        anon.DefaultRequestHeaders.Add("X-Taskloom", "1");
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.PatchAsJsonAsync("/api/me", new { displayName = "x" })).StatusCode);
        user.Client.DefaultRequestHeaders.Remove("X-Taskloom");
        Assert.Equal(HttpStatusCode.BadRequest, (await user.Client.PatchAsJsonAsync("/api/me", new { displayName = "No header" })).StatusCode);
    }

    [Fact]
    public async Task Tree_lists_carry_space_id_and_statuses_and_members_say_who_is_pending()
    {
        var user = await factory.NewUserAsync();
        var second = await user.Client.CreateListAsync(user.Space.Id, "Second");
        var invited = await user.Client.InviteAsync(user.Workspace.Id, "zed." + Builders.NewEmail(), WorkspaceRole.Member);
        var signedIn = await factory.NewUserAsync();
        await user.Client.InviteAsync(user.Workspace.Id, signedIn.Me.Email, WorkspaceRole.Guest);

        var space = (await user.Client.TreeAsync()).Single().Spaces.Single();
        Assert.Equal([user.List.Id, second.Id], space.Lists.Select(l => l.Id));
        Assert.All(space.Lists, l =>
        {
            Assert.Equal(space.Id, l.SpaceId);
            Assert.Equal([1024d, 2048, 3072, 4096], l.Statuses.Select(s => s.Position));
            Assert.All(l.Statuses, s => Assert.Equal(l.Id, s.ListId));
        });
        Assert.Equal([Palette.Slate, Palette.Blue, Palette.Violet, Palette.Green], second.Statuses.Select(s => s.Color));

        var members = await (await user.Client.GetAsync($"/api/workspaces/{user.Workspace.Id}/members")).ReadAsync<List<MemberDto>>();
        Assert.Equal(members.Select(m => m.DisplayName).Order(StringComparer.OrdinalIgnoreCase), members.Select(m => m.DisplayName));
        Assert.False(members.Single(m => m.UserId == user.Me.Id).Pending);
        Assert.True(members.Single(m => m.UserId == invited.UserId).Pending);
        Assert.True(invited.Pending);
        Assert.False(members.Single(m => m.UserId == signedIn.Me.Id).Pending);
        Assert.Equal(WorkspaceRole.Guest, members.Single(m => m.UserId == signedIn.Me.Id).Role);

        // New spaces default to teal; new statuses default to slate.
        var newSpace = await (await user.Client.PostAsJsonAsync($"/api/workspaces/{user.Workspace.Id}/spaces", new { name = "Teal" })).ReadAsync<SpaceDto>();
        Assert.Equal(Palette.Teal, newSpace.Color);
        Assert.Empty(newSpace.Lists);
        var status = await (await user.Client.PostAsJsonAsync($"/api/lists/{second.Id}/statuses", new { name = "Blocked", category = "Active" })).ReadAsync<StatusDto>();
        Assert.Equal((second.Id, Palette.Slate), (status.ListId, status.Color));
    }

    [Fact]
    public async Task Responses_use_the_contract_shapes()
    {
        var user = await factory.NewUserAsync();
        (await user.Client.SeedAsync(user.Workspace.Id, fillers: 0)).EnsureSuccessStatusCode();

        using var me = await GetJsonAsync(user.Client, "/api/me");
        Assert.Equal(Sorted("id", "email", "displayName"), me.RootElement.Keys());

        using var tree = await GetJsonAsync(user.Client, "/api/workspaces");
        var ws = tree.RootElement[0];
        Assert.Equal(Sorted("id", "name", "role", "spaces"), ws.Keys());
        Assert.Equal("Owner", ws.GetProperty("role").GetString());
        var space = ws.GetProperty("spaces")[0];
        Assert.Equal(Sorted("id", "name", "color", "position", "lists"), space.Keys());
        var list = space.GetProperty("lists")[0];
        Assert.Equal(Sorted("id", "spaceId", "name", "position", "statuses"), list.Keys());
        var status = list.GetProperty("statuses")[0];
        Assert.Equal(Sorted("id", "listId", "name", "color", "category", "position"), status.Keys());
        Assert.Equal("NotStarted", status.GetProperty("category").GetString());
        Assert.Equal(JsonValueKind.Number, status.GetProperty("position").ValueKind);

        using var members = await GetJsonAsync(user.Client, $"/api/workspaces/{user.Workspace.Id}/members");
        Assert.All(members.RootElement.EnumerateArray(), m => Assert.Equal(Sorted("userId", "email", "displayName", "role", "pending"), m.Keys()));

        var work = (await user.Client.TreeAsync()).Single().Spaces.Single(s => s.Name == "Work");
        var website = work.Lists.Single(l => l.Name == "Website refresh");
        var onboarding = (await user.Client.ListTasksAsync(website.Id)).Single(t => t.Title == "Write onboarding guide");
        using (var form = new MultipartFormDataContent { { new ByteArrayContent("pdf"u8.ToArray()), "file", "brief.pdf" } })
            (await user.Client.PostAsync($"/api/tasks/{onboarding.Id}/attachments", form)).EnsureSuccessStatusCode();

        string[] taskKeys = Sorted("id", "listId", "statusId", "parentTaskId", "title", "hasDescription", "priority", "assigneeIds", "tagIds",
            "startDate", "dueDate", "position", "createdById", "createdAt", "updatedAt", "completedAt",
            "subtaskCount", "subtaskDoneCount", "commentCount", "attachmentCount");
        using var tasks = await GetJsonAsync(user.Client, $"/api/lists/{website.Id}/tasks");
        Assert.All(tasks.RootElement.EnumerateArray(), t => Assert.Equal(taskKeys, t.Keys()));

        using var detail = await GetJsonAsync(user.Client, $"/api/tasks/{onboarding.Id}");
        var d = detail.RootElement;
        Assert.Equal(Sorted("task", "description", "workspaceId", "createdBy", "parent", "subtasks", "comments", "attachments", "activity"), d.Keys());
        var task = d.GetProperty("task");
        Assert.Equal(taskKeys, task.Keys());
        Assert.Equal("High", task.GetProperty("priority").GetString());
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", task.GetProperty("dueDate").GetString());
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d+)?\+00:00$", task.GetProperty("createdAt").GetString());
        Assert.Equal(JsonValueKind.Null, task.GetProperty("parentTaskId").ValueKind);
        Assert.Equal(JsonValueKind.Null, d.GetProperty("parent").ValueKind);
        Assert.Equal(Sorted("id", "email", "displayName"), d.GetProperty("createdBy").Keys());
        Assert.Equal(user.Workspace.Id.ToString(), d.GetProperty("workspaceId").GetString());
        Assert.All(d.GetProperty("subtasks").EnumerateArray(), s => Assert.Equal(taskKeys, s.Keys()));
        Assert.All(d.GetProperty("comments").EnumerateArray(), c =>
        {
            Assert.Equal(Sorted("id", "taskId", "author", "body", "createdAt", "editedAt"), c.Keys());
            Assert.Equal(Sorted("id", "email", "displayName"), c.GetProperty("author").Keys());
        });
        Assert.Equal(Sorted("id", "taskId", "fileName", "contentType", "sizeBytes", "uploadedById", "createdAt"), Assert.Single(d.GetProperty("attachments").EnumerateArray()).Keys());
        // The seed's 14 rows, then the upload above.
        var activity = d.GetProperty("activity").EnumerateArray().ToList();
        Assert.Equal(15, activity.Count);
        Assert.All(activity, a =>
        {
            Assert.Equal(Sorted("id", "taskId", "actor", "kind", "from", "to", "createdAt"), a.Keys());
            Assert.Equal(Sorted("id", "email", "displayName"), a.GetProperty("actor").Keys());
        });
        Assert.Equal("StatusChanged", activity[^2].GetProperty("kind").GetString());
        Assert.Equal(("AttachmentAdded", JsonValueKind.Null, "brief.pdf"),
            (activity[^1].GetProperty("kind").GetString(), activity[^1].GetProperty("from").ValueKind, activity[^1].GetProperty("to").GetString()));

        var subtaskId = d.GetProperty("subtasks")[0].GetProperty("id").GetString();
        using var sub = await GetJsonAsync(user.Client, $"/api/tasks/{subtaskId}");
        Assert.Equal(Sorted("id", "title", "listId"), sub.RootElement.GetProperty("parent").Keys());
    }

    [Fact]
    public async Task Tags_workspace_tasks_bulk_and_writes_use_the_contract_shapes()
    {
        var user = await factory.NewUserAsync();
        (await user.Client.SeedAsync(user.Workspace.Id, fillers: 0)).EnsureSuccessStatusCode();
        string[] taskKeys = Sorted("id", "listId", "statusId", "parentTaskId", "title", "hasDescription", "priority", "assigneeIds", "tagIds",
            "startDate", "dueDate", "position", "createdById", "createdAt", "updatedAt", "completedAt",
            "subtaskCount", "subtaskDoneCount", "commentCount", "attachmentCount");
        string[] userKeys = Sorted("id", "email", "displayName");

        using var tags = await GetJsonAsync(user.Client, $"/api/workspaces/{user.Workspace.Id}/tags");
        Assert.Equal(4, tags.RootElement.GetArrayLength());
        Assert.All(tags.RootElement.EnumerateArray(), t => Assert.Equal(Sorted("id", "workspaceId", "name", "color", "createdAt"), t.Keys()));
        using var tag = await JsonAsync(await user.Client.PostAsJsonAsync($"/api/workspaces/{user.Workspace.Id}/tags", new { name = "urgent" }));
        Assert.Equal(Sorted("id", "workspaceId", "name", "color", "createdAt"), tag.RootElement.Keys());
        using var renamed = await JsonAsync(await user.Client.PatchAsJsonAsync($"/api/tags/{tag.RootElement.GetProperty("id").GetString()}", new { name = "later" }));
        Assert.Equal(Sorted("id", "workspaceId", "name", "color", "createdAt"), renamed.RootElement.Keys());

        // Search finds the subtask "Draft outline", whose parent comes back as a TaskRefDto.
        using var found = await GetJsonAsync(user.Client, $"/api/workspaces/{user.Workspace.Id}/tasks?scope=all&q=draft%20outline&includeDone=true");
        Assert.Equal(Sorted("tasks", "parents"), found.RootElement.Keys());
        var hit = Assert.Single(found.RootElement.GetProperty("tasks").EnumerateArray());
        Assert.Equal(taskKeys, hit.Keys());
        Assert.Equal(Sorted("id", "title", "listId"), Assert.Single(found.RootElement.GetProperty("parents").EnumerateArray()).Keys());

        var list = user.List;
        var task = await user.Client.CreateTaskAsync(list.Id, new { title = "Shapes" });
        using var created = await JsonAsync(await user.Client.PostAsJsonAsync($"/api/lists/{list.Id}/tasks", new { title = "Raw" }));
        Assert.Equal(taskKeys, created.RootElement.Keys());
        using var patched = await JsonAsync(await user.Client.PatchAsJsonAsync($"/api/tasks/{task.Id}", new { priority = "Low" }));
        Assert.Equal(taskKeys, patched.RootElement.Keys());
        using var moved = await JsonAsync(await user.Client.PostAsJsonAsync($"/api/tasks/{task.Id}/move", new { statusId = user.Status("Done").Id }));
        Assert.Equal(taskKeys, moved.RootElement.Keys());
        using var bulk = await JsonAsync(await user.Client.BulkAsync(new { id = task.Id, priority = "High" }));
        Assert.Equal(["tasks"], bulk.RootElement.Keys());
        Assert.Equal(taskKeys, Assert.Single(bulk.RootElement.GetProperty("tasks").EnumerateArray()).Keys());

        using var comment = await JsonAsync(await user.Client.PostAsJsonAsync($"/api/tasks/{task.Id}/comments", new { body = "First" }));
        Assert.Equal(Sorted("id", "taskId", "author", "body", "createdAt", "editedAt"), comment.RootElement.Keys());
        Assert.Equal(JsonValueKind.Null, comment.RootElement.GetProperty("editedAt").ValueKind);
        using var edited = await JsonAsync(await user.Client.PatchAsJsonAsync($"/api/comments/{comment.RootElement.GetProperty("id").GetString()}", new { body = "Edited" }));
        Assert.Equal(Sorted("id", "taskId", "author", "body", "createdAt", "editedAt"), edited.RootElement.Keys());
        Assert.Equal(userKeys, edited.RootElement.GetProperty("author").Keys());
        Assert.Equal(JsonValueKind.String, edited.RootElement.GetProperty("editedAt").ValueKind);

        using var space = await JsonAsync(await user.Client.PostAsJsonAsync($"/api/workspaces/{user.Workspace.Id}/spaces", new { name = "S" }));
        Assert.Equal(Sorted("id", "name", "color", "position", "lists"), space.RootElement.Keys());
        using var newList = await JsonAsync(await user.Client.PostAsJsonAsync($"/api/spaces/{space.RootElement.GetProperty("id").GetString()}/lists", new { name = "L", statusPreset = "Requests" }));
        Assert.Equal(Sorted("id", "spaceId", "name", "position", "statuses"), newList.RootElement.Keys());
        var listId = newList.RootElement.GetProperty("id").GetString();
        using var status = await JsonAsync(await user.Client.PostAsJsonAsync($"/api/lists/{listId}/statuses", new { name = "Blocked", category = "Active" }));
        Assert.Equal(Sorted("id", "listId", "name", "color", "category", "position"), status.RootElement.Keys());
        using var statusPatched = await JsonAsync(await user.Client.PatchAsJsonAsync($"/api/statuses/{status.RootElement.GetProperty("id").GetString()}", new { name = "On hold" }));
        Assert.Equal(Sorted("id", "listId", "name", "color", "category", "position"), statusPatched.RootElement.Keys());
        using var member = await JsonAsync(await user.Client.PostAsJsonAsync($"/api/workspaces/{user.Workspace.Id}/members", new { email = Builders.NewEmail(), role = "Member" }));
        Assert.Equal(Sorted("userId", "email", "displayName", "role", "pending"), member.RootElement.Keys());
        using var me = await JsonAsync(await user.Client.PatchAsJsonAsync("/api/me", new { displayName = "Me" }));
        Assert.Equal(userKeys, me.RootElement.Keys());
        using var workspace = await JsonAsync(await user.Client.PostAsJsonAsync("/api/workspaces", new { name = "W" }));
        Assert.Equal(Sorted("id", "name", "role", "spaces"), workspace.RootElement.Keys());
        using (var form = new MultipartFormDataContent { { new ByteArrayContent("x"u8.ToArray()), "file", "x.txt" } })
        {
            using var attachment = await JsonAsync(await user.Client.PostAsync($"/api/tasks/{task.Id}/attachments", form));
            Assert.Equal(Sorted("id", "taskId", "fileName", "contentType", "sizeBytes", "uploadedById", "createdAt"), attachment.RootElement.Keys());
        }
        using var providers = await GetJsonAsync(user.Client, "/auth/providers");
        Assert.Equal(Sorted("devLogin", "providers"), providers.RootElement.Keys());
    }

    private static async Task<JsonDocument> GetJsonAsync(HttpClient client, string url) => await JsonAsync(await client.GetAsync(url));

    private static async Task<JsonDocument> JsonAsync(HttpResponseMessage res)
    {
        var body = await res.Content.ReadAsStringAsync();
        Assert.True(res.IsSuccessStatusCode, $"{res.RequestMessage?.Method} {res.RequestMessage?.RequestUri} returned {(int)res.StatusCode}: {body}");
        return JsonDocument.Parse(body);
    }
}
