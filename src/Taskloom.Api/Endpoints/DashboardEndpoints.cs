using Microsoft.EntityFrameworkCore;
using Taskloom.Domain;
using Taskloom.Infrastructure;

namespace Taskloom.Api.Endpoints;

/// <summary>
/// GET /api/workspaces/{id}/dashboard: counts of the workspace's tasks by status, priority, due date, assignee and list,
/// with a 14-day trend and the most overdue tasks. Two trips to the database whatever the size of the workspace: the
/// membership check, then one compact row per task, which is counted here. Like Home, it only ever counts tasks in lists
/// the caller can see (<see cref="Access.VisibleListIds"/>), so a private space they aren't in adds nothing.
/// </summary>
/// <remarks>
/// Query: <c>scope</c> = all (default) or mine (assigned to me, or created by me and assigned to nobody, as on Home);
/// <c>spaceId</c> or <c>listId</c> to narrow it to one space or list; <c>offset</c> = the browser's
/// <c>Date.getTimezoneOffset()</c> in minutes, so the trend's days are the caller's own. "Today" is the browser's date
/// (<see cref="ClientDate"/>). Subtasks count as tasks.
/// </remarks>
public static class DashboardEndpoints
{
    public const int TrendDays = 14, OverdueShown = 8;

    public static void MapDashboardEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/workspaces/{id:guid}/dashboard", RunAsync);
    }

    private sealed record Row(
        Guid Id, Guid ListId, Guid StatusId, TaskPriority Priority, DateOnly? DueDate,
        DateTimeOffset CreatedAt, DateTimeOffset? CompletedAt, bool IsSubtask, List<Guid> Assignees, string? OverdueTitle);

    public static async Task<DashboardDto> RunAsync(Guid id, HttpRequest request, HttpContext http, Access access,
        TaskloomDbContext db, CancellationToken ct)
    {
        await access.RequireWorkspaceAsync(id);
        var query = request.Query;
        var mine = Single(query, "scope")?.Trim().ToLowerInvariant() switch
        {
            null or "" or "all" => false,
            "mine" => true,
            _ => throw ProblemException.BadRequest("scope must be all or mine."),
        };
        Guid? spaceId = null, listId = null;
        if (Single(query, "spaceId") is { Length: > 0 } rawSpace)
            spaceId = Guid.TryParse(rawSpace, out var s) ? s : throw ProblemException.BadRequest("spaceId must be an id.");
        if (Single(query, "listId") is { Length: > 0 } rawList)
            listId = Guid.TryParse(rawList, out var l) ? l : throw ProblemException.BadRequest("listId must be an id.");
        var offset = TimeSpan.Zero;
        if (Single(query, "offset") is { Length: > 0 } rawOffset)
        {
            if (!int.TryParse(rawOffset, System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out var minutes)
                || minutes is < -900 or > 900)
                throw ProblemException.BadRequest("offset must be minutes from UTC, from -900 to 900.");
            // getTimezoneOffset() is UTC minus local time.
            offset = TimeSpan.FromMinutes(-minutes);
        }

        var now = Clock.Now();
        var today = ClientDate.Today(http, now);
        var me = access.UserId;

        // Only lists the caller can see, narrowed to the space or list asked for (one they can't see counts nothing).
        var visible = access.VisibleListIds(id);
        var lists = db.Lists.Where(l => visible.Contains(l.Id));
        if (spaceId is { } sid) lists = lists.Where(l => l.SpaceId == sid);
        if (listId is { } lid) lists = lists.Where(l => l.Id == lid);
        var listIds = lists.Select(l => l.Id);

        var tasks = db.Tasks.Where(t => listIds.Contains(t.ListId));
        if (mine) tasks = tasks.Where(t => t.Assignees.Any(a => a.UserId == me) || (t.CreatedById == me && !t.Assignees.Any()));

        // One query: the assignees come in the same statement (a join), and only overdue tasks bring their title.
        var rows = await tasks
            .Select(t => new Row(t.Id, t.ListId, t.StatusId, t.Priority, t.DueDate, t.CreatedAt, t.CompletedAt, t.ParentTaskId != null,
                t.Assignees.Select(a => a.UserId).ToList(),
                t.CompletedAt == null && t.DueDate < today ? t.Title : null))
            .ToListAsync(ct);

        return Count(rows, today, offset);
    }

    private static DashboardDto Count(List<Row> rows, DateOnly today, TimeSpan offset)
    {
        DateOnly Local(DateTimeOffset at) => DateOnly.FromDateTime(at.ToOffset(offset).DateTime);
        var weekEnd = today.AddDays(7);
        var trendStart = today.AddDays(-(TrendDays - 1));

        int open = 0, done = 0, overdue = 0, dueToday = 0, dueTomorrow = 0, dueWeek = 0, dueLater = 0, noDue = 0,
            subtasks = 0, unassigned = 0, completed7 = 0, created7 = 0;
        var byStatus = new Dictionary<Guid, int>();
        var byPriority = new Dictionary<TaskPriority, (int Open, int Done)>();
        var byList = new Dictionary<Guid, (int Open, int Done, int Overdue)>();
        var byAssignee = new Dictionary<Guid, (int Open, int Overdue, int DueSoon, int Done)>();
        var unassignedCounts = (Open: 0, Overdue: 0, DueSoon: 0, Done: 0);
        var created = new int[TrendDays];
        var completed = new int[TrendDays];

        foreach (var r in rows)
        {
            var isDone = r.CompletedAt is not null;
            var isOverdue = !isDone && r.DueDate < today;
            var isSoon = !isDone && r.DueDate is { } d && d >= today && d <= weekEnd;
            if (r.IsSubtask) subtasks++;
            byStatus[r.StatusId] = byStatus.GetValueOrDefault(r.StatusId) + 1;

            var p = byPriority.GetValueOrDefault(r.Priority);
            byPriority[r.Priority] = isDone ? (p.Open, p.Done + 1) : (p.Open + 1, p.Done);

            var l = byList.GetValueOrDefault(r.ListId);
            byList[r.ListId] = (l.Open + (isDone ? 0 : 1), l.Done + (isDone ? 1 : 0), l.Overdue + (isOverdue ? 1 : 0));

            if (r.Assignees.Count == 0) unassignedCounts = Add(unassignedCounts, isDone, isOverdue, isSoon);
            foreach (var u in r.Assignees.Distinct()) byAssignee[u] = Add(byAssignee.GetValueOrDefault(u), isDone, isOverdue, isSoon);

            var createdDay = Local(r.CreatedAt);
            if (createdDay >= trendStart && createdDay <= today) created[createdDay.DayNumber - trendStart.DayNumber]++;
            if (createdDay > today.AddDays(-7) && createdDay <= today) created7++;

            if (isDone)
            {
                done++;
                var doneDay = Local(r.CompletedAt!.Value);
                if (doneDay >= trendStart && doneDay <= today) completed[doneDay.DayNumber - trendStart.DayNumber]++;
                if (doneDay > today.AddDays(-7) && doneDay <= today) completed7++;
                continue;
            }

            open++;
            if (r.Assignees.Count == 0) unassigned++;
            switch (r.DueDate)
            {
                case null: noDue++; break;
                case var due when due < today: overdue++; break;
                case var due when due == today: dueToday++; break;
                case var due when due == today.AddDays(1): dueTomorrow++; break;
                case var due when due <= weekEnd: dueWeek++; break;
                default: dueLater++; break;
            }
        }

        var overdueTasks = rows.Where(r => r.OverdueTitle is not null)
            .OrderBy(r => r.DueDate).ThenBy(r => r.Priority == TaskPriority.None ? 9 : (int)r.Priority).ThenBy(r => r.Id)
            .Take(OverdueShown)
            .Select(r => new DashboardTaskDto(r.Id, r.OverdueTitle!, r.ListId, r.DueDate!.Value, r.Priority, r.Assignees.Distinct().ToList()))
            .ToList();

        var people = byAssignee
            .Select(kv => new DashboardPersonDto(kv.Key, kv.Value.Open, kv.Value.Overdue, kv.Value.DueSoon, kv.Value.Done))
            .OrderByDescending(p => p.Open).ThenByDescending(p => p.Done).ThenBy(p => p.UserId)
            .ToList();
        if (unassignedCounts != (0, 0, 0, 0))
            people.Add(new DashboardPersonDto(null, unassignedCounts.Open, unassignedCounts.Overdue, unassignedCounts.DueSoon, unassignedCounts.Done));

        return new DashboardDto(
            today,
            new DashboardTotalsDto(rows.Count, open, done, overdue, dueToday, noDue, unassigned, subtasks, completed7, created7),
            new DashboardDueDto(overdue, dueToday, dueTomorrow, dueWeek, dueLater, noDue),
            byStatus.Select(kv => new DashboardCountDto(kv.Key, kv.Value)).ToList(),
            Enum.GetValues<TaskPriority>().Select(p =>
            {
                var c = byPriority.GetValueOrDefault(p);
                return new DashboardPriorityDto(p, c.Open, c.Done);
            }).ToList(),
            byList.Select(kv => new DashboardListDto(kv.Key, kv.Value.Open, kv.Value.Done, kv.Value.Overdue)).ToList(),
            people,
            Enumerable.Range(0, TrendDays).Select(i => new DashboardDayDto(trendStart.AddDays(i), created[i], completed[i])).ToList(),
            overdueTasks);
    }

    private static (int Open, int Overdue, int DueSoon, int Done) Add((int Open, int Overdue, int DueSoon, int Done) a,
        bool isDone, bool isOverdue, bool isSoon) =>
        (a.Open + (isDone ? 0 : 1), a.Overdue + (isOverdue ? 1 : 0), a.DueSoon + (isSoon ? 1 : 0), a.Done + (isDone ? 1 : 0));

    private static string? Single(IQueryCollection query, string key) =>
        query.TryGetValue(key, out var values) && values.Count > 0 ? values[0] : null;
}

// ---- Wire shapes (web/src/types.ts mirrors them) ----

/// <summary>GET /api/workspaces/{id}/dashboard. Counts are of tasks the caller can see, subtasks included.</summary>
/// <param name="Today">The date the counts are relative to (the browser's).</param>
/// <param name="ByStatus">Tasks per status id; the client names and groups them from the workspace tree.</param>
/// <param name="ByPriority">Every priority, None included, in enum order.</param>
/// <param name="ByAssignee">Per person, most open tasks first; a task with two assignees counts for both. A last row with a
/// null id counts unassigned tasks.</param>
/// <param name="Trend">The last 14 days, oldest first, in the caller's time zone.</param>
/// <param name="Overdue">The most overdue open tasks (up to 8), oldest due date first.</param>
public record DashboardDto(
    DateOnly Today, DashboardTotalsDto Totals, DashboardDueDto Due, List<DashboardCountDto> ByStatus,
    List<DashboardPriorityDto> ByPriority, List<DashboardListDto> ByList, List<DashboardPersonDto> ByAssignee,
    List<DashboardDayDto> Trend, List<DashboardTaskDto> Overdue);

/// <param name="Overdue">Open, due before today.</param>
/// <param name="NoDueDate">Open, without a due date.</param>
/// <param name="Unassigned">Open, assigned to nobody.</param>
/// <param name="CompletedLast7">Completed in the last 7 days, today included.</param>
/// <param name="CreatedLast7">Created in the last 7 days, today included.</param>
public record DashboardTotalsDto(int Total, int Open, int Done, int Overdue, int DueToday, int NoDueDate, int Unassigned,
    int Subtasks, int CompletedLast7, int CreatedLast7);

/// <summary>Open tasks by due date. NextWeek is 2 to 7 days from today; Later is after that.</summary>
public record DashboardDueDto(int Overdue, int Today, int Tomorrow, int NextWeek, int Later, int None);

public record DashboardCountDto(Guid Id, int Count);

public record DashboardPriorityDto(TaskPriority Priority, int Open, int Done);

public record DashboardListDto(Guid ListId, int Open, int Done, int Overdue);

/// <param name="UserId">Null for the unassigned row.</param>
/// <param name="DueSoon">Open and due from today to 7 days from today.</param>
public record DashboardPersonDto(Guid? UserId, int Open, int Overdue, int DueSoon, int Done);

public record DashboardDayDto(DateOnly Date, int Created, int Completed);

public record DashboardTaskDto(Guid Id, string Title, Guid ListId, DateOnly DueDate, TaskPriority Priority, List<Guid> AssigneeIds);
