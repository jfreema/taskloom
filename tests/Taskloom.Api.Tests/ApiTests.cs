using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Taskloom.Api.Tests;

public class ApiTests(TaskloomFactory factory) : IClassFixture<TaskloomFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private static async Task<T> Read<T>(HttpResponseMessage res)
    {
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<T>(Json))!;
    }

    private async Task<(HttpClient Client, WorkspaceTreeDto Workspace, ListDetailDto List)> NewUserAsync()
    {
        var client = await factory.SignInAsync($"{Guid.NewGuid():N}@example.com");
        var workspaces = await Read<List<WorkspaceTreeDto>>(await client.GetAsync("/api/workspaces"));
        var listId = workspaces.Single().Spaces.Single().Lists.Single().Id;
        var list = await Read<ListDetailDto>(await client.GetAsync($"/api/lists/{listId}"));
        return (client, workspaces.Single(), list);
    }

    [Fact]
    public async Task First_sign_in_creates_a_starter_workspace_with_default_statuses()
    {
        var (_, workspace, list) = await NewUserAsync();
        Assert.Equal(Domain.WorkspaceRole.Owner, workspace.Role);
        Assert.Equal(["To do", "In progress", "Review", "Done"], list.Statuses.Select(s => s.Name));
    }

    [Fact]
    public async Task Api_requires_sign_in()
    {
        var anon = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/api/workspaces")).StatusCode);
    }

    [Fact]
    public async Task Writes_without_the_csrf_header_are_rejected()
    {
        var (client, _, list) = await NewUserAsync();
        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/lists/{list.Id}/tasks") { Content = JsonContent.Create(new { title = "x" }) };
        client.DefaultRequestHeaders.Remove("X-Taskloom");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(req)).StatusCode);
    }

    [Fact]
    public async Task Users_cannot_see_other_workspaces()
    {
        var (alice, _, aliceList) = await NewUserAsync();
        var task = await Read<TaskDto>(await alice.PostAsJsonAsync($"/api/lists/{aliceList.Id}/tasks", new { title = "Private" }));
        var (bob, _, _) = await NewUserAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/api/lists/{aliceList.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/api/tasks/{task.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.PatchAsJsonAsync($"/api/tasks/{task.Id}", new { title = "Hacked" })).StatusCode);
    }

    [Fact]
    public async Task Client_generated_ids_are_kept()
    {
        var (client, _, list) = await NewUserAsync();
        var id = Guid.NewGuid();
        var task = await Read<TaskDto>(await client.PostAsJsonAsync($"/api/lists/{list.Id}/tasks", new { id, title = "Offline-made" }));
        Assert.Equal(id, task.Id);
    }

    [Fact]
    public async Task Patch_updates_only_given_fields_and_null_clears()
    {
        var (client, _, list) = await NewUserAsync();
        var task = await Read<TaskDto>(await client.PostAsJsonAsync($"/api/lists/{list.Id}/tasks",
            new { title = "Plan", dueDate = "2026-12-01", priority = "High" }));

        var updated = await Read<TaskDto>(await client.PatchAsJsonAsync($"/api/tasks/{task.Id}", new { dueDate = (string?)null }));
        Assert.Null(updated.DueDate);
        Assert.Equal(Domain.TaskPriority.High, updated.Priority);
        Assert.Equal("Plan", updated.Title);
    }

    [Fact]
    public async Task Moving_to_a_done_status_completes_the_task_and_orders_between_neighbours()
    {
        var (client, _, list) = await NewUserAsync();
        var done = list.Statuses.Single(s => s.Name == "Done");
        async Task<TaskDto> Add(string title) =>
            await Read<TaskDto>(await client.PostAsJsonAsync($"/api/lists/{list.Id}/tasks", new { title, statusId = done.Id }));
        var a = await Add("A");
        var c = await Add("C");
        var b = await Read<TaskDto>(await client.PostAsJsonAsync($"/api/lists/{list.Id}/tasks", new { title = "B" }));
        Assert.Null(b.CompletedAt);

        var moved = await Read<TaskDto>(await client.PostAsJsonAsync($"/api/tasks/{b.Id}/move",
            new { statusId = done.Id, beforeTaskId = a.Id, afterTaskId = c.Id }));

        Assert.NotNull(moved.CompletedAt);
        var order = (await Read<List<TaskDto>>(await client.GetAsync($"/api/lists/{list.Id}/tasks")))
            .Where(t => t.StatusId == done.Id).OrderBy(t => t.Position).Select(t => t.Title);
        Assert.Equal(["A", "B", "C"], order);
    }

    [Fact]
    public async Task Deleting_a_status_moves_its_tasks_to_the_first_status()
    {
        var (client, _, list) = await NewUserAsync();
        var review = list.Statuses.Single(s => s.Name == "Review");
        var task = await Read<TaskDto>(await client.PostAsJsonAsync($"/api/lists/{list.Id}/tasks", new { title = "T", statusId = review.Id }));

        (await client.DeleteAsync($"/api/statuses/{review.Id}")).EnsureSuccessStatusCode();

        var after = await Read<TaskDetailDto>(await client.GetAsync($"/api/tasks/{task.Id}"));
        Assert.Equal(list.Statuses.First().Id, after.Task.StatusId);
    }

    [Fact]
    public async Task Deleting_a_list_removes_its_tasks_and_subtasks()
    {
        var (client, workspace, _) = await NewUserAsync();
        var space = workspace.Spaces.Single();
        var newList = await Read<ListSummaryDto>(await client.PostAsJsonAsync($"/api/spaces/{space.Id}/lists", new { name = "Temp" }));
        var parent = await Read<TaskDto>(await client.PostAsJsonAsync($"/api/lists/{newList.Id}/tasks", new { title = "P" }));
        await Read<TaskDto>(await client.PostAsJsonAsync($"/api/lists/{newList.Id}/tasks", new { title = "S", parentTaskId = parent.Id }));

        (await client.DeleteAsync($"/api/lists/{newList.Id}")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/tasks/{parent.Id}")).StatusCode);
    }

    [Fact]
    public async Task Invited_members_can_be_assigned_and_guests_cannot_edit()
    {
        var (owner, workspace, list) = await NewUserAsync();
        var guestEmail = $"{Guid.NewGuid():N}@example.com";
        var guest = await Read<MemberDto>(await owner.PostAsJsonAsync($"/api/workspaces/{workspace.Id}/members", new { email = guestEmail, role = "Guest" }));
        var task = await Read<TaskDto>(await owner.PostAsJsonAsync($"/api/lists/{list.Id}/tasks", new { title = "Shared", assigneeId = guest.UserId }));
        Assert.Equal(guest.UserId, task.AssigneeId);

        var guestClient = await factory.SignInAsync(guestEmail);
        Assert.Equal(HttpStatusCode.OK, (await guestClient.GetAsync($"/api/tasks/{task.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await guestClient.PatchAsJsonAsync($"/api/tasks/{task.Id}", new { title = "No" })).StatusCode);
        // Guests can still comment.
        Assert.Equal(HttpStatusCode.OK, (await guestClient.PostAsJsonAsync($"/api/tasks/{task.Id}/comments", new { body = "Looks good" })).StatusCode);
    }

    [Fact]
    public async Task Attachments_round_trip_through_the_file_store()
    {
        var (client, _, list) = await NewUserAsync();
        var task = await Read<TaskDto>(await client.PostAsJsonAsync($"/api/lists/{list.Id}/tasks", new { title = "With file" }));
        using var form = new MultipartFormDataContent { { new ByteArrayContent("hello"u8.ToArray()), "file", "hello.txt" } };
        var att = await Read<AttachmentDto>(await client.PostAsync($"/api/tasks/{task.Id}/attachments", form));
        Assert.Equal("hello", await client.GetStringAsync($"/api/attachments/{att.Id}"));
    }
}
