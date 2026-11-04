using System.Net;
using Taskloom.Api.Tests.Support;
using Taskloom.Domain;

namespace Taskloom.Api.Tests;

/// <summary>Comments (spec 5.4 #37–#39, 5.5 Comments): only the author edits or deletes, whatever the role.</summary>
public class CommentTests(TaskloomFactory factory) : IClassFixture<TaskloomFactory>
{
    [Fact]
    public async Task Only_the_author_can_edit_or_delete_and_edits_set_edited_at()
    {
        var owner = await factory.NewUserAsync();
        var (guest, guestInfo) = await factory.JoinAsync(owner, WorkspaceRole.Guest, "Gus Guest");
        var (admin, _) = await factory.JoinAsync(owner, WorkspaceRole.Admin, "Ada Admin");
        var (member, _) = await factory.JoinAsync(owner, WorkspaceRole.Member, "Max Member");
        var task = await owner.Client.CreateTaskAsync(owner.List.Id, new { title = "Discussed" });
        var url = $"/api/tasks/{task.Id}/comments";

        // A Guest may comment: the body is trimmed, the author is the caller, and it isn't edited yet.
        var start = DateTimeOffset.UtcNow.AddSeconds(-1);
        var commentId = Guid.NewGuid();
        var comment = await (await guest.PostAsJsonAsync(url, new { id = commentId, body = "  First!\nSecond line  " })).ReadAsync<CommentDto>();
        Assert.Equal((commentId, task.Id, "First!\nSecond line", (DateTimeOffset?)null), (comment.Id, comment.TaskId, comment.Body, comment.EditedAt));
        Assert.Equal(new UserDto(guestInfo.UserId, guestInfo.Email, "Gus Guest"), comment.Author);
        Assert.InRange(comment.CreatedAt, start, DateTimeOffset.UtcNow.AddSeconds(1));
        foreach (var body in new[] { "", "   ", null })
            Assert.Equal("Write a comment first.", await (await guest.PostAsJsonAsync(url, new { body })).ProblemAsync(HttpStatusCode.BadRequest));
        Assert.Equal("Comments can be up to 10,000 characters.", await (await guest.PostAsJsonAsync(url, new { body = new string('c', 10_001) })).ProblemAsync(HttpStatusCode.BadRequest));
        Assert.Equal(1, (await owner.Client.TaskDetailAsync(task.Id)).Task.CommentCount);

        // Nobody else edits or deletes it: not a Member, not an Admin, not the Owner.
        var commentUrl = $"/api/comments/{comment.Id}";
        foreach (var other in new[] { member, admin, owner.Client })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await other.PatchAsJsonAsync(commentUrl, new { body = "Hijacked" })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await other.DeleteAsync(commentUrl)).StatusCode);
        }
        Assert.Equal([comment], (await owner.Client.TaskDetailAsync(task.Id)).Comments);

        // The author edits it: the body changes and EditedAt is set; CreatedAt stays.
        start = DateTimeOffset.UtcNow.AddSeconds(-1);
        var edited = await (await guest.PatchAsJsonAsync(commentUrl, new { body = " Edited " })).ReadAsync<CommentDto>();
        Assert.Equal(comment with { Body = "Edited", EditedAt = edited.EditedAt }, edited);
        Assert.InRange(edited.EditedAt!.Value, start, DateTimeOffset.UtcNow.AddSeconds(1));
        Assert.Equal([edited], (await owner.Client.TaskDetailAsync(task.Id)).Comments);
        // Sending the same text again is not an edit.
        Assert.Equal(edited, await (await guest.PatchAsJsonAsync(commentUrl, new { body = "Edited" })).ReadAsync<CommentDto>());
        Assert.Equal("Write a comment first.", await (await guest.PatchAsJsonAsync(commentUrl, new { body = "  " })).ProblemAsync(HttpStatusCode.BadRequest));
        Assert.Equal("Comments can be up to 10,000 characters.", await (await guest.PatchAsJsonAsync(commentUrl, new { body = new string('c', 10_001) })).ProblemAsync(HttpStatusCode.BadRequest));
        Assert.Equal([edited], (await owner.Client.TaskDetailAsync(task.Id)).Comments);

        // Comments come oldest first; the Owner's own comment is theirs to change.
        var mine = await (await owner.Client.PostAsJsonAsync(url, new { body = "Thanks" })).ReadAsync<CommentDto>();
        Assert.Equal([edited.Id, mine.Id], (await owner.Client.TaskDetailAsync(task.Id)).Comments.Select(c => c.Id));
        Assert.Equal(HttpStatusCode.Forbidden, (await guest.DeleteAsync($"/api/comments/{mine.Id}")).StatusCode);

        // The author deletes it.
        Assert.Equal(HttpStatusCode.NoContent, (await guest.DeleteAsync(commentUrl)).StatusCode);
        var detail = await owner.Client.TaskDetailAsync(task.Id);
        Assert.Equal([mine.Id], detail.Comments.Select(c => c.Id));
        Assert.Equal(1, detail.Task.CommentCount);
        Assert.Equal(HttpStatusCode.NotFound, (await guest.DeleteAsync(commentUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await guest.PatchAsJsonAsync(commentUrl, new { body = "Back" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await guest.PostAsJsonAsync($"/api/tasks/{Guid.NewGuid()}/comments", new { body = "Nowhere" })).StatusCode);
    }

    [Fact]
    public async Task A_removed_member_gets_404_on_their_old_comments()
    {
        var owner = await factory.NewUserAsync();
        var (mia, miaInfo) = await factory.JoinAsync(owner, WorkspaceRole.Member, "Mia Member");
        var task = await owner.Client.CreateTaskAsync(owner.List.Id, new { title = "Discussed" });
        var comment = await (await mia.PostAsJsonAsync($"/api/tasks/{task.Id}/comments", new { body = "Mine" })).ReadAsync<CommentDto>();
        var commentUrl = $"/api/comments/{comment.Id}";

        Assert.Equal(HttpStatusCode.NoContent, (await owner.Client.DeleteAsync($"/api/workspaces/{owner.Workspace.Id}/members/{miaInfo.UserId}")).StatusCode);

        // Membership comes before authorship: once removed, her own comments are 404 to her, like the task.
        Assert.Equal(HttpStatusCode.NotFound, (await mia.PatchAsJsonAsync(commentUrl, new { body = "Changed" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await mia.DeleteAsync(commentUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await mia.GetAsync($"/api/tasks/{task.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await mia.PostAsJsonAsync($"/api/tasks/{task.Id}/comments", new { body = "Still here?" })).StatusCode);

        // The comment stays, still hers: the workspace sees it, and nobody else may change it.
        Assert.Equal([comment], (await owner.Client.TaskDetailAsync(task.Id)).Comments);
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.Client.DeleteAsync(commentUrl)).StatusCode);

        // Invited back, she may edit it again.
        await owner.Client.InviteAsync(owner.Workspace.Id, miaInfo.Email, WorkspaceRole.Guest);
        Assert.Equal("Changed", (await (await mia.PatchAsJsonAsync(commentUrl, new { body = "Changed" })).ReadAsync<CommentDto>()).Body);
    }
}
