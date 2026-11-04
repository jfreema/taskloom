using System.Net;
using Taskloom.Api.Tests.Support;
using Taskloom.Domain;

namespace Taskloom.Api.Tests;

/// <summary>Deleting statuses and changing their category (spec 5.4 #26–#27, 5.5 Statuses).</summary>
public class StatusTests(TaskloomFactory factory) : IClassFixture<TaskloomFactory>
{
    private const string NeedsDone = "A list needs at least one Done status.";
    private const string NeedsNotDone = "A list needs at least one status that isn't Done.";
    private const string MoveToMessage = "Choose another status of this list to move its tasks to.";

    [Fact]
    public async Task Deleting_a_status_moves_its_tasks_to_another_status_of_the_same_list()
    {
        var user = await factory.NewUserAsync();
        var (toDo, inProgress, review, done) = (user.Status("To do"), user.Status("In progress"), user.Status("Review"), user.Status("Done"));
        var shipped = await AddStatusAsync(user, user.List.Id, "Shipped", StatusCategory.Done);
        var blocked = await AddStatusAsync(user, user.List.Id, "Blocked", StatusCategory.Active);
        var other = await user.Client.CreateListAsync(user.Space.Id, "Other");

        var inReview = await user.Client.CreateTaskAsync(user.List.Id, new { title = "In review", statusId = review.Id });
        var subInReview = await user.Client.CreateTaskAsync(user.List.Id, new { title = "Sub in review", statusId = review.Id, parentTaskId = inReview.Id });
        var finished = await user.Client.CreateTaskAsync(user.List.Id, new { title = "Finished", statusId = done.Id });
        var bystander = await user.Client.CreateTaskAsync(user.List.Id, new { title = "Bystander", statusId = toDo.Id });
        Assert.NotNull(finished.CompletedAt);

        // moveTo must be another status of this list: not the status itself, not another list's, not garbage. Nothing changes.
        var before = await SnapshotAsync(user);
        foreach (var moveTo in new[] { review.Id.ToString(), other.Statuses[0].Id.ToString(), Guid.NewGuid().ToString(), "not-a-guid" })
            Assert.Equal(MoveToMessage, await (await user.Client.DeleteAsync($"/api/statuses/{review.Id}?moveTo={moveTo}")).ProblemAsync(HttpStatusCode.BadRequest));
        Assert.Equal(before, await SnapshotAsync(user));
        Assert.Equal(HttpStatusCode.NotFound, (await user.Client.DeleteAsync($"/api/statuses/{Guid.NewGuid()}?moveTo={toDo.Id}")).StatusCode);

        // Review → Blocked: its tasks (the subtask too) move there and stay open; other tasks don't move.
        Assert.Equal(HttpStatusCode.NoContent, (await user.Client.DeleteAsync($"/api/statuses/{review.Id}?moveTo={blocked.Id}")).StatusCode);
        var list = await user.Client.ListAsync(user.List.Id);
        Assert.Equal(["To do", "In progress", "Done", "Shipped", "Blocked"], list.Statuses.Select(s => s.Name));
        var tasks = (await user.Client.ListTasksAsync(user.List.Id)).ToDictionary(t => t.Id);
        Assert.Equal((blocked.Id, (DateTimeOffset?)null, inReview.Position), (tasks[inReview.Id].StatusId, tasks[inReview.Id].CompletedAt, tasks[inReview.Id].Position));
        Assert.Equal((blocked.Id, (DateTimeOffset?)null), (tasks[subInReview.Id].StatusId, tasks[subInReview.Id].CompletedAt));
        Assert.Equal((toDo.Id, done.Id), (tasks[bystander.Id].StatusId, tasks[finished.Id].StatusId));

        // Blocked → Shipped: the tasks enter Done, so they are completed now (and the parent counts its done subtask).
        var start = DateTimeOffset.UtcNow.AddSeconds(-1);
        Assert.Equal(HttpStatusCode.NoContent, (await user.Client.DeleteAsync($"/api/statuses/{blocked.Id}?moveTo={shipped.Id}")).StatusCode);
        tasks = (await user.Client.ListTasksAsync(user.List.Id)).ToDictionary(t => t.Id);
        Assert.Equal((shipped.Id, shipped.Id), (tasks[inReview.Id].StatusId, tasks[subInReview.Id].StatusId));
        Assert.InRange(tasks[inReview.Id].CompletedAt!.Value, start, DateTimeOffset.UtcNow.AddSeconds(1));
        Assert.InRange(tasks[subInReview.Id].CompletedAt!.Value, start, DateTimeOffset.UtcNow.AddSeconds(1));
        Assert.Equal(1, tasks[inReview.Id].SubtaskDoneCount);

        // Done → Shipped: both are Done, so completion times are kept.
        Assert.Equal(HttpStatusCode.NoContent, (await user.Client.DeleteAsync($"/api/statuses/{done.Id}?moveTo={shipped.Id}")).StatusCode);
        var finishedNow = (await user.Client.TaskDetailAsync(finished.Id)).Task;
        Assert.Equal((shipped.Id, finished.CompletedAt), (finishedNow.StatusId, finishedNow.CompletedAt));

        // Shipped is now the last Done status, and can't go; In progress can, without moveTo its tasks go to the first status.
        before = await SnapshotAsync(user);
        Assert.Equal(NeedsDone, await (await user.Client.DeleteAsync($"/api/statuses/{shipped.Id}?moveTo={toDo.Id}")).ProblemAsync(HttpStatusCode.BadRequest));
        Assert.Equal(NeedsDone, await (await user.Client.DeleteAsync($"/api/statuses/{shipped.Id}")).ProblemAsync(HttpStatusCode.BadRequest));
        Assert.Equal(before, await SnapshotAsync(user));
        var working = await user.Client.CreateTaskAsync(user.List.Id, new { title = "Working", statusId = inProgress.Id });
        Assert.Equal(HttpStatusCode.NoContent, (await user.Client.DeleteAsync($"/api/statuses/{inProgress.Id}")).StatusCode);
        Assert.Equal(toDo.Id, (await user.Client.TaskDetailAsync(working.Id)).Task.StatusId);

        // To do is now the last status that isn't Done, and can't go either.
        Assert.Equal(["To do", "Shipped"], (await user.Client.ListAsync(user.List.Id)).Statuses.Select(s => s.Name));
        Assert.Equal(NeedsNotDone, await (await user.Client.DeleteAsync($"/api/statuses/{toDo.Id}?moveTo={shipped.Id}")).ProblemAsync(HttpStatusCode.BadRequest));
        Assert.Equal(["To do", "Shipped"], (await user.Client.ListAsync(user.List.Id)).Statuses.Select(s => s.Name));

        // A task in a deleted Done status that moves to an open one is reopened.
        var reopenList = await user.Client.CreateListAsync(user.Space.Id, "Reopen", new { name = "Reopen", statusPreset = "Simple" });
        var extraDone = await AddStatusAsync(user, reopenList.Id, "Closed", StatusCategory.Done);
        var closed = await user.Client.CreateTaskAsync(reopenList.Id, new { title = "Closed", statusId = extraDone.Id });
        Assert.NotNull(closed.CompletedAt);
        Assert.Equal(HttpStatusCode.NoContent, (await user.Client.DeleteAsync($"/api/statuses/{extraDone.Id}?moveTo={reopenList.Statuses[1].Id}")).StatusCode);
        var reopened = (await user.Client.TaskDetailAsync(closed.Id)).Task;
        Assert.Equal((reopenList.Statuses[1].Id, (DateTimeOffset?)null), (reopened.StatusId, reopened.CompletedAt));
    }

    [Fact]
    public async Task Status_guards_and_category_changes()
    {
        var user = await factory.NewUserAsync();
        var list = await user.Client.CreateListAsync(user.Space.Id, "Simple", new { name = "Simple", statusPreset = "Simple" });
        var (toDo, inProgress, done) = (list.Statuses[0], list.Statuses[1], list.Statuses[2]);
        var parent = await user.Client.CreateTaskAsync(list.Id, new { title = "Parent", statusId = toDo.Id });
        var working = await user.Client.CreateTaskAsync(list.Id, new { title = "Working", statusId = inProgress.Id, parentTaskId = parent.Id });
        var alsoWorking = await user.Client.CreateTaskAsync(list.Id, new { title = "Also working", statusId = inProgress.Id });
        var finished = await user.Client.CreateTaskAsync(list.Id, new { title = "Finished", statusId = done.Id });

        // The last Done status can't stop being Done; nothing in the request is applied.
        var before = await SnapshotAsync(user, list.Id);
        Assert.Equal(NeedsDone, await (await user.Client.PatchAsJsonAsync($"/api/statuses/{done.Id}", new { category = "Active", name = "Not done" })).ProblemAsync(HttpStatusCode.BadRequest));
        Assert.Equal(NeedsDone, await (await user.Client.PatchAsJsonAsync($"/api/statuses/{done.Id}", new { category = "NotStarted" })).ProblemAsync(HttpStatusCode.BadRequest));
        Assert.Equal(before, await SnapshotAsync(user, list.Id));

        // NotStarted → Active changes no task.
        var renamed = await (await user.Client.PatchAsJsonAsync($"/api/statuses/{toDo.Id}", new { category = "Active", name = "Started" })).ReadAsync<StatusDto>();
        Assert.Equal(toDo with { Name = "Started", Category = StatusCategory.Active }, renamed);
        Assert.Null((await user.Client.TaskDetailAsync(parent.Id)).Task.CompletedAt);

        // Active → Done completes the status's tasks now (subtasks too), and the parent counts it.
        var start = DateTimeOffset.UtcNow.AddSeconds(-1);
        var nowDone = await (await user.Client.PatchAsJsonAsync($"/api/statuses/{inProgress.Id}", new { category = "Done" })).ReadAsync<StatusDto>();
        Assert.Equal(StatusCategory.Done, nowDone.Category);
        var tasks = (await user.Client.ListTasksAsync(list.Id)).ToDictionary(t => t.Id);
        foreach (var id in new[] { working.Id, alsoWorking.Id })
            Assert.InRange(tasks[id].CompletedAt!.Value, start, DateTimeOffset.UtcNow.AddSeconds(1));
        Assert.Equal((1, 1), (tasks[parent.Id].SubtaskCount, tasks[parent.Id].SubtaskDoneCount));
        Assert.Null(tasks[parent.Id].CompletedAt);
        Assert.Equal(finished.CompletedAt, tasks[finished.Id].CompletedAt);

        // The last open status can't become Done.
        before = await SnapshotAsync(user, list.Id);
        Assert.Equal(NeedsNotDone, await (await user.Client.PatchAsJsonAsync($"/api/statuses/{toDo.Id}", new { category = "Done", color = "#2E9F5E" })).ProblemAsync(HttpStatusCode.BadRequest));
        Assert.Equal(before, await SnapshotAsync(user, list.Id));

        // Now there are two Done statuses, the original one may reopen: its task loses CompletedAt; the other Done status keeps its tasks done.
        var reopened = await (await user.Client.PatchAsJsonAsync($"/api/statuses/{done.Id}", new { category = "Active" })).ReadAsync<StatusDto>();
        Assert.Equal(StatusCategory.Active, reopened.Category);
        tasks = (await user.Client.ListTasksAsync(list.Id)).ToDictionary(t => t.Id);
        Assert.Null(tasks[finished.Id].CompletedAt);
        Assert.NotNull(tasks[working.Id].CompletedAt);

        // Sending the category a status already has changes nothing.
        var kept = tasks[working.Id].CompletedAt;
        Assert.Equal(nowDone, await (await user.Client.PatchAsJsonAsync($"/api/statuses/{inProgress.Id}", new { category = "Done" })).ReadAsync<StatusDto>());
        Assert.Equal(kept, (await user.Client.TaskDetailAsync(working.Id)).Task.CompletedAt);

        // Done → NotStarted with no other Done status left is refused; an unknown category is unreadable.
        Assert.Equal(NeedsDone, await (await user.Client.PatchAsJsonAsync($"/api/statuses/{inProgress.Id}", new { category = "NotStarted" })).ProblemAsync(HttpStatusCode.BadRequest));
        Assert.NotNull(await (await user.Client.PatchAsJsonAsync($"/api/statuses/{inProgress.Id}", new { category = "Closed" })).ProblemAsync(HttpStatusCode.BadRequest));
        Assert.Equal(StatusCategory.Done, (await user.Client.ListAsync(list.Id)).Statuses.Single(s => s.Id == inProgress.Id).Category);
    }

    private static async Task<StatusDto> AddStatusAsync(TestUser user, Guid listId, string name, StatusCategory category) =>
        await (await user.Client.PostAsJsonAsync($"/api/lists/{listId}/statuses", new { name, category = category.ToString() })).ReadAsync<StatusDto>();

    /// <summary>The tree and the list's tasks as text, to show that a refused request changed nothing.</summary>
    private static async Task<string> SnapshotAsync(TestUser user, Guid? listId = null) =>
        await (await user.Client.GetAsync("/api/workspaces")).Content.ReadAsStringAsync()
        + await (await user.Client.GetAsync($"/api/lists/{listId ?? user.List.Id}/tasks")).Content.ReadAsStringAsync();
}
