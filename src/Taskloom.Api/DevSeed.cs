using Microsoft.EntityFrameworkCore;
using Taskloom.Application;
using Taskloom.Domain;
using Taskloom.Infrastructure;

namespace Taskloom.Api;

/// <summary>
/// POST /api/dev/seed (spec 11.3): adds a realistic copy of sample content to a workspace, with dates relative to the
/// server's current UTC date. Development and the end-to-end tests only. Each call adds a new copy (no idempotency);
/// the shared people and tags are reused, so parallel seeds don't collide.
/// </summary>
public class DevSeed(TaskloomDbContext db)
{
    public const int DefaultFillers = 48;
    public const int MaxFillers = 2000;

    public const string AnaEmail = "ana@example.com", AnaName = "Ana Moreno";
    public const string SamEmail = "sam@example.com", SamName = "Sam Lee";

    /// <summary>Filler titles: indexes 0–19 go to Website refresh, 20–35 to Admin and 36–47 to Personal / Tasks.</summary>
    public static readonly string[] FillerTitles =
    [
        // Website refresh
        "Audit current page speed", "Draft new homepage copy", "Choose hero image", "Update navigation labels",
        "Write FAQ section", "Review accessibility report", "Set up redirects for old URLs", "Compress product images",
        "Add cookie banner", "Test contact form", "Update team photos", "Refresh case studies", "Fix broken footer links",
        "Add structured data", "Proofread About page", "Prepare launch checklist", "Check mobile layout",
        "Update privacy policy link", "Create 404 page", "Plan launch announcement",
        // Admin
        "Renew software licences", "Order office supplies", "Update vendor contacts", "Reconcile October expenses",
        "Schedule fire drill", "Review insurance policy", "File quarterly VAT return", "Archive 2025 contracts",
        "Set up new starter laptop", "Book meeting room for review", "Update emergency contact list", "Pay electricity bill",
        "Collect timesheets", "Cancel unused subscriptions", "Prepare board pack", "Check parking permits",
        // Personal / Tasks
        "Book dentist appointment", "Buy birthday present for Sam", "Return library books", "Plan weekend hike",
        "Renew gym membership", "Fix bike puncture", "Back up phone photos", "Call the bank about card",
        "Pick up dry cleaning", "Sort recycling", "Order new glasses", "Write thank-you notes",
    ];

    private static readonly int?[] FillerDueOffsets = [-5, -2, 0, 1, 2, 3, 5, 7, 9, 12, 14, 20, 30, null, null, null];

    private static readonly TaskPriority[] FillerPriorities =
    [
        TaskPriority.None, TaskPriority.Normal, TaskPriority.High, TaskPriority.Low,
        TaskPriority.None, TaskPriority.Urgent, TaskPriority.Normal, TaskPriority.None,
    ];

    private static readonly string?[] FillerTags = ["design", "docs", null, "bug", "ops"];

    private static readonly (string Name, string Color)[] SeedTags =
        [("design", Palette.Violet), ("bug", Palette.Red), ("docs", Palette.Cyan), ("ops", Palette.Amber)];

    public const string OnboardingDescription =
        """<h2>Outline</h2><ul data-type="taskList"><li data-type="taskItem" data-checked="true"><label><input type="checkbox" checked></label><div><p>Welcome page</p></div></li><li data-type="taskItem" data-checked="false"><label><input type="checkbox"></label><div><p>First task walkthrough</p></div></li></ul>""";

    private readonly Dictionary<Guid, double> _nextPosition = [];
    private DateTimeOffset _now;
    private DateTimeOffset _createdAt;
    private Guid _me;

    public async Task RunAsync(Guid me, Guid workspaceId, int fillers, CancellationToken ct = default)
    {
        _me = me;
        _now = DateTimeOffset.UtcNow;
        var today = DateOnly.FromDateTime(_now.UtcDateTime);
        // Seed rows count as created two weeks ago, one second apart in seed order.
        _createdAt = At(today.AddDays(-14), 9);

        // The shared people, before the transaction so concurrent seeds never hold each other's row locks.
        var ana = await UpsertUserAsync(AnaEmail, AnaName, ct);
        var sam = await UpsertUserAsync(SamEmail, SamName, ct);

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // An existing membership is left as it is.
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO workspace_members ("WorkspaceId", "UserId", "Role", "JoinedAt")
            VALUES ({workspaceId}, {ana.Id}, {nameof(WorkspaceRole.Member)}, {_now}), ({workspaceId}, {sam.Id}, {nameof(WorkspaceRole.Guest)}, {_now})
            ON CONFLICT ("WorkspaceId", "UserId") DO NOTHING
            """, ct);

        // A tag that already exists by name is reused.
        for (var i = 0; i < SeedTags.Length; i++)
        {
            var (name, color) = SeedTags[i];
            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO tags ("Id", "WorkspaceId", "Name", "NormalizedName", "Color", "CreatedAt")
                VALUES ({Guid.NewGuid()}, {workspaceId}, {name}, {Tag.Normalize(name)}, {color}, {_now.AddMilliseconds(i)})
                ON CONFLICT ("WorkspaceId", "NormalizedName") DO NOTHING
                """, ct);
        }
        var normalizedTagNames = SeedTags.Select(t => Tag.Normalize(t.Name)).ToList();
        var tags = await db.Tags.AsNoTracking()
            .Where(t => t.WorkspaceId == workspaceId && normalizedTagNames.Contains(t.NormalizedName))
            .ToDictionaryAsync(t => t.NormalizedName, ct);

        // ---- Structure: the existing Personal / Tasks list (created if missing) and a new Work space ----
        var lastSpace = await db.Spaces.Where(s => s.WorkspaceId == workspaceId).MaxAsync(s => (double?)s.Position, ct);
        var personal = await db.Spaces.Where(s => s.WorkspaceId == workspaceId && s.Name == "Personal")
            .OrderBy(s => s.Position).ThenBy(s => s.Id).FirstOrDefaultAsync(ct);
        if (personal is null)
        {
            personal = new Space { WorkspaceId = workspaceId, Name = "Personal", Color = Palette.Teal, Position = Positions.Between(lastSpace, null) };
            lastSpace = personal.Position;
            db.Spaces.Add(personal);
        }
        var personalTasks = await db.Lists.Where(l => l.SpaceId == personal.Id && l.Name == "Tasks")
            .OrderBy(l => l.Position).ThenBy(l => l.Id).FirstOrDefaultAsync(ct);
        List<ListStatus> personalStatuses;
        if (personalTasks is null)
        {
            var lastList = await db.Lists.Where(l => l.SpaceId == personal.Id).MaxAsync(l => (double?)l.Position, ct);
            personalTasks = new TaskList { SpaceId = personal.Id, Name = "Tasks", Position = Positions.Between(lastList, null) };
            personalStatuses = ListStatus.Defaults(personalTasks.Id);
            db.Lists.Add(personalTasks);
            db.Statuses.AddRange(personalStatuses);
        }
        else
        {
            personalStatuses = await db.Statuses.Where(s => s.ListId == personalTasks.Id).ToListAsync(ct);
            _nextPosition[personalTasks.Id] = (await db.Tasks.Where(t => t.ListId == personalTasks.Id).MaxAsync(t => (double?)t.Position, ct) ?? 0) + Positions.Step;
        }

        var work = new Space { WorkspaceId = workspaceId, Name = "Work", Color = Palette.Blue, Position = Positions.Between(lastSpace, null) };
        var website = new TaskList { SpaceId = work.Id, Name = "Website refresh", Position = Positions.Step };
        var admin = new TaskList { SpaceId = work.Id, Name = "Admin", Position = 2 * Positions.Step };
        var websiteStatuses = ListStatus.ForPreset(website.Id, StatusPreset.Review);
        var adminStatuses = ListStatus.ForPreset(admin.Id, StatusPreset.Simple);
        db.Spaces.Add(work);
        db.Lists.AddRange(website, admin);
        db.Statuses.AddRange(websiteStatuses);
        db.Statuses.AddRange(adminStatuses);

        ListStatus ToDo(List<ListStatus> s) => StatusNamed(s, "To do", StatusCategory.NotStarted);
        ListStatus InProgress(List<ListStatus> s) => StatusNamed(s, "In progress", StatusCategory.Active);
        ListStatus Review(List<ListStatus> s) => StatusNamed(s, "Review", StatusCategory.Active);
        ListStatus Done(List<ListStatus> s) => StatusNamed(s, "Done", StatusCategory.Done);
        Guid[] Me() => [me];
        Guid[] Ana() => [ana.Id];

        // ---- Named tasks (11.3) ----
        var onboarding = AddTask(website, InProgress(websiteStatuses), "Write onboarding guide", today, TaskPriority.High,
            [me, ana.Id], ["design", "docs"], tags, description: OnboardingDescription);
        AddTask(website, Review(websiteStatuses), "Fix sign-in redirect", today.AddDays(-1), TaskPriority.Urgent, Me(), ["bug"], tags);
        AddTask(website, ToDo(websiteStatuses), "Plan Q1 roadmap", today.AddDays(3), TaskPriority.Normal, [], [], tags);
        AddTask(website, ToDo(websiteStatuses), "Update pricing page", today.AddDays(10), TaskPriority.None, Ana(), ["design"], tags);
        AddTask(website, Done(websiteStatuses), "Set up analytics", today.AddDays(-3), TaskPriority.None, Me(), [], tags, completedAt: _now.AddDays(-3));
        AddTask(admin, ToDo(adminStatuses), "Send invoice to Contoso", today.AddDays(-2), TaskPriority.High, Me(), ["ops"], tags);
        AddTask(admin, InProgress(adminStatuses), "Renew domain names", today.AddDays(1), TaskPriority.None, Me(), [], tags);
        AddTask(admin, ToDo(adminStatuses), "Book team offsite", null, TaskPriority.None, Me(), [], tags);
        AddTask(admin, Done(adminStatuses), "Archive old receipts", null, TaskPriority.None, Me(), [], tags, completedAt: _now.AddDays(-2));
        AddTask(personalTasks, ToDo(personalStatuses), "Call the printer company", today, TaskPriority.None, [], [], tags);
        AddTask(personalTasks, ToDo(personalStatuses), "Read Q3 report", null, TaskPriority.Low, [], [], tags);
        AddTask(personalTasks, InProgress(personalStatuses), "Renew passport", today.AddDays(20), TaskPriority.None, [], [], tags);

        // Task 1's subtasks, comments and one activity row of every kind, so the feed shows every wording.
        var yesterday = today.AddDays(-1);
        var subtaskTime = At(yesterday, 9).AddMinutes(12);
        AddTask(website, Done(websiteStatuses), "Draft outline", null, TaskPriority.None, Me(), [], tags,
            parent: onboarding, createdAt: subtaskTime, completedAt: At(yesterday, 12));
        AddTask(website, ToDo(websiteStatuses), "Collect screenshots", today.AddDays(2), TaskPriority.None, Ana(), [], tags,
            parent: onboarding, createdAt: subtaskTime.AddSeconds(1));
        AddTask(website, ToDo(websiteStatuses), "Review with Ana", null, TaskPriority.None, [], [], tags,
            parent: onboarding, createdAt: subtaskTime.AddSeconds(2));

        db.Comments.AddRange(
            new TaskComment { TaskId = onboarding.Id, AuthorId = me, Body = "Draft is in the shared folder, comments welcome.", CreatedAt = At(yesterday, 15) },
            new TaskComment { TaskId = onboarding.Id, AuthorId = ana.Id, Body = "Looks good, can we add screenshots?", CreatedAt = _now.AddMinutes(-10) });

        string TagName(string normalized) => tags.TryGetValue(normalized, out var t) ? t.Name : normalized;
        (TaskActivityKind Kind, string? From, string? To, Guid Actor)[] history =
        [
            (TaskActivityKind.Renamed, "Write onboarding doc", onboarding.Title, me),
            (TaskActivityKind.PriorityChanged, nameof(TaskPriority.Normal), nameof(TaskPriority.High), me),
            (TaskActivityKind.DueDateChanged, ActivityValues.Date(today.AddDays(1)), ActivityValues.Date(today), me),
            (TaskActivityKind.StartDateChanged, ActivityValues.Date(yesterday), null, me),
            (TaskActivityKind.AssigneeAdded, null, ana.DisplayName, me),
            (TaskActivityKind.AssigneeRemoved, sam.DisplayName, null, ana.Id),
            (TaskActivityKind.TagAdded, null, TagName("docs"), me),
            (TaskActivityKind.TagRemoved, TagName("ops"), null, me),
            (TaskActivityKind.DescriptionChanged, null, null, me),
            (TaskActivityKind.MovedList, admin.Name, website.Name, me),
            (TaskActivityKind.AttachmentAdded, null, "brief.pdf", me),
            (TaskActivityKind.AttachmentRemoved, "brief.pdf", null, me),
            (TaskActivityKind.SubtaskAdded, null, "Draft outline", me),
        ];
        db.Activities.AddRange(history.Select((h, i) => new TaskActivity
        {
            TaskId = onboarding.Id, ActorId = h.Actor, Kind = h.Kind,
            FromValue = ActivityValues.Clip(h.From), ToValue = ActivityValues.Clip(h.To), CreatedAt = At(yesterday, 9).AddMinutes(i),
        }));
        db.Activities.Add(new TaskActivity
        {
            TaskId = onboarding.Id, ActorId = me, Kind = TaskActivityKind.StatusChanged,
            FromValue = ToDo(websiteStatuses).Name, ToValue = InProgress(websiteStatuses).Name, CreatedAt = _now.AddHours(-1),
        });

        // ---- Fillers: deterministic, top-level, appended after the named tasks ----
        var displayOrder = new Dictionary<Guid, List<ListStatus>>
        {
            [website.Id] = InDisplayOrder(websiteStatuses),
            [admin.Id] = InDisplayOrder(adminStatuses),
            [personalTasks.Id] = InDisplayOrder(personalStatuses),
        };
        for (var i = 0; i < fillers; i++)
        {
            var slot = i % FillerTitles.Length;
            var round = i / FillerTitles.Length;
            var list = slot < 20 ? website : slot < 36 ? admin : personalTasks;
            var isWork = list != personalTasks;
            var statuses = displayOrder[list.Id];
            var status = statuses[i % statuses.Count];
            var due = FillerDueOffsets[i % FillerDueOffsets.Length] is { } offset ? today.AddDays(offset) : (DateOnly?)null;
            Guid[] assignees = (i % 4) switch
            {
                0 or 1 => [me],
                2 => isWork ? [ana.Id] : [me],
                _ => [],
            };
            string[] tagNames = isWork && FillerTags[i % FillerTags.Length] is { } tag ? [tag] : [];
            var title = round == 0 ? FillerTitles[slot] : $"{FillerTitles[slot]} ({round + 1})";
            AddTask(list, status, title, due, FillerPriorities[i % FillerPriorities.Length], assignees, tagNames, tags,
                completedAt: _now.AddDays(-(i % 10)));
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    /// <summary>
    /// Adds a task created by the caller. Top-level tasks take the next position of their list (subtasks of their
    /// parent), so seed order is display order. A task in a Done status is completed at <paramref name="completedAt"/>.
    /// </summary>
    private TaskItem AddTask(TaskList list, ListStatus status, string title, DateOnly? due, TaskPriority priority,
        Guid[] assigneeIds, string[] tagNames, Dictionary<string, Tag> tags, TaskItem? parent = null,
        DateTimeOffset? createdAt = null, DateTimeOffset? completedAt = null, string? description = null)
    {
        var html = RichText.Sanitize(description);
        var created = createdAt ?? NextCreatedAt();
        var task = new TaskItem
        {
            ListId = list.Id,
            StatusId = status.Id,
            ParentTaskId = parent?.Id,
            Title = title,
            Description = html,
            DescriptionText = RichText.ToPlainText(html),
            Priority = priority,
            DueDate = due,
            Position = NextPosition(parent?.Id ?? list.Id),
            CreatedById = _me,
            CreatedAt = created,
            UpdatedAt = _now,
            CompletedAt = status.Category == StatusCategory.Done ? completedAt ?? _now : null,
        };
        db.Tasks.Add(task);
        // Assignment order is the order given (the avatar stack orders by assignment time).
        db.TaskAssignees.AddRange(assigneeIds.Distinct().Select((userId, i) =>
            new TaskAssignee { TaskId = task.Id, UserId = userId, CreatedAt = created.AddSeconds(i) }));
        db.TaskTags.AddRange(tagNames.Select(Tag.Normalize).Distinct().Where(tags.ContainsKey)
            .Select(name => new TaskTag { TaskId = task.Id, TagId = tags[name].Id }));
        return task;
    }

    private double NextPosition(Guid groupId)
    {
        var position = _nextPosition.GetValueOrDefault(groupId, Positions.Step);
        _nextPosition[groupId] = position + Positions.Step;
        return position;
    }

    private DateTimeOffset NextCreatedAt()
    {
        var created = _createdAt;
        _createdAt = _createdAt.AddSeconds(1);
        return created;
    }

    /// <summary>Inserts the user unless one with the email exists, then reads it back; safe when seeds run in parallel.</summary>
    private async Task<User> UpsertUserAsync(string email, string displayName, CancellationToken ct)
    {
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO users ("Id", "Email", "DisplayName", "CreatedAt")
            VALUES ({Guid.NewGuid()}, {email}, {displayName}, {_now})
            ON CONFLICT ("Email") DO NOTHING
            """, ct);
        return await db.Users.AsNoTracking().FirstAsync(u => u.Email == email, ct);
    }

    /// <summary>The status with this name, else the first of the category by position, else the first status.</summary>
    private static ListStatus StatusNamed(List<ListStatus> statuses, string name, StatusCategory category) =>
        statuses.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase))
        ?? statuses.Where(s => s.Category == category).OrderBy(s => s.Position).FirstOrDefault()
        ?? statuses.OrderBy(s => s.Position).First();

    /// <summary>Display order (spec 10.2): Not started, Active, Done, then position.</summary>
    private static List<ListStatus> InDisplayOrder(List<ListStatus> statuses) =>
        statuses.OrderBy(s => s.Category).ThenBy(s => s.Position).ThenBy(s => s.Id).ToList();

    private static DateTimeOffset At(DateOnly day, int hour) => new(day.ToDateTime(new TimeOnly(hour, 0)), TimeSpan.Zero);
}
