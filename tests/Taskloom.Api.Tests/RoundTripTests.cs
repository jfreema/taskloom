using Taskloom.Api.Tests.Support;
using Taskloom.Domain;

namespace Taskloom.Api.Tests;

/// <summary>
/// Efficiency 2: the trips to the database that everyday requests make. On a hosted database each trip waits on the
/// network, so these are budgets, and a change that needs another trip should raise one on purpose. Before Efficiency 2
/// ticking a task complete took 12, a rename 11, moving a task to another list 13 and opening a task 8 to 10.
/// </summary>
public class RoundTripTests(RoundTripTests.Site factory) : IClassFixture<RoundTripTests.Site>
{
    public sealed class Site : TaskloomFactory
    {
        public RoundTripCounter Counter { get; } = new();

        public Site()
        {
            // In real use a session is checked against the database once a minute, not on each request; leave it out.
            Settings["Auth:Session:RecheckSeconds"] = "86400";
            TestServices = Counter.Register;
        }
    }

    private async Task TripsAsync(int budget, Func<HttpClient, Task<HttpResponseMessage>> send, HttpClient client)
    {
        factory.Counter.Reset();
        var res = await send(client);
        Assert.True(res.IsSuccessStatusCode, $"{(int)res.StatusCode}: {await res.Content.ReadAsStringAsync()}");
        var trips = factory.Counter.Trips;
        Assert.True(trips.Count <= budget, $"{trips.Count} trips to the database, budget {budget}:\n\n{string.Join("\n\n", trips)}");
    }

    private static Func<HttpClient, Task<HttpResponseMessage>> Patch(Guid taskId, object body) =>
        c => c.PatchAsJsonAsync($"/api/tasks/{taskId}", body);

    private static Func<HttpClient, Task<HttpResponseMessage>> Get(string url) => c => c.GetAsync(url);

    [Fact]
    public async Task Ticking_a_task_complete_and_reopening_it_take_six_trips()
    {
        var user = await factory.NewUserAsync();
        var task = await user.Client.CreateTaskAsync(user.List.Id, new { title = "Pay rent", assigneeIds = new[] { user.Me.Id } });
        var done = user.Status("Done").Id;
        var toDo = user.Status("To do").Id;

        // Access with the task, the list's statuses, the top of the target status, the save, COMMIT, the reply.
        await TripsAsync(6, Patch(task.Id, new { statusId = done }), user.Client);
        await TripsAsync(6, Patch(task.Id, new { statusId = toDo }), user.Client);
        // Dragging on the board: access, the list's statuses, the neighbours, the save, COMMIT, the reply.
        await TripsAsync(6, c => c.PostAsJsonAsync($"/api/tasks/{task.Id}/move", new { statusId = done }), user.Client);

        // A repeating task also reads its subtasks to copy (and who may still be assigned), or checks whether its next
        // task can be taken back.
        var repeating = await user.Client.CreateTaskAsync(user.List.Id, new { title = "Water plants", repeat = new { unit = "Day" } });
        await TripsAsync(7, Patch(repeating.Id, new { statusId = done }), user.Client);
        await TripsAsync(7, Patch(repeating.Id, new { statusId = toDo }), user.Client);
    }

    [Fact]
    public async Task A_change_reads_only_what_it_needs()
    {
        var user = await factory.NewUserAsync();
        var (_, colleague) = await factory.JoinAsync(user, WorkspaceRole.Member, "Colleague");
        var tag = await user.Client.CreateTagAsync(user.Workspace.Id, "home");
        await user.Client.CreateTagAsync(user.Workspace.Id, "work");
        var other = await user.Client.CreateListAsync(user.Space.Id, "Other");
        var task = await user.Client.CreateTaskAsync(user.List.Id, new { title = "Pay rent" });
        await user.Client.CreateTaskAsync(user.List.Id, new { title = "Receipt", parentTaskId = task.Id });

        // Access with the task, the save, COMMIT and the reply: nothing of the workspace is read.
        await TripsAsync(4, Patch(task.Id, new { title = "Pay the rent" }), user.Client);
        await TripsAsync(4, Patch(task.Id, new { dueDate = "2026-12-01", priority = "High" }), user.Client);
        // Plus the latest history row, which a run of description edits updates instead of adding to.
        await TripsAsync(5, Patch(task.Id, new { description = "<p>Before the 1st</p>" }), user.Client);
        // Plus the tags named.
        await TripsAsync(5, Patch(task.Id, new { tagIds = new[] { tag.Id } }), user.Client);
        // Plus the list (whose space decides who may be assigned) and the people named.
        await TripsAsync(6, Patch(task.Id, new { assigneeIds = new[] { colleague.UserId, user.Me.Id } }), user.Client);
        // Plus both lists with their statuses, the subtasks that move along, and the bottom of the new status.
        await TripsAsync(7, Patch(task.Id, new { listId = other.Id }), user.Client);
    }

    [Fact]
    public async Task Opening_any_task_takes_five_trips()
    {
        var user = await factory.NewUserAsync();
        var (colleagueClient, colleague) = await factory.JoinAsync(user, WorkspaceRole.Member, "Colleague");
        var tag = await user.Client.CreateTagAsync(user.Workspace.Id, "home");
        var parent = await user.Client.CreateTaskAsync(user.List.Id, new
        {
            title = "Move house", description = "<p>Boxes</p>", assigneeIds = new[] { colleague.UserId, user.Me.Id }, tagIds = new[] { tag.Id },
        });
        var subs = new List<TaskDto>();
        for (var i = 0; i < 3; i++)
            subs.Add(await user.Client.CreateTaskAsync(user.List.Id, new { title = $"Step {i}", parentTaskId = parent.Id, assigneeIds = new[] { colleague.UserId } }));
        (await colleagueClient.PostAsJsonAsync($"/api/tasks/{parent.Id}/comments", new { body = "On it" })).EnsureSuccessStatusCode();
        await user.Client.UploadAsync(parent.Id, "list.txt", "boxes"u8.ToArray());
        var plain = await user.Client.CreateTaskAsync(user.List.Id, new { title = "Plain" });

        // Access with the description and creator, the task with its subtasks or parent, comments, files, activity.
        await TripsAsync(5, Get($"/api/tasks/{parent.Id}"), user.Client);
        await TripsAsync(5, Get($"/api/tasks/{subs[1].Id}"), user.Client);
        await TripsAsync(5, Get($"/api/tasks/{plain.Id}"), colleagueClient);

        // And the panel still shows the same thing.
        var detail = await user.Client.TaskDetailAsync(parent.Id);
        Assert.Equal(("Move house", "<p>Boxes</p>", user.Me.Id), (detail.Task.Title, detail.Description, detail.CreatedBy.Id));
        Assert.Equal([colleague.UserId, user.Me.Id], detail.Task.AssigneeIds);
        Assert.Equal((3, 1, 1), (detail.Task.SubtaskCount, detail.Task.CommentCount, detail.Task.AttachmentCount));
        Assert.Equal(subs.Select(s => s.Id), detail.Subtasks.Select(s => s.Id));
        Assert.Null(detail.Parent);
        var subDetail = await user.Client.TaskDetailAsync(subs[1].Id);
        Assert.Equal(new TaskRefDto(parent.Id, "Move house", user.List.Id), subDetail.Parent);
        Assert.Empty(subDetail.Subtasks);
        Assert.Equal([colleague.UserId], subDetail.Task.AssigneeIds);
    }

    [Fact]
    public async Task Every_task_route_checks_access_in_one_query()
    {
        var user = await factory.NewUserAsync();
        var task = await user.Client.CreateTaskAsync(user.List.Id, new { title = "Pay rent" });

        // Access with the task, the insert, and the author for the reply.
        await TripsAsync(3, c => c.PostAsJsonAsync($"/api/tasks/{task.Id}/comments", new { body = "Paid" }), user.Client);
        // The same trips for two tasks or eight: everything is read once for all of them.
        var tasks = new List<TaskDto>();
        for (var i = 0; i < 10; i++) tasks.Add(await user.Client.CreateTaskAsync(user.List.Id, new { title = $"Task {i}" }));
        var done = user.Status("Done").Id;
        await TripsAsync(11, c => c.BulkAsync(tasks.Take(2).Select(t => (object)new { id = t.Id, statusId = done }).ToArray()), user.Client);
        await TripsAsync(11, c => c.BulkAsync(tasks.Skip(2).Select(t => (object)new { id = t.Id, statusId = done }).ToArray()), user.Client);
    }
}
