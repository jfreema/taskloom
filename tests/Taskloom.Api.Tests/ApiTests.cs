using System.Net;
using Microsoft.EntityFrameworkCore;
using Taskloom.Api.Tests.Support;
using Taskloom.Domain;

namespace Taskloom.Api.Tests;

/// <summary>The first draft's tests (spec 11.2 "Existing tests"), on the iteration-2 shapes.</summary>
public class ApiTests(TaskloomFactory factory) : IClassFixture<TaskloomFactory>
{
    [Fact]
    public async Task First_sign_in_creates_a_starter_workspace_with_default_statuses()
    {
        var user = await factory.NewUserAsync();
        Assert.Equal(WorkspaceRole.Owner, user.Workspace.Role);
        var space = Assert.Single(user.Workspace.Spaces);
        Assert.Equal("Personal", space.Name);
        var list = Assert.Single(space.Lists);
        Assert.Equal("Tasks", list.Name);
        Assert.Equal(space.Id, list.SpaceId);
        Assert.Equal(["To do", "In progress", "Review", "Done"], list.Statuses.Select(s => s.Name));
        Assert.All(list.Statuses, s => Assert.Equal(list.Id, s.ListId));
        Assert.Equal([StatusCategory.NotStarted, StatusCategory.Active, StatusCategory.Active, StatusCategory.Done], list.Statuses.Select(s => s.Category));
    }

    [Fact]
    public async Task Api_requires_sign_in()
    {
        var anon = factory.CreateClient();
        anon.DefaultRequestHeaders.Add("X-Taskloom", "1");
        // 401 without a body (spec 5.1), for reads, writes and paths that don't exist.
        Func<Task<HttpResponseMessage>>[] requests =
        [
            () => anon.GetAsync("/api/workspaces"),
            () => anon.GetAsync("/api/me"),
            () => anon.GetAsync("/api/no-such-path"),
            () => anon.PatchAsJsonAsync("/api/me", new { displayName = "x" }),
        ];
        foreach (var send in requests)
        {
            var res = await send();
            Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
            Assert.Equal("", await res.Content.ReadAsStringAsync());
            Assert.Null(res.Content.Headers.ContentType);
        }

        // A session whose user no longer exists is signed out too.
        var user = await factory.NewUserAsync();
        await factory.DbAsync(db => db.Users.Where(u => u.Id == user.Me.Id).ExecuteDeleteAsync());
        foreach (var res in new[] { await user.Client.GetAsync("/api/me"), await user.Client.PatchAsJsonAsync("/api/me", new { displayName = "x" }) })
        {
            Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
            Assert.Equal("", await res.Content.ReadAsStringAsync());
        }
    }

    [Fact]
    public async Task Writes_without_the_csrf_header_are_rejected()
    {
        var user = await factory.NewUserAsync();
        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/lists/{user.List.Id}/tasks") { Content = JsonContent.Create(new { title = "x" }) };
        var task = await user.Client.CreateTaskAsync(user.List.Id, new { title = "Kept" });
        user.Client.DefaultRequestHeaders.Remove("X-Taskloom");
        Assert.Equal("Missing X-Taskloom header.", await (await user.Client.SendAsync(req)).ProblemAsync(HttpStatusCode.BadRequest));
        // Bulk calls too, including the keep-alive delete that runs when the page closes.
        Assert.Equal("Missing X-Taskloom header.", await (await user.Client.BulkAsync(new { id = task.Id, priority = "High" })).ProblemAsync(HttpStatusCode.BadRequest));
        Assert.Equal("Missing X-Taskloom header.", await (await user.Client.BulkDeleteAsync(task.Id)).ProblemAsync(HttpStatusCode.BadRequest));
        Assert.Equal("Missing X-Taskloom header.", await (await user.Client.DeleteAsync($"/api/tasks/{task.Id}")).ProblemAsync(HttpStatusCode.BadRequest));
        var left = Assert.Single(await user.Client.ListTasksAsync(user.List.Id));
        Assert.Equal((task.Id, TaskPriority.None), (left.Id, left.Priority));
    }

    [Fact]
    public async Task Users_cannot_see_other_workspaces()
    {
        var alice = await factory.NewUserAsync();
        var task = await alice.Client.CreateTaskAsync(alice.List.Id, new { title = "Private" });
        var bob = await factory.NewUserAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await bob.Client.GetAsync($"/api/lists/{alice.List.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.Client.GetAsync($"/api/lists/{alice.List.Id}/tasks")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.Client.GetAsync($"/api/tasks/{task.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.Client.GetAsync($"/api/workspaces/{alice.Workspace.Id}/members")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.Client.PatchAsJsonAsync($"/api/tasks/{task.Id}", new { title = "Hacked" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.Client.PatchAsJsonAsync($"/api/workspaces/{alice.Workspace.Id}", new { name = "Mine" })).StatusCode);
        // Tags and the workspace task query too.
        var tag = await alice.Client.CreateTagAsync(alice.Workspace.Id, "secret");
        Assert.Equal(HttpStatusCode.NotFound, (await bob.Client.GetAsync($"/api/workspaces/{alice.Workspace.Id}/tags")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.Client.PostAsJsonAsync($"/api/workspaces/{alice.Workspace.Id}/tags", new { name = "mine" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.Client.PatchAsJsonAsync($"/api/tags/{tag.Id}", new { name = "mine" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.Client.DeleteAsync($"/api/tags/{tag.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.Client.GetAsync($"/api/workspaces/{alice.Workspace.Id}/tasks?scope=all")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.Client.GetAsync($"/api/workspaces/{alice.Workspace.Id}/tasks?scope=nonsense")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.Client.BulkAsync(new { id = task.Id, title = "Hacked" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.Client.BulkDeleteAsync(task.Id)).StatusCode);
        Assert.Equal("Private", (await alice.Client.TaskDetailAsync(task.Id)).Task.Title);
        Assert.Equal(["secret"], (await alice.Client.TagsAsync(alice.Workspace.Id)).Select(t => t.Name));
    }

    [Fact]
    public async Task Client_generated_ids_are_kept()
    {
        var user = await factory.NewUserAsync();
        var id = Guid.NewGuid();
        var task = await user.Client.CreateTaskAsync(user.List.Id, new { id, title = "Offline-made" });
        Assert.Equal(id, task.Id);

        var again = await user.Client.PostAsJsonAsync($"/api/lists/{user.List.Id}/tasks", new { id, title = "Twice" });
        Assert.NotNull(await again.ProblemAsync(HttpStatusCode.Conflict));
        Assert.Equal("Offline-made", Assert.Single(await user.Client.ListTasksAsync(user.List.Id)).Title);
    }

    [Fact]
    public async Task Patch_updates_only_given_fields_and_null_clears()
    {
        var user = await factory.NewUserAsync();
        var task = await user.Client.CreateTaskAsync(user.List.Id,
            new { title = "Plan", dueDate = "2026-12-01", priority = "High", description = "<p>Agenda</p>" });
        Assert.True(task.HasDescription);

        var updated = await (await user.Client.PatchAsJsonAsync($"/api/tasks/{task.Id}", new { dueDate = (string?)null })).ReadAsync<TaskDto>();
        Assert.Null(updated.DueDate);
        Assert.Equal(TaskPriority.High, updated.Priority);
        Assert.Equal("Plan", updated.Title);
        Assert.True(updated.HasDescription);

        var cleared = await (await user.Client.PatchAsJsonAsync($"/api/tasks/{task.Id}", new { description = (string?)null, priority = (string?)null })).ReadAsync<TaskDto>();
        Assert.False(cleared.HasDescription);
        Assert.Equal(TaskPriority.None, cleared.Priority);
        Assert.Equal("Plan", cleared.Title);
        Assert.Equal("", (await user.Client.TaskDetailAsync(task.Id)).Description);
    }

    [Fact]
    public async Task Moving_to_a_done_status_completes_the_task_and_orders_between_neighbours()
    {
        var user = await factory.NewUserAsync();
        var done = user.Status("Done");
        var a = await user.Client.CreateTaskAsync(user.List.Id, new { title = "A", statusId = done.Id });
        var c = await user.Client.CreateTaskAsync(user.List.Id, new { title = "C", statusId = done.Id });
        var b = await user.Client.CreateTaskAsync(user.List.Id, new { title = "B" });
        Assert.Null(b.CompletedAt);

        var moved = await (await user.Client.PostAsJsonAsync($"/api/tasks/{b.Id}/move",
            new { statusId = done.Id, beforeTaskId = a.Id, afterTaskId = c.Id })).ReadAsync<TaskDto>();

        Assert.NotNull(moved.CompletedAt);
        var order = (await user.Client.ListTasksAsync(user.List.Id)).Where(t => t.StatusId == done.Id).Select(t => t.Title);
        Assert.Equal(["A", "B", "C"], order);
    }

    [Fact]
    public async Task Deleting_a_status_moves_its_tasks_to_the_first_status()
    {
        var user = await factory.NewUserAsync();
        var review = user.Status("Review");
        var task = await user.Client.CreateTaskAsync(user.List.Id, new { title = "T", statusId = review.Id });

        (await user.Client.DeleteAsync($"/api/statuses/{review.Id}")).EnsureSuccessStatusCode();

        var after = await user.Client.TaskDetailAsync(task.Id);
        Assert.Equal(user.List.Statuses.First().Id, after.Task.StatusId);
    }

    [Fact]
    public async Task Deleting_a_list_removes_its_tasks_and_subtasks()
    {
        var user = await factory.NewUserAsync();
        var newList = await user.Client.CreateListAsync(user.Space.Id, "Temp");
        Assert.Equal(user.Space.Id, newList.SpaceId);
        Assert.Equal(4, newList.Statuses.Count);
        var parent = await user.Client.CreateTaskAsync(newList.Id, new { title = "P" });
        var sub = await user.Client.CreateTaskAsync(newList.Id, new { title = "S", parentTaskId = parent.Id });

        (await user.Client.DeleteAsync($"/api/lists/{newList.Id}")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound, (await user.Client.GetAsync($"/api/tasks/{parent.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await user.Client.GetAsync($"/api/tasks/{sub.Id}")).StatusCode);
    }

    [Fact]
    public async Task Invited_members_can_be_assigned_and_guests_cannot_edit()
    {
        var owner = await factory.NewUserAsync();
        var guestEmail = Builders.NewEmail();
        var guest = await owner.Client.InviteAsync(owner.Workspace.Id, guestEmail, WorkspaceRole.Guest);
        Assert.True(guest.Pending);
        var task = await owner.Client.CreateTaskAsync(owner.List.Id, new { title = "Shared", assigneeIds = new[] { guest.UserId, owner.Me.Id } });
        Assert.Equal(new[] { guest.UserId, owner.Me.Id }.Order(), task.AssigneeIds.Order());

        var stranger = await factory.NewUserAsync();
        var refused = await owner.Client.PostAsJsonAsync($"/api/lists/{owner.List.Id}/tasks", new { title = "No", assigneeIds = new[] { stranger.Me.Id } });
        Assert.Equal("Everyone assigned must be a member of this workspace.", await refused.ProblemAsync(HttpStatusCode.BadRequest));

        var guestClient = await factory.SignInAsync(guestEmail);
        Assert.Equal(HttpStatusCode.OK, (await guestClient.GetAsync($"/api/tasks/{task.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await guestClient.PatchAsJsonAsync($"/api/tasks/{task.Id}", new { title = "No" })).StatusCode);
        // Guests can still comment.
        Assert.Equal(HttpStatusCode.OK, (await guestClient.PostAsJsonAsync($"/api/tasks/{task.Id}/comments", new { body = "Looks good" })).StatusCode);
        var members = await (await owner.Client.GetAsync($"/api/workspaces/{owner.Workspace.Id}/members")).ReadAsync<List<MemberDto>>();
        Assert.False(members.Single(m => m.UserId == guest.UserId).Pending);
    }

    [Fact]
    public async Task Attachments_round_trip_through_the_file_store()
    {
        var user = await factory.NewUserAsync();
        var task = await user.Client.CreateTaskAsync(user.List.Id, new { title = "With file" });
        using var form = new MultipartFormDataContent { { new ByteArrayContent("hello"u8.ToArray()), "file", "hello.txt" } };
        var att = await (await user.Client.PostAsync($"/api/tasks/{task.Id}/attachments", form)).ReadAsync<AttachmentDto>();
        Assert.Equal(user.Me.Id, att.UploadedById);
        Assert.Equal("hello", await user.Client.GetStringAsync($"/api/attachments/{att.Id}"));
        Assert.Equal(1, (await user.Client.TaskDetailAsync(task.Id)).Task.AttachmentCount);
    }
}
