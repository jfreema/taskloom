using Microsoft.EntityFrameworkCore;
using Taskloom.Domain;
using Taskloom.Infrastructure;

namespace Taskloom.Api;

/// <summary>
/// Records task history (spec 5.6): one row per changed field, added to the context so it is written in the same
/// SaveChanges as the change. Values are display values copied at the time (<see cref="ActivityValues"/>). Callers
/// pass names from dictionaries they loaded once per request, never per item.
/// </summary>
public sealed class ActivityRecorder(TaskloomDbContext db, Guid actorId, DateTimeOffset now)
{
    /// <summary>A description edit within this time of the actor's previous one updates that row instead of adding one.</summary>
    public static readonly TimeSpan DescriptionMergeWindow = TimeSpan.FromMinutes(10);

    private int _count;
    private readonly HashSet<Guid> _recorded = [];

    public Guid ActorId => actorId;

    /// <summary>
    /// Rows of one request are a microsecond apart (the database keeps microseconds), so the feed shows them in the
    /// order they were recorded.
    /// </summary>
    private DateTimeOffset NextTime() => now.AddTicks(10 * _count++);

    /// <summary>Adds a row unless the value didn't change (both sides compared after truncation to 500 characters).</summary>
    public void Record(Guid taskId, TaskActivityKind kind, string? from, string? to)
    {
        from = ActivityValues.Clip(from);
        to = ActivityValues.Clip(to);
        if (string.Equals(from, to, StringComparison.Ordinal)) return;
        _recorded.Add(taskId);
        db.Activities.Add(new TaskActivity
        {
            TaskId = taskId, ActorId = actorId, Kind = kind, FromValue = from, ToValue = to, CreatedAt = NextTime(),
        });
    }

    /// <summary>
    /// DescriptionChanged has no values. If the task's latest row is the same actor's DescriptionChanged from less than
    /// ten minutes ago, that row moves to now instead of a new one being added, so typing doesn't flood the feed. A row
    /// recorded earlier in this request (a rename sent with the description) is the latest one, so it ends the run.
    /// </summary>
    public async Task DescriptionChangedAsync(Guid taskId, CancellationToken ct = default)
    {
        if (_recorded.Add(taskId))
        {
            var latest = await db.Activities.Where(a => a.TaskId == taskId)
                .OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Id).FirstOrDefaultAsync(ct);
            if (latest is { Kind: TaskActivityKind.DescriptionChanged } && latest.ActorId == actorId && now - latest.CreatedAt < DescriptionMergeWindow)
            {
                latest.CreatedAt = NextTime();
                return;
            }
        }
        db.Activities.Add(new TaskActivity
        {
            TaskId = taskId, ActorId = actorId, Kind = TaskActivityKind.DescriptionChanged, CreatedAt = NextTime(),
        });
    }
}
