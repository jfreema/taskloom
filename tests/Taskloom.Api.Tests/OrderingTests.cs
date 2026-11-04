using System.Net;
using Microsoft.EntityFrameworkCore;
using Taskloom.Api.Tests.Support;
using Taskloom.Domain;

namespace Taskloom.Api.Tests;

/// <summary>Positions (spec 5.5): the top-of-status rule, true siblings on move, and the stable order of equal positions.</summary>
public class OrderingTests(TaskloomFactory factory) : IClassFixture<TaskloomFactory>
{
    private static async Task<List<string>> TitlesAsync(TestUser user, Guid statusId) =>
        (await user.Client.ListTasksAsync(user.List.Id)).Where(t => t.StatusId == statusId && t.ParentTaskId == null).Select(t => t.Title).ToList();

    [Fact]
    public async Task Status_change_without_position_puts_the_task_first()
    {
        var user = await factory.NewUserAsync();
        var todo = user.Status("To do");
        var doing = user.Status("In progress");
        var a = await user.Client.CreateTaskAsync(user.List.Id, new { title = "A", statusId = doing.Id });
        var b = await user.Client.CreateTaskAsync(user.List.Id, new { title = "B", statusId = doing.Id });
        var c = await user.Client.CreateTaskAsync(user.List.Id, new { title = "C" });

        // The re-statused task goes to the top of its new status: the lowest position there − 1024.
        var moved = await user.Client.PatchTaskAsync(c.Id, new { statusId = doing.Id });
        Assert.Equal(a.Position - 1024, moved.Position);
        Assert.Equal(["C", "A", "B"], await TitlesAsync(user, doing.Id));
        // Into an empty status: 1024. A just-completed task is first in Done.
        var done = await user.Client.PatchTaskAsync(b.Id, new { statusId = user.Status("Done").Id });
        Assert.Equal(1024d, done.Position);
        Assert.NotNull(done.CompletedAt);
        var doneToo = await user.Client.PatchTaskAsync(a.Id, new { statusId = user.Status("Done").Id });
        Assert.Equal(0d, doneToo.Position);
        Assert.Equal(["A", "B"], await TitlesAsync(user, user.Status("Done").Id));

        // An explicit position is kept exactly (undo sends the old status and position together).
        var undone = await user.Client.PatchTaskAsync(c.Id, new { statusId = todo.Id, position = c.Position });
        Assert.Equal((todo.Id, c.Position), (undone.StatusId, undone.Position));
        var restored = await user.Client.PatchTaskAsync(a.Id, new { position = 2048.5, statusId = doing.Id });
        Assert.Equal((doing.Id, 2048.5, (DateTimeOffset?)null), (restored.StatusId, restored.Position, restored.CompletedAt));
        // The same status again leaves the position alone.
        Assert.Equal(2048.5, (await user.Client.PatchTaskAsync(a.Id, new { statusId = doing.Id })).Position);

        // Bulk follows the same rule, one update after the other.
        var bulk = await (await user.Client.BulkAsync(new { id = a.Id, statusId = todo.Id }, new { id = b.Id, statusId = todo.Id }, new { id = c.Id, priority = "High" }))
            .ReadAsync<BulkUpdateResultDto>();
        Assert.Equal([a.Id, b.Id, c.Id], bulk.Tasks.Select(t => t.Id));
        Assert.Equal(c.Position - 1024, bulk.Tasks[0].Position);
        Assert.Equal(c.Position - 2048, bulk.Tasks[1].Position);
        Assert.Equal(["B", "A", "C"], await TitlesAsync(user, todo.Id));
        Assert.Equal(HttpStatusCode.BadRequest, (await user.Client.PatchAsJsonAsync($"/api/tasks/{a.Id}", new { position = "top" })).StatusCode);

        // Subtasks go to the top among their parent's subtasks only.
        var sub1 = await user.Client.CreateTaskAsync(user.List.Id, new { title = "S1", parentTaskId = c.Id, statusId = doing.Id });
        var sub2 = await user.Client.CreateTaskAsync(user.List.Id, new { title = "S2", parentTaskId = c.Id });
        Assert.Equal(sub1.Position - 1024, (await user.Client.PatchTaskAsync(sub2.Id, new { statusId = doing.Id })).Position);
    }

    [Fact]
    public async Task Move_uses_true_siblings_and_renumbers_close_positions()
    {
        var user = await factory.NewUserAsync();
        var todo = user.Status("To do");
        var doing = user.Status("In progress");
        var a = await user.Client.CreateTaskAsync(user.List.Id, new { title = "A" });      // 1024
        var hidden = await user.Client.CreateTaskAsync(user.List.Id, new { title = "H" }); // 2048, hidden from the client by a filter
        var c = await user.Client.CreateTaskAsync(user.List.Id, new { title = "C" });      // 3072
        var x = await user.Client.CreateTaskAsync(user.List.Id, new { title = "X", statusId = doing.Id });

        // The client saw A and C next to each other. The task goes right after A, before the hidden H, not onto H's position.
        var moved = await user.Client.MoveTaskAsync(x.Id, new { statusId = todo.Id, beforeTaskId = a.Id, afterTaskId = c.Id });
        Assert.Equal((todo.Id, 1536d), (moved.StatusId, moved.Position));
        Assert.Equal(["A", "X", "H", "C"], await TitlesAsync(user, todo.Id));
        // With only the task below: right before it, after its true previous sibling.
        moved = await user.Client.MoveTaskAsync(x.Id, new { statusId = todo.Id, afterTaskId = c.Id });
        Assert.Equal(2560d, moved.Position);
        Assert.Equal(["A", "H", "X", "C"], await TitlesAsync(user, todo.Id));
        // With neither: the bottom of the status. To the top: before the first task.
        Assert.Equal(4096d, (await user.Client.MoveTaskAsync(x.Id, new { statusId = todo.Id })).Position);
        Assert.Equal(0d, (await user.Client.MoveTaskAsync(x.Id, new { statusId = todo.Id, afterTaskId = a.Id })).Position);
        Assert.Equal(["X", "A", "H", "C"], await TitlesAsync(user, todo.Id));

        // A neighbour that isn't a sibling in the target status is refused, and nothing moves.
        var y = await user.Client.CreateTaskAsync(user.List.Id, new { title = "Y", statusId = doing.Id });
        Assert.NotNull(await (await user.Client.PostAsJsonAsync($"/api/tasks/{x.Id}/move", new { statusId = todo.Id, beforeTaskId = y.Id })).ProblemAsync(HttpStatusCode.BadRequest));
        Assert.NotNull(await (await user.Client.PostAsJsonAsync($"/api/tasks/{x.Id}/move", new { statusId = todo.Id, beforeTaskId = a.Id, afterTaskId = y.Id })).ProblemAsync(HttpStatusCode.BadRequest));
        Assert.NotNull(await (await user.Client.PostAsJsonAsync($"/api/tasks/{x.Id}/move", new { statusId = todo.Id, afterTaskId = x.Id })).ProblemAsync(HttpStatusCode.BadRequest));
        Assert.Equal("Choose a status of this list.",
            await (await user.Client.PostAsJsonAsync($"/api/tasks/{x.Id}/move", new { statusId = Guid.NewGuid() })).ProblemAsync(HttpStatusCode.BadRequest));
        Assert.Equal(["X", "A", "H", "C"], await TitlesAsync(user, todo.Id));

        // Neighbours less than 0.001 apart are renumbered 1024, 2048, … in their current order first.
        await user.Client.PatchTaskAsync(hidden.Id, new { position = 1024.0004 });
        moved = await user.Client.MoveTaskAsync(y.Id, new { statusId = todo.Id, beforeTaskId = a.Id, afterTaskId = hidden.Id });
        var positions = (await user.Client.ListTasksAsync(user.List.Id)).Where(t => t.StatusId == todo.Id).ToDictionary(t => t.Title, t => t.Position);
        Assert.Equal(new Dictionary<string, double> { ["X"] = 1024, ["A"] = 2048, ["Y"] = 2560, ["H"] = 3072, ["C"] = 4096 }, positions);
        Assert.Equal(2560d, moved.Position);
        // Equal neighbours are renumbered too.
        await user.Client.PatchTaskAsync(c.Id, new { position = 3072 });
        moved = await user.Client.MoveTaskAsync(x.Id, new { statusId = todo.Id, afterTaskId = c.Id });
        Assert.Equal(["A", "Y", "H", "X", "C"], await TitlesAsync(user, todo.Id));
        Assert.Equal(1024, (await user.Client.ListTasksAsync(user.List.Id)).Single(t => t.Id == a.Id).Position);

        // A status change by move is recorded and completes the task.
        var completed = await user.Client.MoveTaskAsync(y.Id, new { statusId = user.Status("Done").Id });
        Assert.NotNull(completed.CompletedAt);
        var status = (await user.Client.TaskDetailAsync(y.Id)).Activity.Where(e => e.Kind == TaskActivityKind.StatusChanged).Select(e => (e.From, e.To));
        Assert.Equal([("In progress", "To do"), ("To do", "Done")], status);
    }

    [Fact]
    public async Task Equal_positions_order_by_created_at_then_id()
    {
        var user = await factory.NewUserAsync();
        var first = await user.Client.CreateTaskAsync(user.List.Id, new { title = "First", dueDate = "2026-12-01" });
        var second = await user.Client.CreateTaskAsync(user.List.Id, new { title = "Second", dueDate = "2026-12-01" });
        var third = await user.Client.CreateTaskAsync(user.List.Id, new { title = "Third", dueDate = "2026-12-01" });
        foreach (var t in new[] { third, second, first }) await user.Client.PatchTaskAsync(t.Id, new { position = 5000 });
        Assert.Equal(["First", "Second", "Third"], (await user.Client.ListTasksAsync(user.List.Id)).Select(t => t.Title));

        // Same position and creation time: the id decides (ascending, as PostgreSQL compares uuids).
        Guid low = Guid.Parse("00000000-0000-4000-8000-000000000001"), high = Guid.Parse("ffffffff-0000-4000-8000-000000000001");
        await user.Client.CreateTaskAsync(user.List.Id, new { id = high, title = "High id", dueDate = "2026-12-01" });
        await user.Client.CreateTaskAsync(user.List.Id, new { id = low, title = "Low id", dueDate = "2026-12-01" });
        var parentSubLow = Guid.Parse("00000000-0000-4000-8000-000000000002");
        var parentSubHigh = Guid.Parse("ffffffff-0000-4000-8000-000000000002");
        await user.Client.CreateTaskAsync(user.List.Id, new { id = parentSubHigh, title = "Sub high", parentTaskId = first.Id });
        await user.Client.CreateTaskAsync(user.List.Id, new { id = parentSubLow, title = "Sub low", parentTaskId = first.Id });
        var when = DateTimeOffset.UtcNow.AddDays(-1);
        await factory.DbAsync(db => db.Tasks.Where(t => t.Id == low || t.Id == high || t.Id == parentSubLow || t.Id == parentSubHigh)
            .ExecuteUpdateAsync(u => u.SetProperty(t => t.Position, 5000).SetProperty(t => t.CreatedAt, when)));

        string[] expected = ["Low id", "High id", "First", "Second", "Third"];
        Assert.Equal(expected, (await user.Client.ListTasksAsync(user.List.Id)).Where(t => t.ParentTaskId == null).Select(t => t.Title));
        Assert.Equal(["Sub low", "Sub high"], (await user.Client.TaskDetailAsync(first.Id)).Subtasks.Select(t => t.Title));
        // Workspace results with the same due date keep the same order.
        Assert.Equal(expected, (await user.Client.WorkspaceTasksAsync(user.Workspace.Id, "?scope=all")).Tasks.Where(t => t.ParentTaskId == null).Select(t => t.Title));
    }
}
