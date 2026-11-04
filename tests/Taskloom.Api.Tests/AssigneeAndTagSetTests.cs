using System.Net;
using Microsoft.EntityFrameworkCore;
using Taskloom.Api.Tests.Support;
using Taskloom.Infrastructure;

namespace Taskloom.Api.Tests;

/// <summary>Several assignees and workspace tags on create and PATCH (spec 4.2, 5.2, 5.5).</summary>
public class AssigneeAndTagSetTests(TaskloomFactory factory) : IClassFixture<TaskloomFactory>
{
    private static async Task<Dictionary<string, Guid>> TagIdsAsync(TestUser user) =>
        (await user.Client.TagsAsync(user.Workspace.Id)).ToDictionary(t => t.Name, t => t.Id);

    [Fact]
    public async Task Create_and_patch_replace_assignee_and_tag_sets_within_the_workspace()
    {
        var user = await factory.NewUserAsync();
        (await user.Client.SeedAsync(user.Workspace.Id, fillers: 0)).EnsureSuccessStatusCode();
        var tags = await TagIdsAsync(user);
        var members = await (await user.Client.GetAsync($"/api/workspaces/{user.Workspace.Id}/members")).ReadAsync<List<MemberDto>>();
        var me = user.Me.Id;
        var ana = members.Single(m => m.Email == "ana@example.com").UserId;
        var sam = members.Single(m => m.Email == "sam@example.com").UserId;

        // Duplicates collapse; tag ids come back ordered by tag name.
        var task = await user.Client.CreateTaskAsync(user.List.Id,
            new { title = "Sets", assigneeIds = new[] { me, me }, tagIds = new[] { tags["ops"], tags["design"], tags["ops"] } });
        Assert.Equal([me], task.AssigneeIds);
        Assert.Equal([tags["design"], tags["ops"]], task.TagIds);

        // PATCH replaces each set; people keep the order they were assigned in.
        var url = $"/api/tasks/{task.Id}";
        var patched = await (await user.Client.PatchAsJsonAsync(url, new { assigneeIds = new[] { ana, me, ana }, tagIds = new[] { tags["docs"], tags["bug"] } })).ReadAsync<TaskDto>();
        Assert.Equal([me, ana], patched.AssigneeIds);
        Assert.Equal([tags["bug"], tags["docs"]], patched.TagIds);
        patched = await (await user.Client.PatchAsJsonAsync(url, new { assigneeIds = new[] { sam, ana } })).ReadAsync<TaskDto>();
        Assert.Equal([ana, sam], patched.AssigneeIds);
        Assert.Equal([tags["bug"], tags["docs"]], patched.TagIds);

        // People from outside the workspace and tags of another workspace are refused, and nothing changes.
        var other = await factory.NewUserAsync();
        (await other.Client.SeedAsync(other.Workspace.Id, fillers: 0)).EnsureSuccessStatusCode();
        var otherTag = (await TagIdsAsync(other))["design"];
        Assert.Equal("Everyone assigned must be a member of this workspace.",
            await (await user.Client.PatchAsJsonAsync(url, new { assigneeIds = new[] { ana, other.Me.Id }, tagIds = new[] { tags["ops"] } })).ProblemAsync(HttpStatusCode.BadRequest));
        Assert.Equal("Tags must belong to this workspace.",
            await (await user.Client.PatchAsJsonAsync(url, new { assigneeIds = new[] { me }, tagIds = new[] { otherTag } })).ProblemAsync(HttpStatusCode.BadRequest));
        Assert.Equal("Tags must belong to this workspace.",
            await (await user.Client.PostAsJsonAsync($"/api/lists/{user.List.Id}/tasks", new { title = "No", tagIds = new[] { otherTag } })).ProblemAsync(HttpStatusCode.BadRequest));
        Assert.Equal(HttpStatusCode.BadRequest, (await user.Client.PatchAsJsonAsync(url, new { assigneeIds = "everyone" })).StatusCode);
        var unchanged = (await user.Client.ListTasksAsync(user.List.Id)).Single(t => t.Id == task.Id);
        Assert.Equal([ana, sam], unchanged.AssigneeIds);
        Assert.Equal([tags["bug"], tags["docs"]], unchanged.TagIds);

        // Empty sets clear them.
        var cleared = await (await user.Client.PatchAsJsonAsync(url, new { assigneeIds = Array.Empty<Guid>(), tagIds = Array.Empty<Guid>() })).ReadAsync<TaskDto>();
        Assert.Empty(cleared.AssigneeIds);
        Assert.Empty(cleared.TagIds);

        // Deleting the task removes its assignee and tag rows.
        (await user.Client.DeleteAsync(url)).EnsureSuccessStatusCode();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TaskloomDbContext>();
        Assert.False(await db.TaskAssignees.AnyAsync(a => a.TaskId == task.Id));
        Assert.False(await db.TaskTags.AnyAsync(t => t.TaskId == task.Id));
    }
}
