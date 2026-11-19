using System.Net;
using Microsoft.EntityFrameworkCore;
using Taskloom.Api.Jobs;
using Taskloom.Api.Notifications;
using Taskloom.Api.Tests.Support;
using Taskloom.Domain;
using static Taskloom.Api.Tests.Support.Builders;

namespace Taskloom.Api.Tests;

/// <summary>
/// Features 2, the emails: an assignment or mention is emailed a minute later unless it has been read by then; people
/// who turned the emails off, lost their account or can't see the task get none; several go out as one; a mail server
/// that fails is tried again and given up on after a day.
/// </summary>
public class EmailJobTests(InboxSite site) : IClassFixture<InboxSite>
{
    private async Task<(TestUser Owner, HttpClient Ana, MemberDto AnaInfo)> CreatePairAsync()
    {
        var owner = await site.NewUserAsync();
        var (ana, anaInfo) = await site.JoinAsync(owner, WorkspaceRole.Member, "Ana Assignee");
        return (owner, ana, anaInfo);
    }

    private async Task<NotificationEmail[]> EmailStatesAsync(Guid userId) => (await site.NotificationRowsAsync(userId)).Select(n => n.Email).ToArray();

    [Fact]
    public async Task An_assignment_is_emailed_once_after_a_short_wait()
    {
        // Its own site, with a clock no other test has moved: the wait is the point.
        await using var site = new InboxSite();
        var owner = await site.NewUserAsync();
        var (_, anaInfo) = await site.JoinAsync(owner, WorkspaceRole.Member, "Ana Assignee");
        await owner.Client.PatchAsJsonAsync("/api/me", new { displayName = "Olive Owner" });
        var list = await owner.Client.CreateListAsync(owner.Space.Id, "Launch <plan>");
        var task = await owner.Client.CreateTaskAsync(list.Id, new { title = "Book <b>venue</b> & band", dueDate = "2026-12-04", assigneeIds = new[] { anaInfo.UserId } });

        // Not yet: she may be looking at the app right now.
        await site.RunJobAsync("emails");
        site.Clock.Advance(TimeSpan.FromSeconds(50));
        await site.RunJobAsync("emails");
        Assert.Empty(site.Outbox.To(anaInfo.Email));
        Assert.Equal(NotificationEmail.Pending, (await site.NotificationRowsAsync(anaInfo.UserId)).Single().Email);

        var result = await site.SendEmailsAsync();
        Assert.True(result.Ran);
        Assert.Null(result.Error);
        var mail = Assert.Single(site.Outbox.To(anaInfo.Email));
        Assert.Equal("Ana Assignee", mail.ToName);
        Assert.Equal("Olive Owner assigned you “Book <b>venue</b> & band”", mail.Subject);
        // The same in text and HTML, with a link to the task, the Inbox and the settings.
        Assert.Contains("Olive Owner assigned you \"Book <b>venue</b> & band\" (Launch <plan> · due Fri, Dec 4)", mail.Text);
        Assert.Contains($"{InboxSite.PublicUrl}/t/{task.Id}", mail.Text);
        Assert.Contains($"{InboxSite.PublicUrl}/settings/notifications", mail.Text);
        Assert.Contains($"<a href=\"{InboxSite.PublicUrl}/t/{task.Id}\"", mail.Html);
        Assert.Contains($"href=\"{InboxSite.PublicUrl}/inbox\"", mail.Html);
        // What people typed is text in the HTML, never markup.
        Assert.Contains("Book &lt;b&gt;venue&lt;/b&gt; &amp; band", mail.Html);
        Assert.Contains("Launch &lt;plan&gt;", mail.Html);
        Assert.DoesNotContain("<b>venue", mail.Html);
        Assert.Equal(NotificationEmail.Sent, (await site.NotificationRowsAsync(anaInfo.UserId)).Single().Email);

        // Sent once: later runs find nothing.
        await site.SendEmailsAsync();
        Assert.Single(site.Outbox.To(anaInfo.Email));
        // The Inbox row is still unread: the email doesn't count as reading it.
        Assert.Null((await site.NotificationRowsAsync(anaInfo.UserId)).Single().ReadAt);
    }

    [Fact]
    public async Task A_mention_is_emailed_with_the_comment()
    {
        var (owner, ana, anaInfo) = await CreatePairAsync();
        var (_, benInfo) = await site.JoinAsync(owner, WorkspaceRole.Member, "Ben <i>Bold</i>");
        var task = await owner.Client.CreateTaskAsync(owner.List.Id, new { title = "Pick a date" });
        await ana.CommentAsync(task.Id, $"{At(benInfo.UserId)} can you do <script>alert(1)</script>\nthe 4th?\n\nOr {At(owner.Me.Id)} the 5th?");

        await site.SendEmailsAsync();
        var mail = Assert.Single(site.Outbox.To(benInfo.Email));
        Assert.Equal("Ana Assignee mentioned you in “Pick a date”", mail.Subject);
        // The whole comment, with names for the mentions and its line breaks.
        var ownerName = owner.Me.DisplayName;
        Assert.Contains($"@Ben <i>Bold</i> can you do <script>alert(1)</script>\nthe 4th?\n\nOr @{ownerName} the 5th?", mail.Text);
        Assert.Contains("@Ben &lt;i&gt;Bold&lt;/i&gt; can you do &lt;script&gt;alert(1)&lt;/script&gt;", mail.Html);
        Assert.DoesNotContain("<script>", mail.Html);
        // The owner was mentioned too and gets the same; nobody gets an email for a plain comment or change.
        Assert.Single(site.Outbox.To(owner.Me.Email));
        await owner.Client.PatchTaskAsync(task.Id, new { assigneeIds = new[] { anaInfo.UserId } });
        await site.SendEmailsAsync();
        await owner.Client.CommentAsync(task.Id, "Done");
        await owner.Client.PatchTaskAsync(task.Id, new { priority = "High", statusId = owner.Status("Done").Id });
        await site.SendEmailsAsync();
        Assert.Single(site.Outbox.To(anaInfo.Email));
        Assert.Equal([NotificationEmail.Sent, NotificationEmail.None, NotificationEmail.None, NotificationEmail.None], await EmailStatesAsync(anaInfo.UserId));
    }

    [Fact]
    public async Task Nothing_is_emailed_once_it_has_been_read_or_to_people_who_turned_emails_off()
    {
        var (owner, ana, anaInfo) = await CreatePairAsync();
        var (ben, benInfo) = await site.JoinAsync(owner, WorkspaceRole.Member, "Ben");
        await ben.NotificationSettingsAsync(new { emailMentions = false });
        var task = await owner.Client.CreateTaskAsync(owner.List.Id, new { title = "Seen in time", assigneeIds = new[] { anaInfo.UserId, benInfo.UserId } });
        await owner.Client.CommentAsync(task.Id, $"{At(anaInfo.UserId)} {At(benInfo.UserId)} hello");

        // Ana opens the task within the minute.
        (await ana.MarkReadAsync(new { taskId = task.Id })).EnsureSuccessStatusCode();
        await site.SendEmailsAsync();
        Assert.Empty(site.Outbox.To(anaInfo.Email));
        Assert.Empty(site.Outbox.To(benInfo.Email));
        Assert.Equal([NotificationEmail.Skipped, NotificationEmail.Skipped], await EmailStatesAsync(anaInfo.UserId));
        Assert.Equal([NotificationEmail.Skipped, NotificationEmail.Skipped], await EmailStatesAsync(benInfo.UserId));
        // Ben's are still in his Inbox, unread.
        Assert.Equal(2, await ben.UnreadAsync(owner.Workspace.Id));

        // Emails back on: the next one is sent, the skipped ones are not sent late.
        await ben.NotificationSettingsAsync(new { emailMentions = true });
        await owner.Client.CommentAsync(task.Id, $"{At(benInfo.UserId)} again");
        await site.SendEmailsAsync();
        Assert.Contains("@Ben again", Assert.Single(site.Outbox.To(benInfo.Email)).Text);
    }

    [Fact]
    public async Task Nothing_is_emailed_to_someone_who_can_no_longer_see_the_task_or_was_taken_off_it()
    {
        var (owner, _, anaInfo) = await CreatePairAsync();
        var (_, benInfo) = await site.JoinAsync(owner, WorkspaceRole.Member, "Ben");
        var (_, cyInfo) = await site.JoinAsync(owner, WorkspaceRole.Member, "Cy");
        var (_, didInfo) = await site.JoinAsync(owner, WorkspaceRole.Member, "Did");
        var space = await (await owner.Client.PostAsJsonAsync($"/api/workspaces/{owner.Workspace.Id}/spaces",
            new { name = "Board", isPrivate = true, memberIds = new[] { anaInfo.UserId, benInfo.UserId, cyInfo.UserId, didInfo.UserId } })).ReadAsync<SpaceDto>();
        var list = await owner.Client.CreateListAsync(space.Id, "Agenda");
        var task = await owner.Client.CreateTaskAsync(list.Id, new { title = "Salaries", assigneeIds = new[] { anaInfo.UserId, benInfo.UserId, cyInfo.UserId, didInfo.UserId } });

        // Within the minute: Ana leaves the private space, Ben is taken off the task, Cy's account is turned off.
        Assert.Equal(HttpStatusCode.NoContent, (await owner.Client.DeleteAsync($"/api/spaces/{space.Id}/members/{anaInfo.UserId}")).StatusCode);
        await owner.Client.PatchTaskAsync(task.Id, new { assigneeIds = new[] { cyInfo.UserId, didInfo.UserId } });
        await site.DbAsync(db => db.Users.Where(u => u.Id == cyInfo.UserId).ExecuteUpdateAsync(s => s.SetProperty(u => u.DisabledAt, DateTimeOffset.UtcNow)));

        await site.SendEmailsAsync();
        Assert.Empty(site.Outbox.To(anaInfo.Email));
        Assert.Empty(site.Outbox.To(benInfo.Email));
        Assert.Empty(site.Outbox.To(cyInfo.Email));
        Assert.Contains("assigned you \"Salaries\"", Assert.Single(site.Outbox.To(didInfo.Email)).Text);
        foreach (var id in new[] { anaInfo.UserId, benInfo.UserId, cyInfo.UserId })
            Assert.Equal([NotificationEmail.Skipped], await EmailStatesAsync(id));
    }

    [Fact]
    public async Task Several_things_for_one_person_go_out_as_one_email()
    {
        var (owner, ana, anaInfo) = await CreatePairAsync();
        var (ben, benInfo) = await site.JoinAsync(owner, WorkspaceRole.Member, "Ben");
        var first = await owner.Client.CreateTaskAsync(owner.List.Id, new { title = "First thing", assigneeIds = new[] { anaInfo.UserId } });
        var second = await owner.Client.CreateTaskAsync(owner.List.Id, new { title = "Second thing", assigneeIds = new[] { anaInfo.UserId } });
        await owner.Client.CommentAsync(second.Id, $"{At(anaInfo.UserId)} start here");

        await site.SendEmailsAsync();
        var mail = Assert.Single(site.Outbox.To(anaInfo.Email));
        Assert.Equal($"{owner.Me.DisplayName}: 3 updates for you in Taskloom", mail.Subject);
        Assert.Contains("assigned you \"First thing\"", mail.Text);
        Assert.Contains("assigned you \"Second thing\"", mail.Text);
        Assert.Contains("mentioned you in \"Second thing\"", mail.Text);
        Assert.Contains("@Ana Assignee start here", mail.Text);
        Assert.Contains($"{InboxSite.PublicUrl}/t/{first.Id}", mail.Text);
        Assert.All(await EmailStatesAsync(anaInfo.UserId), s => Assert.Equal(NotificationEmail.Sent, s));

        // From two people the subject names neither; and beyond 25 tasks the rest are counted, not listed.
        var tasks = new List<TaskDto>();
        for (var i = 0; i < 27; i++) tasks.Add(await owner.Client.CreateTaskAsync(owner.List.Id, new { title = $"Batch {i:00}" }));
        (await owner.Client.BulkAsync(tasks.Select(t => (object)new { id = t.Id, assigneeIds = new[] { benInfo.UserId } }).ToArray())).EnsureSuccessStatusCode();
        await ana.CommentAsync(first.Id, $"{At(benInfo.UserId)} one more");
        await site.SendEmailsAsync();
        var big = Assert.Single(site.Outbox.To(benInfo.Email));
        Assert.Equal("28 updates for you in Taskloom", big.Subject);
        Assert.Contains("Batch 00", big.Text);
        Assert.Contains("Batch 24", big.Text);
        Assert.DoesNotContain("Batch 25", big.Text);
        Assert.Contains("And 3 more in your Inbox.", big.Text);
        Assert.Contains("And 3 more in your Inbox.", big.Html);
        Assert.Equal(28, (await EmailStatesAsync(benInfo.UserId)).Count(s => s == NotificationEmail.Sent));
        Assert.Equal(28, await ben.UnreadAsync(owner.Workspace.Id));
    }

    [Fact]
    public async Task A_failed_send_is_tried_again_later_and_given_up_on_after_a_day()
    {
        // Its own site: this test moves the clock a long way.
        await using var own = new InboxSite();
        var owner = await own.NewUserAsync();
        var (_, anaInfo) = await own.JoinAsync(owner, WorkspaceRole.Member, "Ana");
        var (_, benInfo) = await own.JoinAsync(owner, WorkspaceRole.Member, "Ben");
        var task = await owner.Client.CreateTaskAsync(owner.List.Id, new { title = "Flaky", assigneeIds = new[] { anaInfo.UserId, benInfo.UserId } });
        Task<Notification> RowAsync(Guid userId) => own.DbAsync(db => db.Notifications.AsNoTracking().SingleAsync(n => n.UserId == userId && n.TaskId == task.Id));

        // The mail server refuses Ana's email; Ben's goes out in the same run.
        own.Outbox.Fails = m => m.To == anaInfo.Email;
        var result = await own.SendEmailsAsync();
        Assert.Null(result.Error);
        Assert.Contains("1 could not be sent", result.Summary);
        Assert.Single(own.Outbox.To(benInfo.Email));
        var row = await RowAsync(anaInfo.UserId);
        Assert.Equal((NotificationEmail.Pending, 1), (row.Email, row.EmailAttempts));
        // Waiting two minutes: a run before then doesn't try.
        var attempts = own.Outbox.Attempts;
        await own.SendEmailsAsync();
        Assert.Equal(attempts, own.Outbox.Attempts);
        // Then it tries again and waits twice as long.
        own.Clock.Advance(TimeSpan.FromMinutes(2));
        await own.RunJobAsync("emails");
        Assert.Equal(attempts + 1, own.Outbox.Attempts);
        row = await RowAsync(anaInfo.UserId);
        Assert.Equal((NotificationEmail.Pending, 2), (row.Email, row.EmailAttempts));
        Assert.InRange(row.EmailAfter!.Value - own.Clock.GetUtcNow(), TimeSpan.FromMinutes(3.9), TimeSpan.FromMinutes(4.1));

        // The server recovers: the email goes out, once.
        own.Outbox.Fails = null;
        own.Clock.Advance(TimeSpan.FromMinutes(5));
        await own.RunJobAsync("emails");
        Assert.Single(own.Outbox.To(anaInfo.Email));
        Assert.Equal(NotificationEmail.Sent, (await RowAsync(anaInfo.UserId)).Email);

        // A server that stays down for a day: the email is given up on, and the Inbox row stays.
        var second = await owner.Client.CreateTaskAsync(owner.List.Id, new { title = "Never", assigneeIds = new[] { anaInfo.UserId } });
        own.Outbox.Fails = _ => true;
        await own.SendEmailsAsync();
        own.Clock.Advance(TimeSpan.FromHours(25));
        await own.RunJobAsync("emails");
        var gone = await own.DbAsync(db => db.Notifications.AsNoTracking().SingleAsync(n => n.TaskId == second.Id));
        Assert.Equal((NotificationEmail.Failed, 2, null), (gone.Email, gone.EmailAttempts, gone.EmailAfter));
        attempts = own.Outbox.Attempts;
        own.Clock.Advance(TimeSpan.FromHours(2));
        await own.RunJobAsync("emails");
        Assert.Equal(attempts, own.Outbox.Attempts);
        Assert.Single(own.Outbox.To(anaInfo.Email));

        // With the server down, a run stops after three failures in a row and leaves the rest for the next run.
        var people = new List<MemberDto>();
        for (var i = 0; i < 5; i++) people.Add((await own.JoinAsync(owner, WorkspaceRole.Member, $"Person {i}")).Member);
        await owner.Client.CreateTaskAsync(owner.List.Id, new { title = "Everyone", assigneeIds = people.Select(p => p.UserId) });
        attempts = own.Outbox.Attempts;
        await own.SendEmailsAsync();
        Assert.Equal(attempts + EmailJob.MaxFailuresInARow, own.Outbox.Attempts);
    }

    [Fact]
    public void Emails_carry_no_links_until_the_public_address_is_set()
    {
        var options = new NotificationOptions();
        var item = new EmailItem(NotificationKind.Assigned, Guid.NewGuid(), "A task", "A list", null, "Someone\r\nBcc: x@example.com", null);
        var mail = EmailTemplates.Activity("to@example.com", "To\nName", [item], options);
        Assert.DoesNotContain("http", mail.Html);
        Assert.DoesNotContain("http", mail.Text);
        Assert.Contains("You can turn it off in Taskloom under Settings, Notifications.", mail.Text);
        // Headers stay on one line whatever a name holds.
        Assert.Equal("Someone  Bcc: x@example.com assigned you “A task”", mail.Subject);
        Assert.Equal("To Name", mail.ToName);
        Assert.Equal(200, EmailTemplates.OneLine(new string('s', 500), 200).Length);
    }
}
