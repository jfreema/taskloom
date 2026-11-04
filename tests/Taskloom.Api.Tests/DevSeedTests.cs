using System.Net;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Taskloom.Api.Tests.Support;
using Taskloom.Domain;
using Taskloom.Infrastructure;

namespace Taskloom.Api.Tests;

/// <summary>POST /api/dev/seed (spec 11.3).</summary>
public class DevSeedTests(TaskloomFactory factory) : IClassFixture<TaskloomFactory>
{
    private record Seeded(ListSummaryDto Website, ListSummaryDto Admin, ListSummaryDto Personal, List<TaskDto> Tasks);

    private static async Task<Seeded> ReadSeedAsync(TestUser user, int workIndex = 0)
    {
        var tree = (await user.Client.TreeAsync()).Single(w => w.Id == user.Workspace.Id);
        var work = tree.Spaces.Where(s => s.Name == "Work").ElementAt(workIndex);
        var personal = tree.Spaces.Single(s => s.Name == "Personal").Lists.Single(l => l.Name == "Tasks");
        var tasks = new List<TaskDto>();
        foreach (var list in work.Lists.Append(personal)) tasks.AddRange(await user.Client.ListTasksAsync(list.Id));
        return new Seeded(work.Lists.Single(l => l.Name == "Website refresh"), work.Lists.Single(l => l.Name == "Admin"), personal, tasks);
    }

    private static async Task<Dictionary<string, TagDto>> TagsAsync(TestUser user) =>
        (await user.Client.TagsAsync(user.Workspace.Id)).ToDictionary(t => t.Name);

    [Fact]
    public async Task Dev_seed_creates_the_dataset()
    {
        var user = await factory.NewUserAsync();
        var before = DateOnly.FromDateTime(DateTime.UtcNow);
        Assert.Equal(HttpStatusCode.NoContent, (await user.Client.SeedAsync(user.Workspace.Id)).StatusCode);
        var after = DateOnly.FromDateTime(DateTime.UtcNow);

        // Structure: a new Work space (blue) with Website refresh (Review preset) and Admin (Simple); the existing Personal / Tasks.
        var tree = (await user.Client.TreeAsync()).Single();
        var work = Assert.Single(tree.Spaces, s => s.Name == "Work");
        Assert.Equal(Palette.Blue, work.Color);
        Assert.Equal(["Website refresh", "Admin"], work.Lists.Select(l => l.Name));
        var seeded = await ReadSeedAsync(user);
        Assert.Equal(user.List.Id, seeded.Personal.Id);
        Assert.Equal(["To do", "In progress", "Review", "Done"], seeded.Website.Statuses.Select(s => s.Name));
        Assert.Equal(["To do", "In progress", "Done"], seeded.Admin.Statuses.Select(s => s.Name));

        // Four tags with their colours.
        var tags = await TagsAsync(user);
        Assert.Equal(new Dictionary<string, string> { ["design"] = "#9B5DE5", ["bug"] = "#E0484E", ["docs"] = "#1C93B8", ["ops"] = "#B07A0A" },
            tags.ToDictionary(t => t.Key, t => t.Value.Color));
        Assert.Equal(["bug", "design", "docs", "ops"], (await user.Client.TagsAsync(user.Workspace.Id)).Select(t => t.Name));

        // Two pending members: Ana (Member) and Sam (Guest).
        var members = await (await user.Client.GetAsync($"/api/workspaces/{user.Workspace.Id}/members")).ReadAsync<List<MemberDto>>();
        var ana = members.Single(m => m.Email == "ana@example.com");
        var sam = members.Single(m => m.Email == "sam@example.com");
        Assert.Equal(("Ana Moreno", WorkspaceRole.Member, true), (ana.DisplayName, ana.Role, ana.Pending));
        Assert.Equal(("Sam Lee", WorkspaceRole.Guest, true), (sam.DisplayName, sam.Role, sam.Pending));
        Assert.Equal(3, members.Count);

        // The twelve named tasks with their fields, relative to T (today in UTC).
        var t = seeded.Tasks.Single(x => x.Title == "Call the printer company").DueDate!.Value;
        Assert.True(t == before || t == after);
        var me = user.Me.Id;
        void Named(string title, ListSummaryDto list, string status, int? due, TaskPriority priority, Guid[] assignees, string[] tagNames, int? completedDaysAgo = null)
        {
            var task = seeded.Tasks.Single(x => x.Title == title);
            Assert.Equal(list.Id, task.ListId);
            Assert.Equal(status, list.Statuses.Single(s => s.Id == task.StatusId).Name);
            Assert.Equal(due is null ? (DateOnly?)null : t.AddDays(due.Value), task.DueDate);
            Assert.Equal(priority, task.Priority);
            Assert.Equal(assignees, task.AssigneeIds);
            Assert.Equal(tagNames.Select(n => tags[n].Id), task.TagIds);
            Assert.Null(task.ParentTaskId);
            Assert.Equal(me, task.CreatedById);
            Assert.Null(task.StartDate);
            if (completedDaysAgo is null) Assert.Null(task.CompletedAt);
            else Assert.Equal(t.AddDays(-completedDaysAgo.Value), DateOnly.FromDateTime(task.CompletedAt!.Value.UtcDateTime));
        }
        Named("Write onboarding guide", seeded.Website, "In progress", 0, TaskPriority.High, [me, ana.UserId], ["design", "docs"]);
        Named("Fix sign-in redirect", seeded.Website, "Review", -1, TaskPriority.Urgent, [me], ["bug"]);
        Named("Plan Q1 roadmap", seeded.Website, "To do", 3, TaskPriority.Normal, [], []);
        Named("Update pricing page", seeded.Website, "To do", 10, TaskPriority.None, [ana.UserId], ["design"]);
        Named("Set up analytics", seeded.Website, "Done", -3, TaskPriority.None, [me], [], completedDaysAgo: 3);
        Named("Send invoice to Contoso", seeded.Admin, "To do", -2, TaskPriority.High, [me], ["ops"]);
        Named("Renew domain names", seeded.Admin, "In progress", 1, TaskPriority.None, [me], []);
        Named("Book team offsite", seeded.Admin, "To do", null, TaskPriority.None, [me], []);
        Named("Archive old receipts", seeded.Admin, "Done", null, TaskPriority.None, [me], [], completedDaysAgo: 2);
        Named("Call the printer company", seeded.Personal, "To do", 0, TaskPriority.None, [], []);
        Named("Read Q3 report", seeded.Personal, "To do", null, TaskPriority.Low, [], []);
        Named("Renew passport", seeded.Personal, "In progress", 20, TaskPriority.None, [], []);

        // Task 1: description, subtasks, comments and one activity row of every kind.
        var onboarding = seeded.Tasks.Single(x => x.Title == "Write onboarding guide");
        Assert.True(onboarding.HasDescription);
        Assert.Equal((3, 1, 2, 0), (onboarding.SubtaskCount, onboarding.SubtaskDoneCount, onboarding.CommentCount, onboarding.AttachmentCount));
        var detail = await user.Client.TaskDetailAsync(onboarding.Id);
        Assert.Equal(RichText.Sanitize(DevSeed.OnboardingDescription), detail.Description);
        Assert.Contains("data-type=\"taskList\"", detail.Description);
        Assert.Equal(["Draft outline", "Collect screenshots", "Review with Ana"], detail.Subtasks.Select(s => s.Title));
        Assert.NotNull(detail.Subtasks[0].CompletedAt);
        Assert.Equal([me], detail.Subtasks[0].AssigneeIds);
        Assert.Equal([ana.UserId], detail.Subtasks[1].AssigneeIds);
        Assert.Equal(t.AddDays(2), detail.Subtasks[1].DueDate);
        Assert.Empty(detail.Subtasks[2].AssigneeIds);
        Assert.All(detail.Subtasks, s => Assert.Equal(onboarding.Id, s.ParentTaskId));
        Assert.Equal([(me, "Draft is in the shared folder, comments welcome."), (ana.UserId, "Looks good, can we add screenshots?")],
            detail.Comments.Select(c => (c.Author.Id, c.Body)));
        Assert.Equal(new DateTimeOffset(t.AddDays(-1).ToDateTime(new TimeOnly(15, 0)), TimeSpan.Zero), detail.Comments[0].CreatedAt);
        Assert.Equal(Enum.GetValues<TaskActivityKind>().Order(), detail.Activity.Select(a => a.Kind).Order());
        Assert.Equal(detail.Activity.Select(a => a.CreatedAt).Order(), detail.Activity.Select(a => a.CreatedAt));
        var status = detail.Activity[^1];
        Assert.Equal((TaskActivityKind.StatusChanged, "To do", "In progress", me), (status.Kind, status.From, status.To, status.Actor.Id));
        var dueChange = detail.Activity.Single(a => a.Kind == TaskActivityKind.DueDateChanged);
        Assert.Equal((t.AddDays(1).ToString("yyyy-MM-dd"), t.ToString("yyyy-MM-dd")), (dueChange.From, dueChange.To));
        var startChange = detail.Activity.Single(a => a.Kind == TaskActivityKind.StartDateChanged);
        Assert.Equal((t.AddDays(-1).ToString("yyyy-MM-dd"), (string?)null), (startChange.From, startChange.To));
        var removed = detail.Activity.Single(a => a.Kind == TaskActivityKind.AssigneeRemoved);
        Assert.Equal(("Sam Lee", (string?)null, ana.UserId), (removed.From, removed.To, removed.Actor.Id));
        Assert.Equal(("Admin", "Website refresh"), detail.Activity.Where(a => a.Kind == TaskActivityKind.MovedList).Select(a => (a.From, a.To)).Single());
        Assert.Equal(new DateTimeOffset(t.AddDays(-1).ToDateTime(new TimeOnly(9, 0)), TimeSpan.Zero), detail.Activity[0].CreatedAt);
        Assert.Equal((TaskActivityKind.Renamed, "Write onboarding doc", "Write onboarding guide"), (detail.Activity[0].Kind, detail.Activity[0].From, detail.Activity[0].To));

        // 48 fillers, top level: 20 in Website refresh, 16 in Admin, 12 in Personal / Tasks.
        var fillers = seeded.Tasks.Where(x => DevSeed.FillerTitles.Contains(x.Title)).ToList();
        Assert.Equal(48, fillers.Count);
        Assert.All(fillers, f => Assert.Null(f.ParentTaskId));
        Assert.Equal((20, 16, 12), (fillers.Count(f => f.ListId == seeded.Website.Id), fillers.Count(f => f.ListId == seeded.Admin.Id), fillers.Count(f => f.ListId == seeded.Personal.Id)));
        void Filler(string title, string status, int? due, TaskPriority priority, Guid[] assignees, string[] tagNames, int? completedDaysAgo = null)
        {
            var f = fillers.Single(x => x.Title == title);
            var list = new[] { seeded.Website, seeded.Admin, seeded.Personal }.Single(l => l.Id == f.ListId);
            Assert.Equal(status, list.Statuses.Single(s => s.Id == f.StatusId).Name);
            Assert.Equal(due is null ? (DateOnly?)null : t.AddDays(due.Value), f.DueDate);
            Assert.Equal(priority, f.Priority);
            Assert.Equal(assignees, f.AssigneeIds);
            Assert.Equal(tagNames.Select(n => tags[n].Id), f.TagIds);
            if (completedDaysAgo is null) Assert.Null(f.CompletedAt);
            else Assert.Equal(t.AddDays(-completedDaysAgo.Value), DateOnly.FromDateTime(f.CompletedAt!.Value.UtcDateTime));
        }
        Filler("Audit current page speed", "To do", -5, TaskPriority.None, [me], ["design"]);              // i = 0
        Filler("Choose hero image", "Review", 0, TaskPriority.High, [ana.UserId], []);                     // i = 2
        Filler("Update navigation labels", "Done", 1, TaskPriority.Low, [], ["bug"], completedDaysAgo: 3); // i = 3
        Filler("Renew software licences", "Done", 2, TaskPriority.None, [me], ["design"], completedDaysAgo: 0); // i = 20
        Filler("Order office supplies", "To do", 3, TaskPriority.Urgent, [me], ["docs"]);                  // i = 21
        Filler("Book dentist appointment", "To do", 2, TaskPriority.None, [me], []);                       // i = 36
        Filler("Return library books", "Review", 5, TaskPriority.Normal, [me], []);                        // i = 38
        Filler("Call the bank about card", "Done", 20, TaskPriority.Low, [], [], completedDaysAgo: 3);    // i = 43
        // Fillers come after the named tasks of their list.
        var websiteTop = (await user.Client.ListTasksAsync(seeded.Website.Id)).Where(x => x.ParentTaskId is null).ToList();
        Assert.True(websiteTop.Where(x => DevSeed.FillerTitles.Contains(x.Title)).Min(x => x.Position) >
                    websiteTop.Where(x => !DevSeed.FillerTitles.Contains(x.Title)).Max(x => x.Position));

        // "walkthrough" appears only in task 1's description, and searching finds it through the plain-text copy.
        var hits = await user.Client.WorkspaceTasksAsync(user.Workspace.Id, "?scope=all&q=walkthrough&includeDone=true");
        Assert.Equal([onboarding.Id], hits.Tasks.Select(x => x.Id));
        Assert.Empty(hits.Parents);

        // Another call adds another copy; fillers: 5 gives 5.
        Assert.Equal(HttpStatusCode.NoContent, (await user.Client.SeedAsync(user.Workspace.Id, fillers: 5)).StatusCode);
        Assert.Equal(2, (await user.Client.TreeAsync()).Single().Spaces.Count(s => s.Name == "Work"));
        var second = await ReadSeedAsync(user, workIndex: 1);
        var workFillers = second.Tasks.Where(x => x.ListId != second.Personal.Id && DevSeed.FillerTitles.Contains(x.Title)).ToList();
        Assert.Equal(["Audit current page speed", "Draft new homepage copy", "Choose hero image", "Update navigation labels", "Write FAQ section"],
            workFillers.OrderBy(x => x.CreatedAt).Select(x => x.Title));
        Assert.Equal(15 + 3, second.Tasks.Count(x => x.ListId == second.Personal.Id));
        Assert.Equal(4, (await TagsAsync(user)).Count);
        Assert.Equal(3, (await (await user.Client.GetAsync($"/api/workspaces/{user.Workspace.Id}/members")).ReadAsync<List<MemberDto>>()).Count);
    }

    [Fact]
    public async Task Two_parallel_seeds_both_succeed()
    {
        var a = await factory.NewUserAsync();
        var b = await factory.NewUserAsync();

        // Two workers seeding their own workspaces at once, plus a second seed of the same workspace.
        var results = await Task.WhenAll(
            a.Client.SeedAsync(a.Workspace.Id, fillers: 60),
            b.Client.SeedAsync(b.Workspace.Id, fillers: 60),
            a.Client.SeedAsync(a.Workspace.Id, fillers: 60));
        Assert.All(results, r => Assert.Equal(HttpStatusCode.NoContent, r.StatusCode));

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TaskloomDbContext>();
            Assert.Equal(1, await db.Users.CountAsync(u => u.Email == "ana@example.com"));
            Assert.Equal(1, await db.Users.CountAsync(u => u.Email == "sam@example.com"));
        }
        Assert.Equal(2, (await a.Client.TreeAsync()).Single().Spaces.Count(s => s.Name == "Work"));
        Assert.Equal(1, (await b.Client.TreeAsync()).Single().Spaces.Count(s => s.Name == "Work"));
        Assert.Equal(4, (await TagsAsync(a)).Count);
        Assert.Equal(4, (await TagsAsync(b)).Count);

        // Over 48 fillers, the titles repeat with " (2)".
        var seeded = await ReadSeedAsync(b);
        Assert.Equal(60, seeded.Tasks.Count(x => DevSeed.FillerTitles.Contains(x.Title) || x.Title.EndsWith(" (2)")));
        Assert.Contains(seeded.Tasks, x => x.Title == "Audit current page speed (2)" && x.ListId == seeded.Website.Id);
        Assert.Contains(seeded.Tasks, x => x.Title == "Renew software licences" && x.ListId == seeded.Admin.Id);
    }

    [Fact]
    public async Task Dev_seed_requires_the_owner_and_is_not_mapped_without_dev_login()
    {
        var owner = await factory.NewUserAsync();
        var memberEmail = Builders.NewEmail();
        var adminEmail = Builders.NewEmail();
        await owner.Client.InviteAsync(owner.Workspace.Id, memberEmail, WorkspaceRole.Member);
        await owner.Client.InviteAsync(owner.Workspace.Id, adminEmail, WorkspaceRole.Admin);
        var member = await factory.SignInAsync(memberEmail);
        var admin = await factory.SignInAsync(adminEmail);
        var stranger = await factory.NewUserAsync();

        Assert.Equal(HttpStatusCode.Forbidden, (await member.SeedAsync(owner.Workspace.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.SeedAsync(owner.Workspace.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.Client.SeedAsync(owner.Workspace.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.Client.SeedAsync(Guid.NewGuid())).StatusCode);
        Assert.Equal("Fillers must be between 0 and 2000.", await (await owner.Client.SeedAsync(owner.Workspace.Id, -1)).ProblemAsync(HttpStatusCode.BadRequest));
        Assert.Equal("Fillers must be between 0 and 2000.", await (await owner.Client.SeedAsync(owner.Workspace.Id, 2001)).ProblemAsync(HttpStatusCode.BadRequest));
        owner.Client.DefaultRequestHeaders.Remove("X-Taskloom");
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.Client.SeedAsync(owner.Workspace.Id)).StatusCode);
        Assert.DoesNotContain((await stranger.Client.TreeAsync()).Single().Spaces, s => s.Name == "Work");
        Assert.DoesNotContain((await member.TreeAsync()).Single(w => w.Id == owner.Workspace.Id).Spaces, s => s.Name == "Work");

        static bool HasSeed(IServiceProvider services) => services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>().Any(e => e.RoutePattern.RawText == "/api/dev/seed");
        Assert.True(HasSeed(factory.Services));
        await using var withoutDevLogin = new NoDevLoginFactory();
        Assert.False(HasSeed(withoutDevLogin.Services));
    }
}
