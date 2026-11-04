using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Taskloom.Application;
using Taskloom.Domain;
using Taskloom.Infrastructure;

namespace Taskloom.Api;

/// <summary>A value that may be absent, so "absent" and "null" differ in PATCH bodies.</summary>
public readonly record struct Optional<T>(bool Has, T Value)
{
    public static implicit operator Optional<T>(T value) => new(true, value);
}

public enum TaskChangeKind
{
    /// <summary>PATCH /api/tasks/{id} (<c>TaskPatch</c>): title and description allowed; add/remove sets ignored.</summary>
    Patch,
    /// <summary>One item of POST /api/tasks/bulk (<c>BulkTaskUpdate</c>): title and description ignored; add/remove sets allowed.</summary>
    Bulk,
}

/// <summary>
/// The fields of one <c>TaskPatch</c> or <c>BulkTaskUpdate</c> (spec 5.3), read from JSON: only present properties
/// change, <c>null</c> clears a nullable field, unknown properties are ignored. Wrong types are a 400.
/// </summary>
public sealed class TaskChange
{
    public Optional<string> Title { get; private set; }
    /// <summary>The raw HTML as sent ("" for null); sanitized when applied.</summary>
    public Optional<string> Description { get; private set; }
    public Optional<TaskPriority> Priority { get; private set; }
    public Optional<DateOnly?> StartDate { get; private set; }
    public Optional<DateOnly?> DueDate { get; private set; }
    public Guid? StatusId { get; private set; }
    public Guid? ListId { get; private set; }
    public double? Position { get; private set; }
    public List<Guid>? AssigneeIds { get; private set; }
    public List<Guid>? AddAssigneeIds { get; private set; }
    public List<Guid>? RemoveAssigneeIds { get; private set; }
    public List<Guid>? TagIds { get; private set; }
    public List<Guid>? AddTagIds { get; private set; }
    public List<Guid>? RemoveTagIds { get; private set; }

    public const string StatusMessage = "Choose a status of this list.";
    public const string ListMessage = "Choose a list in this workspace.";
    private const string AssigneeIdsMessage = "assigneeIds must be a list of user ids.";
    private const string TagIdsMessage = "tagIds must be a list of tag ids.";

    public static TaskChange Parse(JsonElement body, TaskChangeKind kind)
    {
        if (body.ValueKind != JsonValueKind.Object)
            throw ProblemException.BadRequest(kind == TaskChangeKind.Patch ? "Send the fields to change as a JSON object." : "Each update must be a JSON object.");
        var c = new TaskChange();
        var patch = kind == TaskChangeKind.Patch;
        foreach (var prop in body.EnumerateObject())
        {
            var v = prop.Value;
            var isNull = v.ValueKind == JsonValueKind.Null;
            switch (prop.Name.ToLowerInvariant())
            {
                case "title" when patch:
                    c.Title = Text.Require(v.ValueKind == JsonValueKind.String ? v.GetString() : null,
                        Limits.TaskTitle, "Enter a task title.", "Task titles can be up to 500 characters.");
                    break;
                case "description" when patch:
                    if (!isNull && v.ValueKind != JsonValueKind.String) throw ProblemException.BadRequest("The description must be text.");
                    var html = isNull ? "" : v.GetString() ?? "";
                    if (html.Length > Limits.DescriptionHtml) throw ProblemException.BadRequest("Descriptions can be up to 200,000 characters.");
                    c.Description = html;
                    break;
                case "priority":
                    if (isNull) c.Priority = TaskPriority.None;
                    else if (v.ValueKind == JsonValueKind.String && Enums.TryParseName<TaskPriority>(v.GetString(), out var p)) c.Priority = p;
                    else throw ProblemException.BadRequest("Choose Urgent, High, Normal, Low or None as the priority.");
                    break;
                case "startdate":
                    c.StartDate = Json.ReadDate(v);
                    break;
                case "duedate":
                    c.DueDate = Json.ReadDate(v);
                    break;
                case "statusid":
                    c.StatusId = Json.ReadGuid(v, StatusMessage);
                    break;
                case "listid":
                    c.ListId = Json.ReadGuid(v, ListMessage);
                    break;
                case "position":
                    c.Position = Json.ReadPosition(v);
                    break;
                case "assigneeids":
                    c.AssigneeIds = Json.ReadIds(v, AssigneeIdsMessage);
                    break;
                case "addassigneeids" when !patch:
                    c.AddAssigneeIds = Json.ReadIds(v, "addAssigneeIds must be a list of user ids.");
                    break;
                case "removeassigneeids" when !patch:
                    c.RemoveAssigneeIds = Json.ReadIds(v, "removeAssigneeIds must be a list of user ids.");
                    break;
                case "tagids":
                    c.TagIds = Json.ReadIds(v, TagIdsMessage);
                    break;
                case "addtagids" when !patch:
                    c.AddTagIds = Json.ReadIds(v, "addTagIds must be a list of tag ids.");
                    break;
                case "removetagids" when !patch:
                    c.RemoveTagIds = Json.ReadIds(v, "removeTagIds must be a list of tag ids.");
                    break;
            }
        }
        if (c.AssigneeIds is not null && (c.AddAssigneeIds is not null || c.RemoveAssigneeIds is not null))
            throw ProblemException.BadRequest("Send assigneeIds, or addAssigneeIds and removeAssigneeIds, not both.");
        if (c.TagIds is not null && (c.AddTagIds is not null || c.RemoveTagIds is not null))
            throw ProblemException.BadRequest("Send tagIds, or addTagIds and removeTagIds, not both.");
        return c;
    }
}

/// <summary>
/// Applies <see cref="TaskChange"/>s with the rules of spec 5.5 "Patch task", for PATCH /api/tasks/{id} and
/// POST /api/tasks/bulk alike. Everything it needs (the workspace's lists, statuses, members and tags, the subtasks
/// of tasks moving list, and the edge positions of the statuses tasks may move into) is loaded once in
/// <see cref="CreateAsync"/>, so applying makes no per-item queries. It only changes tracked entities and records
/// activity; the caller saves, so a 400 from any item leaves nothing changed.
/// </summary>
public sealed class TaskPatcher
{
    private readonly TaskloomDbContext _db;
    private readonly ActivityRecorder _activity;
    private readonly DateTimeOffset _now;
    private readonly Dictionary<Guid, TaskList> _lists;
    private readonly Dictionary<Guid, ListStatus> _statuses;
    private readonly Dictionary<Guid, List<ListStatus>> _statusesByList;
    private readonly Dictionary<Guid, string> _memberNames;
    private readonly Dictionary<Guid, string> _formerMemberNames;
    private readonly Dictionary<Guid, Tag> _tags;
    private readonly ILookup<Guid, TaskItem> _subtasks;
    private readonly PositionBook _positions;
    private int _assigned;

    private TaskPatcher(TaskloomDbContext db, ActivityRecorder activity, DateTimeOffset now, Dictionary<Guid, TaskList> lists,
        List<ListStatus> statuses, Dictionary<Guid, string> memberNames, Dictionary<Guid, string> formerMemberNames,
        Dictionary<Guid, Tag> tags, ILookup<Guid, TaskItem> subtasks, PositionBook positions)
    {
        _db = db;
        _activity = activity;
        _now = now;
        _lists = lists;
        _statuses = statuses.ToDictionary(s => s.Id);
        _statusesByList = statuses.GroupBy(s => s.ListId)
            .ToDictionary(g => g.Key, g => g.OrderBy(s => s.Position).ThenBy(s => s.Id).ToList());
        _memberNames = memberNames;
        _formerMemberNames = formerMemberNames;
        _tags = tags;
        _subtasks = subtasks;
        _positions = positions;
    }

    public const string AssigneesMustBeMembers = "Everyone assigned must be a member of this workspace.";
    public const string TagsMustBelongToWorkspace = "Tags must belong to this workspace.";

    /// <param name="items">The tasks (tracked, with <c>Assignees</c> and <c>Tags</c> loaded) and their changes.</param>
    public static async Task<TaskPatcher> CreateAsync(TaskloomDbContext db, Guid workspaceId, ActivityRecorder activity,
        DateTimeOffset now, IReadOnlyList<(TaskItem Task, TaskChange Change)> items)
    {
        var lists = await db.Lists.AsNoTracking().Where(l => l.Space!.WorkspaceId == workspaceId).ToDictionaryAsync(l => l.Id);
        var statuses = await db.Statuses.AsNoTracking()
            .Where(s => db.Lists.Any(l => l.Id == s.ListId && l.Space!.WorkspaceId == workspaceId)).ToListAsync();
        var memberNames = await db.WorkspaceMembers.Where(m => m.WorkspaceId == workspaceId)
            .Select(m => new { m.UserId, m.User!.DisplayName }).ToDictionaryAsync(m => m.UserId, m => m.DisplayName);
        var tags = await db.Tags.AsNoTracking().Where(t => t.WorkspaceId == workspaceId).ToDictionaryAsync(t => t.Id);

        // Assignees who have left the workspace still need a name when they are removed.
        var formerIds = items.SelectMany(i => i.Task.Assignees).Select(a => a.UserId).Where(u => !memberNames.ContainsKey(u)).Distinct().ToList();
        var formerNames = formerIds.Count == 0 ? []
            : await db.Users.Where(u => formerIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName);

        // Subtasks move with a top-level task that changes list.
        var moving = items.Where(i => i.Change.ListId is { } l && l != i.Task.ListId && i.Task.ParentTaskId is null)
            .Select(i => i.Task.Id).ToList();
        var subtasks = moving.Count == 0 ? []
            : await db.Tasks.Where(t => t.ParentTaskId != null && moving.Contains(t.ParentTaskId.Value)).ToListAsync();

        // The statuses a task may move into: the ones named, and every status of a list a task moves to.
        var targets = new HashSet<Guid>();
        foreach (var (_, change) in items)
        {
            if (change.StatusId is { } s) targets.Add(s);
            if (change.ListId is { } l) targets.UnionWith(statuses.Where(x => x.ListId == l).Select(x => x.Id));
        }
        var positions = await PositionBook.LoadAsync(db, targets);

        return new TaskPatcher(db, activity, now, lists, statuses, memberNames, formerNames, tags,
            subtasks.ToLookup(t => t.ParentTaskId!.Value), positions);
    }

    /// <summary>Applies one change to a tracked task. Throws a 400 <see cref="ProblemException"/> when a rule fails.</summary>
    public async Task ApplyAsync(TaskItem task, TaskChange change)
    {
        var changed = false;
        var oldStatus = _statuses[task.StatusId];

        // ---- list and status (resolved first: a statusId must belong to listId's list when both are sent) ----
        TaskList? targetList = null;
        if (change.ListId is { } listId && listId != task.ListId)
        {
            if (task.ParentTaskId is not null) throw ProblemException.BadRequest("Subtasks move with their parent.");
            if (!_lists.TryGetValue(listId, out targetList)) throw ProblemException.BadRequest(TaskChange.ListMessage);
        }
        ListStatus? newStatus = null;
        if (change.StatusId is { } statusId)
        {
            if (!_statuses.TryGetValue(statusId, out newStatus) || newStatus.ListId != (targetList?.Id ?? task.ListId))
                throw ProblemException.BadRequest(TaskChange.StatusMessage);
        }
        else if (targetList is not null)
        {
            newStatus = MapStatus(oldStatus, targetList.Id);
        }

        if (targetList is not null)
        {
            _activity.Record(task.Id, TaskActivityKind.MovedList, _lists[task.ListId].Name, targetList.Name);
            task.ListId = targetList.Id;
            // Subtasks follow, each status mapped the same way; their positions among their siblings stay.
            foreach (var sub in _subtasks[task.Id])
            {
                var from = _statuses[sub.StatusId];
                var to = MapStatus(from, targetList.Id);
                sub.ListId = targetList.Id;
                SetStatus(sub, from, to, _now);
                sub.UpdatedAt = _now;
            }
            changed = true;
        }

        if (newStatus is not null && newStatus.Id != task.StatusId)
        {
            _activity.Record(task.Id, TaskActivityKind.StatusChanged, oldStatus.Name, newStatus.Name);
            SetStatus(task, oldStatus, newStatus, _now);
            // A task moved to another list goes to the bottom of its status there; a re-statused one to the top.
            if (change.Position is null)
                task.Position = targetList is not null
                    ? _positions.Bottom(task.ListId, task.StatusId, task.ParentTaskId)
                    : _positions.Top(task.ListId, task.StatusId, task.ParentTaskId);
            changed = true;
        }

        // Applied after the status rule; sent by undo to put the task back exactly where it was.
        if (change.Position is { } position && position != task.Position)
        {
            task.Position = position;
            _positions.Note(task.ListId, task.StatusId, task.ParentTaskId, position);
            changed = true;
        }

        // ---- plain fields ----
        if (change.Title is { Has: true, Value: var title } && title != task.Title)
        {
            _activity.Record(task.Id, TaskActivityKind.Renamed, task.Title, title);
            task.Title = title;
            changed = true;
        }
        if (change.Description.Has)
        {
            var html = RichText.Sanitize(change.Description.Value);
            if (html != task.Description)
            {
                task.Description = html;
                task.DescriptionText = RichText.ToPlainText(html);
                await _activity.DescriptionChangedAsync(task.Id);
                changed = true;
            }
        }
        if (change.Priority is { Has: true, Value: var priority } && priority != task.Priority)
        {
            _activity.Record(task.Id, TaskActivityKind.PriorityChanged, task.Priority.ToString(), priority.ToString());
            task.Priority = priority;
            changed = true;
        }
        if (change.StartDate is { Has: true, Value: var start } && start != task.StartDate)
        {
            _activity.Record(task.Id, TaskActivityKind.StartDateChanged, ActivityValues.Date(task.StartDate), ActivityValues.Date(start));
            task.StartDate = start;
            changed = true;
        }
        if (change.DueDate is { Has: true, Value: var due } && due != task.DueDate)
        {
            _activity.Record(task.Id, TaskActivityKind.DueDateChanged, ActivityValues.Date(task.DueDate), ActivityValues.Date(due));
            task.DueDate = due;
            changed = true;
        }

        changed |= ApplyAssignees(task, change);
        changed |= ApplyTags(task, change);

        if (changed) task.UpdatedAt = _now;
    }

    private bool ApplyAssignees(TaskItem task, TaskChange change)
    {
        var current = task.Assignees.Select(a => a.UserId).ToList();
        List<Guid> added, removed;
        if (change.AssigneeIds is { } wanted)
        {
            added = wanted.Where(u => !current.Contains(u)).ToList();
            removed = current.Where(u => !wanted.Contains(u)).ToList();
        }
        else
        {
            // Ids to remove must be people of this workspace: members, or assignees of these tasks who have since left.
            // Ones the task doesn't carry are ignored.
            var remove = change.RemoveAssigneeIds ?? [];
            if (remove.Any(u => !_memberNames.ContainsKey(u) && !_formerMemberNames.ContainsKey(u)))
                throw ProblemException.BadRequest(AssigneesMustBeMembers);
            added = (change.AddAssigneeIds ?? []).Where(u => !current.Contains(u)).ToList();
            removed = remove.Where(current.Contains).ToList();
        }
        // Only people being added must be members; an assignee who has left the workspace can stay or go.
        if (added.Any(u => !_memberNames.ContainsKey(u))) throw ProblemException.BadRequest(AssigneesMustBeMembers);

        foreach (var userId in added)
        {
            // A microsecond apart, so people added together keep the order they were sent in.
            task.Assignees.Add(new TaskAssignee { TaskId = task.Id, UserId = userId, CreatedAt = _now.AddTicks(10 * _assigned++) });
            _activity.Record(task.Id, TaskActivityKind.AssigneeAdded, null, _memberNames[userId]);
        }
        foreach (var userId in removed)
        {
            var row = task.Assignees.First(a => a.UserId == userId);
            task.Assignees.Remove(row);
            _db.TaskAssignees.Remove(row);
            _activity.Record(task.Id, TaskActivityKind.AssigneeRemoved, NameOf(userId), null);
        }
        return added.Count + removed.Count > 0;
    }

    private bool ApplyTags(TaskItem task, TaskChange change)
    {
        var current = task.Tags.Select(t => t.TagId).ToList();
        List<Guid> added, removed;
        if (change.TagIds is { } wanted)
        {
            if (wanted.Any(t => !_tags.ContainsKey(t))) throw ProblemException.BadRequest(TagsMustBelongToWorkspace);
            added = wanted.Where(t => !current.Contains(t)).ToList();
            removed = current.Where(t => !wanted.Contains(t)).ToList();
        }
        else
        {
            var add = change.AddTagIds ?? [];
            var remove = change.RemoveTagIds ?? [];
            if (add.Concat(remove).Any(t => !_tags.ContainsKey(t))) throw ProblemException.BadRequest(TagsMustBelongToWorkspace);
            added = add.Where(t => !current.Contains(t)).ToList();
            removed = remove.Where(current.Contains).ToList();
        }

        foreach (var tagId in added)
        {
            task.Tags.Add(new TaskTag { TaskId = task.Id, TagId = tagId });
            _activity.Record(task.Id, TaskActivityKind.TagAdded, null, _tags[tagId].Name);
        }
        foreach (var tagId in removed)
        {
            var row = task.Tags.First(t => t.TagId == tagId);
            task.Tags.Remove(row);
            _db.TaskTags.Remove(row);
            _activity.Record(task.Id, TaskActivityKind.TagRemoved, _tags.TryGetValue(tagId, out var tag) ? tag.Name : null, null);
        }
        return added.Count + removed.Count > 0;
    }

    private string NameOf(Guid userId) =>
        _memberNames.TryGetValue(userId, out var name) ? name
        : _formerMemberNames.TryGetValue(userId, out var former) ? former
        : "Former member";

    /// <summary>
    /// The status a task takes in another list (spec 5.5): the same name ignoring case, else the first status of the
    /// same category by position, else the first status.
    /// </summary>
    private ListStatus MapStatus(ListStatus from, Guid listId)
    {
        if (!_statusesByList.TryGetValue(listId, out var statuses) || statuses.Count == 0)
            throw ProblemException.BadRequest("That list has no statuses.");
        return statuses.FirstOrDefault(s => string.Equals(s.Name, from.Name, StringComparison.OrdinalIgnoreCase))
               ?? statuses.FirstOrDefault(s => s.Category == from.Category)
               ?? statuses[0];
    }

    /// <summary>
    /// Moves a task to a status: CompletedAt is set when it enters Done, kept while it stays in Done, and cleared when
    /// the new status isn't Done.
    /// </summary>
    public static void SetStatus(TaskItem task, ListStatus from, ListStatus to, DateTimeOffset now)
    {
        task.StatusId = to.Id;
        if (to.Category != StatusCategory.Done) task.CompletedAt = null;
        else if (from.Category != StatusCategory.Done || task.CompletedAt is null) task.CompletedAt = now;
    }
}

/// <summary>
/// The lowest and highest positions of each group of siblings (same list, status and parent) for the statuses a
/// request may move tasks into, loaded with one query and kept current as tasks are placed.
/// </summary>
public sealed class PositionBook
{
    private readonly HashSet<Guid> _statuses;
    private readonly Dictionary<(Guid List, Guid Status, Guid? Parent), (double Min, double Max)> _groups;

    private PositionBook(HashSet<Guid> statuses, Dictionary<(Guid, Guid, Guid?), (double, double)> groups)
    {
        _statuses = statuses;
        _groups = groups;
    }

    public static async Task<PositionBook> LoadAsync(TaskloomDbContext db, HashSet<Guid> statusIds)
    {
        var groups = new Dictionary<(Guid, Guid, Guid?), (double, double)>();
        if (statusIds.Count > 0)
        {
            var ids = statusIds.ToList();
            var rows = await db.Tasks.Where(t => ids.Contains(t.StatusId))
                .GroupBy(t => new { t.ListId, t.StatusId, t.ParentTaskId })
                .Select(g => new { g.Key.ListId, g.Key.StatusId, g.Key.ParentTaskId, Min = g.Min(t => t.Position), Max = g.Max(t => t.Position) })
                .ToListAsync();
            foreach (var r in rows) groups[(r.ListId, r.StatusId, r.ParentTaskId)] = (r.Min, r.Max);
        }
        return new PositionBook(statusIds, groups);
    }

    /// <summary>Above every sibling: the lowest position − 1024, or 1024 when there are none.</summary>
    public double Top(Guid listId, Guid statusId, Guid? parentId)
    {
        var key = Key(listId, statusId, parentId);
        var position = _groups.TryGetValue(key, out var g) ? g.Min - Positions.Step : Positions.Step;
        Note(key, position);
        return position;
    }

    /// <summary>Below every sibling: the highest position + 1024, or 1024 when there are none.</summary>
    public double Bottom(Guid listId, Guid statusId, Guid? parentId)
    {
        var key = Key(listId, statusId, parentId);
        var position = _groups.TryGetValue(key, out var g) ? g.Max + Positions.Step : Positions.Step;
        Note(key, position);
        return position;
    }

    public void Note(Guid listId, Guid statusId, Guid? parentId, double position)
    {
        if (_statuses.Contains(statusId)) Note((listId, statusId, parentId), position);
    }

    private void Note((Guid, Guid, Guid?) key, double position) =>
        _groups[key] = _groups.TryGetValue(key, out var g) ? (Math.Min(g.Min, position), Math.Max(g.Max, position)) : (position, position);

    private (Guid, Guid, Guid?) Key(Guid listId, Guid statusId, Guid? parentId) =>
        _statuses.Contains(statusId)
            ? (listId, statusId, parentId)
            : throw new InvalidOperationException($"Positions of status {statusId} were not loaded.");
}
