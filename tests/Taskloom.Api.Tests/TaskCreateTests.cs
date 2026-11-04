using System.Net;
using Taskloom.Api.Tests.Support;
using Taskloom.Domain;

namespace Taskloom.Api.Tests;

/// <summary>POST /api/lists/{id}/tasks (spec 5.5 Create task) and the counts of the shared projection (5.2).</summary>
public class TaskCreateTests(TaskloomFactory factory) : IClassFixture<TaskloomFactory>
{
    [Fact]
    public async Task Default_status_is_the_first_not_started_status_and_top_placement_comes_first()
    {
        var user = await factory.NewUserAsync();
        var list = await user.Client.CreateListAsync(user.Space.Id, "Ordered");
        StatusDto Status(string name) => list.Statuses.Single(s => s.Name == name);
        // "In progress" now comes first by position and a second NotStarted status comes last:
        // the default is still the first NotStarted status by position.
        (await user.Client.PatchAsJsonAsync($"/api/statuses/{Status("In progress").Id}", new { position = 100 })).EnsureSuccessStatusCode();
        var backlog = await (await user.Client.PostAsJsonAsync($"/api/lists/{list.Id}/statuses", new { name = "Backlog", category = "NotStarted" })).ReadAsync<StatusDto>();

        var first = await user.Client.CreateTaskAsync(list.Id, new { title = "First" });
        Assert.Equal((Status("To do").Id, 1024d), (first.StatusId, first.Position));
        // Bottom (the default) is the highest position + 1024; Top is the lowest − 1024.
        var second = await user.Client.CreateTaskAsync(list.Id, new { title = "Second" });
        Assert.Equal(2048d, second.Position);
        var top = await user.Client.CreateTaskAsync(list.Id, new { title = "Top", placement = "Top" });
        Assert.Equal(0d, top.Position);
        var topmost = await user.Client.CreateTaskAsync(list.Id, new { title = "Topmost", placement = "Top" });
        Assert.Equal(-1024d, topmost.Position);
        var bottom = await user.Client.CreateTaskAsync(list.Id, new { title = "Bottom", placement = "Bottom" });
        Assert.Equal(3072d, bottom.Position);
        Assert.Equal(["Topmost", "Top", "First", "Second", "Bottom"],
            (await user.Client.ListTasksAsync(list.Id)).Where(t => t.StatusId == Status("To do").Id).Select(t => t.Title));

        // Only tasks with the same status and parent count: the first task of another status starts at 1024.
        var elsewhere = await user.Client.CreateTaskAsync(list.Id, new { title = "Elsewhere", statusId = backlog.Id, placement = "Top" });
        Assert.Equal((backlog.Id, 1024d), (elsewhere.StatusId, elsewhere.Position));
        var sub = await user.Client.CreateTaskAsync(list.Id, new { title = "Sub", parentTaskId = first.Id, placement = "Top" });
        Assert.Equal((Status("To do").Id, 1024d), (sub.StatusId, sub.Position));

        // A task created in a Done status is completed; others aren't.
        var done = await user.Client.CreateTaskAsync(list.Id, new { title = "Done already", statusId = Status("Done").Id });
        Assert.NotNull(done.CompletedAt);
        Assert.Null(first.CompletedAt);

        // Statuses of another list are refused.
        Assert.Equal("Choose a status of this list.", await (await user.Client.PostAsJsonAsync($"/api/lists/{list.Id}/tasks",
            new { title = "Wrong", statusId = user.Status("To do").Id })).ProblemAsync(HttpStatusCode.BadRequest));

        // With no NotStarted status the default is the first status by position.
        var noBacklog = await user.Client.CreateListAsync(user.Space.Id, "Active only", new { name = "Active only", statusPreset = "Simple" });
        (await user.Client.PatchAsJsonAsync($"/api/statuses/{noBacklog.Statuses[0].Id}", new { category = "Active", position = 5000 })).EnsureSuccessStatusCode();
        var defaulted = await user.Client.CreateTaskAsync(noBacklog.Id, new { title = "Defaulted" });
        Assert.Equal(noBacklog.Statuses.Single(s => s.Name == "In progress").Id, defaulted.StatusId);

        // Titles are trimmed and required; fields come back as sent.
        Assert.Equal("Enter a task title.", await (await user.Client.PostAsJsonAsync($"/api/lists/{list.Id}/tasks", new { title = "  " })).ProblemAsync(HttpStatusCode.BadRequest));
        Assert.Equal("Task titles can be up to 500 characters.",
            await (await user.Client.PostAsJsonAsync($"/api/lists/{list.Id}/tasks", new { title = new string('t', 501) })).ProblemAsync(HttpStatusCode.BadRequest));
        var full = await user.Client.CreateTaskAsync(list.Id, new { title = "  Full  ", priority = "Urgent", startDate = "2026-11-01", dueDate = "2026-11-30", assigneeIds = new[] { user.Me.Id } });
        Assert.Equal(("Full", TaskPriority.Urgent, new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30), user.Me.Id),
            (full.Title, full.Priority, full.StartDate!.Value, full.DueDate!.Value, Assert.Single(full.AssigneeIds)));
        Assert.Equal(HttpStatusCode.BadRequest, (await user.Client.PostAsJsonAsync($"/api/lists/{list.Id}/tasks", new { title = "Bad", priority = 2 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await user.Client.PostAsJsonAsync($"/api/lists/{list.Id}/tasks", new { title = "Bad", dueDate = "11/30/2026" })).StatusCode);
    }

    [Fact]
    public async Task Parent_must_be_a_top_level_task_in_the_same_list()
    {
        var user = await factory.NewUserAsync();
        var parent = await user.Client.CreateTaskAsync(user.List.Id, new { title = "Parent" });
        var sub = await user.Client.CreateTaskAsync(user.List.Id, new { title = "Sub", parentTaskId = parent.Id });
        Assert.Equal(parent.Id, sub.ParentTaskId);

        var url = $"/api/lists/{user.List.Id}/tasks";
        Assert.Equal("Subtasks can't have subtasks.",
            await (await user.Client.PostAsJsonAsync(url, new { title = "Grandchild", parentTaskId = sub.Id })).ProblemAsync(HttpStatusCode.BadRequest));
        var other = await user.Client.CreateListAsync(user.Space.Id, "Other");
        var elsewhere = await user.Client.CreateTaskAsync(other.Id, new { title = "Elsewhere" });
        Assert.Equal("Choose a parent task in this list.",
            await (await user.Client.PostAsJsonAsync(url, new { title = "Cross", parentTaskId = elsewhere.Id })).ProblemAsync(HttpStatusCode.BadRequest));
        Assert.Equal("Choose a parent task in this list.",
            await (await user.Client.PostAsJsonAsync(url, new { title = "Missing", parentTaskId = Guid.NewGuid() })).ProblemAsync(HttpStatusCode.BadRequest));
        // A task of another workspace can't be a parent either.
        var stranger = await factory.NewUserAsync();
        var foreign = await stranger.Client.CreateTaskAsync(stranger.List.Id, new { title = "Foreign" });
        Assert.Equal("Choose a parent task in this list.",
            await (await user.Client.PostAsJsonAsync(url, new { title = "Foreign child", parentTaskId = foreign.Id })).ProblemAsync(HttpStatusCode.BadRequest));

        // Subtasks are placed among their own siblings, and their creation is recorded on the parent.
        var sub2 = await user.Client.CreateTaskAsync(user.List.Id, new { title = "Sub 2", parentTaskId = parent.Id });
        Assert.Equal((1024d, 2048d), (sub.Position, sub2.Position));
        var detail = await user.Client.TaskDetailAsync(parent.Id);
        Assert.Equal(["Sub", "Sub 2"], detail.Subtasks.Select(s => s.Title));
        Assert.Equal([(TaskActivityKind.SubtaskAdded, (string?)null, "Sub"), (TaskActivityKind.SubtaskAdded, null, "Sub 2")],
            detail.Activity.Select(a => (a.Kind, a.From, a.To)));
        Assert.Equal(user.Me.Id, detail.Activity[0].Actor.Id);
        Assert.Equal(new TaskRefDto(parent.Id, "Parent", user.List.Id), (await user.Client.TaskDetailAsync(sub.Id)).Parent);
        Assert.Empty((await user.Client.TaskDetailAsync(sub.Id)).Activity);
    }

    [Fact]
    public async Task Counts_are_projected_for_subtasks_comments_and_attachments()
    {
        var user = await factory.NewUserAsync();
        var parent = await user.Client.CreateTaskAsync(user.List.Id, new { title = "Parent" });
        Assert.Equal((0, 0, 0, 0), (parent.SubtaskCount, parent.SubtaskDoneCount, parent.CommentCount, parent.AttachmentCount));
        await user.Client.CreateTaskAsync(user.List.Id, new { title = "Open", parentTaskId = parent.Id });
        await user.Client.CreateTaskAsync(user.List.Id, new { title = "Done", parentTaskId = parent.Id, statusId = user.Status("Done").Id });
        var third = await user.Client.CreateTaskAsync(user.List.Id, new { title = "Later done", parentTaskId = parent.Id });
        await user.Client.MoveTaskAsync(third.Id, new { statusId = user.Status("Done").Id });
        (await user.Client.PostAsJsonAsync($"/api/tasks/{parent.Id}/comments", new { body = "One" })).EnsureSuccessStatusCode();
        (await user.Client.PostAsJsonAsync($"/api/tasks/{parent.Id}/comments", new { body = "Two" })).EnsureSuccessStatusCode();
        (await user.Client.PostAsJsonAsync($"/api/tasks/{third.Id}/comments", new { body = "On the subtask" })).EnsureSuccessStatusCode();
        await user.Client.UploadAsync(parent.Id, "a.txt", "a"u8.ToArray());

        var expected = (3, 2, 2, 1);
        static (int, int, int, int) Counts(TaskDto t) => (t.SubtaskCount, t.SubtaskDoneCount, t.CommentCount, t.AttachmentCount);
        // Every endpoint that returns TaskDto builds it with the same projection.
        Assert.Equal(expected, Counts((await user.Client.ListTasksAsync(user.List.Id)).Single(t => t.Id == parent.Id)));
        var detail = await user.Client.TaskDetailAsync(parent.Id);
        Assert.Equal(expected, Counts(detail.Task));
        Assert.Equal(expected, Counts(await user.Client.PatchTaskAsync(parent.Id, new { priority = "High" })));
        Assert.Equal(expected, Counts(await user.Client.MoveTaskAsync(parent.Id, new { statusId = user.Status("Review").Id })));
        Assert.Equal(expected, Counts(Assert.Single((await (await user.Client.BulkAsync(new { id = parent.Id, priority = "Low" })).ReadAsync<BulkUpdateResultDto>()).Tasks)));
        Assert.Equal(expected, Counts((await user.Client.WorkspaceTasksAsync(user.Workspace.Id, "?scope=all")).Tasks.Single(t => t.Id == parent.Id)));
        // Subtasks count their own rows only.
        Assert.Equal((0, 0, 1, 0), Counts(detail.Subtasks.Single(s => s.Id == third.Id)));
        Assert.Equal(["Open", "Done", "Later done"], detail.Subtasks.Select(s => s.Title));
    }
}
