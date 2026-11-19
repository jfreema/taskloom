using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Taskloom.Api.Tests.Support;
using Taskloom.Domain;
using static Taskloom.Api.Tests.Support.TestJson;

namespace Taskloom.Api.Tests;

/// <summary>
/// Features 2, the Inbox: changes to a task reach the people it belongs to (its creator and assignees) and never the
/// person who made them, an assignment and a mention reach the person named, rows about tasks someone can't see are
/// never written and never shown, and everyone reads and marks only their own.
/// </summary>
public class InboxTests(TaskloomFactory factory) : IClassFixture<TaskloomFactory>
{
    /// <summary>The owner's workspace with Ana, Ben and Cy (Members).</summary>
    private sealed record Team(TestUser Owner, HttpClient Ana, MemberDto AnaInfo, HttpClient Ben, MemberDto BenInfo, HttpClient Cy, MemberDto CyInfo)
    {
        public Guid WorkspaceId => Owner.Workspace.Id;
    }

    private async Task<Team> CreateTeamAsync()
    {
        var owner = await factory.NewUserAsync();
        var (ana, anaInfo) = await factory.JoinAsync(owner, WorkspaceRole.Member, "Ana Assignee");
        var (ben, benInfo) = await factory.JoinAsync(owner, WorkspaceRole.Member, "Ben Bystander");
        var (cy, cyInfo) = await factory.JoinAsync(owner, WorkspaceRole.Member, "Cy Colleague");
        return new Team(owner, ana, anaInfo, ben, benInfo, cy, cyInfo);
    }

    private static string[] Kinds(NotificationsDto inbox) => inbox.Items.Select(n => n.Kind.ToString()).ToArray();

    [Fact]
    public async Task Changes_reach_the_creator_and_the_assignees_but_never_the_person_who_made_them()
    {
        var t = await CreateTeamAsync();
        var task = await t.Owner.Client.CreateTaskAsync(t.Owner.List.Id, new { title = "Book venue", assigneeIds = new[] { t.AnaInfo.UserId } });

        // Ben, who has nothing to do with the task, works on it.
        await t.Ben.PatchTaskAsync(task.Id, new { statusId = t.Owner.Status("In progress").Id });
        await t.Ben.PatchTaskAsync(task.Id, new { dueDate = "2026-12-01", priority = "High" });
        // What nobody needs telling: a new title, a description, the same value again.
        await t.Ben.PatchTaskAsync(task.Id, new { title = "Book the venue", description = "<p>By the river</p>", priority = "High" });
        await t.Ben.MoveTaskAsync(task.Id, new { statusId = t.Owner.Status("Done").Id });

        // The creator hears of each change, newest first, with who did it and the values as the history shows them.
        var inbox = await t.Owner.Client.InboxAsync(t.WorkspaceId);
        Assert.Equal(["Completed", "DueDateChanged", "PriorityChanged", "StatusChanged"], Kinds(inbox));
        Assert.Equal((4, false), (inbox.Unread, inbox.HasMore));
        Assert.All(inbox.Items, n =>
        {
            Assert.Equal((task.Id, "Book the venue", t.Owner.List.Id, t.BenInfo.UserId, "Ben Bystander"), (n.TaskId, n.TaskTitle, n.ListId, n.Actor!.Id, n.Actor.DisplayName));
            Assert.Null(n.ReadAt);
            Assert.Null(n.CommentId);
        });
        Assert.Equal(("In progress", "Done"), (inbox.Items[0].From, inbox.Items[0].To));
        Assert.Equal((null, "2026-12-01"), (inbox.Items[1].From, inbox.Items[1].To));
        Assert.Equal(("None", "High"), (inbox.Items[2].From, inbox.Items[2].To));
        Assert.Equal(("To do", "In progress"), (inbox.Items[3].From, inbox.Items[3].To));

        // So does the assignee, after the news that the task is hers.
        Assert.Equal(["Completed", "DueDateChanged", "PriorityChanged", "StatusChanged", "Assigned"], Kinds(await t.Ana.InboxAsync(t.WorkspaceId)));
        // Ben did it all, and Cy has nothing to do with the task.
        Assert.Empty((await t.Ben.InboxAsync(t.WorkspaceId)).Items);
        Assert.Equal(0, await t.Ben.UnreadAsync(t.WorkspaceId));
        Assert.Empty((await t.Cy.InboxAsync(t.WorkspaceId)).Items);

        // The owner reopens it: only Ana hears.
        await t.Owner.Client.PatchTaskAsync(task.Id, new { statusId = t.Owner.Status("To do").Id });
        Assert.Equal(4, await t.Owner.Client.UnreadAsync(t.WorkspaceId));
        var reopened = (await t.Ana.InboxAsync(t.WorkspaceId)).Items[0];
        Assert.Equal((NotificationKind.StatusChanged, "Done", "To do", t.Owner.Me.Id), (reopened.Kind, reopened.From, reopened.To, reopened.Actor!.Id));
    }

    [Fact]
    public async Task Being_assigned_is_the_only_news_for_the_person_assigned()
    {
        var t = await CreateTeamAsync();
        // Created with two assignees, one of them the creator: only the other is told.
        var task = await t.Owner.Client.CreateTaskAsync(t.Owner.List.Id, new { title = "Write agenda", assigneeIds = new[] { t.Owner.Me.Id, t.AnaInfo.UserId } });
        var assigned = Assert.Single((await t.Ana.InboxAsync(t.WorkspaceId)).Items);
        Assert.Equal((NotificationKind.Assigned, task.Id, t.Owner.Me.Id, null, null), (assigned.Kind, assigned.TaskId, assigned.Actor!.Id, assigned.From, assigned.To));
        Assert.Empty((await t.Owner.Client.InboxAsync(t.WorkspaceId)).Items);

        // One request hands it to Ben too and changes its status and date: Ben hears "assigned", Ana hears the changes.
        await t.Owner.Client.PatchTaskAsync(task.Id, new
        {
            assigneeIds = new[] { t.Owner.Me.Id, t.AnaInfo.UserId, t.BenInfo.UserId }, statusId = t.Owner.Status("In progress").Id, dueDate = "2026-12-05",
        });
        Assert.Equal(["Assigned"], Kinds(await t.Ben.InboxAsync(t.WorkspaceId)));
        Assert.Equal(["DueDateChanged", "StatusChanged", "Assigned"], Kinds(await t.Ana.InboxAsync(t.WorkspaceId)));

        // Taking someone off a task tells nobody, and assigning yourself tells nobody.
        await t.Owner.Client.PatchTaskAsync(task.Id, new { assigneeIds = new[] { t.Owner.Me.Id, t.AnaInfo.UserId } });
        var own = await t.Cy.CreateTaskAsync(t.Owner.List.Id, new { title = "Mine" });
        await t.Cy.PatchTaskAsync(own.Id, new { assigneeIds = new[] { t.CyInfo.UserId } });
        Assert.Equal(["Assigned"], Kinds(await t.Ben.InboxAsync(t.WorkspaceId)));
        Assert.Empty((await t.Cy.InboxAsync(t.WorkspaceId)).Items);
        Assert.Equal(3, await t.Ana.UnreadAsync(t.WorkspaceId));

        // Assigned again later is news again.
        await t.Owner.Client.PatchTaskAsync(task.Id, new { assigneeIds = new[] { t.BenInfo.UserId } });
        Assert.Equal(["Assigned", "Assigned"], Kinds(await t.Ben.InboxAsync(t.WorkspaceId)));
    }

    [Fact]
    public async Task Bulk_changes_tell_the_same_people_as_single_ones()
    {
        var t = await CreateTeamAsync();
        var first = await t.Owner.Client.CreateTaskAsync(t.Owner.List.Id, new { title = "First", assigneeIds = new[] { t.AnaInfo.UserId } });
        var second = await t.Ben.CreateTaskAsync(t.Owner.List.Id, new { title = "Second" });
        var third = await t.Cy.CreateTaskAsync(t.Owner.List.Id, new { title = "Third" });

        // Cy completes all three and hands the third to Ana.
        var done = t.Owner.Status("Done").Id;
        (await t.Cy.BulkAsync(
            new { id = first.Id, statusId = done },
            new { id = second.Id, statusId = done, priority = "Urgent" },
            new { id = third.Id, statusId = done, assigneeIds = new[] { t.AnaInfo.UserId } })).EnsureSuccessStatusCode();

        var owner = Assert.Single((await t.Owner.Client.InboxAsync(t.WorkspaceId)).Items);
        Assert.Equal((NotificationKind.Completed, first.Id, t.CyInfo.UserId), (owner.Kind, owner.TaskId, owner.Actor!.Id));
        var ben = await t.Ben.InboxAsync(t.WorkspaceId);
        Assert.Equal(["PriorityChanged", "Completed"], Kinds(ben));
        Assert.All(ben.Items, n => Assert.Equal(second.Id, n.TaskId));
        // Ana: assigned to the first at the start, its completion, and the third handed to her (not also its completion).
        var ana = await t.Ana.InboxAsync(t.WorkspaceId);
        Assert.Equal([(NotificationKind.Assigned, third.Id), (NotificationKind.Completed, first.Id), (NotificationKind.Assigned, first.Id)],
            ana.Items.Select(n => (n.Kind, n.TaskId)).ToArray());
        Assert.Empty((await t.Cy.InboxAsync(t.WorkspaceId)).Items);
    }

    [Fact]
    public async Task Comments_reach_the_people_a_task_belongs_to()
    {
        var t = await CreateTeamAsync();
        var task = await t.Owner.Client.CreateTaskAsync(t.Owner.List.Id, new { title = "Plan offsite", assigneeIds = new[] { t.AnaInfo.UserId } });
        var comment = await t.Ben.CommentAsync(task.Id, "  Shall we\n\n  go north?  ");

        var owner = Assert.Single((await t.Owner.Client.InboxAsync(t.WorkspaceId)).Items);
        // The comment on one line, with the comment's id so the app can scroll to it.
        Assert.Equal((NotificationKind.Commented, null, "Shall we go north?", comment.Id, t.BenInfo.UserId), (owner.Kind, owner.From, owner.To, owner.CommentId, owner.Actor!.Id));
        Assert.Equal(["Commented", "Assigned"], Kinds(await t.Ana.InboxAsync(t.WorkspaceId)));
        Assert.Empty((await t.Ben.InboxAsync(t.WorkspaceId)).Items);

        // A long comment is cut for the Inbox.
        await t.Ana.CommentAsync(task.Id, new string('x', 400));
        var cut = (await t.Owner.Client.InboxAsync(t.WorkspaceId)).Items[0].To!;
        Assert.Equal((280, '…'), (cut.Length, cut[^1]));

        // Editing a comment is not news; deleting it takes its rows out of every Inbox.
        (await t.Ben.PatchAsJsonAsync($"/api/comments/{comment.Id}", new { body = "Shall we go south?" })).EnsureSuccessStatusCode();
        Assert.Equal(2, (await t.Owner.Client.InboxAsync(t.WorkspaceId)).Items.Count);
        Assert.Equal(HttpStatusCode.NoContent, (await t.Ben.DeleteAsync($"/api/comments/{comment.Id}")).StatusCode);
        Assert.Equal(["Commented"], Kinds(await t.Owner.Client.InboxAsync(t.WorkspaceId)));
        Assert.Equal(["Assigned"], Kinds(await t.Ana.InboxAsync(t.WorkspaceId)));

        // And a deleted task takes all of its rows.
        Assert.Equal(HttpStatusCode.NoContent, (await t.Owner.Client.DeleteAsync($"/api/tasks/{task.Id}")).StatusCode);
        Assert.Empty((await t.Ana.InboxAsync(t.WorkspaceId)).Items);
        Assert.Equal(0, await t.Owner.Client.UnreadAsync(t.WorkspaceId));
    }

    [Fact]
    public async Task Nobody_hears_about_a_task_they_cannot_see()
    {
        var t = await CreateTeamAsync();
        // A private space with the owner, Ana and Ben; Cy isn't in it.
        var space = await (await t.Owner.Client.PostAsJsonAsync($"/api/workspaces/{t.WorkspaceId}/spaces",
            new { name = "Board", isPrivate = true, memberIds = new[] { t.AnaInfo.UserId, t.BenInfo.UserId } })).ReadAsync<SpaceDto>();
        var list = await t.Owner.Client.CreateListAsync(space.Id, "Agenda");
        var task = await t.Ana.CreateTaskAsync(list.Id, new { title = "Salaries", assigneeIds = new[] { t.BenInfo.UserId } });
        await t.Owner.Client.PatchTaskAsync(task.Id, new { priority = "High" });
        Assert.Equal(1, await t.Ana.UnreadAsync(t.WorkspaceId));
        Assert.Equal(2, await t.Ben.UnreadAsync(t.WorkspaceId));

        // Ben is taken out of the space: his rows about it leave his Inbox and its count, though he is still assigned.
        Assert.Equal(HttpStatusCode.NoContent, (await t.Owner.Client.DeleteAsync($"/api/spaces/{space.Id}/members/{t.BenInfo.UserId}")).StatusCode);
        var ben = await t.Ben.InboxAsync(t.WorkspaceId);
        Assert.Equal((0, 0), (ben.Items.Count, ben.Unread));
        Assert.Equal(0, await t.Ben.UnreadAsync(t.WorkspaceId));

        // And nothing new is written for him: not for changes, comments or bulk changes.
        var before = (await factory.NotificationRowsAsync(t.BenInfo.UserId)).Count;
        await t.Owner.Client.PatchTaskAsync(task.Id, new { dueDate = "2026-12-24" });
        await t.Owner.Client.MoveTaskAsync(task.Id, new { statusId = list.Statuses.Single(s => s.Category == StatusCategory.Done).Id });
        await t.Owner.Client.CommentAsync(task.Id, "Agreed in the room");
        (await t.Owner.Client.BulkAsync(new { id = task.Id, priority = "Low" })).EnsureSuccessStatusCode();
        Assert.Equal(before, (await factory.NotificationRowsAsync(t.BenInfo.UserId)).Count);
        // Ana, who is still in the space, heard all four.
        Assert.Equal(5, await t.Ana.UnreadAsync(t.WorkspaceId));

        // Back in the space, his earlier rows show again.
        (await t.Owner.Client.PostAsJsonAsync($"/api/spaces/{space.Id}/members", new { userIds = new[] { t.BenInfo.UserId } })).EnsureSuccessStatusCode();
        Assert.Equal(2, await t.Ben.UnreadAsync(t.WorkspaceId));

        // Someone who leaves the workspace loses their rows for good, and a later invite starts with an empty Inbox.
        Assert.Equal(HttpStatusCode.NoContent, (await t.Owner.Client.DeleteAsync($"/api/workspaces/{t.WorkspaceId}/members/{t.AnaInfo.UserId}")).StatusCode);
        Assert.Empty(await factory.NotificationRowsAsync(t.AnaInfo.UserId));
        await t.Owner.Client.PatchTaskAsync(task.Id, new { priority = "Urgent" });
        Assert.Empty(await factory.NotificationRowsAsync(t.AnaInfo.UserId));
        Assert.Equal(HttpStatusCode.NotFound, (await t.Ana.GetAsync($"/api/workspaces/{t.WorkspaceId}/notifications")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await t.Ana.GetAsync($"/api/workspaces/{t.WorkspaceId}/notifications/count")).StatusCode);
    }

    [Fact]
    public async Task Rows_are_read_and_unread_one_by_one_by_task_or_all_at_once()
    {
        var t = await CreateTeamAsync();
        var first = await t.Owner.Client.CreateTaskAsync(t.Owner.List.Id, new { title = "First", assigneeIds = new[] { t.AnaInfo.UserId } });
        var second = await t.Owner.Client.CreateTaskAsync(t.Owner.List.Id, new { title = "Second", assigneeIds = new[] { t.AnaInfo.UserId } });
        await t.Owner.Client.PatchTaskAsync(first.Id, new { priority = "High" });
        await t.Owner.Client.PatchTaskAsync(second.Id, new { priority = "Low" });
        await t.Owner.Client.PatchTaskAsync(first.Id, new { dueDate = "2026-12-01" });

        var all = await t.Ana.InboxAsync(t.WorkspaceId);
        Assert.Equal((5, 5), (all.Items.Count, all.Unread));
        var newest = all.Items[0];

        // One row, by id; reading it again keeps the first time.
        var start = DateTimeOffset.UtcNow.AddSeconds(-1);
        Assert.Equal(HttpStatusCode.NoContent, (await t.Ana.MarkReadAsync(new { ids = new[] { newest.Id } })).StatusCode);
        var read = (await t.Ana.InboxAsync(t.WorkspaceId)).Items[0];
        Assert.InRange(read.ReadAt!.Value, start, DateTimeOffset.UtcNow.AddSeconds(1));
        await t.Ana.MarkReadAsync(new { ids = new[] { newest.Id, newest.Id } });
        Assert.Equal(read.ReadAt, (await t.Ana.InboxAsync(t.WorkspaceId)).Items[0].ReadAt);
        Assert.Equal(4, await t.Ana.UnreadAsync(t.WorkspaceId));

        // ?unread=true leaves read rows out.
        var unread = await t.Ana.InboxAsync(t.WorkspaceId, "?unread=true");
        Assert.Equal((4, 4), (unread.Items.Count, unread.Unread));
        Assert.DoesNotContain(unread.Items, n => n.Id == newest.Id);

        // Back to unread.
        await t.Ana.MarkReadAsync(new { ids = new[] { newest.Id }, read = false });
        Assert.Null((await t.Ana.InboxAsync(t.WorkspaceId)).Items[0].ReadAt);

        // Everything about one task (what opening the task does).
        Assert.Equal(HttpStatusCode.NoContent, (await t.Ana.MarkReadAsync(new { taskId = first.Id })).StatusCode);
        var afterTask = await t.Ana.InboxAsync(t.WorkspaceId);
        Assert.Equal(2, afterTask.Unread);
        Assert.All(afterTask.Items, n => Assert.Equal(n.TaskId == first.Id, n.ReadAt is not null));

        // Nobody marks anyone else's rows: the call succeeds and changes nothing.
        Assert.Equal(HttpStatusCode.NoContent, (await t.Ben.MarkReadAsync(new { ids = afterTask.Items.Select(n => n.Id) })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await t.Ben.MarkReadAsync(new { taskId = second.Id })).StatusCode);
        Assert.Equal(2, await t.Ana.UnreadAsync(t.WorkspaceId));

        // Mark all as read, up to the newest row the page showed: what arrives meanwhile stays unread.
        await t.Owner.Client.PatchTaskAsync(second.Id, new { priority = "Urgent" });
        var url = $"/api/workspaces/{t.WorkspaceId}/notifications/read-all";
        Assert.Equal(HttpStatusCode.NoContent, (await t.Ana.PostAsJsonAsync(url, new { before = newest.CreatedAt })).StatusCode);
        var late = Assert.Single((await t.Ana.InboxAsync(t.WorkspaceId, "?unread=true")).Items);
        Assert.Equal(("Low", "Urgent"), (late.From, late.To));
        Assert.Equal(HttpStatusCode.NoContent, (await t.Ana.PostAsJsonAsync(url, new { })).StatusCode);
        Assert.Equal(0, await t.Ana.UnreadAsync(t.WorkspaceId));
        Assert.Equal(6, (await t.Ana.InboxAsync(t.WorkspaceId)).Items.Count);

        Assert.Equal("Send the ids of 1 to 500 notifications, or a taskId.", await (await t.Ana.MarkReadAsync(new { })).ProblemAsync(HttpStatusCode.BadRequest));
        Assert.Equal("Send the ids of 1 to 500 notifications, or a taskId.", await (await t.Ana.MarkReadAsync(new { ids = Array.Empty<Guid>() })).ProblemAsync(HttpStatusCode.BadRequest));
        Assert.Equal("Send the ids of 1 to 500 notifications, or a taskId.",
            await (await t.Ana.MarkReadAsync(new { ids = new[] { newest.Id }, taskId = first.Id })).ProblemAsync(HttpStatusCode.BadRequest));
        Assert.Equal("Send the ids of 1 to 500 notifications, or a taskId.",
            await (await t.Ana.MarkReadAsync(new { ids = Enumerable.Range(0, 501).Select(_ => Guid.NewGuid()) })).ProblemAsync(HttpStatusCode.BadRequest));
    }

    [Fact]
    public async Task The_inbox_pages_newest_first()
    {
        var t = await CreateTeamAsync();
        var task = await t.Owner.Client.CreateTaskAsync(t.Owner.List.Id, new { title = "Paged", assigneeIds = new[] { t.AnaInfo.UserId } });
        string[] priorities = ["High", "Low", "Urgent", "Normal", "None", "High"];
        foreach (var priority in priorities) await t.Owner.Client.PatchTaskAsync(task.Id, new { priority });

        var first = await t.Ana.InboxAsync(t.WorkspaceId, "?limit=3");
        Assert.Equal((3, true, 7), (first.Items.Count, first.HasMore, first.Unread));
        Assert.Equal(["High", "None", "Normal"], first.Items.Select(n => n.To).ToArray());
        var before = Uri.EscapeDataString(first.Items[^1].CreatedAt.ToString("O"));
        var second = await t.Ana.InboxAsync(t.WorkspaceId, $"?limit=3&before={before}");
        Assert.Equal(["Urgent", "Low", "High"], second.Items.Select(n => n.To).ToArray());
        Assert.True(second.HasMore);
        var last = await t.Ana.InboxAsync(t.WorkspaceId, $"?limit=3&before={Uri.EscapeDataString(second.Items[^1].CreatedAt.ToString("O"))}");
        Assert.Equal((NotificationKind.Assigned, false), (Assert.Single(last.Items).Kind, last.HasMore));

        var url = $"/api/workspaces/{t.WorkspaceId}/notifications";
        Assert.Equal("limit must be a whole number from 1 to 100.", await (await t.Ana.GetAsync($"{url}?limit=0")).ProblemAsync(HttpStatusCode.BadRequest));
        Assert.Equal("limit must be a whole number from 1 to 100.", await (await t.Ana.GetAsync($"{url}?limit=101")).ProblemAsync(HttpStatusCode.BadRequest));
        Assert.Equal("unread must be true or false.", await (await t.Ana.GetAsync($"{url}?unread=yes")).ProblemAsync(HttpStatusCode.BadRequest));
        Assert.Equal("before must be a time like 2026-11-18T14:30:00Z.", await (await t.Ana.GetAsync($"{url}?before=soon")).ProblemAsync(HttpStatusCode.BadRequest));
    }

    [Fact]
    public async Task Each_workspace_has_its_own_inbox()
    {
        var t = await CreateTeamAsync();
        var other = await factory.NewUserAsync();
        // Ana is in both workspaces and is handed a task in each.
        await other.Client.InviteAsync(other.Workspace.Id, t.AnaInfo.Email, WorkspaceRole.Member);
        var here = await t.Owner.Client.CreateTaskAsync(t.Owner.List.Id, new { title = "Here", assigneeIds = new[] { t.AnaInfo.UserId } });
        var there = await other.Client.CreateTaskAsync(other.List.Id, new { title = "There", assigneeIds = new[] { t.AnaInfo.UserId } });

        Assert.Equal(here.Id, Assert.Single((await t.Ana.InboxAsync(t.WorkspaceId)).Items).TaskId);
        Assert.Equal(there.Id, Assert.Single((await t.Ana.InboxAsync(other.Workspace.Id)).Items).TaskId);
        (await t.Ana.PostAsJsonAsync($"/api/workspaces/{t.WorkspaceId}/notifications/read-all", new { })).EnsureSuccessStatusCode();
        Assert.Equal((0, 1), (await t.Ana.UnreadAsync(t.WorkspaceId), await t.Ana.UnreadAsync(other.Workspace.Id)));

        // Not for people outside the workspace.
        Assert.Equal(HttpStatusCode.NotFound, (await t.Ben.GetAsync($"/api/workspaces/{other.Workspace.Id}/notifications")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await t.Ben.GetAsync($"/api/workspaces/{other.Workspace.Id}/notifications/count")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await t.Ben.PostAsJsonAsync($"/api/workspaces/{other.Workspace.Id}/notifications/read-all", new { })).StatusCode);
    }

    [Fact]
    public async Task Inbox_responses_use_the_contract_shapes()
    {
        var t = await CreateTeamAsync();
        var task = await t.Owner.Client.CreateTaskAsync(t.Owner.List.Id, new { title = "Shapes", assigneeIds = new[] { t.AnaInfo.UserId } });
        await t.Owner.Client.PatchTaskAsync(task.Id, new { priority = "High" });

        using var inbox = JsonDocument.Parse(await t.Ana.GetStringAsync($"/api/workspaces/{t.WorkspaceId}/notifications"));
        Assert.Equal(Sorted("items", "unread", "hasMore"), inbox.RootElement.Keys());
        var row = inbox.RootElement.GetProperty("items")[0];
        Assert.Equal(Sorted("id", "kind", "taskId", "taskTitle", "listId", "actor", "from", "to", "commentId", "createdAt", "readAt"), row.Keys());
        Assert.Equal("PriorityChanged", row.GetProperty("kind").GetString());
        Assert.Equal(Sorted("id", "email", "displayName", "photoUrl"), row.GetProperty("actor").Keys());
        using var count = JsonDocument.Parse(await t.Ana.GetStringAsync($"/api/workspaces/{t.WorkspaceId}/notifications/count"));
        Assert.Equal(["unread"], count.RootElement.Keys());
        Assert.Equal(2, count.RootElement.GetProperty("unread").GetInt32());
    }

    [Fact]
    public async Task People_choose_which_emails_they_get_and_their_time_zone()
    {
        var user = await factory.NewUserAsync();
        var me = await (await user.Client.GetAsync("/api/me")).ReadAsync<MeDto>();
        // Emails are on until turned off; the test server doesn't really send any.
        Assert.Equal((null, true, true, false), (me.TimeZone, me.EmailMentions, me.EmailReminders, me.EmailConfigured));

        me = await user.Client.NotificationSettingsAsync(new { timeZone = "America/Chicago" });
        Assert.Equal(("America/Chicago", true, true), (me.TimeZone, me.EmailMentions, me.EmailReminders));
        me = await user.Client.NotificationSettingsAsync(new { emailMentions = false });
        Assert.Equal(("America/Chicago", false, true), (me.TimeZone, me.EmailMentions, me.EmailReminders));
        me = await user.Client.NotificationSettingsAsync(new { emailReminders = false, emailMentions = true });
        Assert.Equal(("America/Chicago", true, false), (me.TimeZone, me.EmailMentions, me.EmailReminders));
        // Nothing sent, nothing changed; and it is what GET /api/me says.
        me = await user.Client.NotificationSettingsAsync(new { });
        Assert.Equal(("America/Chicago", true, false), (me.TimeZone, me.EmailMentions, me.EmailReminders));
        Assert.Equal(me, await (await user.Client.GetAsync("/api/me")).ReadAsync<MeDto>());

        foreach (var zone in new[] { "Mars/Olympus", "", "  ", new string('z', 65), "America/Chicago; DROP TABLE users" })
        {
            Assert.Equal("Send an IANA time zone name such as America/Chicago.",
                await (await user.Client.PatchAsJsonAsync("/api/me/notifications", new { timeZone = zone })).ProblemAsync(HttpStatusCode.BadRequest));
        }
        Assert.Equal("America/Chicago", (await (await user.Client.GetAsync("/api/me")).ReadAsync<MeDto>()).TimeZone);
    }
}
