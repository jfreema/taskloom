using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Taskloom.Infrastructure;

namespace Taskloom.Api.Tests;

/// <summary>Each migration on rows the versions before it wrote, in a database of its own.</summary>
public class MigrationTests
{
    private const string Initial = "20261103223954_Initial";
    private const string Redesign = "20261104083442_Redesign";

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

    [Fact]
    public async Task SessionsAndDisabledUsers_migration_keeps_existing_users_and_starts_with_no_sessions()
    {
        var database = $"taskloom_mig_{Guid.NewGuid():N}";
        var options = new DbContextOptionsBuilder<TaskloomDbContext>().UseNpgsql($"{TaskloomFactory.DatabaseServer};Database={database}").Options;
        await using var db = new TaskloomDbContext(options);
        try
        {
            await db.GetService<IMigrator>().MigrateAsync(Redesign);
            Guid user = Guid.NewGuid(), identity = Guid.NewGuid(), workspace = Guid.NewGuid();
            var created = new DateTimeOffset(2026, 11, 5, 9, 0, 0, TimeSpan.Zero);
            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO users ("Id", "Email", "DisplayName", "CreatedAt") VALUES ({user}, 'kept@example.com', 'Kept', {created})
                """);
            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO user_identities ("Id", "UserId", "Provider", "Subject") VALUES ({identity}, {user}, 'google', 'kept-sub')
                """);
            await db.Database.ExecuteSqlAsync($"""INSERT INTO workspaces ("Id", "Name", "CreatedAt") VALUES ({workspace}, 'Kept workspace', {created})""");
            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO workspace_members ("WorkspaceId", "UserId", "Role", "JoinedAt") VALUES ({workspace}, {user}, 'Owner', {created})
                """);

            await db.GetService<IMigrator>().MigrateAsync();

            // The user is untouched and on; nobody has a session yet (old cookies sign in again).
            var kept = await db.Users.AsNoTracking().Include(u => u.Identities).SingleAsync();
            Assert.Equal((user, "kept@example.com", "Kept", created, (DateTimeOffset?)null), (kept.Id, kept.Email, kept.DisplayName, kept.CreatedAt, kept.DisabledAt));
            Assert.Equal(("google", "kept-sub"), (Assert.Single(kept.Identities).Provider, kept.Identities[0].Subject));
            Assert.Equal(1, await db.WorkspaceMembers.CountAsync());
            Assert.Equal(0, await db.UserSessions.CountAsync());

            // Sessions can be stored, and go with their user.
            db.UserSessions.Add(new Domain.UserSession
            {
                UserId = user, Provider = "google", CreatedAt = created, LastSeenAt = created, ExpiresAt = created.AddDays(30),
                UserAgent = new string('u', 300),
            });
            await db.SaveChangesAsync();
            await db.Users.Where(u => u.Id == user).ExecuteDeleteAsync();
            Assert.Equal(0, await db.UserSessions.CountAsync());
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task Notifications_migration_keeps_users_and_comments_and_turns_emails_on()
    {
        const string before = "20261119031049_ProfilePhotosAndListLooks";
        var database = $"taskloom_mig_{Guid.NewGuid():N}";
        var options = new DbContextOptionsBuilder<TaskloomDbContext>().UseNpgsql($"{TaskloomFactory.DatabaseServer};Database={database}").Options;
        await using var db = new TaskloomDbContext(options);
        try
        {
            await db.GetService<IMigrator>().MigrateAsync(before);
            Guid user = Guid.NewGuid(), other = Guid.NewGuid(), workspace = Guid.NewGuid(), space = Guid.NewGuid(), list = Guid.NewGuid(),
                status = Guid.NewGuid(), task = Guid.NewGuid(), comment = Guid.NewGuid();
            var created = new DateTimeOffset(2026, 11, 18, 9, 0, 0, TimeSpan.Zero);
            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO users ("Id", "Email", "DisplayName", "CreatedAt", "PhotoChosen")
                VALUES ({user}, 'kept@example.com', 'Kept', {created}, false), ({other}, 'other@example.com', 'Other', {created}, false)
                """);
            await db.Database.ExecuteSqlAsync($"""INSERT INTO workspaces ("Id", "Name", "CreatedAt") VALUES ({workspace}, 'Kept workspace', {created})""");
            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO workspace_members ("WorkspaceId", "UserId", "Role", "JoinedAt") VALUES ({workspace}, {user}, 'Owner', {created}), ({workspace}, {other}, 'Member', {created})
                """);
            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO spaces ("Id", "WorkspaceId", "Name", "Color", "Position", "IsPrivate") VALUES ({space}, {workspace}, 'Personal', '#14998A', 1, false)
                """);
            await db.Database.ExecuteSqlAsync($"""INSERT INTO lists ("Id", "SpaceId", "Name", "Position") VALUES ({list}, {space}, 'Tasks', 1)""");
            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO list_statuses ("Id", "ListId", "Name", "Color", "Category", "Position") VALUES ({status}, {list}, 'To do', '#7A868C', 'NotStarted', 1)
                """);
            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO tasks ("Id", "ListId", "StatusId", "Title", "Description", "DescriptionText", "Priority", "Position", "CreatedById", "CreatedAt", "UpdatedAt")
                VALUES ({task}, {list}, {status}, 'Kept task', '', '', 'None', 1, {user}, {created}, {created})
                """);
            // A comment from before mentions existed, with text that happens to look like one.
            var body = $"Ask <@{other:D}> about it";
            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO task_comments ("Id", "TaskId", "AuthorId", "Body", "CreatedAt") VALUES ({comment}, {task}, {user}, {body}, {created})
                """);

            await db.GetService<IMigrator>().MigrateAsync();

            // Everyone starts with both kinds of email on, no time zone (UTC until their browser says) and no reminder sent.
            var users = await db.Users.AsNoTracking().OrderBy(u => u.Email).ToListAsync();
            Assert.Equal(["kept@example.com", "other@example.com"], users.Select(u => u.Email).ToArray());
            Assert.All(users, u => Assert.Equal((true, true, null, null), (u.EmailMentions, u.EmailReminders, u.TimeZone, (DateOnly?)u.RemindedOn)));
            // Old comments are kept as they were, mention nobody, and nobody has an Inbox row yet.
            var kept = await db.Comments.AsNoTracking().Include(c => c.Mentions).SingleAsync();
            Assert.Equal((body, 0), (kept.Body, kept.Mentions.Count));
            Assert.Equal(0, await db.Notifications.CountAsync());

            // The new tables take rows, and the rows go with their comment, their task and their reader.
            db.CommentMentions.Add(new Domain.CommentMention { CommentId = comment, UserId = other });
            db.Notifications.Add(new Domain.Notification { UserId = other, TaskId = task, ActorId = user, Kind = Domain.NotificationKind.Mentioned, CommentId = comment, ToValue = new string('x', 500), CreatedAt = created });
            db.Notifications.Add(new Domain.Notification { UserId = other, TaskId = task, ActorId = user, Kind = Domain.NotificationKind.Assigned, Email = Domain.NotificationEmail.Pending, CreatedAt = created });
            db.Notifications.Add(new Domain.Notification { UserId = user, TaskId = task, Kind = Domain.NotificationKind.DueToday, ToValue = "2026-11-18", CreatedAt = created });
            await db.SaveChangesAsync();
            await db.Comments.Where(c => c.Id == comment).ExecuteDeleteAsync();
            Assert.Equal((0, 2), (await db.CommentMentions.CountAsync(), await db.Notifications.CountAsync()));
            await db.Users.Where(u => u.Id == other).ExecuteDeleteAsync();
            Assert.Equal(Domain.NotificationKind.DueToday, (await db.Notifications.AsNoTracking().SingleAsync()).Kind);
            await db.Tasks.Where(t => t.Id == task).ExecuteDeleteAsync();
            Assert.Equal(0, await db.Notifications.CountAsync());
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }
}
