using System.Net;
using Taskloom.Api.Endpoints;
using Taskloom.Api.Tests.Support;
using Taskloom.Domain;

namespace Taskloom.Api.Tests;

/// <summary>GET /api/workspaces/{id}/dashboard and the demo data behind it (POST /api/dev/demo-seed).</summary>
public class DashboardTests(TaskloomFactory factory) : IClassFixture<TaskloomFactory>
{
    private static async Task<DashboardDto> DashboardAsync(HttpClient client, Guid workspaceId, string query = "") =>
        await (await client.GetAsync($"/api/workspaces/{workspaceId}/dashboard{query}")).ReadAsync<DashboardDto>();

    private static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd");

    [Fact]
    public async Task It_counts_tasks_by_status_priority_due_date_assignee_and_list()
    {
        var user = await factory.NewUserAsync();
        var (_, colleague) = await factory.JoinAsync(user, WorkspaceRole.Member, "Colleague");
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var list = user.List.Id;
        var toDo = user.Status("To do").Id;
        var done = user.Status("Done").Id;

        var late = await user.Client.CreateTaskAsync(list, new { title = "Late", dueDate = Iso(today.AddDays(-3)), priority = "Urgent", assigneeIds = new[] { user.Me.Id } });
        await user.Client.CreateTaskAsync(list, new { title = "Today", dueDate = Iso(today), priority = "High", assigneeIds = new[] { user.Me.Id, colleague.UserId } });
        await user.Client.CreateTaskAsync(list, new { title = "Tomorrow", dueDate = Iso(today.AddDays(1)), assigneeIds = new[] { colleague.UserId } });
        await user.Client.CreateTaskAsync(list, new { title = "Next week", dueDate = Iso(today.AddDays(5)) });
        await user.Client.CreateTaskAsync(list, new { title = "Later", dueDate = Iso(today.AddDays(30)), priority = "Low" });
        await user.Client.CreateTaskAsync(list, new { title = "Undated", parentTaskId = late.Id });
        await user.Client.CreateTaskAsync(list, new { title = "Finished", statusId = done, dueDate = Iso(today.AddDays(-10)), assigneeIds = new[] { user.Me.Id } });

        var d = await DashboardAsync(user.Client, user.Workspace.Id);
        Assert.Equal(today, d.Today);
        Assert.Equal(new DashboardTotalsDto(Total: 7, Open: 6, Done: 1, Overdue: 1, DueToday: 1, NoDueDate: 1, Unassigned: 3, Subtasks: 1,
            CompletedLast7: 1, CreatedLast7: 7), d.Totals);
        Assert.Equal(new DashboardDueDto(Overdue: 1, Today: 1, Tomorrow: 1, NextWeek: 1, Later: 1, None: 1), d.Due);
        Assert.Equal(6, d.ByStatus.Single(s => s.Id == toDo).Count);
        Assert.Equal(1, d.ByStatus.Single(s => s.Id == done).Count);
        Assert.Equal(Enum.GetValues<TaskPriority>(), d.ByPriority.Select(p => p.Priority));
        Assert.Equal((1, 0), d.ByPriority.Where(p => p.Priority == TaskPriority.Urgent).Select(p => (p.Open, p.Done)).Single());
        Assert.Equal((3, 1), d.ByPriority.Where(p => p.Priority == TaskPriority.None).Select(p => (p.Open, p.Done)).Single());
        Assert.Equal(new DashboardListDto(list, 6, 1, 1), Assert.Single(d.ByList));

        // A task with two assignees counts for both; unassigned tasks come last.
        Assert.Equal(new DashboardPersonDto(user.Me.Id, Open: 2, Overdue: 1, DueSoon: 1, Done: 1), d.ByAssignee[0]);
        Assert.Equal(new DashboardPersonDto(colleague.UserId, Open: 2, Overdue: 0, DueSoon: 2, Done: 0), d.ByAssignee[1]);
        Assert.Equal(new DashboardPersonDto(null, Open: 3, Overdue: 0, DueSoon: 1, Done: 0), d.ByAssignee[2]);

        Assert.Equal(DashboardEndpoints.TrendDays, d.Trend.Count);
        Assert.Equal(today, d.Trend[^1].Date);
        Assert.Equal((7, 1), (d.Trend[^1].Created, d.Trend[^1].Completed));

        var overdue = Assert.Single(d.Overdue);
        Assert.Equal((late.Id, "Late", TaskPriority.Urgent), (overdue.Id, overdue.Title, overdue.Priority));

        // Mine: assigned to me, or mine and assigned to nobody.
        var mine = await DashboardAsync(user.Client, user.Workspace.Id, "?scope=mine");
        Assert.Equal(6, mine.Totals.Total);
    }

    [Fact]
    public async Task It_narrows_to_a_space_or_list_and_never_counts_what_the_caller_cannot_see()
    {
        var owner = await factory.NewUserAsync();
        var (pia, piaInfo) = await factory.JoinAsync(owner, WorkspaceRole.Member, "Pia");
        var other = await owner.Client.CreateListAsync(owner.Space.Id, "Other");
        await owner.Client.CreateTaskAsync(owner.List.Id, new { title = "Public" });
        await owner.Client.CreateTaskAsync(other.Id, new { title = "Elsewhere" });
        var hr = await (await pia.PostAsJsonAsync($"/api/workspaces/{owner.Workspace.Id}/spaces", new { name = "HR", isPrivate = true }))
            .ReadAsync<SpaceDto>();
        var hrList = await pia.CreateListAsync(hr.Id, "Reviews");
        // Assigned to the owner, but in a space the owner isn't in.
        var secret = await pia.CreateTaskAsync(hrList.Id, new { title = "Payroll review", dueDate = "2020-01-01" });

        var ownerView = await DashboardAsync(owner.Client, owner.Workspace.Id);
        Assert.Equal(2, ownerView.Totals.Total);
        Assert.DoesNotContain(ownerView.ByList, l => l.ListId == hrList.Id);
        Assert.Empty(ownerView.Overdue);
        // Asking for the private space or its list by id counts nothing either.
        Assert.Equal(0, (await DashboardAsync(owner.Client, owner.Workspace.Id, $"?spaceId={hr.Id}")).Totals.Total);
        Assert.Equal(0, (await DashboardAsync(owner.Client, owner.Workspace.Id, $"?listId={hrList.Id}")).Totals.Total);

        var piaView = await DashboardAsync(pia, owner.Workspace.Id);
        Assert.Equal(3, piaView.Totals.Total);
        Assert.Equal(secret.Id, Assert.Single(piaView.Overdue).Id);
        Assert.Equal(1, (await DashboardAsync(pia, owner.Workspace.Id, $"?spaceId={hr.Id}")).Totals.Total);
        Assert.Equal(1, (await DashboardAsync(owner.Client, owner.Workspace.Id, $"?listId={other.Id}")).Totals.Total);
        Assert.Equal(2, (await DashboardAsync(owner.Client, owner.Workspace.Id, $"?spaceId={owner.Space.Id}")).Totals.Total);

        // Someone outside the workspace gets a 404; a bad parameter is a 400.
        var stranger = await factory.NewUserAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.Client.GetAsync($"/api/workspaces/{owner.Workspace.Id}/dashboard")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.Client.GetAsync($"/api/workspaces/{owner.Workspace.Id}/dashboard?scope=team")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.Client.GetAsync($"/api/workspaces/{owner.Workspace.Id}/dashboard?offset=9999")).StatusCode);
        _ = piaInfo;
    }

    [Fact]
    public async Task Demo_data_only_adds_and_replacing_or_removing_it_leaves_everything_else_alone()
    {
        var user = await factory.NewUserAsync();
        var mine = await user.Client.CreateTaskAsync(user.List.Id, new { title = "My own task" });
        var ws = user.Workspace.Id;

        var res = await user.Client.PostAsJsonAsync("/api/dev/demo-seed", new { workspaceId = ws });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var tree = (await user.Client.TreeAsync()).Single();
        Assert.Equal(["Personal", "Product", "Marketing", "Operations", "Leadership", "Home & life"], tree.Spaces.Select(s => s.Name));
        Assert.True(tree.Spaces.Single(s => s.Name == "Leadership").IsPrivate);
        var first = await DashboardAsync(user.Client, ws);
        Assert.True(first.Totals.Total > 300, $"{first.Totals.Total} tasks");
        Assert.True(first.Totals.Overdue > 10 && first.Totals.Done > 50 && first.Totals.Subtasks > 20);
        Assert.True(first.ByAssignee.Count >= 8);

        // A second run refuses unless asked to replace; replacing rebuilds the same amount.
        Assert.Equal(HttpStatusCode.Conflict, (await user.Client.PostAsJsonAsync("/api/dev/demo-seed", new { workspaceId = ws })).StatusCode);
        (await user.Client.PostAsJsonAsync("/api/dev/demo-seed", new { workspaceId = ws, replace = true })).EnsureSuccessStatusCode();
        var second = await DashboardAsync(user.Client, ws);
        Assert.Equal(first.Totals.Total, second.Totals.Total);
        Assert.Equal(6, (await user.Client.TreeAsync()).Single().Spaces.Count);

        // Removing it leaves the user's own space and task.
        (await user.Client.DeleteAsync($"/api/dev/demo-seed/{ws}")).EnsureSuccessStatusCode();
        var after = (await user.Client.TreeAsync()).Single();
        Assert.Equal(["Personal"], after.Spaces.Select(s => s.Name));
        Assert.Equal(mine.Id, Assert.Single(await user.Client.ListTasksAsync(user.List.Id)).Id);

        // Only an Owner can seed.
        var (member, _) = await factory.JoinAsync(user, WorkspaceRole.Admin);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.PostAsJsonAsync("/api/dev/demo-seed", new { workspaceId = ws })).StatusCode);
    }
}
