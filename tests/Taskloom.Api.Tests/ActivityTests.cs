using Microsoft.EntityFrameworkCore;
using Taskloom.Api.Tests.Support;
using Taskloom.Domain;

namespace Taskloom.Api.Tests;

/// <summary>Task history (spec 5.6): one row per changed field, with display values, saved with the change.</summary>
public class ActivityTests(TaskloomFactory factory) : IClassFixture<TaskloomFactory>
{
    private static (TaskActivityKind, string?, string?) Row(TaskActivity a) => (a.Kind, a.FromValue, a.ToValue);

    [Fact]
    public async Task Changes_record_activity_with_display_values()
    {
        var user = await factory.NewUserAsync();
        var (mia, miaMember) = await factory.JoinAsync(user, WorkspaceRole.Member, "Mia Member");
        var design = await user.Client.CreateTagAsync(user.Workspace.Id, "Design");
        var other = await user.Client.CreateListAsync(user.Space.Id, "Other list");
        var task = await user.Client.CreateTaskAsync(user.List.Id, new { title = "Draft" });
        // Creating a top-level task records nothing.
        Assert.Empty(await factory.ActivityRowsAsync(task.Id));

        Task<TaskDto> Patch(object body) => user.Client.PatchTaskAsync(task.Id, body);
        await Patch(new { title = "Draft v2" });
        await Patch(new { statusId = user.Status("In progress").Id });
        await Patch(new { assigneeIds = new[] { miaMember.UserId } });
        await Patch(new { dueDate = "2026-11-30", startDate = "2026-11-02" });
        await Patch(new { dueDate = "2026-12-01" });
        await Patch(new { priority = "High" });
        await Patch(new { tagIds = new[] { design.Id } });
        await Patch(new { description = "<p>Hello</p>" });
        // Another member's change; a null priority is None.
        await mia.PatchTaskAsync(task.Id, new { priority = (string?)null, dueDate = (string?)null });
        await user.Client.MoveTaskAsync(task.Id, new { statusId = user.Status("Done").Id });
        var child = await user.Client.CreateTaskAsync(user.List.Id, new { title = "Child", parentTaskId = task.Id });
        var file = await user.Client.UploadAsync(task.Id, "notes.txt", "n"u8.ToArray());
        (await user.Client.DeleteAsync($"/api/attachments/{file.Id}")).EnsureSuccessStatusCode();
        await Patch(new { tagIds = Array.Empty<Guid>(), assigneeIds = Array.Empty<Guid>() });
        // "Done" maps to "Done" by name, so only the list move is recorded.
        var moved = await Patch(new { listId = other.Id });
        Assert.Equal(other.Statuses.Single(s => s.Name == "Done").Id, moved.StatusId);
        (await user.Client.BulkAsync(new { id = task.Id, priority = "Low", addTagIds = new[] { design.Id } })).EnsureSuccessStatusCode();

        (TaskActivityKind, string?, string?)[] expected =
        [
            (TaskActivityKind.Renamed, "Draft", "Draft v2"),
            (TaskActivityKind.StatusChanged, "To do", "In progress"),
            (TaskActivityKind.AssigneeAdded, null, "Mia Member"),
            (TaskActivityKind.StartDateChanged, null, "2026-11-02"),
            (TaskActivityKind.DueDateChanged, null, "2026-11-30"),
            (TaskActivityKind.DueDateChanged, "2026-11-30", "2026-12-01"),
            (TaskActivityKind.PriorityChanged, "None", "High"),
            (TaskActivityKind.TagAdded, null, "Design"),
            (TaskActivityKind.DescriptionChanged, null, null),
            (TaskActivityKind.PriorityChanged, "High", "None"),
            (TaskActivityKind.DueDateChanged, "2026-12-01", null),
            (TaskActivityKind.StatusChanged, "In progress", "Done"),
            (TaskActivityKind.SubtaskAdded, null, "Child"),
            (TaskActivityKind.AttachmentAdded, null, "notes.txt"),
            (TaskActivityKind.AttachmentRemoved, "notes.txt", null),
            (TaskActivityKind.AssigneeRemoved, "Mia Member", null),
            (TaskActivityKind.TagRemoved, "Design", null),
            (TaskActivityKind.MovedList, "Tasks", "Other list"),
            (TaskActivityKind.PriorityChanged, "None", "Low"),
            (TaskActivityKind.TagAdded, null, "Design"),
        ];
        var rows = await factory.ActivityRowsAsync(task.Id);
        Assert.Equal(expected, rows.Select(Row));
        Guid me = user.Me.Id, other2 = miaMember.UserId;
        Assert.Equal([me, me, me, me, me, me, me, me, me, other2, other2, me, me, me, me, me, me, me, me, me], rows.Select(r => r.ActorId));

        // The detail returns the same rows, oldest first, with the actor.
        var detail = await user.Client.TaskDetailAsync(task.Id);
        Assert.Equal(expected, detail.Activity.Select(a => (a.Kind, a.From, a.To)));
        Assert.Equal(("Mia Member", user.Me.DisplayName), (detail.Activity[9].Actor.DisplayName, detail.Activity[0].Actor.DisplayName));
        Assert.Equal(rows.Select(r => r.Id), detail.Activity.Select(a => a.Id));
        // The subtask moved with its parent and has no history of its own.
        Assert.Equal(other.Id, (await user.Client.TaskDetailAsync(child.Id)).Task.ListId);
        Assert.Empty(await factory.ActivityRowsAsync(child.Id));
    }

    [Fact]
    public async Task Unchanged_values_record_nothing_and_description_edits_merge_within_ten_minutes()
    {
        var user = await factory.NewUserAsync();
        var (mia, _) = await factory.JoinAsync(user, WorkspaceRole.Member, "Mia Member");
        var tag = await user.Client.CreateTagAsync(user.Workspace.Id, "ops");
        var task = await user.Client.CreateTaskAsync(user.List.Id, new
        {
            title = "Same", description = "<p>Body</p>", priority = "High", startDate = "2026-11-01", dueDate = "2026-11-30",
            assigneeIds = new[] { user.Me.Id }, tagIds = new[] { tag.Id },
        });

        // Every field sent with the value it already has (after trimming and sanitizing): no rows, and UpdatedAt stays.
        var same = await user.Client.PatchTaskAsync(task.Id, new
        {
            title = "  Same  ", description = "<p>Body</p><script>alert(1)</script>", priority = "high", startDate = "2026-11-01", dueDate = "2026-11-30",
            statusId = task.StatusId, listId = task.ListId, position = task.Position, assigneeIds = new[] { user.Me.Id }, tagIds = new[] { tag.Id },
        });
        Assert.Equal(task.UpdatedAt, same.UpdatedAt);
        (await user.Client.BulkAsync(new { id = task.Id, priority = "High", statusId = task.StatusId, addAssigneeIds = new[] { user.Me.Id }, addTagIds = new[] { tag.Id } }))
            .EnsureSuccessStatusCode();
        Assert.Equal(task.Position, (await user.Client.MoveTaskAsync(task.Id, new { statusId = task.StatusId })).Position);
        Assert.Empty(await factory.ActivityRowsAsync(task.Id));
        Assert.Equal(task.UpdatedAt, (await user.Client.TaskDetailAsync(task.Id)).Task.UpdatedAt);

        // One person's description edits within ten minutes are one row, moved to the latest edit.
        await user.Client.PatchTaskAsync(task.Id, new { description = "<p>One</p>" });
        var first = Assert.Single(await factory.ActivityRowsAsync(task.Id));
        await user.Client.PatchTaskAsync(task.Id, new { description = "<p>Two</p>" });
        var merged = Assert.Single(await factory.ActivityRowsAsync(task.Id));
        Assert.Equal((first.Id, TaskActivityKind.DescriptionChanged, (string?)null, (string?)null, user.Me.Id), (merged.Id, merged.Kind, merged.FromValue, merged.ToValue, merged.ActorId));
        Assert.True(merged.CreatedAt > first.CreatedAt);

        // Someone else's edit is a row of its own, so is the next edit after it, and so is an edit after another change.
        await mia.PatchTaskAsync(task.Id, new { description = "<p>Three</p>" });
        await user.Client.PatchTaskAsync(task.Id, new { description = "<p>Four</p>" });
        await user.Client.PatchTaskAsync(task.Id, new { priority = "Low" });
        await user.Client.PatchTaskAsync(task.Id, new { description = "<p>Five</p>" });
        const TaskActivityKind d = TaskActivityKind.DescriptionChanged;
        Assert.Equal([d, d, d, TaskActivityKind.PriorityChanged, d], (await factory.ActivityRowsAsync(task.Id)).Select(r => r.Kind));

        // The window counts from the previous edit: nine minutes later it still merges, eleven minutes later it doesn't.
        async Task AgeRowsAsync(int minutes) => await factory.DbAsync(db => db.Database.ExecuteSqlAsync(
            $"""UPDATE task_activities SET "CreatedAt" = "CreatedAt" - make_interval(mins => {minutes}) WHERE "TaskId" = {task.Id}"""));
        await AgeRowsAsync(9);
        await user.Client.PatchTaskAsync(task.Id, new { description = "<p>Six</p>" });
        Assert.Equal(5, (await factory.ActivityRowsAsync(task.Id)).Count);
        await AgeRowsAsync(11);
        await user.Client.PatchTaskAsync(task.Id, new { description = "<p>Seven</p>" });
        Assert.Equal([d, d, d, TaskActivityKind.PriorityChanged, d, d], (await factory.ActivityRowsAsync(task.Id)).Select(r => r.Kind));

        // The same HTML again is no change; null clears the description and merges into the run.
        var unchanged = await user.Client.PatchTaskAsync(task.Id, new { description = "<p>Seven</p>" });
        var cleared = await user.Client.PatchTaskAsync(task.Id, new { description = (string?)null });
        Assert.True(cleared.UpdatedAt > unchanged.UpdatedAt);
        Assert.False(cleared.HasDescription);
        Assert.Equal("", (await user.Client.TaskDetailAsync(task.Id)).Description);
        Assert.Equal(6, (await factory.ActivityRowsAsync(task.Id)).Count);
        // Bulk ignores descriptions.
        (await user.Client.BulkAsync(new { id = task.Id, description = "<p>Ignored</p>" })).EnsureSuccessStatusCode();
        Assert.Equal(6, (await factory.ActivityRowsAsync(task.Id)).Count);
        // A change recorded earlier in the same request ends the run as well: the rename comes before the description.
        await user.Client.PatchTaskAsync(task.Id, new { title = "Renamed", description = "<p>Eight</p>" });
        Assert.Equal([d, d, d, TaskActivityKind.PriorityChanged, d, d, TaskActivityKind.Renamed, d], (await factory.ActivityRowsAsync(task.Id)).Select(r => r.Kind));
    }
}
