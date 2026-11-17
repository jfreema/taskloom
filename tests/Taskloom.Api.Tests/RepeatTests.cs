using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Taskloom.Api.Tests.Support;
using Taskloom.Domain;

namespace Taskloom.Api.Tests;

/// <summary>Repeating tasks: setting a rule, completing (PATCH, move and bulk), taking the next task back, and the checks.</summary>
public class RepeatTests(TaskloomFactory factory) : IClassFixture<TaskloomFactory>
{
    private static DateOnly UtcToday => DateOnly.FromDateTime(DateTime.UtcNow);
    private static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd");

    /// <summary>A user whose requests carry today's UTC date as their own, like the web app.</summary>
    private async Task<TestUser> UserAsync()
    {
        var user = await factory.NewUserAsync();
        user.Client.DefaultRequestHeaders.Add("X-Taskloom-Date", Iso(UtcToday));
        return user;
    }

    private static Task<HttpResponseMessage> PatchAsync(TestUser user, Guid taskId, object body) =>
        user.Client.PatchAsJsonAsync($"/api/tasks/{taskId}", body);

    [Fact]
    public async Task Setting_a_rule_fits_it_to_the_task_and_gives_a_task_without_a_due_date_its_first_date()
    {
        var user = await UserAsync();
        var today = UtcToday;
        var task = await user.Client.CreateTaskAsync(user.List.Id, new { title = "Water the plants" });

        // A weekly rule without days falls on the due date's weekday, here today's, and the task becomes due today.
        var weekly = await user.Client.PatchTaskAsync(task.Id, new { repeat = new { unit = "Week" } });
        Assert.Equivalent(new RepeatDto(RepeatUnit.Week, 1, RepeatBasis.Schedule, [today.DayOfWeek], null, null, null, null), weekly.Repeat, strict: true);
        Assert.Equal(today, weekly.DueDate);
        var rows = await factory.ActivityRowsAsync(task.Id);
        Assert.Equal([TaskActivityKind.RepeatChanged, TaskActivityKind.DueDateChanged], rows.Select(r => r.Kind));
        Assert.Null(rows[0].FromValue);
        Assert.Equal($$"""{"unit":"Week","every":1,"basis":"Schedule","weekdays":["{{today.DayOfWeek}}"]}""", rows[0].ToValue);

        // Changing it keeps the due date; days and month parts that don't fit the unit are dropped.
        var monthly = await user.Client.PatchTaskAsync(task.Id, new { repeat = new { unit = "Month", every = 2, weekdays = new[] { "Friday" }, monthWeek = -1, monthWeekday = "Friday" } });
        Assert.Equivalent(new RepeatDto(RepeatUnit.Month, 2, RepeatBasis.Schedule, [], null, -1, DayOfWeek.Friday, null), monthly.Repeat, strict: true);
        Assert.Equal(today, monthly.DueDate);
        // Sending the same rule again records nothing.
        await user.Client.PatchTaskAsync(task.Id, new { repeat = new { unit = "Month", every = 2, monthWeek = -1, monthWeekday = "Friday" } });
        Assert.Equal(3, (await factory.ActivityRowsAsync(task.Id)).Count);

        // After completion: no weekdays, and no due date is added.
        var other = await user.Client.CreateTaskAsync(user.List.Id, new { title = "Refill prescription" });
        var afterDone = await user.Client.PatchTaskAsync(other.Id, new { repeat = new { unit = "Day", every = 30, basis = "Completion", weekdays = new[] { "Monday" } } });
        Assert.Equivalent(new RepeatDto(RepeatUnit.Day, 30, RepeatBasis.Completion, [], null, null, null, null), afterDone.Repeat, strict: true);
        Assert.Null(afterDone.DueDate);

        // null stops it.
        var stopped = await user.Client.PatchTaskAsync(task.Id, new { repeat = (object?)null });
        Assert.Null(stopped.Repeat);
        var last = (await factory.ActivityRowsAsync(task.Id))[^1];
        Assert.Equal((TaskActivityKind.RepeatChanged, (string?)null), (last.Kind, last.ToValue));

        // Create takes a rule too.
        var created = await user.Client.CreateTaskAsync(user.List.Id, new { title = "Pay rent", repeat = new { unit = "Month", monthDay = 1 } });
        var firstOfMonth = new DateOnly(today.Year, today.Month, 1);
        Assert.Equal(today.Day == 1 ? today : firstOfMonth.AddMonths(1), created.DueDate);
        Assert.Equal(1, created.Repeat!.MonthDay);
    }

    [Fact]
    public async Task Completing_creates_the_next_task_with_the_rule_and_an_undo_takes_it_back()
    {
        var user = await UserAsync();
        var today = UtcToday;
        var (_, colleague) = await factory.JoinAsync(user, WorkspaceRole.Member, "Colleague");
        var tag = await user.Client.CreateTagAsync(user.Workspace.Id, "finance");
        var toDo = user.Status("To do");
        var keep = await user.Client.CreateTaskAsync(user.List.Id, new { title = "Above" });
        var task = await user.Client.CreateTaskAsync(user.List.Id, new
        {
            title = "Weekly report", description = "<p>Send it to <strong>Ana</strong></p>", priority = "High",
            assigneeIds = new[] { colleague.UserId, user.Me.Id }, tagIds = new[] { tag.Id },
            startDate = Iso(today.AddDays(-2)), dueDate = Iso(today), repeat = new { unit = "Day", every = 7 },
        });
        var below = await user.Client.CreateTaskAsync(user.List.Id, new { title = "Below" });
        var subDone = await user.Client.CreateTaskAsync(user.List.Id, new { title = "Gather numbers", parentTaskId = task.Id, dueDate = Iso(today.AddDays(-1)) });
        await user.Client.PatchTaskAsync(subDone.Id, new { statusId = user.Status("Done").Id });
        var subOpen = await user.Client.CreateTaskAsync(user.List.Id, new { title = "Write summary", parentTaskId = task.Id, assigneeIds = new[] { colleague.UserId } });
        (await user.Client.PostAsJsonAsync($"/api/tasks/{task.Id}/comments", new { body = "Last week's was late" })).EnsureSuccessStatusCode();

        var completed = await user.Client.PatchTaskAsync(task.Id, new { statusId = user.Status("Done").Id });
        Assert.Null(completed.Repeat);
        var nextId = Assert.IsType<Guid>(completed.NextRepeatId);

        var detail = await user.Client.TaskDetailAsync(nextId);
        var next = detail.Task;
        Assert.Equal(("Weekly report", "<p>Send it to <strong>Ana</strong></p>", TaskPriority.High), (next.Title, detail.Description, next.Priority));
        Assert.Equal([colleague.UserId, user.Me.Id], next.AssigneeIds);
        Assert.Equal([tag.Id], next.TagIds);
        // Due a week on; the start date keeps its distance.
        Assert.Equal((today.AddDays(5), today.AddDays(7)), (next.StartDate!.Value, next.DueDate!.Value));
        var weeklyRule = new RepeatDto(RepeatUnit.Day, 7, RepeatBasis.Schedule, [], null, null, null, null);
        Assert.Equivalent(weeklyRule, next.Repeat, strict: true);
        // Open, in the place the task had in To do, created by its creator; no comments, attachments or history.
        Assert.Equal((toDo.Id, task.Position, null, user.Me.Id), (next.StatusId, next.Position, next.CompletedAt, next.CreatedById));
        Assert.Equal(["Above", "Weekly report", "Below"], (await user.Client.ListTasksAsync(user.List.Id))
            .Where(t => t.StatusId == toDo.Id && t.ParentTaskId is null).Select(t => t.Title));
        Assert.Empty(detail.Comments);
        Assert.Empty(detail.Activity);
        // Subtasks come along, open again, in order, with dates moved and assignees kept.
        Assert.Equal(["Gather numbers", "Write summary"], detail.Subtasks.Select(s => s.Title));
        Assert.All(detail.Subtasks, s => Assert.Equal((toDo.Id, (DateTimeOffset?)null), (s.StatusId, s.CompletedAt)));
        Assert.Equal(today.AddDays(6), detail.Subtasks[0].DueDate);
        Assert.Equal([colleague.UserId], detail.Subtasks[1].AssigneeIds);
        Assert.All(detail.Subtasks, s => Assert.Null(s.Repeat));
        // The completed task keeps its own subtasks and comments, and says what happened.
        var old = await user.Client.TaskDetailAsync(task.Id);
        Assert.Equal([subDone.Id, subOpen.Id], old.Subtasks.Select(s => s.Id));
        Assert.Single(old.Comments);
        Assert.Equal((TaskActivityKind.Repeated, Iso(today.AddDays(7))), (old.Activity[^1].Kind, old.Activity[^1].To));

        // Undo (the old status and position, as the web app sends it): the next task goes and the rule comes back.
        var reopened = await user.Client.PatchTaskAsync(task.Id, new { statusId = toDo.Id, position = task.Position });
        Assert.Equivalent(weeklyRule, reopened.Repeat, strict: true);
        Assert.Null(reopened.NextRepeatId);
        Assert.Equal(HttpStatusCode.NotFound, (await user.Client.GetAsync($"/api/tasks/{nextId}")).StatusCode);
        Assert.Equal(5, (await user.Client.ListTasksAsync(user.List.Id)).Count);
        Assert.Contains(keep.Id, (await user.Client.ListTasksAsync(user.List.Id)).Select(t => t.Id));
        Assert.Contains(below.Id, (await user.Client.ListTasksAsync(user.List.Id)).Select(t => t.Id));

        // Completing again creates it again.
        var again = await user.Client.PatchTaskAsync(task.Id, new { statusId = user.Status("Done").Id });
        Assert.NotNull(again.NextRepeatId);
        Assert.NotEqual(nextId, again.NextRepeatId);
    }

    [Fact]
    public async Task A_next_task_someone_touched_stays_when_the_completed_one_reopens()
    {
        var user = await UserAsync();
        var done = user.Status("Done").Id;
        var toDo = user.Status("To do").Id;

        async Task<(TaskDto Task, Guid Next)> CompletedAsync(string title)
        {
            var t = await user.Client.CreateTaskAsync(user.List.Id, new { title, repeat = new { unit = "Day" } });
            var c = await user.Client.PatchTaskAsync(t.Id, new { statusId = done });
            return (t, c.NextRepeatId!.Value);
        }

        var (renamed, renamedNext) = await CompletedAsync("Renamed");
        await user.Client.PatchTaskAsync(renamedNext, new { title = "Renamed, edited" });
        var (commented, commentedNext) = await CompletedAsync("Commented");
        (await user.Client.PostAsJsonAsync($"/api/tasks/{commentedNext}/comments", new { body = "Started" })).EnsureSuccessStatusCode();
        var (withSub, withSubNext) = await CompletedAsync("With a subtask");
        await user.Client.CreateTaskAsync(user.List.Id, new { title = "Added later", parentTaskId = withSubNext });

        foreach (var (task, next) in new[] { (renamed, renamedNext), (commented, commentedNext), (withSub, withSubNext) })
        {
            var reopened = await user.Client.PatchTaskAsync(task.Id, new { statusId = toDo });
            // Both stay; the rule stays with the next task, so the series doesn't fork.
            Assert.Equal(((RepeatDto?)null, (Guid?)next), (reopened.Repeat, reopened.NextRepeatId));
            Assert.NotNull((await user.Client.TaskDetailAsync(next)).Task.Repeat);
            // Completing the reopened task again creates nothing.
            Assert.Equal(next, (await user.Client.PatchTaskAsync(task.Id, new { statusId = done })).NextRepeatId);
        }

        // Deleting the next task unlinks the completed one.
        (await user.Client.DeleteAsync($"/api/tasks/{renamedNext}")).EnsureSuccessStatusCode();
        Assert.Null((await user.Client.TaskDetailAsync(renamed.Id)).Task.NextRepeatId);
    }

    [Fact]
    public async Task Dragging_to_done_and_bulk_completion_create_next_tasks_too()
    {
        var user = await UserAsync();
        var done = user.Status("Done").Id;
        var toDo = user.Status("To do").Id;
        var dragged = await user.Client.CreateTaskAsync(user.List.Id, new { title = "Dragged", repeat = new { unit = "Week" } });

        var moved = await user.Client.MoveTaskAsync(dragged.Id, new { statusId = done });
        var nextId = moved.NextRepeatId!.Value;
        Assert.Equal(dragged.DueDate!.Value.AddDays(7), (await user.Client.TaskDetailAsync(nextId)).Task.DueDate);
        // Dragging it back out takes the next one back.
        var back = await user.Client.MoveTaskAsync(dragged.Id, new { statusId = toDo });
        Assert.NotNull(back.Repeat);
        Assert.Equal(HttpStatusCode.NotFound, (await user.Client.GetAsync($"/api/tasks/{nextId}")).StatusCode);

        // Two repeating tasks and a plain one completed together: one next task each for the repeating ones, at the
        // bottom of To do (they waited in In progress, not To do).
        var inProgress = user.Status("In progress").Id;
        var a = await user.Client.CreateTaskAsync(user.List.Id, new { title = "A", statusId = inProgress, repeat = new { unit = "Day" } });
        var b = await user.Client.CreateTaskAsync(user.List.Id, new { title = "B", statusId = inProgress, repeat = new { unit = "Month" } });
        var plain = await user.Client.CreateTaskAsync(user.List.Id, new { title = "Plain", statusId = inProgress });
        var result = await (await user.Client.BulkAsync(new { id = a.Id, statusId = done }, new { id = b.Id, statusId = done }, new { id = plain.Id, statusId = done }))
            .ReadAsync<BulkUpdateResultDto>();
        Assert.Equal(3, result.Tasks.Count);
        Assert.Null(result.Tasks[2].NextRepeatId);
        var nextA = (await user.Client.TaskDetailAsync(result.Tasks[0].NextRepeatId!.Value)).Task;
        var nextB = (await user.Client.TaskDetailAsync(result.Tasks[1].NextRepeatId!.Value)).Task;
        Assert.Equal((toDo, toDo), (nextA.StatusId, nextB.StatusId));
        Assert.True(nextA.Position < nextB.Position);
        Assert.Equal(["Dragged", "A", "B"], (await user.Client.ListTasksAsync(user.List.Id))
            .Where(t => t.StatusId == toDo).Select(t => t.Title));
        Assert.Equal(a.DueDate!.Value.AddDays(1), nextA.DueDate);
    }

    [Fact]
    public async Task A_rule_past_its_end_creates_nothing_and_stays_on_the_last_task()
    {
        var user = await UserAsync();
        var today = UtcToday;
        var task = await user.Client.CreateTaskAsync(user.List.Id, new { title = "Last one", dueDate = Iso(today), repeat = new { unit = "Day", until = Iso(today) } });
        var completed = await user.Client.PatchTaskAsync(task.Id, new { statusId = user.Status("Done").Id });
        Assert.Null(completed.NextRepeatId);
        Assert.Equal(DateOnly.FromDateTime(DateTime.UtcNow), completed.Repeat!.Until);
        Assert.Single(await user.Client.ListTasksAsync(user.List.Id));
        Assert.DoesNotContain(await factory.ActivityRowsAsync(task.Id), r => r.Kind == TaskActivityKind.Repeated);
    }

    [Fact]
    public async Task Rules_counted_from_completion_use_the_callers_own_date()
    {
        var user = await factory.NewUserAsync();
        var utc = UtcToday;
        var task = await user.Client.CreateTaskAsync(user.List.Id, new { title = "Change the filter", repeat = new { unit = "Day", every = 3, basis = "Completion" } });

        // The browser's date is a day ahead of UTC (east of Greenwich after midnight there).
        var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/tasks/{task.Id}") { Content = JsonContent.Create(new { statusId = user.Status("Done").Id }) };
        request.Headers.Add("X-Taskloom-Date", Iso(utc.AddDays(1)));
        var completed = await (await user.Client.SendAsync(request)).ReadAsync<TaskDto>();
        Assert.Equal(utc.AddDays(4), (await user.Client.TaskDetailAsync(completed.NextRepeatId!.Value)).Task.DueDate);

        // A date further off than any time zone is ignored: UTC's date is used.
        var other = await user.Client.CreateTaskAsync(user.List.Id, new { title = "Other", repeat = new { unit = "Day", basis = "Completion" } });
        request = new HttpRequestMessage(HttpMethod.Patch, $"/api/tasks/{other.Id}") { Content = JsonContent.Create(new { statusId = user.Status("Done").Id }) };
        request.Headers.Add("X-Taskloom-Date", Iso(utc.AddDays(5)));
        completed = await (await user.Client.SendAsync(request)).ReadAsync<TaskDto>();
        Assert.Equal(utc.AddDays(1), (await user.Client.TaskDetailAsync(completed.NextRepeatId!.Value)).Task.DueDate);
    }

    [Fact]
    public async Task Next_tasks_in_a_private_space_keep_only_assignees_who_can_still_see_it()
    {
        var user = await UserAsync();
        var (_, stays) = await factory.JoinAsync(user, WorkspaceRole.Member, "Stays");
        var (_, leaves) = await factory.JoinAsync(user, WorkspaceRole.Member, "Leaves");
        var task = await user.Client.CreateTaskAsync(user.List.Id, new
        {
            title = "Payroll", assigneeIds = new[] { stays.UserId, leaves.UserId }, repeat = new { unit = "Month" },
        });
        // Private to the owner and one of the two; the other keeps the old assignment (access changes never remove one).
        (await user.Client.PatchAsJsonAsync($"/api/spaces/{user.Space.Id}", new { isPrivate = true })).EnsureSuccessStatusCode();
        (await user.Client.PostAsJsonAsync($"/api/spaces/{user.Space.Id}/members", new { userIds = new[] { stays.UserId } })).EnsureSuccessStatusCode();

        var completed = await user.Client.PatchTaskAsync(task.Id, new { statusId = user.Status("Done").Id });
        Assert.Equal([stays.UserId, leaves.UserId], completed.AssigneeIds);
        Assert.Equal([stays.UserId], (await user.Client.TaskDetailAsync(completed.NextRepeatId!.Value)).Task.AssigneeIds);
    }

    [Fact]
    public async Task Bad_rules_subtasks_and_guests_are_refused()
    {
        var user = await UserAsync();
        var task = await user.Client.CreateTaskAsync(user.List.Id, new { title = "Task" });
        var sub = await user.Client.CreateTaskAsync(user.List.Id, new { title = "Sub", parentTaskId = task.Id });

        async Task<string?> Refused(Guid id, object repeat) => await (await PatchAsync(user, id, new { repeat })).ProblemAsync(HttpStatusCode.BadRequest);

        Assert.Equal("Only top-level tasks can repeat. Set the repeat on the parent task.", await Refused(sub.Id, new { unit = "Day" }));
        Assert.Equal("Only top-level tasks can repeat. Set the repeat on the parent task.", await (await user.Client.PostAsJsonAsync(
            $"/api/lists/{user.List.Id}/tasks", new { title = "Sub", parentTaskId = task.Id, repeat = new { unit = "Day" } })).ProblemAsync(HttpStatusCode.BadRequest));
        Assert.Equal("Choose Day, Week, Month or Year as the repeat unit.", await Refused(task.Id, new { every = 2 }));
        Assert.Equal("Repeats can be every 1 to 365 days, weeks, months or years.", await Refused(task.Id, new { unit = "Day", every = 0 }));
        Assert.Equal("Repeats can be every 1 to 365 days, weeks, months or years.", await Refused(task.Id, new { unit = "Day", every = 366 }));
        Assert.Equal("The day of the month must be 1 to 31, or -1 for the last day.", await Refused(task.Id, new { unit = "Month", monthDay = 32 }));
        Assert.Equal("The week of the month must be 1 to 4, or -1 for the last one.", await Refused(task.Id, new { unit = "Month", monthWeek = 5, monthWeekday = "Monday" }));
        Assert.Equal("Repeat on a day of the month or on a weekday of the month, not both.", await Refused(task.Id, new { unit = "Month", monthDay = 3, monthWeek = 1 }));
        const string unreadable = "Send the repeat as an object with a unit of Day, Week, Month or Year, or null to stop repeating.";
        Assert.Equal(unreadable, await Refused(task.Id, "Daily"));
        Assert.Equal(unreadable, await Refused(task.Id, new { unit = "Fortnight" }));
        Assert.Equal(unreadable, await Refused(task.Id, new { unit = 1 }));
        Assert.Equal(unreadable, await Refused(task.Id, new { unit = "Week", weekdays = new[] { "Funday" } }));
        Assert.Equal(unreadable, await Refused(task.Id, new { unit = "Day", until = "next year" }));
        Assert.Null((await user.Client.TaskDetailAsync(task.Id)).Task.Repeat);

        // Bulk updates ignore "repeat", like title and description.
        var bulk = await (await user.Client.BulkAsync(new { id = task.Id, repeat = new { unit = "Day" } })).ReadAsync<BulkUpdateResultDto>();
        Assert.Null(bulk.Tasks[0].Repeat);

        var (guest, _) = await factory.JoinAsync(user, WorkspaceRole.Guest);
        Assert.Equal(HttpStatusCode.Forbidden, (await guest.PatchAsJsonAsync($"/api/tasks/{task.Id}", new { repeat = new { unit = "Day" } })).StatusCode);
    }

    [Fact]
    public async Task Rules_are_stored_as_readable_json()
    {
        var user = await UserAsync();
        var task = await user.Client.CreateTaskAsync(user.List.Id, new
        {
            title = "Stand-up notes", dueDate = "2026-11-16", repeat = new { unit = "Week", weekdays = new[] { "Wednesday", "Monday" }, until = "2027-06-30" },
        });
        var json = await factory.DbAsync(db => db.Database
            .SqlQuery<string>($"""SELECT "Repeat"::text AS "Value" FROM tasks WHERE "Id" = {task.Id}""").SingleAsync());
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("Week", doc.RootElement.GetProperty("unit").GetString());
        Assert.Equal("Monday, Wednesday", doc.RootElement.GetProperty("weekdays").GetString());
        Assert.Equal("2027-06-30", doc.RootElement.GetProperty("until").GetString());
        Assert.Equal([DayOfWeek.Monday, DayOfWeek.Wednesday], task.Repeat!.Weekdays);
    }
}
