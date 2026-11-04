using System.Net;
using Taskloom.Api.Tests.Support;
using Taskloom.Domain;

namespace Taskloom.Api.Tests;

/// <summary>PATCH /api/tasks/{id} and bulk updates with <c>listId</c> (spec 5.5 Patch task): the status mapping, subtasks and the refusals.</summary>
public class MoveToListTests(TaskloomFactory factory) : IClassFixture<TaskloomFactory>
{
    private static (TaskActivityKind, string?, string?) Row(TaskActivity a) => (a.Kind, a.FromValue, a.ToValue);

    [Fact]
    public async Task Moving_maps_status_by_name_then_category_and_takes_subtasks()
    {
        var user = await factory.NewUserAsync();
        // The target: New (NotStarted), IN PROGRESS (Active), Waiting (Active, now first of the Active ones), Done.
        var requests = await user.Client.CreateListAsync(user.Space.Id, "Requests", new { name = "Requests", statusPreset = "Requests" });
        StatusDto In(ListSummaryDto list, string name) => list.Statuses.Single(s => s.Name == name);
        (await user.Client.PatchAsJsonAsync($"/api/statuses/{In(requests, "In progress").Id}", new { name = "IN PROGRESS" })).EnsureSuccessStatusCode();
        (await user.Client.PatchAsJsonAsync($"/api/statuses/{In(requests, "Waiting").Id}", new { position = 1500 })).EnsureSuccessStatusCode();
        requests = await user.Client.ListAsync(requests.Id);
        var already = await user.Client.CreateTaskAsync(requests.Id, new { title = "Already there", statusId = In(requests, "IN PROGRESS").Id });

        // A parent in "In progress" with subtasks in To do, Review and Done; a second task in Review.
        var parent = await user.Client.CreateTaskAsync(user.List.Id, new { title = "Parent", statusId = user.Status("In progress").Id });
        var todoSub = await user.Client.CreateTaskAsync(user.List.Id, new { title = "To do sub", parentTaskId = parent.Id });
        var reviewSub = await user.Client.CreateTaskAsync(user.List.Id, new { title = "Review sub", parentTaskId = parent.Id, statusId = user.Status("Review").Id });
        var doneSub = await user.Client.CreateTaskAsync(user.List.Id, new { title = "Done sub", parentTaskId = parent.Id, statusId = user.Status("Done").Id });
        var reviewed = await user.Client.CreateTaskAsync(user.List.Id, new { title = "Reviewed", statusId = user.Status("Review").Id });
        var todo = await user.Client.CreateTaskAsync(user.List.Id, new { title = "To do", priority = "High" });
        var done = await user.Client.CreateTaskAsync(user.List.Id, new { title = "Done", statusId = user.Status("Done").Id });

        // Same name, ignoring case: "In progress" becomes "IN PROGRESS", at the bottom of that status.
        var moved = await user.Client.PatchTaskAsync(parent.Id, new { listId = requests.Id });
        Assert.Equal((requests.Id, In(requests, "IN PROGRESS").Id, already.Position + 1024), (moved.ListId, moved.StatusId, moved.Position));
        Assert.True(moved.UpdatedAt > parent.UpdatedAt);
        Assert.Equal((3, 1), (moved.SubtaskCount, moved.SubtaskDoneCount));
        // Its subtasks come along, each mapped the same way, keeping their places among their siblings:
        // To do (no name match) → the first NotStarted status, Review → the first Active status by position, Done → Done.
        var subs = (await user.Client.TaskDetailAsync(parent.Id)).Subtasks.ToDictionary(t => t.Id);
        Assert.All(subs.Values, s => Assert.Equal(requests.Id, s.ListId));
        Assert.Equal((In(requests, "New").Id, todoSub.Position, (DateTimeOffset?)null), (subs[todoSub.Id].StatusId, subs[todoSub.Id].Position, subs[todoSub.Id].CompletedAt));
        Assert.Equal((In(requests, "Waiting").Id, reviewSub.Position), (subs[reviewSub.Id].StatusId, subs[reviewSub.Id].Position));
        Assert.Equal((In(requests, "Done").Id, doneSub.CompletedAt), (subs[doneSub.Id].StatusId, subs[doneSub.Id].CompletedAt));
        Assert.DoesNotContain(await user.Client.ListTasksAsync(user.List.Id), t => t.ParentTaskId == parent.Id);
        // The move is recorded on the moved task only (with the status, whose name changed); subtasks get nothing.
        Assert.Equal([(TaskActivityKind.MovedList, "Tasks", "Requests"), (TaskActivityKind.StatusChanged, "In progress", "IN PROGRESS")],
            (await factory.ActivityRowsAsync(parent.Id)).Where(r => r.Kind != TaskActivityKind.SubtaskAdded).Select(Row));
        foreach (var sub in new[] { todoSub, reviewSub, doneSub }) Assert.Empty(await factory.ActivityRowsAsync(sub.Id));

        // No name match: the first status of the same category by position.
        var mapped = await user.Client.PatchTaskAsync(reviewed.Id, new { listId = requests.Id });
        Assert.Equal(In(requests, "Waiting").Id, mapped.StatusId);
        Assert.Equal(1024d, mapped.Position);
        // Done stays Done (by name) and keeps its completion time.
        var stillDone = await user.Client.PatchTaskAsync(done.Id, new { listId = requests.Id });
        Assert.Equal((In(requests, "Done").Id, done.CompletedAt), (stillDone.StatusId, stillDone.CompletedAt));
        Assert.Equal([(TaskActivityKind.MovedList, "Tasks", "Requests")], (await factory.ActivityRowsAsync(done.Id)).Select(Row));

        // No name or category match: the first status. "Odd" has Backlog (Active) and Shipped (Done) only.
        var odd = await user.Client.CreateListAsync(user.Space.Id, "Odd", new { name = "Odd", statusPreset = "Simple" });
        (await user.Client.PatchAsJsonAsync($"/api/statuses/{In(odd, "To do").Id}", new { name = "Backlog", category = "Active", position = 3000 })).EnsureSuccessStatusCode();
        (await user.Client.DeleteAsync($"/api/statuses/{In(odd, "In progress").Id}")).EnsureSuccessStatusCode();
        (await user.Client.PatchAsJsonAsync($"/api/statuses/{In(odd, "Done").Id}", new { name = "Shipped", position = 1000 })).EnsureSuccessStatusCode();
        var toOdd = await user.Client.PatchTaskAsync(todo.Id, new { listId = odd.Id });
        Assert.Equal((In(odd, "Done").Id, TaskPriority.High), (toOdd.StatusId, toOdd.Priority));
        // The first status here is Done, so the task is completed by the move; moving back by name reopens it.
        Assert.NotNull(toOdd.CompletedAt);
        var back = await user.Client.PatchTaskAsync(todo.Id, new { listId = user.List.Id, statusId = user.Status("To do").Id });
        Assert.Equal((user.List.Id, user.Status("To do").Id, (DateTimeOffset?)null), (back.ListId, back.StatusId, back.CompletedAt));

        // A statusId sent with listId wins over the mapping, and must belong to that list.
        var chosen = await user.Client.PatchTaskAsync(todo.Id, new { listId = requests.Id, statusId = In(requests, "Done").Id });
        Assert.Equal((requests.Id, In(requests, "Done").Id), (chosen.ListId, chosen.StatusId));
        Assert.NotNull(chosen.CompletedAt);

        // Bulk moves follow the same rules, task by task, and keep the request order.
        var a = await user.Client.CreateTaskAsync(user.List.Id, new { title = "A", statusId = user.Status("Review").Id });
        var b = await user.Client.CreateTaskAsync(user.List.Id, new { title = "B" });
        var bSub = await user.Client.CreateTaskAsync(user.List.Id, new { title = "B sub", parentTaskId = b.Id, statusId = user.Status("In progress").Id });
        var bulk = await (await user.Client.BulkAsync(new { id = b.Id, listId = requests.Id }, new { id = a.Id, listId = requests.Id }))
            .ReadAsync<BulkUpdateResultDto>();
        Assert.Equal([(b.Id, In(requests, "New").Id), (a.Id, In(requests, "Waiting").Id)], bulk.Tasks.Select(t => (t.Id, t.StatusId)));
        Assert.Equal(mapped.Position + 1024, bulk.Tasks[1].Position);
        var bSubAfter = (await user.Client.TaskDetailAsync(bSub.Id)).Task;
        Assert.Equal((requests.Id, In(requests, "IN PROGRESS").Id), (bSubAfter.ListId, bSubAfter.StatusId));
    }

    [Fact]
    public async Task Moving_a_subtask_alone_or_to_another_workspace_is_rejected()
    {
        var user = await factory.NewUserAsync();
        var other = await user.Client.CreateListAsync(user.Space.Id, "Other");
        var parent = await user.Client.CreateTaskAsync(user.List.Id, new { title = "Parent" });
        var sub = await user.Client.CreateTaskAsync(user.List.Id, new { title = "Sub", parentTaskId = parent.Id });
        // A workspace the user also owns: its lists still aren't this task's to move to.
        var elsewhere = await (await user.Client.PostAsJsonAsync("/api/workspaces", new { name = "Elsewhere" })).ReadAsync<WorkspaceTreeDto>();
        var elsewhereSpace = await (await user.Client.PostAsJsonAsync($"/api/workspaces/{elsewhere.Id}/spaces", new { name = "Far" })).ReadAsync<SpaceDto>();
        var farList = await user.Client.CreateListAsync(elsewhereSpace.Id, "Far list");
        var before = await SnapshotAsync(user, parent.Id, sub.Id);

        async Task Refused(string expected, HttpResponseMessage res)
        {
            Assert.Equal(expected, await res.ProblemAsync(HttpStatusCode.BadRequest));
            Assert.Equal(before, await SnapshotAsync(user, parent.Id, sub.Id));
            Assert.Empty(await user.Client.ListTasksAsync(other.Id));
            Assert.Empty(await user.Client.ListTasksAsync(farList.Id));
        }

        // A subtask moves only with its parent, alone or in bulk.
        await Refused("Subtasks move with their parent.", await user.Client.PatchAsJsonAsync($"/api/tasks/{sub.Id}", new { listId = other.Id }));
        await Refused("Subtasks move with their parent.", await user.Client.BulkAsync(new { id = parent.Id, priority = "High" }, new { id = sub.Id, listId = other.Id }));
        // Lists of another workspace (even one the caller owns), or no list at all, are refused.
        await Refused("Choose a list in this workspace.", await user.Client.PatchAsJsonAsync($"/api/tasks/{parent.Id}", new { listId = farList.Id }));
        await Refused("Choose a list in this workspace.", await user.Client.PatchAsJsonAsync($"/api/tasks/{parent.Id}", new { listId = farList.Id, statusId = farList.Statuses[0].Id }));
        await Refused("Choose a list in this workspace.", await user.Client.PatchAsJsonAsync($"/api/tasks/{parent.Id}", new { listId = Guid.NewGuid() }));
        await Refused("Choose a list in this workspace.", await user.Client.BulkAsync(new { id = parent.Id, listId = farList.Id }));
        await Refused("Choose a list in this workspace.", await user.Client.PatchAsJsonAsync($"/api/tasks/{parent.Id}", new { listId = "not-a-list" }));
        // With both, the status must be one of the target list's.
        await Refused("Choose a status of this list.", await user.Client.PatchAsJsonAsync($"/api/tasks/{parent.Id}", new { listId = other.Id, statusId = user.Status("Done").Id }));
        await Refused("Choose a status of this list.", await user.Client.BulkAsync(new { id = parent.Id, listId = other.Id, statusId = user.Status("Done").Id }));

        // The task's own list changes nothing, for a subtask too.
        Assert.Equal(sub.UpdatedAt, (await user.Client.PatchTaskAsync(sub.Id, new { listId = user.List.Id })).UpdatedAt);
        Assert.Equal(before, await SnapshotAsync(user, parent.Id, sub.Id));
        // A Guest can't move anything; a non-member gets 404.
        var (guest, _) = await factory.JoinAsync(user, WorkspaceRole.Guest);
        Assert.Equal(HttpStatusCode.Forbidden, (await guest.PatchAsJsonAsync($"/api/tasks/{parent.Id}", new { listId = other.Id })).StatusCode);
        var stranger = await factory.NewUserAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.Client.PatchAsJsonAsync($"/api/tasks/{parent.Id}", new { listId = stranger.List.Id })).StatusCode);
        Assert.Equal(before, await SnapshotAsync(user, parent.Id, sub.Id));
    }

    /// <summary>The two tasks as the API returns them, with their activity: equal snapshots mean nothing changed.</summary>
    private static async Task<string> SnapshotAsync(TestUser user, params Guid[] ids)
    {
        var parts = new List<string>();
        foreach (var id in ids) parts.Add(await (await user.Client.GetAsync($"/api/tasks/{id}")).Content.ReadAsStringAsync());
        return string.Join("\n", parts);
    }
}
