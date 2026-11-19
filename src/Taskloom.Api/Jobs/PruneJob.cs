using Microsoft.EntityFrameworkCore;
using Taskloom.Api.Notifications;
using Taskloom.Infrastructure;

namespace Taskloom.Api.Jobs;

/// <summary>Deletes Inbox rows older than Notifications:KeepDays (90), read or not, so the table stays small.</summary>
public sealed class PruneJob(TaskloomDbContext db, NotificationOptions options, TimeProvider time) : IJob
{
    public string Name => "prune";
    public TimeSpan Every => TimeSpan.FromHours(24);

    public async Task<string?> RunAsync(CancellationToken ct)
    {
        var before = time.GetUtcNow().AddDays(-options.KeepDays);
        var deleted = await db.Notifications.Where(n => n.CreatedAt < before).ExecuteDeleteAsync(ct);
        return deleted == 0 ? null : $"deleted {deleted} notification{(deleted == 1 ? "" : "s")} older than {options.KeepDays} days";
    }
}
