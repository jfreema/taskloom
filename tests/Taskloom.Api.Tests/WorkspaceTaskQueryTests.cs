using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Taskloom.Api.Endpoints;
using Taskloom.Api.Tests.Support;
using Taskloom.Domain;
using Taskloom.Infrastructure;

namespace Taskloom.Api.Tests;

/// <summary>GET /api/workspaces/{id}/tasks (spec 5.5): scope, search, the done filters, order, limit and parents.</summary>
public class WorkspaceTaskQueryTests(TaskloomFactory factory) : IClassFixture<TaskloomFactory>
{
    private static async Task<List<string>> TitlesAsync(HttpClient client, Guid workspaceId, string query) =>
        (await client.WorkspaceTasksAsync(workspaceId, query)).Tasks.Select(t => t.Title).ToList();

    private static string Q(string q) => Uri.EscapeDataString(q);

    [Fact]
    public async Task Mine_scope_includes_assigned_and_created_unassigned_tasks()
    {
        var alice = await factory.NewUserAsync();
        var (bobClient, bob) = await factory.JoinAsync(alice, WorkspaceRole.Member, "Bob");
        var ws = alice.Workspace.Id;
        var list = alice.List.Id;
        await alice.Client.CreateTaskAsync(list, new { title = "Mine, unassigned", dueDate = "2026-11-20" });
        var forBob = await alice.Client.CreateTaskAsync(list, new { title = "For Bob", assigneeIds = new[] { bob.UserId } });
        await alice.Client.CreateTaskAsync(list, new { title = "Shared", assigneeIds = new[] { bob.UserId, alice.Me.Id }, dueDate = "2026-11-10" });
        await bobClient.CreateTaskAsync(list, new { title = "Bob's own" });
        var fromBob = await bobClient.CreateTaskAsync(list, new { title = "From Bob", assigneeIds = new[] { alice.Me.Id } });
        await alice.Client.CreateTaskAsync(list, new { title = "Sub for Alice", parentTaskId = forBob.Id, assigneeIds = new[] { alice.Me.Id } });
        await alice.Client.CreateTaskAsync(list, new { title = "Done, mine", statusId = alice.Status("Done").Id });
        var elsewhere = await alice.Client.CreateListAsync(alice.Space.Id, "Elsewhere");
        await alice.Client.CreateTaskAsync(elsewhere.Id, new { title = "Elsewhere, mine", dueDate = "2026-11-15" });

        // Mine (the default): assigned to me, or created by me and assigned to nobody; subtasks too; open only.
        // Without q: due date first (nulls last), then position.
        string[] aliceMine = ["Shared", "Elsewhere, mine", "Mine, unassigned", "Sub for Alice", "From Bob"];
        var result = await alice.Client.WorkspaceTasksAsync(ws);
        Assert.Equal(aliceMine, result.Tasks.Select(t => t.Title));
        Assert.Equal(aliceMine, await TitlesAsync(alice.Client, ws, "?scope=MINE"));
        // The subtask's parent isn't returned, so it comes as a ref.
        Assert.Equal([new TaskRefDto(forBob.Id, "For Bob", list)], result.Parents);
        var bobMine = await bobClient.WorkspaceTasksAsync(ws, "?scope=mine");
        Assert.Equal(["Shared", "For Bob", "Bob's own"], bobMine.Tasks.Select(t => t.Title));
        Assert.Empty(bobMine.Parents);

        // All: every open task of the workspace, case-insensitive; then the done ones on request.
        var all = await alice.Client.WorkspaceTasksAsync(ws, "?scope=All");
        Assert.Equal(["Shared", "Elsewhere, mine", "Mine, unassigned", "Sub for Alice", "For Bob", "Bob's own", "From Bob"], all.Tasks.Select(t => t.Title));
        Assert.Empty(all.Parents);
        Assert.Equal(["Shared", "Elsewhere, mine", "Mine, unassigned", "Sub for Alice", "Done, mine", "From Bob"],
            await TitlesAsync(alice.Client, ws, "?includeDone=true"));

        // Unassigning everyone gives the task back to its creator's Mine.
        await alice.Client.PatchTaskAsync(fromBob.Id, new { assigneeIds = Array.Empty<Guid>() });
        Assert.Equal(["Shared", "For Bob", "Bob's own", "From Bob"], await TitlesAsync(bobClient, ws, ""));
        Assert.DoesNotContain("From Bob", await TitlesAsync(alice.Client, ws, ""));

        // A Guest may look; someone outside the workspace gets 404.
        var (guest, _) = await factory.JoinAsync(alice, WorkspaceRole.Guest);
        Assert.Equal(8, (await guest.WorkspaceTasksAsync(ws, "?scope=all&includeDone=true")).Tasks.Count);
        var stranger = await factory.NewUserAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.Client.GetAsync($"/api/workspaces/{ws}/tasks?scope=all")).StatusCode);
    }

    [Fact]
    public async Task Search_matches_title_and_description_open_first_and_escapes_like_wildcards()
    {
        var user = await factory.NewUserAsync();
        var ws = user.Workspace.Id;
        var list = user.List.Id;
        var alpha = await user.Client.CreateTaskAsync(list, new { title = "Report alpha" });
        await user.Client.CreateTaskAsync(list, new { title = "Annual report" });
        await user.Client.CreateTaskAsync(list, new { title = "Reporting tool" });
        await user.Client.CreateTaskAsync(list, new { title = "Numbers", description = "<p>See the <strong>report</strong> draft</p>" });
        await user.Client.CreateTaskAsync(list, new { title = "Report archived", statusId = user.Status("Done").Id });
        await user.Client.CreateTaskAsync(list, new { title = "Unrelated", description = "<p>Nothing here</p>" });

        // Open first; then titles starting with q, titles containing it, description-only matches; then the latest updated.
        Assert.Equal(["Reporting tool", "Report alpha", "Annual report", "Numbers", "Report archived"],
            await TitlesAsync(user.Client, ws, "?scope=all&includeDone=true&q=report"));
        await user.Client.PatchTaskAsync(alpha.Id, new { priority = "High" });
        string[] expected = ["Report alpha", "Reporting tool", "Annual report", "Numbers"];
        Assert.Equal([.. expected, "Report archived"], await TitlesAsync(user.Client, ws, "?scope=all&includeDone=true&q=report"));
        // Case-insensitive, trimmed, and done tasks only on request.
        Assert.Equal(expected, await TitlesAsync(user.Client, ws, $"?scope=all&q={Q("  REPORT ")}"));
        // A blank q is no search: every open task, in the usual order.
        Assert.Equal(["Report alpha", "Annual report", "Reporting tool", "Numbers", "Unrelated"], await TitlesAsync(user.Client, ws, $"?scope=all&q={Q("   ")}"));

        // %, _ and \ are literal characters.
        await user.Client.CreateTaskAsync(list, new { title = "Ready 100% done" });
        await user.Client.CreateTaskAsync(list, new { title = "Ready 1000 rows" });
        await user.Client.CreateTaskAsync(list, new { title = "Plan_b" });
        await user.Client.CreateTaskAsync(list, new { title = "Planxb" });
        await user.Client.CreateTaskAsync(list, new { title = @"C:\temp" });
        await user.Client.CreateTaskAsync(list, new { title = "C:temp" });
        await user.Client.CreateTaskAsync(list, new { title = "Sale", description = "<p>50% off</p>" });
        await user.Client.CreateTaskAsync(list, new { title = "Stock", description = "<p>500 items</p>" });
        Assert.Equal(["Ready 100% done"], await TitlesAsync(user.Client, ws, $"?scope=all&q={Q("100%")}"));
        Assert.Equal(["Plan_b"], await TitlesAsync(user.Client, ws, $"?scope=all&q={Q("plan_b")}"));
        Assert.Equal([@"C:\temp"], await TitlesAsync(user.Client, ws, $"?scope=all&q={Q(@"c:\t")}"));
        Assert.Equal(["Sale"], await TitlesAsync(user.Client, ws, $"?scope=all&q={Q("50%")}"));
        Assert.Equal(["Ready 100% done", "Sale"], await TitlesAsync(user.Client, ws, $"?scope=all&q={Q("%")}"));

        // With q the default limit is 20; limit changes it.
        for (var i = 1; i <= 21; i++) await user.Client.CreateTaskAsync(list, new { title = $"Batch item {i}" });
        Assert.Equal(20, (await TitlesAsync(user.Client, ws, "?scope=all&q=batch")).Count);
        Assert.Equal(21, (await TitlesAsync(user.Client, ws, "?scope=all&q=batch&limit=21")).Count);
        Assert.Equal(["Batch item 21", "Batch item 20"], await TitlesAsync(user.Client, ws, "?scope=all&q=batch&limit=2"));
        // Search covers Mine too: the user created all of these and assigned nobody.
        Assert.Equal(expected, await TitlesAsync(user.Client, ws, "?q=report"));
    }

    [Fact]
    public async Task Done_since_parents_and_invalid_parameters()
    {
        var user = await factory.NewUserAsync();
        var ws = user.Workspace.Id;
        var list = user.List.Id;
        var done = user.Status("Done").Id;
        await user.Client.CreateTaskAsync(list, new { title = "Open" });
        var recent = await user.Client.CreateTaskAsync(list, new { title = "Done recently", statusId = done });
        var old = await user.Client.CreateTaskAsync(list, new { title = "Done long ago", statusId = done });

        // doneSince lets back tasks completed from that day's 00:00 UTC; includeDone=true returns every one.
        var since = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-7);
        var midnight = new DateTimeOffset(since.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        await factory.DbAsync(db => db.Tasks.Where(t => t.Id == recent.Id).ExecuteUpdateAsync(u => u.SetProperty(t => t.CompletedAt, midnight)));
        await factory.DbAsync(db => db.Tasks.Where(t => t.Id == old.Id).ExecuteUpdateAsync(u => u.SetProperty(t => t.CompletedAt, midnight.AddTicks(-10))));
        Assert.Equal(["Open", "Done recently"], await TitlesAsync(user.Client, ws, $"?scope=all&doneSince={since:yyyy-MM-dd}"));
        Assert.Equal(["Open"], await TitlesAsync(user.Client, ws, $"?scope=all&doneSince={since.AddDays(1):yyyy-MM-dd}"));
        Assert.Equal(["Open", "Done recently", "Done long ago"], await TitlesAsync(user.Client, ws, $"?scope=all&includeDone=true&doneSince={since.AddDays(1):yyyy-MM-dd}"));
        Assert.Equal(["Open"], await TitlesAsync(user.Client, ws, "?scope=all&includeDone=false&doneSince="));

        // parents: one ref per distinct parent of a returned subtask that isn't returned itself, in order of first appearance.
        var parent = await user.Client.CreateTaskAsync(list, new { title = "Parent" });
        await user.Client.CreateTaskAsync(list, new { title = "Child 1", parentTaskId = parent.Id });
        await user.Client.CreateTaskAsync(list, new { title = "Child 2", parentTaskId = parent.Id });
        var doneParent = await user.Client.CreateTaskAsync(list, new { title = "Done parent", statusId = done });
        await user.Client.CreateTaskAsync(list, new { title = "Open child", parentTaskId = doneParent.Id });
        var search = await user.Client.WorkspaceTasksAsync(ws, "?scope=all&q=child");
        Assert.Equal(["Child 2", "Child 1", "Open child"], search.Tasks.Select(t => t.Title));
        Assert.Equal([new TaskRefDto(parent.Id, "Parent", list), new TaskRefDto(doneParent.Id, "Done parent", list)], search.Parents);
        var everything = await user.Client.WorkspaceTasksAsync(ws, "?scope=all");
        Assert.Equal(["Open", "Child 1", "Open child", "Parent", "Child 2"], everything.Tasks.Select(t => t.Title));
        Assert.Equal([new TaskRefDto(doneParent.Id, "Done parent", list)], everything.Parents);
        var firstTwo = await user.Client.WorkspaceTasksAsync(ws, "?scope=all&q=child&limit=2");
        Assert.Equal([new TaskRefDto(parent.Id, "Parent", list)], firstTwo.Parents);
        Assert.Equal(["Open"], await TitlesAsync(user.Client, ws, "?scope=all&limit=1"));

        // Bad values are 400 with a message; a 200-character q is fine.
        (string Query, string Detail)[] bad =
        [
            ("scope=everyone", "scope must be mine or all."),
            ("limit=0", "limit must be a whole number from 1 to 1000."),
            ("limit=1001", "limit must be a whole number from 1 to 1000."),
            ("limit=-1", "limit must be a whole number from 1 to 1000."),
            ("limit=2.5", "limit must be a whole number from 1 to 1000."),
            ("limit=ten", "limit must be a whole number from 1 to 1000."),
            ("doneSince=11/01/2026", "doneSince must be a date like 2026-11-04."),
            ("doneSince=2026-13-01", "doneSince must be a date like 2026-11-04."),
            ("includeDone=yes", "includeDone must be true or false."),
            ($"q={new string('q', 201)}", "Searches can be up to 200 characters."),
        ];
        foreach (var (query, detail) in bad)
            Assert.Equal(detail, await (await user.Client.GetAsync($"/api/workspaces/{ws}/tasks?{query}")).ProblemAsync(HttpStatusCode.BadRequest));
        Assert.Empty((await user.Client.WorkspaceTasksAsync(ws, $"?q={new string('q', 200)}")).Tasks);
        // Membership is checked before the parameters.
        var stranger = await factory.NewUserAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.Client.GetAsync($"/api/workspaces/{ws}/tasks?scope=everyone")).StatusCode);

        // Without q the default limit is 500, and limit goes up to 1000.
        (await user.Client.SeedAsync(ws, 520)).EnsureSuccessStatusCode();
        Assert.Equal(500, (await user.Client.WorkspaceTasksAsync(ws, "?scope=all&includeDone=true")).Tasks.Count);
        Assert.True((await user.Client.WorkspaceTasksAsync(ws, "?scope=all&includeDone=true&limit=1000")).Tasks.Count > 530);
    }

    [Fact]
    public async Task The_query_stops_when_the_request_is_aborted()
    {
        var user = await factory.NewUserAsync();
        await user.Client.CreateTaskAsync(user.List.Id, new { title = "Anything" });
        using var scope = factory.Services.CreateScope();
        var http = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        http.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimsExtensions.UserIdClaim, user.Me.Id.ToString())], "test"));
        http.Request.QueryString = new QueryString("?scope=all&q=any");
        var db = scope.ServiceProvider.GetRequiredService<TaskloomDbContext>();
        var access = new Access(db, new HttpContextAccessor { HttpContext = http });

        // The endpoint binds this token to HttpContext.RequestAborted; an aborted palette search must not run its query.
        using var aborted = new CancellationTokenSource();
        await aborted.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => WorkspaceTaskQuery.RunAsync(user.Workspace.Id, http.Request, access, db, aborted.Token));
        var found = await WorkspaceTaskQuery.RunAsync(user.Workspace.Id, http.Request, access, db, CancellationToken.None);
        Assert.Equal(["Anything"], found.Tasks.Select(t => t.Title));
    }
}
