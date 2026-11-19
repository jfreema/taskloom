using System.Globalization;
using System.Net;
using Microsoft.EntityFrameworkCore;
using Taskloom.Api.Tests.Support;
using Taskloom.Domain;

namespace Taskloom.Api.Tests;

/// <summary>
/// Features 2, due-date reminders: once a morning, in each person's own time zone, an Inbox row and one email for the
/// tasks of theirs that are due today or have just become overdue.
/// </summary>
public class ReminderTests(InboxSite site) : IClassFixture<InboxSite>
{
    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private async Task<(NotificationKind Kind, Guid TaskId, string? To)[]> RemindersAsync(Guid userId) =>
        (await site.NotificationRowsAsync(userId))
        .Where(n => n.Kind is NotificationKind.DueToday or NotificationKind.Overdue)
        .Select(n => (n.Kind, n.TaskId, n.ToValue)).ToArray();

    /// <summary>The reminder emails sent to the address (not the "assigned you" ones).</summary>
    private List<Application.EmailMessage> ReminderMails(string email) =>
        site.Outbox.To(email).Where(m => m.Subject.Contains("due today") || m.Subject.Contains("overdue")).ToList();

    [Fact]
    public async Task The_morning_reminder_covers_tasks_due_today_and_newly_overdue_once_each()
    {
        var user = await site.NewUserAsync();
        var (_, colleague) = await site.JoinAsync(user, WorkspaceRole.Member, "Colleague");
        await user.Client.NotificationSettingsAsync(new { timeZone = "Etc/UTC" });
        var today = site.Clock.AdvanceToHourUtc(12);
        var list = user.List.Id;
        var me = new[] { user.Me.Id };

        var dueToday = await user.Client.CreateTaskAsync(list, new { title = "Due today, mine", dueDate = Iso(today), assigneeIds = me });
        var unassigned = await user.Client.CreateTaskAsync(list, new { title = "Due today, nobody's but I made it", dueDate = Iso(today) });
        var overdue = await user.Client.CreateTaskAsync(list, new { title = "Two days late", dueDate = Iso(today.AddDays(-2)), assigneeIds = me });
        // Not reminded: long overdue, not due yet, done, someone else's, no date.
        await user.Client.CreateTaskAsync(list, new { title = "Ancient", dueDate = Iso(today.AddDays(-8)), assigneeIds = me });
        var tomorrow = await user.Client.CreateTaskAsync(list, new { title = "Tomorrow", dueDate = Iso(today.AddDays(1)), assigneeIds = me });
        await user.Client.CreateTaskAsync(list, new { title = "Done", dueDate = Iso(today), assigneeIds = me, statusId = user.Status("Done").Id });
        await user.Client.CreateTaskAsync(list, new { title = "Theirs", dueDate = Iso(today), assigneeIds = new[] { colleague.UserId } });
        await user.Client.CreateTaskAsync(list, new { title = "Whenever", assigneeIds = me });

        var result = await site.RunJobAsync("reminders");
        Assert.True(result.Ran);
        Assert.Null(result.Error);
        Assert.Equal([(NotificationKind.Overdue, overdue.Id, Iso(today.AddDays(-2))), (NotificationKind.DueToday, dueToday.Id, Iso(today)), (NotificationKind.DueToday, unassigned.Id, Iso(today))],
            await RemindersAsync(user.Me.Id));
        // They show in the Inbox with no actor.
        var inbox = await user.Client.InboxAsync(user.Workspace.Id);
        Assert.Equal(3, inbox.Unread);
        Assert.All(inbox.Items, n => Assert.Null(n.Actor));

        // One email for all three, due today first.
        await site.RunJobAsync("emails");
        var mail = Assert.Single(site.Outbox.To(user.Me.Email));
        Assert.Equal("2 tasks due today, 1 overdue", mail.Subject);
        Assert.Contains("DUE TODAY\n- Due today, mine (Tasks)", mail.Text);
        Assert.Contains("- Due today, nobody's but I made it (Tasks)", mail.Text);
        Assert.Contains($"OVERDUE\n- Two days late (Tasks · was due {today.AddDays(-2).ToString("ddd, MMM d", CultureInfo.InvariantCulture)})", mail.Text);
        Assert.Contains($"{InboxSite.PublicUrl}/t/{overdue.Id}", mail.Text);
        Assert.DoesNotContain("Ancient", mail.Text);
        Assert.DoesNotContain("Theirs", mail.Text);

        // Later the same day: nothing more, even for a task that turns up due today.
        site.Clock.Advance(TimeSpan.FromHours(3));
        await user.Client.CreateTaskAsync(list, new { title = "Added at lunch", dueDate = Iso(today), assigneeIds = me });
        await site.RunJobAsync("reminders");
        await site.RunJobAsync("emails");
        Assert.Equal(3, (await RemindersAsync(user.Me.Id)).Length);
        Assert.Single(site.Outbox.To(user.Me.Email));

        // The next morning: what was due yesterday and is still open is overdue, once; "Tomorrow" is due today.
        // "Two days late" was already said, and a task finished since is left alone.
        await user.Client.PatchTaskAsync(unassigned.Id, new { statusId = user.Status("Done").Id });
        site.Clock.AdvanceToHourUtc(9);
        await site.RunJobAsync("reminders");
        var second = (await RemindersAsync(user.Me.Id)).Skip(3).Select(r => (r.Kind, r.To)).ToArray();
        Assert.Equal(3, second.Length);
        Assert.Equal(2, second.Count(r => r == (NotificationKind.Overdue, Iso(today))));
        Assert.Contains((NotificationKind.DueToday, Iso(today.AddDays(1))), second);
        Assert.Contains((await RemindersAsync(user.Me.Id)).Skip(3), r => r.TaskId == tomorrow.Id && r.Kind == NotificationKind.DueToday);
        await site.RunJobAsync("emails");
        Assert.Equal("1 task due today, 2 overdue", site.Outbox.To(user.Me.Email)[1].Subject);

        // The morning after: everything still open has been said. Nothing is written and no email goes out.
        await user.Client.PatchTaskAsync(tomorrow.Id, new { statusId = user.Status("Done").Id });
        site.Clock.AdvanceToHourUtc(9);
        await site.RunJobAsync("reminders");
        await site.RunJobAsync("emails");
        Assert.Equal(6, (await RemindersAsync(user.Me.Id)).Length);
        Assert.Equal(2, site.Outbox.To(user.Me.Email).Count);

        // A new due date starts again: due today that day, overdue the day after.
        var moved = site.Clock.AdvanceToHourUtc(9);
        await user.Client.PatchTaskAsync(overdue.Id, new { dueDate = Iso(moved) });
        await site.RunJobAsync("reminders");
        Assert.Equal((NotificationKind.DueToday, overdue.Id, Iso(moved)), (await RemindersAsync(user.Me.Id))[^1]);
    }

    [Fact]
    public async Task Reminders_go_out_from_eight_in_each_persons_own_time_zone()
    {
        var owner = await site.NewUserAsync();
        var (east, eastInfo) = await site.JoinAsync(owner, WorkspaceRole.Member, "East");
        var (west, westInfo) = await site.JoinAsync(owner, WorkspaceRole.Member, "West");
        var (_, nowhereInfo) = await site.JoinAsync(owner, WorkspaceRole.Member, "No zone yet");
        // Fixed offsets (the sign is the POSIX way round): East is 14 hours ahead of UTC, West 8 behind.
        await east.NotificationSettingsAsync(new { timeZone = "Etc/GMT-14" });
        await west.NotificationSettingsAsync(new { timeZone = "Etc/GMT+8" });

        // 07:00 UTC on day D: 21:00 on D in the east, 23:00 on D-1 in the west.
        var day = site.Clock.AdvanceToHourUtc(7);
        async Task<TaskDto> DueAsync(string title, DateOnly due, Guid userId) =>
            await owner.Client.CreateTaskAsync(owner.List.Id, new { title, dueDate = Iso(due), assigneeIds = new[] { userId } });
        var eastTask = await DueAsync("East today", day, eastInfo.UserId);
        var westTask = await DueAsync("West today", day, westInfo.UserId);
        var westYesterday = await DueAsync("West's D-1", day.AddDays(-1), westInfo.UserId);
        var noZoneTask = await DueAsync("UTC today", day, nowhereInfo.UserId);

        await site.RunJobAsync("reminders");
        // East's day D is well past eight. West's D-1 was reminded (it is 23:00 there): "West's D-1" is due today for them.
        Assert.Equal([(NotificationKind.DueToday, eastTask.Id, Iso(day))], await RemindersAsync(eastInfo.UserId));
        Assert.Equal([(NotificationKind.DueToday, westYesterday.Id, Iso(day.AddDays(-1)))], await RemindersAsync(westInfo.UserId));
        // Without a time zone a person's day is UTC's, and it isn't eight yet.
        Assert.Empty(await RemindersAsync(nowhereInfo.UserId));

        // 08:00 UTC: the person without a zone. Midnight in the west: not yet.
        site.Clock.Advance(TimeSpan.FromHours(1));
        await site.RunJobAsync("reminders");
        Assert.Equal([(NotificationKind.DueToday, noZoneTask.Id, Iso(day))], await RemindersAsync(nowhereInfo.UserId));
        Assert.Single(await RemindersAsync(westInfo.UserId));

        // 10:00 UTC: it is midnight on D+1 in the east, hours before eight. Nothing new for anyone.
        site.Clock.Advance(TimeSpan.FromHours(2));
        var eastTomorrow = await DueAsync("East D+1", day.AddDays(1), eastInfo.UserId);
        await site.RunJobAsync("reminders");
        Assert.Single(await RemindersAsync(eastInfo.UserId));

        // 16:00 UTC: eight in the morning of D in the west.
        site.Clock.Advance(TimeSpan.FromHours(6));
        await site.RunJobAsync("reminders");
        Assert.Equal([(NotificationKind.DueToday, westYesterday.Id, Iso(day.AddDays(-1))), (NotificationKind.Overdue, westYesterday.Id, Iso(day.AddDays(-1))),
                (NotificationKind.DueToday, westTask.Id, Iso(day))],
            (await RemindersAsync(westInfo.UserId)).OrderBy(r => r.To).ThenBy(r => r.Kind).ToArray());

        // 18:00 UTC: eight in the morning of D+1 in the east.
        site.Clock.Advance(TimeSpan.FromHours(2));
        await site.RunJobAsync("reminders");
        var eastRows = await RemindersAsync(eastInfo.UserId);
        Assert.Equal(3, eastRows.Length);
        Assert.Contains((NotificationKind.DueToday, eastTomorrow.Id, Iso(day.AddDays(1))), eastRows);
        Assert.Contains((NotificationKind.Overdue, eastTask.Id, Iso(day)), eastRows);
    }

    [Fact]
    public async Task Reminders_respect_who_can_see_a_task_and_who_wants_the_email()
    {
        var owner = await site.NewUserAsync();
        var (ana, anaInfo) = await site.JoinAsync(owner, WorkspaceRole.Member, "Ana");
        var (ben, benInfo) = await site.JoinAsync(owner, WorkspaceRole.Member, "Ben");
        var (_, cyInfo) = await site.JoinAsync(owner, WorkspaceRole.Member, "Cy");
        var (_, goneInfo) = await site.JoinAsync(owner, WorkspaceRole.Member, "Gone");
        await ben.NotificationSettingsAsync(new { emailReminders = false });
        var space = await (await owner.Client.PostAsJsonAsync($"/api/workspaces/{owner.Workspace.Id}/spaces",
            new { name = "Board", isPrivate = true, memberIds = new[] { anaInfo.UserId, benInfo.UserId } })).ReadAsync<SpaceDto>();
        var hiddenList = await owner.Client.CreateListAsync(space.Id, "Agenda");
        var today = site.Clock.AdvanceToHourUtc(12);
        var everyone = new[] { anaInfo.UserId, benInfo.UserId, cyInfo.UserId, goneInfo.UserId };
        var open = await owner.Client.CreateTaskAsync(owner.List.Id, new { title = "For all", dueDate = Iso(today), assigneeIds = everyone });
        var hidden = await owner.Client.CreateTaskAsync(hiddenList.Id, new { title = "For the board", dueDate = Iso(today), assigneeIds = new[] { anaInfo.UserId, benInfo.UserId } });
        // Ana leaves the private space (still assigned there); Cy's account is turned off; Gone leaves the workspace.
        Assert.Equal(HttpStatusCode.NoContent, (await owner.Client.DeleteAsync($"/api/spaces/{space.Id}/members/{anaInfo.UserId}")).StatusCode);
        await site.DbAsync(db => db.Users.Where(u => u.Id == cyInfo.UserId).ExecuteUpdateAsync(s => s.SetProperty(u => u.DisabledAt, DateTimeOffset.UtcNow)));
        Assert.Equal(HttpStatusCode.NoContent, (await owner.Client.DeleteAsync($"/api/workspaces/{owner.Workspace.Id}/members/{goneInfo.UserId}")).StatusCode);

        await site.RunJobAsync("reminders");
        await site.RunJobAsync("emails");

        // Ana: only the task she can see.
        Assert.Equal([(NotificationKind.DueToday, open.Id, Iso(today))], await RemindersAsync(anaInfo.UserId));
        var mail = Assert.Single(ReminderMails(anaInfo.Email));
        Assert.Equal("1 task due today", mail.Subject);
        Assert.DoesNotContain("For the board", mail.Text);
        // Ben: both in his Inbox (after the two "assigned to you"), no reminder email.
        Assert.Equal(2, (await RemindersAsync(benInfo.UserId)).Length);
        Assert.Equal(4, await ben.UnreadAsync(owner.Workspace.Id));
        Assert.Empty(ReminderMails(benInfo.Email));
        Assert.All(await site.NotificationRowsAsync(benInfo.UserId), n => Assert.NotEqual(NotificationEmail.Pending, n.Email));
        // Nothing for an account that is off or a person who left.
        Assert.Empty(await RemindersAsync(cyInfo.UserId));
        Assert.Empty(await RemindersAsync(goneInfo.UserId));
        Assert.Empty(site.Outbox.To(cyInfo.Email));

        // A reminder she has read in the app by the time the email job runs isn't emailed; one finished meanwhile isn't either.
        var next = site.Clock.AdvanceToHourUtc(12);
        var read = await owner.Client.CreateTaskAsync(owner.List.Id, new { title = "Read first", dueDate = Iso(next), assigneeIds = new[] { anaInfo.UserId } });
        var finished = await owner.Client.CreateTaskAsync(owner.List.Id, new { title = "Finished first", dueDate = Iso(next), assigneeIds = new[] { anaInfo.UserId } });
        await owner.Client.PatchTaskAsync(open.Id, new { statusId = owner.Status("Done").Id });
        await site.RunJobAsync("reminders");
        (await ana.MarkReadAsync(new { taskId = read.Id })).EnsureSuccessStatusCode();
        await owner.Client.PatchTaskAsync(finished.Id, new { statusId = owner.Status("Done").Id });
        await site.RunJobAsync("emails");
        Assert.Single(ReminderMails(anaInfo.Email));
    }
}
