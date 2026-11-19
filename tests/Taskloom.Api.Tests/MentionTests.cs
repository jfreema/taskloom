using System.Net;
using Microsoft.EntityFrameworkCore;
using Taskloom.Api.Notifications;
using Taskloom.Api.Tests.Support;
using Taskloom.Domain;
using static Taskloom.Api.Tests.Support.Builders;

namespace Taskloom.Api.Tests;

/// <summary>
/// Features 2, @mentions: a comment's body carries a token per person mentioned; each must be able to see the task, is
/// told in their Inbox (instead of "commented"), and is listed with the comment by name.
/// </summary>
public class MentionTests(TaskloomFactory factory) : IClassFixture<TaskloomFactory>
{
    [Fact]
    public async Task A_mention_tells_the_person_named_whoever_they_are_to_the_task()
    {
        var owner = await factory.NewUserAsync();
        var (ana, anaInfo) = await factory.JoinAsync(owner, WorkspaceRole.Member, "Ana Assignee");
        var (ben, benInfo) = await factory.JoinAsync(owner, WorkspaceRole.Member, "Ben Bystander");
        var (gus, gusInfo) = await factory.JoinAsync(owner, WorkspaceRole.Guest, "Gus Guest");
        var ws = owner.Workspace.Id;
        var task = await owner.Client.CreateTaskAsync(owner.List.Id, new { title = "Launch", assigneeIds = new[] { anaInfo.UserId } });
        (await ana.PostAsJsonAsync($"/api/workspaces/{ws}/notifications/read-all", new { })).EnsureSuccessStatusCode();

        // A Guest mentions the assignee, a bystander, and themselves (twice over).
        var body = $"{At(anaInfo.UserId)} and {At(benInfo.UserId)}: ready?\n{At(anaInfo.UserId)} {At(gusInfo.UserId)}";
        var comment = await gus.CommentAsync(task.Id, body);
        // The body is kept as sent; the people come back by name, in the order first mentioned.
        Assert.Equal(body, comment.Body);
        Assert.Equal([(anaInfo.UserId, "Ana Assignee"), (benInfo.UserId, "Ben Bystander"), (gusInfo.UserId, "Gus Guest")],
            comment.Mentions.Select(m => (m.UserId, m.DisplayName)).ToArray());
        var shown = Assert.Single((await owner.Client.TaskDetailAsync(task.Id)).Comments);
        Assert.Equal(comment.Mentions.OrderBy(m => m.UserId), shown.Mentions.OrderBy(m => m.UserId));

        // The assignee hears "mentioned", not also "commented"; the bystander hears it too; the text shows names.
        const string preview = "@Ana Assignee and @Ben Bystander: ready? @Ana Assignee @Gus Guest";
        var anaRow = Assert.Single((await ana.InboxAsync(ws, "?unread=true")).Items);
        Assert.Equal((NotificationKind.Mentioned, preview, comment.Id, gusInfo.UserId), (anaRow.Kind, anaRow.To, anaRow.CommentId, anaRow.Actor!.Id));
        var benRow = Assert.Single((await ben.InboxAsync(ws)).Items);
        Assert.Equal((NotificationKind.Mentioned, preview, task.Id), (benRow.Kind, benRow.To, benRow.TaskId));
        // The creator, not mentioned, hears that someone commented; nobody is told about their own comment.
        var ownerRow = Assert.Single((await owner.Client.InboxAsync(ws)).Items);
        Assert.Equal((NotificationKind.Commented, preview), (ownerRow.Kind, ownerRow.To));
        Assert.Empty((await gus.InboxAsync(ws)).Items);

        // Mentions and assignments are queued for email; the rest stay in the Inbox.
        Assert.Equal(NotificationEmail.Pending, (await factory.NotificationRowsAsync(benInfo.UserId)).Single().Email);
        Assert.Equal(NotificationEmail.None, (await factory.NotificationRowsAsync(owner.Me.Id)).Single().Email);
    }

    [Fact]
    public async Task Only_people_who_can_see_the_task_can_be_mentioned()
    {
        var owner = await factory.NewUserAsync();
        var (ana, anaInfo) = await factory.JoinAsync(owner, WorkspaceRole.Member, "Ana");
        var (_, benInfo) = await factory.JoinAsync(owner, WorkspaceRole.Member, "Ben");
        var stranger = await factory.NewUserAsync();
        var space = await (await owner.Client.PostAsJsonAsync($"/api/workspaces/{owner.Workspace.Id}/spaces",
            new { name = "Board", isPrivate = true, memberIds = new[] { anaInfo.UserId } })).ReadAsync<SpaceDto>();
        var list = await owner.Client.CreateListAsync(space.Id, "Agenda");
        var hidden = await owner.Client.CreateTaskAsync(list.Id, new { title = "Salaries" });
        var open = await owner.Client.CreateTaskAsync(owner.List.Id, new { title = "Picnic" });

        // Not someone outside the private space, someone outside the workspace, or nobody at all: nothing is saved.
        foreach (var id in new[] { benInfo.UserId, stranger.Me.Id, Guid.NewGuid() })
        {
            Assert.Equal("Only people who can see this task can be mentioned.",
                await (await ana.PostCommentAsync(hidden.Id, $"Ask {At(id)}")).ProblemAsync(HttpStatusCode.BadRequest));
            Assert.Equal("Only people who can see this task can be mentioned.",
                await (await ana.PostCommentAsync(hidden.Id, $"{At(owner.Me.Id)} ask {At(id)}")).ProblemAsync(HttpStatusCode.BadRequest));
        }
        Assert.Empty((await owner.Client.TaskDetailAsync(hidden.Id)).Comments);
        Assert.Empty(await factory.NotificationRowsAsync(benInfo.UserId));
        Assert.Empty(await factory.NotificationRowsAsync(owner.Me.Id));

        // The same person can be mentioned on a task they can see, and a person of the private space on its tasks.
        Assert.Equal("Ben", Assert.Single((await ana.CommentAsync(open.Id, $"Ask {At(benInfo.UserId)}")).Mentions).DisplayName);
        Assert.Equal(owner.Me.Id, Assert.Single((await ana.CommentAsync(hidden.Id, $"Ask {At(owner.Me.Id)}")).Mentions).UserId);

        // Text that only looks like a mention is just text.
        var plain = await ana.CommentAsync(open.Id, "<@nobody> <@1234> @Ben <@ 00000000-0000-0000-0000-000000000000>");
        Assert.Empty(plain.Mentions);

        // More people than anyone means.
        var many = string.Join(" ", Enumerable.Range(0, 51).Select(_ => At(Guid.NewGuid())));
        Assert.Equal("A comment can mention up to 50 people.", await (await ana.PostCommentAsync(open.Id, many)).ProblemAsync(HttpStatusCode.BadRequest));
    }

    [Fact]
    public async Task Editing_a_comment_tells_the_people_newly_mentioned_and_takes_back_unread_mentions()
    {
        var owner = await factory.NewUserAsync();
        var (ana, anaInfo) = await factory.JoinAsync(owner, WorkspaceRole.Member, "Ana");
        var (ben, benInfo) = await factory.JoinAsync(owner, WorkspaceRole.Member, "Ben");
        var (cy, cyInfo) = await factory.JoinAsync(owner, WorkspaceRole.Member, "Cy");
        var ws = owner.Workspace.Id;
        var task = await owner.Client.CreateTaskAsync(owner.List.Id, new { title = "Review" });
        var comment = await owner.Client.CommentAsync(task.Id, $"{At(anaInfo.UserId)} {At(benInfo.UserId)} please look");
        var url = $"/api/comments/{comment.Id}";
        // Ana reads hers; Ben doesn't.
        (await ana.MarkReadAsync(new { taskId = task.Id })).EnsureSuccessStatusCode();

        // The edit drops both and adds Cy.
        var edited = await (await owner.Client.PatchAsJsonAsync(url, new { body = $"{At(cyInfo.UserId)} please look" })).ReadAsync<CommentDto>();
        Assert.Equal([(cyInfo.UserId, "Cy")], edited.Mentions.Select(m => (m.UserId, m.DisplayName)).ToArray());
        var cyRow = Assert.Single((await cy.InboxAsync(ws)).Items);
        Assert.Equal((NotificationKind.Mentioned, "@Cy please look", comment.Id), (cyRow.Kind, cyRow.To, cyRow.CommentId));
        // Ben never saw his mention: it is gone. Ana read hers: it stays as it was.
        Assert.Empty((await ben.InboxAsync(ws)).Items);
        Assert.Equal("@Ana @Ben please look", Assert.Single((await ana.InboxAsync(ws)).Items).To);

        // Keeping a mention through an edit doesn't tell the person again, and the same text is no edit at all.
        await owner.Client.PatchAsJsonAsync(url, new { body = $"{At(cyInfo.UserId)} please look today" });
        await owner.Client.PatchAsJsonAsync(url, new { body = $"{At(cyInfo.UserId)} please look today" });
        Assert.Single((await cy.InboxAsync(ws)).Items);

        // Someone who left the workspace since can stay mentioned through an edit, but can't be newly mentioned.
        Assert.Equal(HttpStatusCode.NoContent, (await owner.Client.DeleteAsync($"/api/workspaces/{ws}/members/{cyInfo.UserId}")).StatusCode);
        var kept = await (await owner.Client.PatchAsJsonAsync(url, new { body = $"{At(cyInfo.UserId)} thanks, {At(anaInfo.UserId)}" })).ReadAsync<CommentDto>();
        Assert.Equal([cyInfo.UserId, anaInfo.UserId], kept.Mentions.Select(m => m.UserId).ToArray());
        var other = await owner.Client.CommentAsync(task.Id, "Second");
        Assert.Equal("Only people who can see this task can be mentioned.",
            await (await owner.Client.PatchAsJsonAsync($"/api/comments/{other.Id}", new { body = $"{At(cyInfo.UserId)}?" })).ProblemAsync(HttpStatusCode.BadRequest));
        Assert.Equal("Second", (await owner.Client.TaskDetailAsync(task.Id)).Comments.Single(c => c.Id == other.Id).Body);

        // The mentions are stored beside the comment and go with it.
        Assert.Equal(2, await factory.DbAsync(db => db.CommentMentions.CountAsync(m => m.CommentId == comment.Id)));
        Assert.Equal(HttpStatusCode.NoContent, (await owner.Client.DeleteAsync(url)).StatusCode);
        Assert.Equal(0, await factory.DbAsync(db => db.CommentMentions.CountAsync(m => m.CommentId == comment.Id)));
        Assert.DoesNotContain((await ana.InboxAsync(ws)).Items, n => n.CommentId == comment.Id);
    }

    [Fact]
    public void Mention_tokens_are_found_and_written_as_names()
    {
        Guid a = Guid.Parse("11111111-1111-1111-1111-111111111111"), b = Guid.Parse("AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE");
        Assert.Equal("<@11111111-1111-1111-1111-111111111111>", Mentions.TokenFor(a));
        Assert.Empty(Mentions.In("No mentions here, not even <@> or user@example.com"));
        // Distinct, in order, either case.
        Assert.Equal([b, a], Mentions.In($"<@AAAAAAAA-bbbb-CCCC-dddd-EEEEEEEEEEEE> then {Mentions.TokenFor(a)} and <@aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee>"));

        var names = new Dictionary<Guid, string> { [a] = "Ana <b>" };
        Assert.Equal("Hi @Ana <b> and @Former member!", Mentions.ToPlainText($"Hi {Mentions.TokenFor(a)} and {Mentions.TokenFor(b)}!", names));
        Assert.Equal("one two @Ana <b> three", Mentions.Preview($"  one\r\n\ttwo {Mentions.TokenFor(a)}\n\nthree  ", names));
        var cut = Mentions.Preview(new string('é', 300), names, max: 20);
        Assert.Equal((20, new string('é', 19) + "…"), (cut.Length, cut));
        Assert.Equal(new string('é', 20), Mentions.Preview(new string('é', 20), names, max: 20));
    }
}
