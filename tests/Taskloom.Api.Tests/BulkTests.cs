using System.Net;
using Microsoft.EntityFrameworkCore;
using Taskloom.Api.Tests.Support;
using Taskloom.Application;
using Taskloom.Domain;

namespace Taskloom.Api.Tests;

/// <summary>POST /api/tasks/bulk and /api/tasks/bulk-delete (spec 5.5): one transaction, one workspace, the PATCH rules for each item.</summary>
public class BulkTests(TaskloomFactory factory) : IClassFixture<TaskloomFactory>
{
    private const string AssigneesMustBeMembers = "Everyone assigned must be a member of this workspace.";
    private const string TagsMustBelongToWorkspace = "Tags must belong to this workspace.";

    private static (TaskActivityKind, string?, string?) Row(TaskActivity a) => (a.Kind, a.FromValue, a.ToValue);

    [Fact]
    public async Task Bulk_update_is_all_or_nothing_and_refuses_tasks_from_other_workspaces()
    {
        var user = await factory.NewUserAsync();
        var a = await user.Client.CreateTaskAsync(user.List.Id, new { title = "A" });
        var b = await user.Client.CreateTaskAsync(user.List.Id, new { title = "B" });
        var c = await user.Client.CreateTaskAsync(user.List.Id, new { title = "C" });
        var other = await user.Client.CreateListAsync(user.Space.Id, "Other");

        async Task AssertNothingChanged()
        {
            foreach (var t in await user.Client.ListTasksAsync(user.List.Id))
            {
                Assert.Equal((TaskPriority.None, user.Status("To do").Id, (DateOnly?)null, (DateTimeOffset?)null), (t.Priority, t.StatusId, t.DueDate, t.CompletedAt));
                Assert.Empty(await factory.ActivityRowsAsync(t.Id));
            }
        }

        // A bad later update undoes the earlier ones: 400 naming the problem, and nothing is saved.
        Assert.Equal("Choose a status of this list.", await (await user.Client.BulkAsync(
            new { id = a.Id, priority = "High" }, new { id = b.Id, statusId = other.Statuses[0].Id })).ProblemAsync(HttpStatusCode.BadRequest));
        Assert.Equal("Dates must look like 2026-11-04.", await (await user.Client.BulkAsync(
            new { id = a.Id, dueDate = "2026-11-30" }, new { id = b.Id, dueDate = "30/11/2026" })).ProblemAsync(HttpStatusCode.BadRequest));
        Assert.Equal("Choose Urgent, High, Normal, Low or None as the priority.", await (await user.Client.BulkAsync(
            new { id = a.Id, statusId = user.Status("Done").Id }, new { id = b.Id, priority = "Highest" })).ProblemAsync(HttpStatusCode.BadRequest));
        await AssertNothingChanged();

        // The shape of the call is checked first.
        Assert.Equal("Send the changes as a list of updates.", await (await user.Client.PostAsJsonAsync("/api/tasks/bulk", new { })).ProblemAsync(HttpStatusCode.BadRequest));
        Assert.Equal("Send between 1 and 500 updates at a time.", await (await user.Client.BulkAsync()).ProblemAsync(HttpStatusCode.BadRequest));
        var tooMany = Enumerable.Range(0, 501).Select(_ => (object)new { id = Guid.NewGuid(), priority = "Low" }).ToArray();
        Assert.Equal("Send between 1 and 500 updates at a time.", await (await user.Client.BulkAsync(tooMany)).ProblemAsync(HttpStatusCode.BadRequest));
        Assert.Equal("Each update needs the id of a task.", await (await user.Client.BulkAsync(new { priority = "Low" })).ProblemAsync(HttpStatusCode.BadRequest));
        Assert.Equal("Each task can only appear once in a bulk update.", await (await user.Client.BulkAsync(
            new { id = a.Id, priority = "Low" }, new { id = a.Id, priority = "High" })).ProblemAsync(HttpStatusCode.BadRequest));

        // Tasks of another workspace, or missing ones, make the whole call 404, mixed with the caller's own or not.
        var stranger = await factory.NewUserAsync();
        var foreign = await stranger.Client.CreateTaskAsync(stranger.List.Id, new { title = "Foreign" });
        Assert.Equal(HttpStatusCode.NotFound, (await user.Client.BulkAsync(new { id = a.Id, priority = "High" }, new { id = foreign.Id, priority = "High" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await user.Client.BulkAsync(new { id = foreign.Id, priority = "High" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await user.Client.BulkAsync(new { id = a.Id, priority = "High" }, new { id = Guid.NewGuid(), priority = "High" })).StatusCode);
        // A Guest can't edit (whatever the updates say), and one call never spans two workspaces, even for a member of both.
        var (guest, _) = await factory.JoinAsync(user, WorkspaceRole.Guest);
        Assert.Equal(HttpStatusCode.Forbidden, (await guest.BulkAsync(new { id = a.Id, priority = "High" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await guest.BulkAsync(new { id = a.Id, priority = "Highest" })).StatusCode);
        await stranger.Client.InviteAsync(stranger.Workspace.Id, user.Me.Email, WorkspaceRole.Member);
        Assert.Equal(HttpStatusCode.NotFound, (await user.Client.BulkAsync(new { id = a.Id, priority = "High" }, new { id = foreign.Id, priority = "High" })).StatusCode);
        await AssertNothingChanged();
        Assert.Equal(TaskPriority.None, (await stranger.Client.TaskDetailAsync(foreign.Id)).Task.Priority);

        // A good call applies every update, ignores title and description, and answers in request order.
        var result = await (await user.Client.BulkAsync(
            new { id = c.Id, priority = "Urgent", title = "Ignored", description = "<p>Ignored</p>" },
            new { id = a.Id, dueDate = "2026-11-30", startDate = (string?)null },
            new { id = b.Id, statusId = user.Status("Done").Id })).ReadAsync<BulkUpdateResultDto>();
        Assert.Equal([c.Id, a.Id, b.Id], result.Tasks.Select(t => t.Id));
        Assert.Equal(("C", false, TaskPriority.Urgent), (result.Tasks[0].Title, result.Tasks[0].HasDescription, result.Tasks[0].Priority));
        Assert.Equal((new DateOnly(2026, 11, 30), (DateOnly?)null), (result.Tasks[1].DueDate!.Value, result.Tasks[1].StartDate));
        Assert.NotNull(result.Tasks[2].CompletedAt);
        Assert.True(result.Tasks[0].UpdatedAt > c.UpdatedAt);
        // Activity is recorded for each task as for PATCH.
        Assert.Equal([(TaskActivityKind.PriorityChanged, "None", "Urgent")], (await factory.ActivityRowsAsync(c.Id)).Select(Row));
        Assert.Equal([(TaskActivityKind.DueDateChanged, null, "2026-11-30")], (await factory.ActivityRowsAsync(a.Id)).Select(Row));
        Assert.Equal([(TaskActivityKind.StatusChanged, "To do", "Done")], (await factory.ActivityRowsAsync(b.Id)).Select(Row));
        // The member of both workspaces can bulk-edit the other one on its own.
        Assert.Equal(TaskPriority.Low, Assert.Single((await (await user.Client.BulkAsync(new { id = foreign.Id, priority = "Low" })).ReadAsync<BulkUpdateResultDto>()).Tasks).Priority);
    }

    [Fact]
    public async Task Bulk_add_and_remove_assignees_and_tags()
    {
        var user = await factory.NewUserAsync();
        var (_, mia) = await factory.JoinAsync(user, WorkspaceRole.Member, "Mia Member");
        var (_, otto) = await factory.JoinAsync(user, WorkspaceRole.Member, "Otto Other");
        var bug = await user.Client.CreateTagAsync(user.Workspace.Id, "bug");
        var docs = await user.Client.CreateTagAsync(user.Workspace.Id, "docs");
        var one = await user.Client.CreateTaskAsync(user.List.Id, new { title = "One", assigneeIds = new[] { user.Me.Id }, tagIds = new[] { bug.Id } });
        var two = await user.Client.CreateTaskAsync(user.List.Id, new { title = "Two" });

        // Adds skip what a task already has; removes ignore what it doesn't carry.
        var result = await (await user.Client.BulkAsync(
                new { id = one.Id, addAssigneeIds = new[] { mia.UserId, user.Me.Id }, removeAssigneeIds = new[] { otto.UserId }, addTagIds = new[] { docs.Id, bug.Id } },
                new { id = two.Id, addAssigneeIds = new[] { otto.UserId, mia.UserId }, addTagIds = new[] { docs.Id }, removeTagIds = new[] { bug.Id } }))
            .ReadAsync<BulkUpdateResultDto>();
        Assert.Equal([user.Me.Id, mia.UserId], result.Tasks[0].AssigneeIds);
        Assert.Equal([bug.Id, docs.Id], result.Tasks[0].TagIds);
        Assert.Equal([otto.UserId, mia.UserId], result.Tasks[1].AssigneeIds); // people added together keep the order sent
        Assert.Equal([docs.Id], result.Tasks[1].TagIds);

        // Removing, and replacing whole sets with assigneeIds and tagIds, in the same call.
        result = await (await user.Client.BulkAsync(
                new { id = one.Id, removeAssigneeIds = new[] { user.Me.Id }, removeTagIds = new[] { bug.Id, docs.Id } },
                new { id = two.Id, assigneeIds = new[] { mia.UserId }, tagIds = new[] { bug.Id } }))
            .ReadAsync<BulkUpdateResultDto>();
        Assert.Equal([mia.UserId], result.Tasks[0].AssigneeIds);
        Assert.Empty(result.Tasks[0].TagIds);
        Assert.Equal([mia.UserId], result.Tasks[1].AssigneeIds);
        Assert.Equal([bug.Id], result.Tasks[1].TagIds);

        Assert.Equal(
        [
            (TaskActivityKind.AssigneeAdded, null, "Mia Member"), (TaskActivityKind.TagAdded, null, "docs"),
            (TaskActivityKind.AssigneeRemoved, user.Me.DisplayName, null), (TaskActivityKind.TagRemoved, "bug", null), (TaskActivityKind.TagRemoved, "docs", null),
        ], (await factory.ActivityRowsAsync(one.Id)).Select(Row));
        Assert.Equal(
        [
            (TaskActivityKind.AssigneeAdded, null, "Otto Other"), (TaskActivityKind.AssigneeAdded, null, "Mia Member"), (TaskActivityKind.TagAdded, null, "docs"),
            (TaskActivityKind.AssigneeRemoved, "Otto Other", null), (TaskActivityKind.TagAdded, null, "bug"), (TaskActivityKind.TagRemoved, "docs", null),
        ], (await factory.ActivityRowsAsync(two.Id)).Select(Row));

        // Adding only what is already there changes nothing: no activity, and UpdatedAt stays.
        var same = Assert.Single((await (await user.Client.BulkAsync(new { id = two.Id, addAssigneeIds = new[] { mia.UserId }, addTagIds = new[] { bug.Id } }))
            .ReadAsync<BulkUpdateResultDto>()).Tasks);
        Assert.Equal(result.Tasks[1].UpdatedAt, same.UpdatedAt);
        Assert.Equal(6, (await factory.ActivityRowsAsync(two.Id)).Count);

        // Someone who has left the workspace but is still assigned can be removed; only additions must be members.
        (await user.Client.BulkAsync(new { id = one.Id, addAssigneeIds = new[] { otto.UserId } })).EnsureSuccessStatusCode();
        await factory.DbAsync(db => db.WorkspaceMembers.Where(m => m.WorkspaceId == user.Workspace.Id && m.UserId == otto.UserId).ExecuteDeleteAsync());
        Assert.Equal(AssigneesMustBeMembers, await (await user.Client.BulkAsync(new { id = two.Id, addAssigneeIds = new[] { otto.UserId } })).ProblemAsync(HttpStatusCode.BadRequest));
        var cleared = Assert.Single((await (await user.Client.BulkAsync(new { id = one.Id, removeAssigneeIds = new[] { otto.UserId } })).ReadAsync<BulkUpdateResultDto>()).Tasks);
        Assert.Equal([mia.UserId], cleared.AssigneeIds);
        Assert.Equal((TaskActivityKind.AssigneeRemoved, "Otto Other", (string?)null), Row((await factory.ActivityRowsAsync(one.Id))[^1]));
    }

    [Fact]
    public async Task Bulk_rejects_foreign_tags_non_members_and_set_with_add_or_remove()
    {
        var user = await factory.NewUserAsync();
        var stranger = await factory.NewUserAsync();
        var mine = await user.Client.CreateTagAsync(user.Workspace.Id, "mine");
        var theirs = await stranger.Client.CreateTagAsync(stranger.Workspace.Id, "theirs");
        var task = await user.Client.CreateTaskAsync(user.List.Id, new { title = "Kept" });
        var other = await user.Client.CreateTaskAsync(user.List.Id, new { title = "Other" });

        // Every bad update follows a good one, which mustn't be saved either.
        var good = new { id = task.Id, priority = "Urgent", addTagIds = new[] { mine.Id }, addAssigneeIds = new[] { user.Me.Id } };
        (object Update, string Detail)[] cases =
        [
            (new { id = other.Id, addTagIds = new[] { theirs.Id } }, TagsMustBelongToWorkspace),
            (new { id = other.Id, addTagIds = new[] { Guid.NewGuid() } }, TagsMustBelongToWorkspace),
            (new { id = other.Id, removeTagIds = new[] { theirs.Id } }, TagsMustBelongToWorkspace),
            (new { id = other.Id, tagIds = new[] { mine.Id, theirs.Id } }, TagsMustBelongToWorkspace),
            (new { id = other.Id, addAssigneeIds = new[] { stranger.Me.Id } }, AssigneesMustBeMembers),
            (new { id = other.Id, addAssigneeIds = new[] { Guid.NewGuid() } }, AssigneesMustBeMembers),
            (new { id = other.Id, removeAssigneeIds = new[] { stranger.Me.Id } }, AssigneesMustBeMembers),
            (new { id = other.Id, assigneeIds = new[] { user.Me.Id, stranger.Me.Id } }, AssigneesMustBeMembers),
            (new { id = other.Id, assigneeIds = new[] { user.Me.Id }, addAssigneeIds = new[] { user.Me.Id } }, "Send assigneeIds, or addAssigneeIds and removeAssigneeIds, not both."),
            (new { id = other.Id, assigneeIds = Array.Empty<Guid>(), removeAssigneeIds = new[] { user.Me.Id } }, "Send assigneeIds, or addAssigneeIds and removeAssigneeIds, not both."),
            (new { id = other.Id, tagIds = new[] { mine.Id }, removeTagIds = new[] { mine.Id } }, "Send tagIds, or addTagIds and removeTagIds, not both."),
            (new { id = other.Id, tagIds = Array.Empty<Guid>(), addTagIds = new[] { mine.Id } }, "Send tagIds, or addTagIds and removeTagIds, not both."),
            (new { id = other.Id, addTagIds = "mine" }, "addTagIds must be a list of tag ids."),
            (new { id = other.Id, removeAssigneeIds = new[] { "me" } }, "removeAssigneeIds must be a list of user ids."),
            (new { id = other.Id, listId = stranger.List.Id }, "Choose a list in this workspace."),
            (new { id = other.Id, statusId = stranger.Status("To do").Id }, "Choose a status of this list."),
            (new { id = other.Id, position = "top" }, "Positions must be numbers."),
        ];
        foreach (var (update, detail) in cases)
            Assert.Equal(detail, await (await user.Client.BulkAsync(good, update)).ProblemAsync(HttpStatusCode.BadRequest));

        foreach (var t in await user.Client.ListTasksAsync(user.List.Id))
        {
            Assert.Equal((TaskPriority.None, 0, 0, t.CreatedAt), (t.Priority, t.AssigneeIds.Count, t.TagIds.Count, t.UpdatedAt));
            Assert.Empty(await factory.ActivityRowsAsync(t.Id));
        }

        // The good update alone goes through.
        var saved = Assert.Single((await (await user.Client.BulkAsync(good)).ReadAsync<BulkUpdateResultDto>()).Tasks);
        Assert.Equal((TaskPriority.Urgent, user.Me.Id, mine.Id), (saved.Priority, Assert.Single(saved.AssigneeIds), Assert.Single(saved.TagIds)));
    }

    [Fact]
    public async Task Bulk_delete_removes_tasks_and_subtasks_and_ignores_missing_ids()
    {
        var user = await factory.NewUserAsync();
        var parent = await user.Client.CreateTaskAsync(user.List.Id, new { title = "Parent" });
        var sub = await user.Client.CreateTaskAsync(user.List.Id, new { title = "Sub", parentTaskId = parent.Id });
        var loose = await user.Client.CreateTaskAsync(user.List.Id, new { title = "Loose" });
        var kept = await user.Client.CreateTaskAsync(user.List.Id, new { title = "Kept" });
        var parentFile = await user.Client.UploadAsync(parent.Id, "parent.txt", "p"u8.ToArray());
        var subFile = await user.Client.UploadAsync(sub.Id, "sub.txt", "s"u8.ToArray());
        var keptFile = await user.Client.UploadAsync(kept.Id, "kept.txt", "k"u8.ToArray());
        (await user.Client.PostAsJsonAsync($"/api/tasks/{sub.Id}/comments", new { body = "Goes too" })).EnsureSuccessStatusCode();

        const string countMessage = "Send between 1 and 500 task ids at a time.";
        Assert.Equal(countMessage, await (await user.Client.BulkDeleteAsync()).ProblemAsync(HttpStatusCode.BadRequest));
        Assert.Equal(countMessage, await (await user.Client.PostAsJsonAsync("/api/tasks/bulk-delete", new { })).ProblemAsync(HttpStatusCode.BadRequest));
        Assert.Equal(countMessage, await (await user.Client.BulkDeleteAsync(Enumerable.Range(0, 501).Select(_ => Guid.NewGuid()).ToArray())).ProblemAsync(HttpStatusCode.BadRequest));

        // Any task in a workspace where the caller isn't a member (404), or is only a Guest (403): nothing is deleted.
        var stranger = await factory.NewUserAsync();
        var foreign = await stranger.Client.CreateTaskAsync(stranger.List.Id, new { title = "Foreign" });
        Assert.Equal(HttpStatusCode.NotFound, (await user.Client.BulkDeleteAsync(loose.Id, foreign.Id)).StatusCode);
        var (guest, _) = await factory.JoinAsync(user, WorkspaceRole.Guest);
        Assert.Equal(HttpStatusCode.Forbidden, (await guest.BulkDeleteAsync(loose.Id)).StatusCode);
        await stranger.Client.InviteAsync(stranger.Workspace.Id, user.Me.Email, WorkspaceRole.Guest);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.Client.BulkDeleteAsync(loose.Id, foreign.Id)).StatusCode);
        Assert.Equal(4, (await user.Client.ListTasksAsync(user.List.Id)).Count);
        Assert.Equal("Foreign", (await stranger.Client.TaskDetailAsync(foreign.Id)).Task.Title);

        // The tasks go with their subtasks, comments, attachments and stored files. Missing and repeated ids are ignored.
        Assert.Equal(HttpStatusCode.NoContent, (await user.Client.BulkDeleteAsync(parent.Id, loose.Id, Guid.NewGuid(), parent.Id)).StatusCode);
        Assert.Equal(["Kept"], (await user.Client.ListTasksAsync(user.List.Id)).Select(t => t.Title));
        Assert.Equal(HttpStatusCode.NotFound, (await user.Client.GetAsync($"/api/tasks/{sub.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await user.Client.GetAsync($"/api/attachments/{subFile.Id}")).StatusCode);
        Assert.Equal(0, await factory.DbAsync(db => db.Comments.CountAsync(x => x.TaskId == sub.Id)));
        var files = factory.Services.GetRequiredService<IFileStore>();
        Assert.Null(await files.OpenReadAsync($"tasks/{parent.Id}/{parentFile.Id}"));
        Assert.Null(await files.OpenReadAsync($"tasks/{sub.Id}/{subFile.Id}"));
        await using (var stream = await files.OpenReadAsync($"tasks/{kept.Id}/{keptFile.Id}")) Assert.NotNull(stream);

        // Committing the same delete again (a retried undo toast) succeeds and does nothing; only missing ids is fine too.
        Assert.Equal(HttpStatusCode.NoContent, (await user.Client.BulkDeleteAsync(parent.Id, loose.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await guest.BulkDeleteAsync(Guid.NewGuid())).StatusCode);
        Assert.Equal(["Kept"], (await user.Client.ListTasksAsync(user.List.Id)).Select(t => t.Title));
    }
}
