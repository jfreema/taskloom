using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Taskloom.Infrastructure;

namespace Taskloom.Api.Tests;

/// <summary>The Redesign migration on rows the first draft wrote (spec 4.5), in a database of its own.</summary>
public class MigrationTests
{
    private const string Initial = "20261103223954_Initial";

    [Fact]
    public async Task Redesign_migration_preserves_assignees_descriptions_and_colours()
    {
        var database = $"taskloom_mig_{Guid.NewGuid():N}";
        var options = new DbContextOptionsBuilder<TaskloomDbContext>().UseNpgsql($"{TaskloomFactory.DatabaseServer};Database={database}").Options;
        await using var db = new TaskloomDbContext(options);
        try
        {
            await db.GetService<IMigrator>().MigrateAsync(Initial);

            Guid member = Guid.NewGuid(), leaver = Guid.NewGuid(), workspace = Guid.NewGuid(), space = Guid.NewGuid(), custom = Guid.NewGuid(),
                list = Guid.NewGuid(), toDo = Guid.NewGuid(), inProgress = Guid.NewGuid(), review = Guid.NewGuid(), done = Guid.NewGuid(),
                picked = Guid.NewGuid(), assigned = Guid.NewGuid(), orphaned = Guid.NewGuid(), unassigned = Guid.NewGuid();
            var created = new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);
            var updated = new DateTimeOffset(2026, 10, 20, 16, 30, 0, TimeSpan.Zero);
            const string description = "a < b & \"c\"\r\nline 2";

            // The first draft's rows: the leaver was assigned, then removed from the workspace (AssigneeId stayed).
            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO users ("Id", "Email", "DisplayName", "CreatedAt")
                VALUES ({member}, 'member@example.com', 'Member', {created}), ({leaver}, 'leaver@example.com', 'Leaver', {created})
                """);
            await db.Database.ExecuteSqlAsync($"""INSERT INTO workspaces ("Id", "Name", "CreatedAt") VALUES ({workspace}, 'Old workspace', {created})""");
            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO workspace_members ("WorkspaceId", "UserId", "Role", "JoinedAt") VALUES ({workspace}, {member}, 'Owner', {created})
                """);
            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO spaces ("Id", "WorkspaceId", "Name", "Color", "Position")
                VALUES ({space}, {workspace}, 'Personal', '#7b68ee', 1), ({custom}, {workspace}, 'Picked', '#E5484D', 2)
                """);
            await db.Database.ExecuteSqlAsync($"""INSERT INTO lists ("Id", "SpaceId", "Name", "Position") VALUES ({list}, {space}, 'Tasks', 1)""");
            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO list_statuses ("Id", "ListId", "Name", "Color", "Category", "Position") VALUES
                  ({toDo}, {list}, 'To do', '#87909e', 'NotStarted', 1),
                  ({inProgress}, {list}, 'In progress', '#4194F6', 'Active', 2),
                  ({review}, {list}, 'Review', '#a875ff', 'Active', 3),
                  ({done}, {list}, 'Done', '#3DB88B', 'Done', 4),
                  ({picked}, {list}, 'Blocked', '#123abc', 'Active', 5)
                """);
            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO tasks ("Id", "ListId", "StatusId", "Title", "Description", "Priority", "AssigneeId", "Position", "CreatedById", "CreatedAt", "UpdatedAt")
                VALUES ({assigned}, {list}, {toDo}, 'Assigned to a member', {description}, 'None', {member}, 1, {member}, {created}, {updated}),
                       ({orphaned}, {list}, {inProgress}, 'Assigned to a leaver', '', 'High', {leaver}, 2, {member}, {created}, {created}),
                       ({unassigned}, {list}, {review}, 'Nobody', 'one line', 'Low', NULL, 3, {member}, {created}, {created})
                """);

            await db.GetService<IMigrator>().MigrateAsync();

            // Only the assignee who is still a member is copied, assigned as of the task's last update.
            var assignees = await db.TaskAssignees.AsNoTracking().ToListAsync();
            var copied = Assert.Single(assignees);
            Assert.Equal((assigned, member, updated), (copied.TaskId, copied.UserId, copied.CreatedAt));

            // Plain-text descriptions become escaped HTML paragraphs, with the text kept beside them.
            var tasks = await db.Tasks.AsNoTracking().ToDictionaryAsync(t => t.Id, t => new { t.Description, t.DescriptionText });
            Assert.Equal("<p>a &lt; b &amp; &quot;c&quot;</p><p>line 2</p>", tasks[assigned].Description);
            Assert.Equal("a < b & \"c\"\nline 2", tasks[assigned].DescriptionText);
            Assert.Equal(("", ""), (tasks[orphaned].Description, tasks[orphaned].DescriptionText));
            Assert.Equal(("<p>one line</p>", "one line"), (tasks[unassigned].Description, tasks[unassigned].DescriptionText));

            // The generated colours move to the Loom palette, ignoring case; colours someone picked stay.
            var statuses = await db.Statuses.AsNoTracking().OrderBy(s => s.Position).Select(s => s.Color).ToListAsync();
            Assert.Equal(["#7A868C", "#3D7DF5", "#9B5DE5", "#2E9F5E", "#123abc"], statuses);
            var spaces = await db.Spaces.AsNoTracking().OrderBy(s => s.Position).Select(s => s.Color).ToListAsync();
            Assert.Equal(["#14998A", "#E5484D"], spaces);
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }
}
