using Microsoft.EntityFrameworkCore;
using Taskloom.Api.Notifications;
using Taskloom.Application;
using Taskloom.Domain;
using Taskloom.Infrastructure;

namespace Taskloom.Api.Jobs;

/// <summary>
/// Sends the emails that Inbox rows are waiting for (Features 2): assignments and mentions once their short wait is
/// over, and the morning reminders. A row is skipped, not sent, when its reader has already read it, turned that kind
/// of email off, lost their account or their view of the task, or (an assignment) is no longer assigned. Everything
/// waiting for one person goes out together: one email about assignments and mentions, one reminder. A send that
/// fails is tried again later, a little longer each time, and given up on after a day.
/// </summary>
public sealed class EmailJob(TaskloomDbContext db, IEmailSender sender, NotificationOptions options, TimeProvider time, ILogger<EmailJob> log) : IJob
{
    /// <summary>Rows taken in one run; the rest wait for the next.</summary>
    public const int Batch = 500;
    /// <summary>Tasks listed in one email; the rest are counted ("and 12 more").</summary>
    public const int MaxItems = 25;
    /// <summary>A run stops after this many sends in a row fail: the mail server is down, not the message.</summary>
    public const int MaxFailuresInARow = 3;
    public static readonly TimeSpan GiveUpAfter = TimeSpan.FromHours(24);

    public string Name => "emails";
    /// <summary>On every pass of the timer (Jobs:PollSeconds), so an email leaves within half a minute of being due.</summary>
    public TimeSpan Every => TimeSpan.Zero;

    public async Task<string?> RunAsync(CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var due = await (
            from n in db.Notifications
            where n.Email == NotificationEmail.Pending && (n.EmailAfter == null || n.EmailAfter <= now)
            join u in db.Users on n.UserId equals u.Id
            join t in db.Tasks on n.TaskId equals t.Id
            join l in db.Lists on t.ListId equals l.Id
            join s in db.Spaces on l.SpaceId equals s.Id
            orderby n.CreatedAt, n.Id
            select new Row
            {
                Notification = n,
                To = u.Email,
                ToName = u.DisplayName,
                Disabled = u.DisabledAt != null,
                EmailMentions = u.EmailMentions,
                EmailReminders = u.EmailReminders,
                TaskTitle = t.Title,
                DueDate = t.DueDate,
                Done = t.CompletedAt != null,
                Assigned = t.Assignees.Any(a => a.UserId == n.UserId),
                ListName = l.Name,
                ActorName = n.Actor!.DisplayName,
                Comment = db.Comments.Where(c => c.Id == n.CommentId).Select(c => c.Body).FirstOrDefault(),
                // The same question as SpaceVisibility.VisibleTo, asked of the reader.
                CanSee = db.WorkspaceMembers.Any(m => m.WorkspaceId == s.WorkspaceId && m.UserId == n.UserId)
                         && (!s.IsPrivate || db.SpaceMembers.Any(m => m.SpaceId == s.Id && m.UserId == n.UserId)),
            }).Take(Batch).ToListAsync(ct);
        if (due.Count == 0) return null;

        // The names behind the mention tokens of the comments about to be quoted.
        var commentIds = due.Where(r => r.Notification.Kind == NotificationKind.Mentioned && r.Notification.CommentId != null)
            .Select(r => r.Notification.CommentId!.Value).Distinct().ToList();
        var names = commentIds.Count == 0
            ? []
            : (await db.CommentMentions.Where(m => commentIds.Contains(m.CommentId))
                .Select(m => new { m.UserId, m.User!.DisplayName }).Distinct().ToListAsync(ct))
            .ToDictionary(m => m.UserId, m => m.DisplayName);

        int sent = 0, emails = 0, skipped = 0, failed = 0, failuresInARow = 0;
        foreach (var person in due.GroupBy(r => r.Notification.UserId))
        {
            var activity = new List<Row>();
            var reminders = new List<Row>();
            foreach (var row in person)
            {
                var n = row.Notification;
                var reminder = n.Kind is NotificationKind.DueToday or NotificationKind.Overdue;
                var wanted = reminder ? row.EmailReminders && !row.Done : row.EmailMentions;
                if (n.Kind == NotificationKind.Assigned && !row.Assigned) wanted = false;
                if (n.ReadAt is not null || row.Disabled || !row.CanSee || !wanted)
                {
                    n.Email = NotificationEmail.Skipped;
                    n.EmailAfter = null;
                    skipped++;
                }
                else (reminder ? reminders : activity).Add(row);
            }

            foreach (var (rows, isReminder) in new[] { (activity, false), (reminders, true) })
            {
                if (rows.Count == 0) continue;
                if (failuresInARow >= MaxFailuresInARow) continue; // left Pending for the next run
                var items = rows.Take(MaxItems).Select(r => Item(r, names)).ToList();
                var more = rows.Count - items.Count;
                var first = rows[0];
                var message = isReminder
                    ? EmailTemplates.Reminder(first.To, first.ToName, items, options, more)
                    : EmailTemplates.Activity(first.To, first.ToName, items, options, more);
                try
                {
                    await sender.SendAsync(message, ct);
                    foreach (var r in rows)
                    {
                        r.Notification.Email = NotificationEmail.Sent;
                        r.Notification.EmailAfter = null;
                    }
                    sent += rows.Count;
                    emails++;
                    failuresInARow = 0;
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    failuresInARow++;
                    failed += rows.Count;
                    // The address stays out of the log; the person's id finds them.
                    log.LogWarning(e, "An email to user {UserId} could not be sent ({Count} notifications); it is tried again later.", person.Key, rows.Count);
                    foreach (var r in rows) Retry(r.Notification, now);
                }
            }
            // Saved person by person, so a crash can only ever repeat one person's email.
            await db.SaveChangesAsync(ct);
        }

        var parts = new List<string>();
        if (emails > 0) parts.Add($"sent {emails} email{(emails == 1 ? "" : "s")} for {sent} notification{(sent == 1 ? "" : "s")}");
        if (skipped > 0) parts.Add($"skipped {skipped}");
        if (failed > 0) parts.Add($"{failed} could not be sent");
        return parts.Count == 0 ? null : string.Join(", ", parts);
    }

    /// <summary>Waits 2, 4, 8 ... minutes (an hour at most) between tries, and gives up a day after the row was written.</summary>
    private static void Retry(Notification n, DateTimeOffset now)
    {
        n.EmailAttempts++;
        if (now - n.CreatedAt >= GiveUpAfter)
        {
            n.Email = NotificationEmail.Failed;
            n.EmailAfter = null;
            return;
        }
        var minutes = Math.Min(60, Math.Pow(2, Math.Min(n.EmailAttempts, 6)));
        n.EmailAfter = now + TimeSpan.FromMinutes(minutes);
    }

    private static EmailItem Item(Row r, Dictionary<Guid, string> names) => new(
        r.Notification.Kind, r.Notification.TaskId, r.TaskTitle, r.ListName, r.DueDate, r.ActorName,
        r.Notification.Kind == NotificationKind.Mentioned && r.Comment is { Length: > 0 } body ? Mentions.ToPlainText(body, names) : null);

    private sealed class Row
    {
        public required Notification Notification { get; init; }
        public required string To { get; init; }
        public required string ToName { get; init; }
        public bool Disabled { get; init; }
        public bool EmailMentions { get; init; }
        public bool EmailReminders { get; init; }
        public required string TaskTitle { get; init; }
        public DateOnly? DueDate { get; init; }
        public bool Done { get; init; }
        public bool Assigned { get; init; }
        public required string ListName { get; init; }
        public string? ActorName { get; init; }
        public string? Comment { get; init; }
        public bool CanSee { get; init; }
    }
}
