using System.Net;
using Taskloom.Api.Tests.Support;
using Taskloom.Domain;

namespace Taskloom.Api.Tests;

/// <summary>Look 2: a list's icon and colour.</summary>
public class ListLookTests(TaskloomFactory factory) : IClassFixture<TaskloomFactory>
{
    private const string ColourMessage = "Colours must be a hex value like #7A868C.";
    private const string IconMessage = "Choose one of the list icons.";

    [Fact]
    public async Task Lists_start_plain_and_take_an_icon_and_colour()
    {
        var user = await factory.NewUserAsync();
        Assert.Equal(((string?)null, (string?)null), (user.List.Color, user.List.Icon));

        var created = await user.Client.CreateListAsync(user.Space.Id, "Launch", new { name = "Launch", color = Palette.Pink, icon = "rocket" });
        Assert.Equal((Palette.Pink, "rocket"), (created.Color, created.Icon));
        Assert.Equal(Row(created), Row(await user.Client.ListAsync(created.Id)));
        // The plain list icon and "" both mean none.
        var plain = await user.Client.CreateListAsync(user.Space.Id, "Plain", new { name = "Plain", color = "", icon = "list" });
        Assert.Equal(((string?)null, (string?)null), (plain.Color, plain.Icon));

        // PATCH changes either one and leaves the other; "" takes it away.
        Assert.Equal(HttpStatusCode.NoContent, (await user.Client.PatchAsJsonAsync($"/api/lists/{created.Id}", new { icon = "bug" })).StatusCode);
        Assert.Equal((Palette.Pink, "bug"), Look(await user.Client.ListAsync(created.Id)));
        Assert.Equal(HttpStatusCode.NoContent, (await user.Client.PatchAsJsonAsync($"/api/lists/{created.Id}", new { color = Palette.Green, name = "Launch 2" })).StatusCode);
        var renamed = await user.Client.ListAsync(created.Id);
        Assert.Equal(("Launch 2", Palette.Green, "bug"), (renamed.Name, renamed.Color, renamed.Icon));
        Assert.Equal(HttpStatusCode.NoContent, (await user.Client.PatchAsJsonAsync($"/api/lists/{created.Id}", new { color = "", icon = "" })).StatusCode);
        Assert.Equal(((string?)null, (string?)null), Look(await user.Client.ListAsync(created.Id)));
        Assert.Equal(HttpStatusCode.NoContent, (await user.Client.PatchAsJsonAsync($"/api/lists/{created.Id}", new { color = "#123abc", icon = "list" })).StatusCode);
        Assert.Equal(("#123abc", (string?)null), Look(await user.Client.ListAsync(created.Id)));

        // Every icon of the set is accepted.
        foreach (var icon in ListIcons.All.Skip(1))
            Assert.Equal(HttpStatusCode.NoContent, (await user.Client.PatchAsJsonAsync($"/api/lists/{created.Id}", new { icon })).StatusCode);
        Assert.Equal(ListIcons.All[^1], (await user.Client.ListAsync(created.Id)).Icon);
    }

    [Fact]
    public async Task Refused_looks_change_nothing()
    {
        var user = await factory.NewUserAsync();
        var list = await user.Client.CreateListAsync(user.Space.Id, "Ops", new { name = "Ops", color = Palette.Blue, icon = "wrench" });
        var url = $"/api/lists/{list.Id}";

        foreach (var color in new[] { "blue", "#12345", "#1234567", "123456", "#GGGGGG" })
        {
            Assert.Equal(ColourMessage, await (await user.Client.PatchAsJsonAsync(url, new { color })).ProblemAsync(HttpStatusCode.BadRequest));
            Assert.Equal(ColourMessage, await (await user.Client.PostAsJsonAsync($"/api/spaces/{user.Space.Id}/lists", new { name = "x", color })).ProblemAsync(HttpStatusCode.BadRequest));
        }
        foreach (var icon in new[] { "Rocket", "rocket ", "skull", "<svg>", "list-checks-2" })
        {
            Assert.Equal(IconMessage, await (await user.Client.PatchAsJsonAsync(url, new { icon, name = "Renamed" })).ProblemAsync(HttpStatusCode.BadRequest));
            Assert.Equal(IconMessage, await (await user.Client.PostAsJsonAsync($"/api/spaces/{user.Space.Id}/lists", new { name = "x", icon })).ProblemAsync(HttpStatusCode.BadRequest));
        }
        Assert.Equal(Row(list), Row(await user.Client.ListAsync(list.Id)));
        Assert.Equal(["Ops", "Tasks"], (await user.Client.TreeAsync()).Single().Spaces.Single().Lists.Select(l => l.Name).Order());
    }

    [Fact]
    public async Task Only_members_and_above_change_a_lists_look()
    {
        var owner = await factory.NewUserAsync();
        var (guest, _) = await factory.JoinAsync(owner, WorkspaceRole.Guest);
        var (member, _) = await factory.JoinAsync(owner, WorkspaceRole.Member);
        var url = $"/api/lists/{owner.List.Id}";

        Assert.Equal(HttpStatusCode.Forbidden, (await guest.PatchAsJsonAsync(url, new { icon = "star" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await member.PatchAsJsonAsync(url, new { icon = "star", color = Palette.Amber })).StatusCode);
        Assert.Equal((Palette.Amber, "star"), Look(await guest.ListAsync(owner.List.Id)));
    }

    private static (string? Color, string? Icon) Look(ListSummaryDto l) => (l.Color, l.Icon);

    private static (Guid, string, double, string?, string?, int) Row(ListSummaryDto l) => (l.Id, l.Name, l.Position, l.Color, l.Icon, l.Statuses.Count);
}
