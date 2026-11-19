using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Taskloom.Api.Notifications;
using Taskloom.Domain;
using Taskloom.Infrastructure;

namespace Taskloom.Api.Jobs;

/// <summary>
/// The morning reminders (Features 2). Once a day for each person, from Reminders:Hour of their own time zone, it
/// writes an Inbox row for each open task of theirs that is due today, and one for each that was due in the last week
/// and is still open. "Theirs" is Home's meaning: assigned to them, or created by them and assigned to nobody; only
/// tasks they can see. A task is reminded once as due and once as overdue for a given due date, so nobody is nagged
/// daily; moving the date starts again. The rows are queued for email, which <see cref="EmailJob"/> sends as one message.
/// </summary>
public sealed class ReminderJob(TaskloomDbContext db, NotificationOptions options, TimeProvider time) : IJob
{
    /// <summary>How far back a task still counts as newly overdue: it covers a long weekend with the app asleep.</summary>
    public const int OverdueDays = 7;

    public string Name => "reminders";
    public TimeSpan Every => TimeSpan.FromMinutes(5);

    public async Task<string?> RunAsync(CancellationToken ct)
    {
        var moment = time.GetUtcNow();
        var now = moment.AddTicks(-(moment.Ticks % TimeSpan.TicksPerMicrosecond));
        // Nowhere is it later than tomorrow's date in UTC, so anyone already reminded on that date can't be due.
        var latest = DateOnly.FromDateTime(now.UtcDateTime).AddDays(1);
        var people = await db.Users
            .Where(u => u.DisabledAt == null && (u.RemindedOn == null || u.RemindedOn < latest) && db.WorkspaceMembers.Any(m => m.UserId == u.Id))
            .Select(u => new { u.Id, u.TimeZone, u.RemindedOn })
            .ToListAsync(ct);
        var due = people
            .Select(p => (p.Id, p.RemindedOn, Local: TimeZones.LocalNow(p.TimeZone, now)))
            .Where(p => p.Local.Hour >= options.ReminderHour && (p.RemindedOn is null || p.RemindedOn < p.Local.Date))
            .ToList();
        if (due.Count == 0) return null;

        int reminded = 0, rows = 0;
        foreach (var (userId, _, local) in due)
        {
            ct.ThrowIfCancellationRequested();
            var today = local.Date;
            var from = today.AddDays(-OverdueDays);
            var user = await db.Users.FirstAsync(u => u.Id == userId, ct);
            var tasks = await (
                from t in db.Tasks
                where t.CompletedAt == null && t.DueDate != null && t.DueDate >= @from && t.DueDate <= today
                      && (t.Assignees.Any(a => a.UserId == userId) || (t.CreatedById == userId && !t.Assignees.Any()))
                join l in db.Lists on t.ListId equals l.Id
                join s in db.Spaces on l.SpaceId equals s.Id
                // The same question as SpaceVisibility.VisibleTo.
                where db.WorkspaceMembers.Any(m => m.WorkspaceId == s.WorkspaceId && m.UserId == userId)
                      && (!s.IsPrivate || db.SpaceMembers.Any(m => m.SpaceId == s.Id && m.UserId == userId))
                orderby t.DueDate, t.CreatedAt
                select new
                {
                    t.Id,
                    DueDate = t.DueDate!.Value,
                    Reminded = db.Notifications
                        .Where(n => n.UserId == userId && n.TaskId == t.Id && (n.Kind == NotificationKind.DueToday || n.Kind == NotificationKind.Overdue))
                        .Select(n => new { n.Kind, n.ToValue }).ToList(),
                }).ToListAsync(ct);

            var count = 0;
            foreach (var task in tasks)
            {
                var kind = task.DueDate == today ? NotificationKind.DueToday : NotificationKind.Overdue;
                var date = task.DueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                if (task.Reminded.Any(r => r.Kind == kind && r.ToValue == date)) continue;
                db.Notifications.Add(new Notification
                {
                    UserId = userId, TaskId = task.Id, ActorId = null, Kind = kind, ToValue = date,
                    CreatedAt = now.AddTicks(10 * count++),
                    Email = NotificationEmail.Pending, EmailAfter = now,
                });
            }
            user.RemindedOn = today;
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
            reminded++;
            rows += count;
        }
        return rows == 0 ? null : $"{rows} reminder{(rows == 1 ? "" : "s")} for {reminded} {(reminded == 1 ? "person" : "people")}";
    }
}
