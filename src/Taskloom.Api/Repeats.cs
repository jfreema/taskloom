using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Taskloom.Application;
using Taskloom.Domain;
using Taskloom.Infrastructure;

namespace Taskloom.Api;

/// <summary>
/// A repeat rule on the wire (TaskDto.Repeat, the "repeat" of PATCH /api/tasks/{id} and of create): the same fields as
/// <see cref="RepeatRule"/>, with the weekdays as a list of day names ("Monday") in week order from Sunday.
/// </summary>
public record RepeatDto(
    RepeatUnit Unit, int Every, RepeatBasis Basis, List<DayOfWeek> Weekdays,
    int? MonthDay, int? MonthWeek, DayOfWeek? MonthWeekday, DateOnly? Until)
{
    public static RepeatDto? From(RepeatRule? rule) => rule is null ? null : new RepeatDto(
        rule.Unit, rule.Every, rule.Basis,
        Enum.GetValues<DayOfWeek>().Where(d => Recurrence.Has(rule.Weekdays, d)).ToList(),
        rule.MonthDay, rule.MonthWeek, rule.MonthWeekday, rule.Until);
}

/// <summary>A repeat rule as a client sends it: every field optional but the unit.</summary>
public record RepeatRequest(
    RepeatUnit? Unit, int? Every, RepeatBasis? Basis, List<DayOfWeek>? Weekdays,
    int? MonthDay, int? MonthWeek, DayOfWeek? MonthWeekday, DateOnly? Until);

/// <summary>Reading and checking repeat rules sent by clients.</summary>
public static class Repeats
{
    public const string SubtaskMessage = "Only top-level tasks can repeat. Set the repeat on the parent task.";
    private const string UnreadableMessage = "Send the repeat as an object with a unit of Day, Week, Month or Year, or null to stop repeating.";

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) },
        NumberHandling = JsonNumberHandling.Strict,
    };

    /// <summary>The "repeat" of a PATCH body: null stops the task repeating. Wrong shapes and values are a 400.</summary>
    public static RepeatRule? Read(JsonElement v)
    {
        if (v.ValueKind == JsonValueKind.Null) return null;
        if (v.ValueKind != JsonValueKind.Object) throw ProblemException.BadRequest(UnreadableMessage);
        RepeatRequest? req;
        try
        {
            req = v.Deserialize<RepeatRequest>(Options);
        }
        catch (Exception e) when (e is JsonException or NotSupportedException or InvalidOperationException)
        {
            throw ProblemException.BadRequest(UnreadableMessage);
        }
        return Check(req ?? throw ProblemException.BadRequest(UnreadableMessage));
    }

    /// <summary>The rule a request describes (not yet normalized), or a 400 naming what's wrong.</summary>
    public static RepeatRule Check(RepeatRequest req)
    {
        if (req.Unit is not { } unit || !Enum.IsDefined(unit))
            throw ProblemException.BadRequest("Choose Day, Week, Month or Year as the repeat unit.");
        var every = req.Every ?? 1;
        if (every is < 1 or > Recurrence.MaxEvery) throw ProblemException.BadRequest("Repeats can be every 1 to 365 days, weeks, months or years.");
        var basis = req.Basis ?? RepeatBasis.Schedule;
        if (!Enum.IsDefined(basis)) throw ProblemException.BadRequest("Repeat on Schedule or after Completion.");
        var weekdays = WeekdaySet.None;
        foreach (var day in req.Weekdays ?? [])
        {
            if (!Enum.IsDefined(day)) throw ProblemException.BadRequest("Weekdays must be day names like Monday.");
            weekdays |= Recurrence.Flag(day);
        }
        if (req.MonthDay is { } monthDay && monthDay != -1 && monthDay is < 1 or > 31)
            throw ProblemException.BadRequest("The day of the month must be 1 to 31, or -1 for the last day.");
        if (req.MonthWeek is { } week && week != -1 && week is < 1 or > 4)
            throw ProblemException.BadRequest("The week of the month must be 1 to 4, or -1 for the last one.");
        if (req.MonthWeekday is { } weekday && !Enum.IsDefined(weekday))
            throw ProblemException.BadRequest("Weekdays must be day names like Monday.");
        if (req.MonthDay is not null && req.MonthWeek is not null)
            throw ProblemException.BadRequest("Repeat on a day of the month or on a weekday of the month, not both.");
        return new RepeatRule
        {
            Unit = unit, Every = every, Basis = basis, Weekdays = weekdays,
            MonthDay = req.MonthDay, MonthWeek = req.MonthWeek, MonthWeekday = req.MonthWeekday, Until = req.Until,
        };
    }
}

/// <summary>
/// The person's own date. Only the browser knows their time zone, so the web app sends its local date in
/// <c>X-Taskloom-Date</c> on every request. A missing or malformed value, or one more than a day from the date in UTC
/// (no time zone is further), gives the UTC date.
/// </summary>
public static class ClientDate
{
    public const string Header = "X-Taskloom-Date";

    public static DateOnly Today(HttpContext? http, DateTimeOffset now)
    {
        var utc = DateOnly.FromDateTime(now.UtcDateTime);
        var raw = http?.Request.Headers[Header].ToString();
        return Dates.TryParse(raw, out var local) && Math.Abs(local.DayNumber - utc.DayNumber) <= 1 ? local : utc;
    }
}

/// <summary>
/// Repeating tasks. Completing one creates the next of its series and hands it the rule; reopening a completed one takes
/// that next task back (deletes it and returns the rule) while nobody has touched it, so a tick and its undo leave no
/// trace. One per request; it reads what it needs only when a repeating task changes, so other changes cost nothing.
/// Changes are tracked, not saved: the caller saves.
/// </summary>
public sealed class Repeater(TaskloomDbContext db, ActivityRecorder activity, DateTimeOffset now, DateOnly today)
{
    /// <summary>A task's place and state before a change.</summary>
    public readonly record struct Before(Guid StatusId, double Position, bool Completed);

    private readonly Dictionary<Guid, List<ListStatus>> _statuses = [];
    private readonly Dictionary<(Guid List, Guid Status), double> _bottoms = [];
    private int _created;

    public DateOnly Today => today;

    public static Before Snapshot(TaskItem task) => new(task.StatusId, task.Position, task.CompletedAt is not null);

    /// <summary>Hands over a list's statuses the caller has already read, so completing a task there reads them no more.</summary>
    public void Know(Guid listId, IEnumerable<ListStatus> statuses) =>
        _statuses[listId] = statuses.OrderBy(s => s.Position).ThenBy(s => s.Id).ToList();

    /// <summary>Called once every change to <paramref name="task"/> is applied. Returns the task it created, if any.</summary>
    public async Task<TaskItem?> AfterChangeAsync(TaskItem task, Before before)
    {
        var completed = task.CompletedAt is not null;
        if (!before.Completed && completed && task.Repeat is not null && task.ParentTaskId is null)
            return await CreateNextAsync(task, before);
        if (before.Completed && !completed && task.NextRepeatId is { } nextId)
            await TakeBackAsync(task, nextId);
        return null;
    }

    /// <summary>
    /// The next task of the series: a copy with the next due date (start date moved by as much), in the list's first Not
    /// started status, where the task was when it waited in that status, else at the bottom. It keeps the title,
    /// description, priority and tags, the assignees who can still be assigned, and the subtasks, all open again.
    /// Comments, attachments and history stay with the completed task. A rule that has ended creates nothing and stays.
    /// </summary>
    private async Task<TaskItem?> CreateNextAsync(TaskItem task, Before before)
    {
        var rule = task.Repeat!;
        if (Recurrence.Next(rule, task.DueDate, today) is not { } due) return null;
        var statuses = await StatusesAsync(task.ListId);
        // Never straight into Done: a list with no open status ends the series.
        var open = statuses.FirstOrDefault(s => s.Category == StatusCategory.NotStarted)
                   ?? statuses.FirstOrDefault(s => s.Category == StatusCategory.Active);
        if (open is null) return null;

        var shift = due.DayNumber - (task.DueDate ?? today).DayNumber;
        // The family as stored, with assignees and tags, in one query (a handful of rows, so not split): the subtasks, and
        // the task itself for its sets when this request didn't load them. A set it did load may have just changed, so
        // that one is taken from the task.
        var stored = await db.Tasks.AsNoTracking().Include(t => t.Assignees).Include(t => t.Tags)
            .Where(t => t.ParentTaskId == task.Id || t.Id == task.Id).InOrder().ToListAsync();
        var subtasks = stored.Where(t => t.Id != task.Id).ToList();
        var self = stored.SingleOrDefault(t => t.Id == task.Id);
        var entry = db.Entry(task);
        var assignees = entry.Collection(t => t.Assignees).IsLoaded ? task.Assignees : self?.Assignees ?? [];
        var tags = entry.Collection(t => t.Tags).IsLoaded ? task.Tags : self?.Tags ?? [];
        var people = await AssignableAsync(task.ListId, assignees.Concat(subtasks.SelectMany(s => s.Assignees)).Select(a => a.UserId));

        var position = open.Id == before.StatusId ? before.Position : await BottomAsync(task.ListId, open.Id);
        var next = Copy(task, assignees, tags, null, open.Id, position, due, Shift(task.StartDate, shift), people);
        next.Repeat = rule;
        foreach (var sub in subtasks)
            Copy(sub, sub.Assignees, sub.Tags, next.Id, open.Id, sub.Position, Shift(sub.DueDate, shift), Shift(sub.StartDate, shift), people);

        task.Repeat = null;
        task.NextRepeatId = next.Id;
        activity.Record(task.Id, TaskActivityKind.Repeated, null, ActivityValues.Date(due));
        return next;
    }

    private TaskItem Copy(TaskItem from, List<TaskAssignee> fromAssignees, List<TaskTag> fromTags, Guid? parentId, Guid statusId, double position,
        DateOnly? due, DateOnly? start, HashSet<Guid> people)
    {
        // A microsecond apart, so the copies keep their order wherever positions tie.
        var created = now.AddTicks(10 * _created++);
        var copy = new TaskItem
        {
            ListId = from.ListId, StatusId = statusId, ParentTaskId = parentId,
            Title = from.Title, Description = from.Description, DescriptionText = from.DescriptionText, Priority = from.Priority,
            StartDate = start, DueDate = due, Position = position,
            CreatedById = from.CreatedById, CreatedAt = created, UpdatedAt = created,
        };
        db.Tasks.Add(copy);
        var assignees = fromAssignees.Where(a => people.Contains(a.UserId)).OrderBy(a => a.CreatedAt).ThenBy(a => a.UserId).ToList();
        db.TaskAssignees.AddRange(assignees.Select((a, i) => new TaskAssignee { TaskId = copy.Id, UserId = a.UserId, CreatedAt = created.AddTicks(10 * i) }));
        db.TaskTags.AddRange(fromTags.Select(t => new TaskTag { TaskId = copy.Id, TagId = t.TagId }));
        return copy;
    }

    /// <summary>
    /// Deletes the next task and gives its rule back, unless someone has changed it, commented on it, attached a file or
    /// added a subtask (or done any of that to its subtasks): then both stay, and this one no longer repeats.
    /// </summary>
    private async Task TakeBackAsync(TaskItem task, Guid nextId)
    {
        var family = db.Tasks.Where(t => t.Id == nextId || t.ParentTaskId == nextId);
        // The next task and whether anyone has touched it, in one query.
        var row = await db.Tasks.Where(t => t.Id == nextId)
            .Select(n => new
            {
                Task = n,
                Touched = family.Any(t => t.UpdatedAt != t.CreatedAt || t.CompletedAt != null
                    || db.Activities.Any(a => a.TaskId == t.Id) || db.Comments.Any(c => c.TaskId == t.Id) || db.Attachments.Any(a => a.TaskId == t.Id)),
            })
            .FirstOrDefaultAsync();
        if (row is null || row.Touched) return;
        var next = row.Task;
        task.Repeat = next.Repeat;
        task.NextRepeatId = null;
        // Its subtasks, assignees and tags go with it (CASCADE).
        db.Tasks.Remove(next);
    }

    private async Task<List<ListStatus>> StatusesAsync(Guid listId)
    {
        if (!_statuses.TryGetValue(listId, out var statuses))
            _statuses[listId] = statuses = await db.Statuses.AsNoTracking().Where(s => s.ListId == listId)
                .OrderBy(s => s.Position).ThenBy(s => s.Id).ToListAsync();
        return statuses;
    }

    private async Task<double> BottomAsync(Guid listId, Guid statusId)
    {
        var key = (listId, statusId);
        if (!_bottoms.TryGetValue(key, out var max))
            max = await db.Tasks.Where(t => t.ListId == listId && t.StatusId == statusId && t.ParentTaskId == null)
                .MaxAsync(t => (double?)t.Position) ?? 0;
        _bottoms[key] = max + Positions.Step;
        return max + Positions.Step;
    }

    /// <summary>The people among <paramref name="userIds"/> who may be assigned in the list: members who can see its space.</summary>
    private async Task<HashSet<Guid>> AssignableAsync(Guid listId, IEnumerable<Guid> userIds)
    {
        var ids = userIds.Distinct().ToList();
        if (ids.Count == 0) return [];
        var rows = await db.Lists.Where(l => l.Id == listId)
            .SelectMany(l => db.WorkspaceMembers
                .Where(m => m.WorkspaceId == l.Space!.WorkspaceId && ids.Contains(m.UserId))
                .Where(m => !l.Space!.IsPrivate || db.SpaceMembers.Any(x => x.SpaceId == l.SpaceId && x.UserId == m.UserId))
                .Select(m => m.UserId))
            .ToListAsync();
        return rows.ToHashSet();
    }

    private static DateOnly? Shift(DateOnly? date, int days)
    {
        if (date is not { } d) return null;
        try
        {
            return d.AddDays(days);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }
}
