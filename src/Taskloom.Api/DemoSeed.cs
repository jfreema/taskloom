using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Taskloom.Application;
using Taskloom.Domain;
using Taskloom.Infrastructure;

namespace Taskloom.Api;

/// <summary>
/// POST /api/dev/demo-seed: a large, realistic set of demo data for trying the dashboard and the views. Five spaces
/// (one private), fourteen lists with their own status sets, about 310 tasks and 200 subtasks spread over the last two
/// months and the next six weeks, eight demo people, tags, comments and a few repeating tasks. Development only.
/// </summary>
/// <remarks>
/// Safe next to real data: it only adds. The demo spaces have ids derived from the workspace id
/// (<see cref="SpaceId"/>), so "replace" and "remove" delete exactly those spaces (with whatever is in them) and
/// nothing else. The demo people stay members of the workspace after a remove; tags that already existed are reused.
/// No Inbox rows or emails are made. The random numbers have a fixed seed, so every run builds the same data, dated
/// around the given day.
/// </remarks>
public class DemoSeed(TaskloomDbContext db)
{
    public const string Marker = "taskloom-demo";

    private sealed record Person(string Key, string Name, string Email);

    private static readonly Person[] People =
    [
        new("maya", "Maya Okafor", "maya.okafor@example.com"),
        new("leo", "Leo Brandt", "leo.brandt@example.com"),
        new("priya", "Priya Raman", "priya.raman@example.com"),
        new("tom", "Tom Alvarez", "tom.alvarez@example.com"),
        new("hana", "Hana Sato", "hana.sato@example.com"),
        new("dev", "Dev Patel", "dev.patel@example.com"),
        new("chloe", "Chloe Martin", "chloe.martin@example.com"),
        new("sam", "Sam Whitaker", "sam.whitaker@example.com"),
    ];

    private static readonly (string Name, string Color)[] Tags =
    [
        ("bug", Palette.Red), ("design", Palette.Violet), ("docs", Palette.Cyan), ("ops", Palette.Amber),
        ("frontend", Palette.Blue), ("backend", Palette.Indigo), ("customer", Palette.Pink), ("blocked", Palette.Orange),
        ("quick win", Palette.Lime), ("q4", Palette.Teal),
    ];

    private sealed record ListSpec(string Name, StatusPreset Preset, string Color, string Icon, string[] Tags, string[] Titles,
        int RepeatEvery = 0);

    private sealed record SpaceSpec(string Key, string Name, string Color, string[] People, bool Private, ListSpec[] Lists);

    private static readonly SpaceSpec[] Spaces =
    [
        new("product", "Product", Palette.Indigo, ["me", "maya", "leo", "priya", "dev"], false,
        [
            new("Roadmap", StatusPreset.Review, Palette.Indigo, "target", ["design", "backend", "frontend", "q4"],
            [
                "Define success metrics for onboarding", "Spec bulk export", "Research offline mode", "Interview five churned customers",
                "Prototype the new search", "Write the Q1 product brief", "Size the permissions rework", "Decide on mobile push",
                "Plan the public API beta", "Draft pricing tiers for teams", "Map the import flows", "Audit notification settings",
                "Kick off the reporting project", "Review competitor dashboards", "Write the accessibility statement",
                "Plan the workspace templates", "Validate calendar sync demand", "Run the roadmap review", "Prioritise the feedback backlog",
                "Draft the release naming scheme", "Scope recurring task improvements", "Collect requirements for time tracking",
            ]),
            new("Bugs", StatusPreset.Requests, Palette.Red, "bug", ["bug", "frontend", "backend", "customer", "blocked"],
            [
                "Board drag drops into the wrong column", "Date picker skips a day near DST", "Search misses accented titles",
                "Avatar stack overflows on small cards", "Upload fails for files with emoji names", "Calendar tray flickers on resize",
                "Duplicate toast after undo", "Sidebar loses scroll position", "Comment edit resets the cursor", "Slow list with 2,000 tasks",
                "Status colour wrong in dark mode", "Keyboard focus lost after closing the panel", "Export CSV has wrong encoding",
                "Due today badge shows on completed tasks", "Mention list includes removed members", "Safari crops the task sheet",
                "Tag chip truncates too early", "Login redirect loops behind proxy", "Session expiry message is blank",
                "Subtask count off by one after move", "Paste of a table loses rows", "Priority menu opens off screen",
                "Recurring task skips February", "Inbox count stuck after mark all read",
            ]),
            new("Sprint backlog", StatusPreset.Review, Palette.Blue, "code", ["frontend", "backend", "quick win", "blocked"],
                Combine(["Refactor", "Add tests for", "Speed up", "Add logging to", "Document", "Clean up", "Review", "Upgrade"],
                    ["the task list", "the board view", "the calendar", "search", "file uploads", "the inbox", "sign-in", "the API client",
                     "notifications", "recurring tasks", "the sidebar", "bulk edits"], 64, 7)),
            new("Release 4.2", StatusPreset.Review, Palette.Teal, "rocket", ["frontend", "backend", "docs", "quick win"],
            [
                "Freeze the release branch", "Write the release notes", "Update the changelog", "Run the full regression suite",
                "Smoke test on staging", "Update screenshots in the help centre", "Bump the API version", "Migrate the staging database",
                "Prepare the rollback plan", "Announce the maintenance window", "Check translations", "Tag the release",
                "Post-release health check", "Close the milestone",
            ]),
        ]),
        new("marketing", "Marketing", Palette.Pink, ["me", "hana", "chloe", "tom", "sam"], false,
        [
            new("Campaigns", StatusPreset.Review, Palette.Pink, "megaphone", ["design", "customer", "q4"],
            [
                "Brief the agency on the spring campaign", "Pick the campaign tagline", "Book paid social for launch week",
                "Design the email header", "Record the product walkthrough video", "Set up UTM tracking", "Plan the webinar",
                "Write landing page copy", "A/B test the signup CTA", "Report on October campaign results", "Line up customer quotes",
                "Order conference swag", "Plan the referral programme", "Draft the partner newsletter", "Review ad spend with finance",
                "Refresh the brand deck", "Prepare the holiday promotion", "Shortlist podcast sponsorships",
            ]),
            new("Content calendar", StatusPreset.Simple, Palette.Orange, "calendar", ["docs", "customer"],
            [
                "Blog: five ways to run a weekly review", "Blog: what's new in 4.2", "Case study with Northwind", "Newsletter for December",
                "Update the getting started guide", "LinkedIn post about the team offsite", "Write the year in review",
                "Guest post for the productivity blog", "Edit the customer story video", "Plan January's content themes",
                "Refresh the comparison page", "Interview a power user", "Draft the template gallery copy", "Schedule social posts for the week",
                "Translate the top three articles",
            ], RepeatEvery: 1),
            new("Website", StatusPreset.Review, Palette.Blue, "globe", ["design", "frontend", "quick win"],
            [
                "Redesign the pricing page", "Speed up the homepage", "Fix broken links in the footer", "Add the new logo to the press kit",
                "Set up cookie consent", "Add structured data for reviews", "Improve the 404 page", "Update team photos",
                "Write alt text for every image", "Move the blog to the new CMS", "Check mobile layout of the docs",
            ]),
        ]),
        new("operations", "Operations", Palette.Amber, ["me", "tom", "priya", "sam", "chloe"], false,
        [
            new("Hiring", StatusPreset.Requests, Palette.Green, "users", ["ops", "blocked"],
            [
                "Post the senior engineer role", "Screen designer applicants", "Schedule panel interviews", "Write the onboarding plan for Dev",
                "Order a laptop for the new starter", "Update the careers page", "Check references for the PM candidate",
                "Send the offer letter", "Plan the interview training", "Review the hiring pipeline", "Set up the intern programme",
                "Update the job ladder",
            ]),
            new("Finance", StatusPreset.Simple, Palette.Amber, "wallet", ["ops", "q4"],
            [
                "Reconcile November expenses", "Pay contractor invoices", "File the quarterly VAT return", "Review SaaS subscriptions",
                "Prepare the board financials", "Update the budget forecast", "Renew the business insurance", "Chase overdue invoices",
                "Set the 2027 budget", "Close the books for October", "Approve expense claims", "Review payroll changes",
            ], RepeatEvery: 1),
            new("Support queue", StatusPreset.Requests, Palette.Cyan, "headphones", ["customer", "bug", "quick win"],
                Combine(["Contoso", "Northwind", "Fabrikam", "Tailspin", "Adventure Works", "Litware", "Wingtip", "Proseware",
                         "Woodgrove", "Lucerne", "Alpine Ski House", "Coho Winery"],
                    ["can't export to CSV", "asks for an invoice copy", "wants SSO set up", "reports slow page loads", "needs a seat added",
                     "asks about the API limits", "lost access after a role change", "wants a demo of dashboards", "reports missing emails",
                     "asks to merge two workspaces"], 80, 11, ": ")),
            new("IT requests", StatusPreset.Requests, Palette.Slate, "wrench", ["ops", "quick win", "blocked"],
            [
                "Reset VPN access for Leo", "Replace the meeting room screen", "Set up SSO for the design tool", "Renew the SSL certificates",
                "Enable MFA for everyone", "Order new headsets", "Rotate the shared Wi-Fi password", "Back up the old file server",
                "Fix printer on the second floor", "Upgrade laptops to the new OS",
            ]),
        ]),
        new("leadership", "Leadership", Palette.Slate, ["me", "maya", "tom"], true,
        [
            new("Planning", StatusPreset.Simple, Palette.Violet, "briefcase", ["q4", "blocked"],
            [
                "Draft the 2027 strategy memo", "Agree on next year's OKRs", "Plan the leadership offsite", "Review compensation bands",
                "Prepare the investor update", "Decide on the office move", "Run the org health survey", "Write the board agenda",
                "Review the headcount plan", "Plan the all-hands",
            ]),
        ]),
        new("home", "Home & life", Palette.Green, ["me"], false,
        [
            new("Errands", StatusPreset.Simple, Palette.Lime, "shopping-cart", ["quick win"],
            [
                "Pick up the dry cleaning", "Buy a birthday gift for Mum", "Return the library books", "Book the car service",
                "Renew the passport", "Order new running shoes", "Get the bike tuned", "Drop off the donations",
                "Buy groceries for the weekend", "Renew the gym membership", "Book a dentist appointment", "Water the plants",
            ], RepeatEvery: 1),
            new("House projects", StatusPreset.Simple, Palette.Brown, "house", ["blocked"],
            [
                "Paint the spare room", "Fix the leaking tap", "Clear out the garage", "Get quotes for the new boiler",
                "Hang the shelves in the office", "Plant the spring bulbs", "Replace the smoke alarm batteries", "Sort the loft insulation",
                "Clean the gutters",
            ]),
        ]),
    ];

    /// <summary><paramref name="count"/> distinct "first second" titles in a fixed shuffled order.</summary>
    private static string[] Combine(string[] first, string[] second, int count, int seed, string join = " ")
    {
        var all = first.SelectMany(a => second.Select(b => $"{a}{join}{b}")).ToArray();
        new Random(seed).Shuffle(all);
        return all.Take(count).ToArray();
    }

    private static readonly string[] SubtaskTitles =
    [
        "Write the first draft", "Get feedback", "Final review", "Update the docs", "QA pass", "Share with the team",
        "Collect inputs", "Book time on the calendar", "Sign-off", "Follow up",
    ];

    private static readonly string[] CommentBodies =
    [
        "I've started on this, should have something by Thursday.", "Can we push this to next week? Waiting on legal.",
        "Looks good to me 👍", "Added notes in the doc.", "This is blocked until the API change ships.",
        "Done on my side, over to you.", "Let's discuss in standup.", "Customer asked about this again today.",
        "I think we can cut scope here.", "Updated the due date to match the release.",
    ];

    /// <summary>The demo space of a workspace for a key: a name-based id (SHA-1 of the workspace id and key).</summary>
    public static Guid SpaceId(Guid workspaceId, string key)
    {
        var hash = SHA1.HashData(Encoding.UTF8.GetBytes($"{Marker}:{workspaceId:N}:{key}"));
        var bytes = hash[..16];
        bytes[7] = (byte)((bytes[7] & 0x0F) | 0x50); // version 5 (in .NET's little-endian layout of the third group)
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes);
    }

    public static List<Guid> SpaceIds(Guid workspaceId) => Spaces.Select(s => SpaceId(workspaceId, s.Key)).ToList();

    /// <summary>Whether the workspace already has any of the demo spaces.</summary>
    public Task<bool> ExistsAsync(Guid workspaceId, CancellationToken ct = default)
    {
        var ids = SpaceIds(workspaceId);
        return db.Spaces.AnyAsync(s => ids.Contains(s.Id), ct);
    }

    /// <summary>Deletes the demo spaces and everything in them (CASCADE). Returns how many spaces went.</summary>
    public Task<int> RemoveAsync(Guid workspaceId, CancellationToken ct = default)
    {
        var ids = SpaceIds(workspaceId);
        return db.Spaces.Where(s => s.WorkspaceId == workspaceId && ids.Contains(s.Id)).ExecuteDeleteAsync(ct);
    }

    private Random _random = new(42);
    private DateTimeOffset _now;
    private DateOnly _today;
    private readonly Dictionary<Guid, double> _positions = [];

    public sealed record Result(int Spaces, int Lists, int Tasks, int Subtasks, int People);

    /// <summary>Builds the demo data (replacing an earlier copy). <paramref name="today"/> is the caller's date.</summary>
    public async Task<Result> RunAsync(Guid me, Guid workspaceId, DateOnly today, CancellationToken ct = default)
    {
        _random = new Random(42);
        _now = DateTimeOffset.UtcNow;
        _today = today;

        // The demo people, outside the transaction (as DevSeed does), so parallel seeds never wait on each other.
        foreach (var p in People)
            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO users ("Id", "Email", "DisplayName", "CreatedAt") VALUES ({Guid.NewGuid()}, {p.Email}, {p.Name}, {_now})
                ON CONFLICT ("Email") DO NOTHING
                """, ct);
        var emails = People.Select(p => p.Email).ToList();
        var users = await db.Users.AsNoTracking().Where(u => emails.Contains(u.Email)).ToDictionaryAsync(u => u.Email, ct);
        var people = People.ToDictionary(p => p.Key, p => users[p.Email].Id);
        people["me"] = me;

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await RemoveAsync(workspaceId, ct);

        foreach (var p in People)
            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO workspace_members ("WorkspaceId", "UserId", "Role", "JoinedAt")
                VALUES ({workspaceId}, {people[p.Key]}, {nameof(WorkspaceRole.Member)}, {_now})
                ON CONFLICT ("WorkspaceId", "UserId") DO NOTHING
                """, ct);
        for (var i = 0; i < Tags.Length; i++)
            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO tags ("Id", "WorkspaceId", "Name", "NormalizedName", "Color", "CreatedAt")
                VALUES ({Guid.NewGuid()}, {workspaceId}, {Tags[i].Name}, {Tag.Normalize(Tags[i].Name)}, {Tags[i].Color}, {_now.AddMilliseconds(i)})
                ON CONFLICT ("WorkspaceId", "NormalizedName") DO NOTHING
                """, ct);
        var tagNames = Tags.Select(t => Tag.Normalize(t.Name)).ToList();
        var tags = await db.Tags.AsNoTracking().Where(t => t.WorkspaceId == workspaceId && tagNames.Contains(t.NormalizedName))
            .ToDictionaryAsync(t => t.NormalizedName, t => t.Id, ct);

        var lastSpace = await db.Spaces.Where(s => s.WorkspaceId == workspaceId).MaxAsync(s => (double?)s.Position, ct) ?? 0;
        int lists = 0, tasks = 0, subtasks = 0;
        foreach (var spec in Spaces)
        {
            lastSpace += Positions.Step;
            var space = new Space
            {
                Id = SpaceId(workspaceId, spec.Key), WorkspaceId = workspaceId, Name = spec.Name, Color = spec.Color,
                Position = lastSpace, IsPrivate = spec.Private,
            };
            db.Spaces.Add(space);
            if (spec.Private)
                db.SpaceMembers.AddRange(spec.People.Select((k, i) => new SpaceMember { SpaceId = space.Id, UserId = people[k], AddedAt = _now.AddMilliseconds(i) }));
            var crew = spec.People.Select(k => people[k]).ToArray();

            for (var li = 0; li < spec.Lists.Length; li++)
            {
                var ls = spec.Lists[li];
                var list = new TaskList { SpaceId = space.Id, Name = ls.Name, Position = Positions.Step * (li + 1), Color = ls.Color, Icon = ls.Icon };
                var statuses = ListStatus.ForPreset(list.Id, ls.Preset);
                db.Lists.Add(list);
                db.Statuses.AddRange(statuses);
                lists++;
                foreach (var title in ls.Titles)
                {
                    var (task, subs) = AddTask(list, statuses, title, ls, crew, me, tags);
                    tasks++;
                    subtasks += subs;
                    if (_random.NextDouble() < 0.12) AddComments(task, crew);
                }
            }
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new Result(Spaces.Length, lists, tasks, subtasks, People.Length);
    }

    private (TaskItem Task, int Subtasks) AddTask(TaskList list, List<ListStatus> statuses, string title, ListSpec spec, Guid[] crew,
        Guid me, Dictionary<string, Guid> tags)
    {
        var createdDaysAgo = _random.Next(0, 60);
        var created = At(_today.AddDays(-createdDaysAgo), 8 + _random.Next(0, 10), _random.Next(0, 60));
        if (created > _now) created = _now.AddMinutes(-_random.Next(10, 240));
        var roll = _random.NextDouble();
        // About a third done, a third under way and a third not started.
        var category = roll < 0.36 ? StatusCategory.Done : roll < 0.66 ? StatusCategory.Active : StatusCategory.NotStarted;
        var choices = statuses.Where(s => s.Category == category).ToList();
        var status = choices[_random.Next(choices.Count)];

        DateTimeOffset? completed = null;
        if (category == StatusCategory.Done)
        {
            // Finished some time after it was made, mostly recently, so the trend has a shape.
            var span = Math.Max(0, createdDaysAgo);
            var doneDaysAgo = span == 0 ? 0 : _random.Next(0, Math.Min(span, _random.NextDouble() < 0.7 ? 14 : 45) + 1);
            completed = At(_today.AddDays(-doneDaysAgo), 9 + _random.Next(0, 9), _random.Next(0, 60));
            if (completed < created) completed = created.AddHours(2);
            if (completed > _now) completed = _now.AddMinutes(-_random.Next(5, 300));
        }

        DateOnly? due = null;
        if (_random.NextDouble() > 0.14)
        {
            due = category == StatusCategory.Done
                ? DateOnly.FromDateTime(completed!.Value.UtcDateTime).AddDays(_random.Next(-3, 6))
                : _today.AddDays(_random.NextDouble() < 0.22 ? -_random.Next(1, 21) : _random.Next(0, 42));
        }
        DateOnly? start = category == StatusCategory.Active && due is { } d && _random.NextDouble() < 0.4 ? d.AddDays(-_random.Next(2, 10)) : null;
        if (start > _today) start = _today;

        var priority = Pick([(TaskPriority.None, 26), (TaskPriority.Low, 14), (TaskPriority.Normal, 32), (TaskPriority.High, 19), (TaskPriority.Urgent, 9)]);
        var assignees = PickAssignees(crew, me);
        var author = crew.Length > 1 && _random.NextDouble() < 0.35 ? crew[_random.Next(crew.Length)] : me;
        var tagIds = spec.Tags.Where(_ => _random.NextDouble() < 0.22).Select(t => tags[Tag.Normalize(t)]).Take(2).ToList();

        var task = NewTask(list, status, title, created, completed, due, start, priority, author, assignees, tagIds, parent: null);
        if (spec.RepeatEvery > 0 && category != StatusCategory.Done && due is { } dd && _random.NextDouble() < 0.3)
        {
            var unit = _random.NextDouble() < 0.5 ? RepeatUnit.Week : RepeatUnit.Month;
            task.Repeat = Recurrence.Normalize(new RepeatRule { Unit = unit, Every = spec.RepeatEvery }, dd);
        }

        var subtaskCount = 0;
        if (_random.NextDouble() < 0.22)
        {
            subtaskCount = _random.Next(2, 5);
            var start0 = _random.Next(SubtaskTitles.Length);
            var notStarted = statuses.Where(s => s.Category == StatusCategory.NotStarted).OrderBy(s => s.Position).First();
            var doneStatus = statuses.Where(s => s.Category == StatusCategory.Done).OrderBy(s => s.Position).First();
            for (var i = 0; i < subtaskCount; i++)
            {
                // A finished parent has finished subtasks; otherwise the first ones are more likely done.
                var subDone = category == StatusCategory.Done || _random.NextDouble() < 0.5 - 0.15 * i;
                var subCompleted = subDone ? (completed ?? _now.AddHours(-_random.Next(2, 200))) : (DateTimeOffset?)null;
                if (subCompleted < created) subCompleted = created.AddMinutes(30);
                DateOnly? subDue = due is { } pd && _random.NextDouble() < 0.6 ? pd.AddDays(-_random.Next(0, 4)) : null;
                NewTask(list, subDone ? doneStatus : notStarted, SubtaskTitles[(start0 + i) % SubtaskTitles.Length],
                    created.AddMinutes(5 + i), subCompleted, subDue, null, TaskPriority.None, author,
                    assignees.Length > 0 ? [assignees[_random.Next(assignees.Length)]] : [], [], task);
            }
        }
        return (task, subtaskCount);
    }

    private TaskItem NewTask(TaskList list, ListStatus status, string title, DateTimeOffset created, DateTimeOffset? completed,
        DateOnly? due, DateOnly? start, TaskPriority priority, Guid author, Guid[] assignees, List<Guid> tagIds, TaskItem? parent)
    {
        if (start > due) start = due;
        var task = new TaskItem
        {
            ListId = list.Id,
            StatusId = status.Id,
            ParentTaskId = parent?.Id,
            Title = title,
            Priority = priority,
            StartDate = start,
            DueDate = due,
            Position = NextPosition(parent?.Id ?? list.Id),
            CreatedById = author,
            CreatedAt = created,
            UpdatedAt = completed ?? created,
            CompletedAt = status.Category == StatusCategory.Done ? completed ?? _now : null,
        };
        db.Tasks.Add(task);
        db.TaskAssignees.AddRange(assignees.Distinct().Select((u, i) => new TaskAssignee { TaskId = task.Id, UserId = u, CreatedAt = created.AddSeconds(i) }));
        db.TaskTags.AddRange(tagIds.Distinct().Select(t => new TaskTag { TaskId = task.Id, TagId = t }));
        return task;
    }

    private void AddComments(TaskItem task, Guid[] crew)
    {
        var count = _random.Next(1, 4);
        var at = task.CreatedAt.AddHours(3);
        for (var i = 0; i < count && at < _now; i++)
        {
            db.Comments.Add(new TaskComment
            {
                TaskId = task.Id, AuthorId = crew[_random.Next(crew.Length)], Body = CommentBodies[_random.Next(CommentBodies.Length)], CreatedAt = at,
            });
            at = at.AddHours(_random.Next(2, 40));
        }
    }

    /// <summary>Nobody (15%), one person (70%, me most often) or two.</summary>
    private Guid[] PickAssignees(Guid[] crew, Guid me)
    {
        var roll = _random.NextDouble();
        if (roll < 0.15) return [];
        Guid One() => _random.NextDouble() < 0.34 ? me : crew[_random.Next(crew.Length)];
        if (roll < 0.85 || crew.Length < 2) return [One()];
        var first = One();
        var others = crew.Where(c => c != first).ToArray();
        return [first, others[_random.Next(others.Length)]];
    }

    private T Pick<T>((T Value, int Weight)[] options)
    {
        var roll = _random.Next(options.Sum(o => o.Weight));
        foreach (var (value, weight) in options)
        {
            if (roll < weight) return value;
            roll -= weight;
        }
        return options[^1].Value;
    }

    private double NextPosition(Guid group)
    {
        var p = _positions.GetValueOrDefault(group, 0) + Positions.Step;
        _positions[group] = p;
        return p;
    }

    private static DateTimeOffset At(DateOnly day, int hour, int minute) =>
        new(day.ToDateTime(new TimeOnly(Math.Min(hour, 23), minute)), TimeSpan.Zero);
}
