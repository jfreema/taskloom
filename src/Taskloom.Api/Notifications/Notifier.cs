using Taskloom.Domain;
using Taskloom.Infrastructure;

namespace Taskloom.Api.Notifications;

/// <summary>Someone who may hear about a task: a member of its workspace who can see it, other than the caller.</summary>
/// <param name="Watches">They created the task or are assigned to it, so its changes go to their Inbox.</param>
public sealed record TaskPerson(Guid UserId, string DisplayName, bool Watches);

/// <summary>Who an access check should also read (<see cref="Access.RequireTaskAsync{T}(Guid, System.Linq.Expressions.Expression{Func{TaskItem, T}}, Audience, WorkspaceRole, CancellationToken)"/>).</summary>
/// <param name="Watchers">The task's creator and assignees.</param>
/// <param name="Named">And these people (the ones a comment mentions), whether or not they watch the task.</param>
public readonly record struct Audience(bool Watchers, IReadOnlyCollection<Guid>? Named = null)
{
    public static readonly Audience OfWatchers = new(true);
}

/// <summary>Starts a <see cref="Notifier"/> for the request (scoped).</summary>
public sealed class NotifierFactory(TaskloomDbContext db, Access access, NotificationOptions options)
{
    public Notifier Start(DateTimeOffset now) => new(db, access.UserId, now, options.EmailDelay);
}

/// <summary>
/// Writes Inbox rows (Features 2) for what one request does, added to the context so they are saved with the change
/// itself, in the same trip to the database. The caller never gets a row about their own doing. A task's changes go to
/// its watchers, handed over with <see cref="Watch"/> by whoever read them (the access check does, in its own query);
/// someone newly assigned gets "assigned to you" and nothing else about that task from the same request. Assignments
/// and mentions are also queued for email (<see cref="Jobs.EmailJob"/>).
/// </summary>
public sealed class Notifier(TaskloomDbContext db, Guid actorId, DateTimeOffset now, TimeSpan emailDelay)
{
    private readonly Dictionary<Guid, HashSet<Guid>> _watchers = [];
    private readonly Dictionary<(Guid User, Guid Task), List<Notification>> _rows = [];
    private readonly HashSet<(Guid User, Guid Task)> _assigned = [];
    private int _count;

    /// <summary>The people who hear about changes to the task: ones who can see it (the caller is left out).</summary>
    public void Watch(Guid taskId, IEnumerable<Guid> userIds)
    {
        if (!_watchers.TryGetValue(taskId, out var set)) _watchers[taskId] = set = [];
        set.UnionWith(userIds.Where(u => u != actorId));
    }

    public void Watch(Guid taskId, IEnumerable<TaskPerson> people) => Watch(taskId, people.Where(p => p.Watches).Select(p => p.UserId));

    /// <summary>A change to a task, told to its watchers (but for <paramref name="except"/>). Nothing when the value didn't change.</summary>
    public void Changed(Guid taskId, NotificationKind kind, string? from, string? to, Guid? commentId = null, IReadOnlyCollection<Guid>? except = null)
    {
        from = ActivityValues.Clip(from);
        to = ActivityValues.Clip(to);
        if (commentId is null && string.Equals(from, to, StringComparison.Ordinal)) return;
        if (!_watchers.TryGetValue(taskId, out var watchers)) return;
        foreach (var userId in watchers)
        {
            if (except is not null && except.Contains(userId)) continue;
            Add(userId, taskId, kind, from, to, commentId, email: false);
        }
    }

    /// <summary>
    /// The caller assigned the task to someone who can see it. It replaces whatever else this request would tell them
    /// about the task: being handed it is the news.
    /// </summary>
    public void Assigned(Guid taskId, Guid userId)
    {
        if (userId == actorId || !_assigned.Add((userId, taskId))) return;
        if (_rows.Remove((userId, taskId), out var earlier)) db.Notifications.RemoveRange(earlier);
        Add(userId, taskId, NotificationKind.Assigned, null, null, null, email: true);
    }

    /// <summary>The caller mentioned someone who can see the task in a comment.</summary>
    public void Mentioned(Guid taskId, Guid userId, Guid commentId, string preview)
    {
        if (userId == actorId) return;
        Add(userId, taskId, NotificationKind.Mentioned, null, ActivityValues.Clip(preview), commentId, email: true);
    }

    private void Add(Guid userId, Guid taskId, NotificationKind kind, string? from, string? to, Guid? commentId, bool email)
    {
        var key = (userId, taskId);
        // Only "assigned to you" for someone this request assigned (comments aside: a mention is still a mention).
        if (commentId is null && _assigned.Contains(key) && kind != NotificationKind.Assigned) return;
        var row = new Notification
        {
            UserId = userId, TaskId = taskId, ActorId = actorId, Kind = kind, FromValue = from, ToValue = to, CommentId = commentId,
            // A microsecond apart, so the Inbox shows a request's rows in the order they happened.
            CreatedAt = now.AddTicks(10 * _count++),
            Email = email ? NotificationEmail.Pending : NotificationEmail.None,
            EmailAfter = email ? now + emailDelay : null,
        };
        db.Notifications.Add(row);
        if (!_rows.TryGetValue(key, out var rows)) _rows[key] = rows = [];
        rows.Add(row);
    }
}
