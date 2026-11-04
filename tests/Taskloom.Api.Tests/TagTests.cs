using System.Net;
using Taskloom.Api.Tests.Support;
using Taskloom.Domain;

namespace Taskloom.Api.Tests;

/// <summary>Workspace tags (spec 5.4 #15–#18, 5.5 Tags).</summary>
public class TagTests(TaskloomFactory factory) : IClassFixture<TaskloomFactory>
{
    [Fact]
    public async Task Tags_round_trip_and_deleting_one_removes_it_from_tasks()
    {
        var user = await factory.NewUserAsync();
        var url = $"/api/workspaces/{user.Workspace.Id}/tags";

        // Create: 201 with the tag, the name trimmed, slate unless a colour is sent, a client id kept.
        var start = DateTimeOffset.UtcNow.AddSeconds(-1);
        var res = await user.Client.PostAsJsonAsync(url, new { name = "  Design  " });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var design = await res.ReadAsync<TagDto>();
        Assert.Equal($"/api/tags/{design.Id}", res.Headers.Location?.OriginalString);
        Assert.Equal((user.Workspace.Id, "Design", Palette.Slate), (design.WorkspaceId, design.Name, design.Color));
        Assert.InRange(design.CreatedAt, start, DateTimeOffset.UtcNow.AddSeconds(1));
        var bugId = Guid.NewGuid();
        var bug = await (await user.Client.PostAsJsonAsync(url, new { id = bugId, name = "bug", color = "#E0484E" })).ReadAsync<TagDto>();
        Assert.Equal((bugId, "#E0484E"), (bug.Id, bug.Color));
        var alpha = await user.Client.CreateTagAsync(user.Workspace.Id, "Alpha", "#6e6cf0");

        // The list is ordered by name ignoring case.
        Assert.Equal([alpha, bug, design], await user.Client.TagsAsync(user.Workspace.Id));

        // Names are unique in the workspace ignoring case (the message names the tag that exists); colours are #RRGGBB.
        var before = await user.Client.TagsAsync(user.Workspace.Id);
        foreach (var name in new[] { "design", " DESIGN ", "Design" })
            Assert.Equal("A tag called “Design” already exists.", await (await user.Client.PostAsJsonAsync(url, new { name })).ProblemAsync(HttpStatusCode.Conflict));
        Assert.Equal("A tag called “Design” already exists.", await (await user.Client.PatchAsJsonAsync($"/api/tags/{bug.Id}", new { name = "dEsIgN" })).ProblemAsync(HttpStatusCode.Conflict));
        foreach (var color in new[] { "red", "#E0484", "#E0484E1", "E0484E", "#E0484G", "" })
        {
            Assert.Equal("Colours must be a hex value like #7A868C.", await (await user.Client.PostAsJsonAsync(url, new { name = "Coloured", color })).ProblemAsync(HttpStatusCode.BadRequest));
            Assert.Equal("Colours must be a hex value like #7A868C.", await (await user.Client.PatchAsJsonAsync($"/api/tags/{bug.Id}", new { color })).ProblemAsync(HttpStatusCode.BadRequest));
        }
        foreach (var body in new object[] { new { }, new { name = "" }, new { name = "   " } })
            Assert.Equal("Enter a tag name.", await (await user.Client.PostAsJsonAsync(url, body)).ProblemAsync(HttpStatusCode.BadRequest));
        Assert.Equal("Enter a tag name.", await (await user.Client.PatchAsJsonAsync($"/api/tags/{bug.Id}", new { name = " ", color = "#2E9F5E" })).ProblemAsync(HttpStatusCode.BadRequest));
        Assert.Equal("Tag names can be up to 50 characters.", await (await user.Client.PostAsJsonAsync(url, new { name = new string('t', 51) })).ProblemAsync(HttpStatusCode.BadRequest));
        Assert.Equal(before, await user.Client.TagsAsync(user.Workspace.Id));
        Assert.Equal(HttpStatusCode.NotFound, (await user.Client.PatchAsJsonAsync($"/api/tags/{Guid.NewGuid()}", new { name = "Nobody" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await user.Client.DeleteAsync($"/api/tags/{Guid.NewGuid()}")).StatusCode);

        // Another workspace may use the same name, and its tags are 404 here.
        var other = await factory.NewUserAsync();
        var theirs = await other.Client.CreateTagAsync(other.Workspace.Id, "design");
        Assert.Equal("design", theirs.Name);
        Assert.Equal(HttpStatusCode.NotFound, (await user.Client.PatchAsJsonAsync($"/api/tags/{theirs.Id}", new { name = "Mine" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await user.Client.DeleteAsync($"/api/tags/{theirs.Id}")).StatusCode);
        Assert.Equal([theirs], await other.Client.TagsAsync(other.Workspace.Id));

        // PATCH changes what it is sent; a tag may change the case of its own name.
        var renamed = await (await user.Client.PatchAsJsonAsync($"/api/tags/{design.Id}", new { name = " DESIGN " })).ReadAsync<TagDto>();
        Assert.Equal(design with { Name = "DESIGN" }, renamed);
        var recoloured = await (await user.Client.PatchAsJsonAsync($"/api/tags/{bug.Id}", new { color = "#D2661B" })).ReadAsync<TagDto>();
        Assert.Equal(bug with { Color = "#D2661B" }, recoloured);
        var both = await (await user.Client.PatchAsJsonAsync($"/api/tags/{alpha.Id}", new { name = "Zulu", color = "#1C93B8" })).ReadAsync<TagDto>();
        Assert.Equal(alpha with { Name = "Zulu", Color = "#1C93B8" }, both);
        Assert.Equal([recoloured, renamed, both], await user.Client.TagsAsync(user.Workspace.Id));
        // The freed name can be taken again.
        var newAlpha = await user.Client.CreateTagAsync(user.Workspace.Id, "alpha");
        Assert.Equal("alpha", newAlpha.Name);

        // Tasks carry tags; deleting one removes it from every task, subtasks and other lists included, and keeps the rest.
        var second = await user.Client.CreateListAsync(user.Space.Id, "Second");
        var parent = await user.Client.CreateTaskAsync(user.List.Id, new { title = "Parent", tagIds = new[] { design.Id, bug.Id } });
        var sub = await user.Client.CreateTaskAsync(user.List.Id, new { title = "Sub", parentTaskId = parent.Id, tagIds = new[] { design.Id } });
        var elsewhere = await user.Client.CreateTaskAsync(second.Id, new { title = "Elsewhere", tagIds = new[] { bug.Id, design.Id, newAlpha.Id } });
        Assert.Equal(new[] { design.Id, bug.Id }.Order(), parent.TagIds.Order());

        Assert.Equal(HttpStatusCode.NoContent, (await user.Client.DeleteAsync($"/api/tags/{design.Id}")).StatusCode);
        Assert.Equal([bug.Id], (await user.Client.TaskDetailAsync(parent.Id)).Task.TagIds);
        Assert.Empty((await user.Client.TaskDetailAsync(sub.Id)).Task.TagIds);
        Assert.Equal(new[] { bug.Id, newAlpha.Id }.Order(), (await user.Client.TaskDetailAsync(elsewhere.Id)).Task.TagIds.Order());
        Assert.DoesNotContain(design.Id, (await user.Client.TagsAsync(user.Workspace.Id)).Select(t => t.Id));
        Assert.DoesNotContain(await user.Client.ListTasksAsync(user.List.Id), t => t.TagIds.Contains(design.Id));
        // The deleted tag can't be used any more, and is 404.
        Assert.Equal(HttpStatusCode.BadRequest, (await user.Client.PatchAsJsonAsync($"/api/tasks/{sub.Id}", new { tagIds = new[] { design.Id } })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await user.Client.DeleteAsync($"/api/tags/{design.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await user.Client.PatchAsJsonAsync($"/api/tags/{design.Id}", new { color = "#2E9F5E" })).StatusCode);
        // Its name is free again.
        Assert.Equal("Design", (await user.Client.CreateTagAsync(user.Workspace.Id, "Design")).Name);
    }
}
