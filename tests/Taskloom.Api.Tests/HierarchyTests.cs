using System.Net;
using Taskloom.Api.Tests.Support;
using Taskloom.Domain;

namespace Taskloom.Api.Tests;

/// <summary>Spaces, lists and their statuses (spec 4.2 presets, 5.4 #19–#26, 5.5 Lists).</summary>
public class HierarchyTests(TaskloomFactory factory) : IClassFixture<TaskloomFactory>
{
    private const string ColourMessage = "Colours must be a hex value like #7A868C.";

    [Fact]
    public async Task Spaces_default_to_teal_statuses_carry_list_id_and_default_to_slate()
    {
        var user = await factory.NewUserAsync();
        var spacesUrl = $"/api/workspaces/{user.Workspace.Id}/spaces";

        // A new space is teal unless a colour is sent, takes the name trimmed, and goes after the others.
        var res = await user.Client.PostAsJsonAsync(spacesUrl, new { name = "  Marketing  " });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var marketing = await res.ReadAsync<SpaceDto>();
        Assert.Equal($"/api/spaces/{marketing.Id}", res.Headers.Location?.OriginalString);
        Assert.Equal(("Marketing", Palette.Teal, user.Space.Position + 1024), (marketing.Name, marketing.Color, marketing.Position));
        Assert.Empty(marketing.Lists);
        var pinkId = Guid.NewGuid();
        var pink = await (await user.Client.PostAsJsonAsync(spacesUrl, new { id = pinkId, name = "Pink", color = "#DB4C8B" })).ReadAsync<SpaceDto>();
        Assert.Equal((pinkId, "#DB4C8B", marketing.Position + 1024), (pink.Id, pink.Color, pink.Position));

        // Refusals change nothing.
        var tree = await SnapshotAsync(user);
        foreach (var body in new object[] { new { }, new { name = "" }, new { name = "   " } })
            Assert.Equal("Enter a space name.", await (await user.Client.PostAsJsonAsync(spacesUrl, body)).ProblemAsync(HttpStatusCode.BadRequest));
        Assert.Equal("Space names can be up to 200 characters.", await (await user.Client.PostAsJsonAsync(spacesUrl, new { name = new string('s', 201) })).ProblemAsync(HttpStatusCode.BadRequest));
        foreach (var color in new[] { "pink", "#DB4C8", "#DB4C8B0", "DB4C8B", "#GGGGGG", "" })
            Assert.Equal(ColourMessage, await (await user.Client.PostAsJsonAsync(spacesUrl, new { name = "Bad", color })).ProblemAsync(HttpStatusCode.BadRequest));
        Assert.Equal(ColourMessage, await (await user.Client.PatchAsJsonAsync($"/api/spaces/{pink.Id}", new { color = "red" })).ProblemAsync(HttpStatusCode.BadRequest));
        Assert.Equal("Enter a space name.", await (await user.Client.PatchAsJsonAsync($"/api/spaces/{pink.Id}", new { name = " ", color = "#2E9F5E" })).ProblemAsync(HttpStatusCode.BadRequest));
        Assert.Equal(tree, await SnapshotAsync(user));

        // PATCH renames, recolours and reorders; the tree orders spaces by position.
        Assert.Equal(HttpStatusCode.NoContent, (await user.Client.PatchAsJsonAsync($"/api/spaces/{pink.Id}", new { name = " Rose ", color = "#E0484E", position = 0.5 })).StatusCode);
        var spaces = (await user.Client.TreeAsync()).Single().Spaces;
        Assert.Equal([pink.Id, user.Space.Id, marketing.Id], spaces.Select(s => s.Id));
        Assert.Equal(("Rose", "#E0484E", 0.5), (spaces[0].Name, spaces[0].Color, spaces[0].Position));
        // Sending only the colour keeps the name.
        Assert.Equal(HttpStatusCode.NoContent, (await user.Client.PatchAsJsonAsync($"/api/spaces/{marketing.Id}", new { color = "#1c93b8" })).StatusCode);
        Assert.Equal(("Marketing", "#1c93b8"), (await user.Client.TreeAsync()).Single().Spaces.Where(s => s.Id == marketing.Id).Select(s => (s.Name, s.Color)).Single());

        // A new status is slate unless a colour is sent, belongs to its list, and goes last.
        var statusesUrl = $"/api/lists/{user.List.Id}/statuses";
        var blocked = await (await user.Client.PostAsJsonAsync(statusesUrl, new { name = "  Blocked ", category = "Active" })).ReadAsync<StatusDto>();
        Assert.Equal((user.List.Id, "Blocked", Palette.Slate, StatusCategory.Active, 4096 + 1024d), (blocked.ListId, blocked.Name, blocked.Color, blocked.Category, blocked.Position));
        var shippedId = Guid.NewGuid();
        var shipped = await (await user.Client.PostAsJsonAsync(statusesUrl, new { id = shippedId, name = "Shipped", color = "#6F9A1B", category = "Done" })).ReadAsync<StatusDto>();
        Assert.Equal((shippedId, user.List.Id, "#6F9A1B", StatusCategory.Done, blocked.Position + 1024), (shipped.Id, shipped.ListId, shipped.Color, shipped.Category, shipped.Position));

        tree = await SnapshotAsync(user);
        Assert.Equal("Choose a status type: NotStarted, Active or Done.", await (await user.Client.PostAsJsonAsync(statusesUrl, new { name = "No type" })).ProblemAsync(HttpStatusCode.BadRequest));
        Assert.Equal("Enter a status name.", await (await user.Client.PostAsJsonAsync(statusesUrl, new { name = " ", category = "Active" })).ProblemAsync(HttpStatusCode.BadRequest));
        Assert.Equal("Status names can be up to 100 characters.", await (await user.Client.PostAsJsonAsync(statusesUrl, new { name = new string('n', 101), category = "Active" })).ProblemAsync(HttpStatusCode.BadRequest));
        Assert.Equal(ColourMessage, await (await user.Client.PostAsJsonAsync(statusesUrl, new { name = "Bad", category = "Active", color = "#12" })).ProblemAsync(HttpStatusCode.BadRequest));
        // Categories are names: an unknown name or a number is an unreadable body, not a stored value.
        Assert.NotNull(await (await user.Client.PostAsJsonAsync(statusesUrl, new { name = "Bad", category = "Finished" })).ProblemAsync(HttpStatusCode.BadRequest));
        Assert.NotNull(await (await user.Client.PostAsJsonAsync(statusesUrl, new { name = "Bad", category = 2 })).ProblemAsync(HttpStatusCode.BadRequest));
        Assert.NotNull(await (await user.Client.PostAsJsonAsync(statusesUrl, new { name = "Bad", category = "2" })).ProblemAsync(HttpStatusCode.BadRequest));
        Assert.Equal(tree, await SnapshotAsync(user));

        // Every status carries its list id, in the tree and in the list detail, ordered by position.
        var list = await user.Client.ListAsync(user.List.Id);
        Assert.Equal(["To do", "In progress", "Review", "Done", "Blocked", "Shipped"], list.Statuses.Select(s => s.Name));
        Assert.All(list.Statuses, s => Assert.Equal(user.List.Id, s.ListId));
        var detail = await (await user.Client.GetAsync($"/api/lists/{user.List.Id}")).ReadAsync<ListDetailDto>();
        Assert.Equal(list.Statuses, detail.Statuses);
        Assert.Equal((user.Space.Id, user.Workspace.Id), (detail.SpaceId, detail.WorkspaceId));

        // PATCH renames, recolours and reorders a status and answers with it.
        var patched = await (await user.Client.PatchAsJsonAsync($"/api/statuses/{blocked.Id}", new { name = " On hold ", color = "#B07A0A", position = 1500 })).ReadAsync<StatusDto>();
        Assert.Equal(blocked with { Name = "On hold", Color = "#B07A0A", Position = 1500 }, patched);
        Assert.Equal(["To do", "On hold", "In progress", "Review", "Done", "Shipped"], (await user.Client.ListAsync(user.List.Id)).Statuses.Select(s => s.Name));
        Assert.Equal(ColourMessage, await (await user.Client.PatchAsJsonAsync($"/api/statuses/{blocked.Id}", new { color = "amber" })).ProblemAsync(HttpStatusCode.BadRequest));
        Assert.Equal("Enter a status name.", await (await user.Client.PatchAsJsonAsync($"/api/statuses/{blocked.Id}", new { name = "", position = 1 })).ProblemAsync(HttpStatusCode.BadRequest));
        Assert.Equal(patched, (await user.Client.ListAsync(user.List.Id)).Statuses.Single(s => s.Id == blocked.Id));
        Assert.Equal(HttpStatusCode.NotFound, (await user.Client.PatchAsJsonAsync($"/api/statuses/{Guid.NewGuid()}", new { name = "Nobody" })).StatusCode);
    }

    [Fact]
    public async Task Lists_get_preset_or_copied_statuses_and_move_to_the_end_of_another_space()
    {
        var user = await factory.NewUserAsync();
        var listsUrl = $"/api/spaces/{user.Space.Id}/lists";
        async Task<ListSummaryDto> Create(object body)
        {
            var res = await user.Client.PostAsJsonAsync(listsUrl, body);
            Assert.Equal(HttpStatusCode.Created, res.StatusCode);
            var created = await res.ReadAsync<ListSummaryDto>();
            Assert.Equal($"/api/lists/{created.Id}", res.Headers.Location?.OriginalString);
            Assert.Equal(user.Space.Id, created.SpaceId);
            Assert.All(created.Statuses, s => Assert.Equal(created.Id, s.ListId));
            return created;
        }
        static (string, string, StatusCategory, double)[] Rows(ListSummaryDto list) => list.Statuses.Select(s => (s.Name, s.Color, s.Category, s.Position)).ToArray();

        // Without a preset a list gets Review; Simple and Requests give their own sets (4.2), each list last in its space.
        var review = await Create(new { name = " Default " });
        Assert.Equal(("Default", user.List.Position + 1024), (review.Name, review.Position));
        Assert.Equal(
        [
            ("To do", Palette.Slate, StatusCategory.NotStarted, 1024d), ("In progress", Palette.Blue, StatusCategory.Active, 2048d),
            ("Review", Palette.Violet, StatusCategory.Active, 3072d), ("Done", Palette.Green, StatusCategory.Done, 4096d),
        ], Rows(review));
        var simple = await Create(new { name = "Simple", statusPreset = "Simple" });
        Assert.Equal(review.Position + 1024, simple.Position);
        Assert.Equal(
        [
            ("To do", Palette.Slate, StatusCategory.NotStarted, 1024d), ("In progress", Palette.Blue, StatusCategory.Active, 2048d),
            ("Done", Palette.Green, StatusCategory.Done, 3072d),
        ], Rows(simple));
        var requests = await Create(new { id = Guid.NewGuid(), name = "Requests", statusPreset = "Requests" });
        Assert.Equal(
        [
            ("New", Palette.Slate, StatusCategory.NotStarted, 1024d), ("In progress", Palette.Blue, StatusCategory.Active, 2048d),
            ("Waiting", Palette.Amber, StatusCategory.Active, 3072d), ("Done", Palette.Green, StatusCategory.Done, 4096d),
        ], Rows(requests));
        Assert.Equal(requests.Statuses, (await user.Client.ListAsync(requests.Id)).Statuses);

        // Copying takes the source's statuses as they are now (names, colours, categories, positions), with new ids,
        // and wins over statusPreset.
        await user.Client.PatchAsJsonAsync($"/api/statuses/{requests.Statuses[2].Id}", new { name = "On hold", color = "#123ABC" });
        await user.Client.PostAsJsonAsync($"/api/lists/{requests.Id}/statuses", new { name = "Blocked", color = "#E0484E", category = "Active" });
        await user.Client.PatchAsJsonAsync($"/api/statuses/{requests.Statuses[0].Id}", new { position = 5000 });
        var source = await user.Client.ListAsync(requests.Id);
        var copy = await Create(new { name = "Copy", statusPreset = "Simple", statusesFromListId = requests.Id });
        Assert.Equal(["In progress", "On hold", "Done", "New", "Blocked"], copy.Statuses.Select(s => s.Name));
        Assert.Equal(Rows(source), Rows(copy));
        Assert.Empty(copy.Statuses.Select(s => s.Id).Intersect(source.Statuses.Select(s => s.Id)));
        Assert.Equal(copy.Statuses, (await user.Client.ListAsync(copy.Id)).Statuses);
        // The source keeps its own statuses.
        Assert.Equal(source.Statuses, (await user.Client.ListAsync(requests.Id)).Statuses);

        // Refusals create nothing.
        var tree = await SnapshotAsync(user);
        Assert.Equal("Choose a list in this workspace to copy statuses from.",
            await (await user.Client.PostAsJsonAsync(listsUrl, new { name = "Nowhere", statusesFromListId = Guid.NewGuid() })).ProblemAsync(HttpStatusCode.BadRequest));
        Assert.Equal("Enter a list name.", await (await user.Client.PostAsJsonAsync(listsUrl, new { name = "  ", statusPreset = "Simple" })).ProblemAsync(HttpStatusCode.BadRequest));
        Assert.Equal("List names can be up to 200 characters.", await (await user.Client.PostAsJsonAsync(listsUrl, new { name = new string('l', 201) })).ProblemAsync(HttpStatusCode.BadRequest));
        foreach (var preset in new object[] { "Kanban", 1, "1", "" })
            Assert.NotNull(await (await user.Client.PostAsJsonAsync(listsUrl, new { name = "Bad preset", statusPreset = preset })).ProblemAsync(HttpStatusCode.BadRequest));
        Assert.Equal(tree, await SnapshotAsync(user));

        // Moving a list to another space puts it last there, and its tasks go with it.
        var archive = await (await user.Client.PostAsJsonAsync($"/api/workspaces/{user.Workspace.Id}/spaces", new { name = "Archive" })).ReadAsync<SpaceDto>();
        var old1 = await user.Client.CreateListAsync(archive.Id, "Old 1");
        var old2 = await user.Client.CreateListAsync(archive.Id, "Old 2");
        Assert.Equal((1024d, 2048d), (old1.Position, old2.Position));
        var parent = await user.Client.CreateTaskAsync(simple.Id, new { title = "Parent" });
        var sub = await user.Client.CreateTaskAsync(simple.Id, new { title = "Sub", parentTaskId = parent.Id });
        var tasksUrl = $"/api/lists/{simple.Id}/tasks";
        var tasks = await (await user.Client.GetAsync(tasksUrl)).Content.ReadAsStringAsync();
        Assert.Equal(2, (await user.Client.ListTasksAsync(simple.Id)).Count);

        Assert.Equal(HttpStatusCode.NoContent, (await user.Client.PatchAsJsonAsync($"/api/lists/{simple.Id}", new { spaceId = archive.Id })).StatusCode);
        var spaces = (await user.Client.TreeAsync()).Single().Spaces;
        Assert.DoesNotContain(simple.Id, spaces.Single(s => s.Id == user.Space.Id).Lists.Select(l => l.Id));
        var moved = spaces.Single(s => s.Id == archive.Id).Lists;
        Assert.Equal([old1.Id, old2.Id, simple.Id], moved.Select(l => l.Id));
        Assert.Equal((simple.Id, archive.Id, "Simple", 3072d), (moved[2].Id, moved[2].SpaceId, moved[2].Name, moved[2].Position));
        Assert.Equal(simple.Statuses, moved[2].Statuses);
        Assert.Equal(tasks, await (await user.Client.GetAsync(tasksUrl)).Content.ReadAsStringAsync());
        Assert.Equal((parent.Id, simple.Id), ((await user.Client.TaskDetailAsync(sub.Id)).Parent?.Id, (await user.Client.TaskDetailAsync(sub.Id)).Task.ListId));
        Assert.Equal(archive.Id, (await (await user.Client.GetAsync($"/api/lists/{simple.Id}")).ReadAsync<ListDetailDto>()).SpaceId);

        // With a position too, the position is kept; the same space or a rename alone leaves the position as it is.
        Assert.Equal(HttpStatusCode.NoContent, (await user.Client.PatchAsJsonAsync($"/api/lists/{review.Id}", new { spaceId = archive.Id, position = 10 })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await user.Client.PatchAsJsonAsync($"/api/lists/{old2.Id}", new { spaceId = archive.Id, name = " Older " })).StatusCode);
        moved = (await user.Client.TreeAsync()).Single().Spaces.Single(s => s.Id == archive.Id).Lists;
        Assert.Equal([(review.Id, 10d), (old1.Id, 1024d), (old2.Id, 2048d), (simple.Id, 3072d)], moved.Select(l => (l.Id, l.Position)));
        Assert.Equal("Older", moved[2].Name);

        // A space that doesn't exist, or a blank name, is refused and nothing moves.
        tree = await SnapshotAsync(user);
        Assert.Equal("Choose a space in this workspace.", await (await user.Client.PatchAsJsonAsync($"/api/lists/{copy.Id}", new { spaceId = Guid.NewGuid() })).ProblemAsync(HttpStatusCode.BadRequest));
        Assert.Equal("Enter a list name.", await (await user.Client.PatchAsJsonAsync($"/api/lists/{copy.Id}", new { name = "", spaceId = archive.Id })).ProblemAsync(HttpStatusCode.BadRequest));
        Assert.Equal(tree, await SnapshotAsync(user));
    }

    /// <summary>The caller's whole tree as text, to show that a refused request changed nothing.</summary>
    private static async Task<string> SnapshotAsync(TestUser user) =>
        await (await user.Client.GetAsync("/api/workspaces")).Content.ReadAsStringAsync();
}
