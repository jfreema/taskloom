namespace Taskloom.Domain;

/// <summary>A person who can sign in. Linked to one or more sign-in identities (Google, Entra ID, dev).</summary>
public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Email { get; set; }
    public required string DisplayName { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    /// <summary>
    /// Set when a site admin turned the account off: the person can't sign in and every session of theirs ends. They
    /// stay a member and assignee wherever they were.
    /// </summary>
    public DateTimeOffset? DisabledAt { get; set; }
    /// <summary>
    /// Names the current photo (<see cref="UserPhoto"/>): the first 16 hex digits of its SHA-256, so its URL changes
    /// whenever the photo does and browsers may keep each one for good. Null when the person has no photo.
    /// </summary>
    public string? PhotoVersion { get; set; }
    /// <summary>
    /// The person uploaded or removed their photo by hand, so signing in no longer replaces it with the provider's.
    /// </summary>
    public bool PhotoChosen { get; set; }
    /// <summary>Empty for a pending user: invited, never signed in.</summary>
    public List<UserIdentity> Identities { get; set; } = [];
}

/// <summary>
/// A person's photo, kept apart from <see cref="User"/> so loading users never loads image bytes. A JPEG, PNG, WebP or
/// GIF of at most 512 KB and 2048 pixels a side, checked by Taskloom.Api ImageInfo.
/// </summary>
public class UserPhoto
{
    public Guid UserId { get; set; }
    /// <summary>"image/jpeg", "image/png", "image/webp" or "image/gif", read from the bytes, never from the sender.</summary>
    public required string ContentType { get; set; }
    public required byte[] Data { get; set; }
    /// <summary>"upload", or the key of the sign-in provider it came from.</summary>
    public required string Source { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>An external sign-in identity: (provider, subject) is unique.</summary>
public class UserIdentity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public required string Provider { get; set; }
    public required string Subject { get; set; }
}

/// <summary>
/// One sign-in on one browser. The cookie names its session, and the server checks the row again every few seconds,
/// so signing out, turning an account off or taking someone off the allowlist ends a cookie even if it was copied.
/// </summary>
public class UserSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public User? User { get; set; }
    /// <summary>How the person signed in: "dev", "google", "microsoft" or another provider's key.</summary>
    public required string Provider { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    /// <summary>The last time the session was checked against the database (at most every Auth:Session:RecheckSeconds).</summary>
    public DateTimeOffset LastSeenAt { get; set; }
    /// <summary>CreatedAt + Auth:Session:MaxDays: the session ends then, however active it is.</summary>
    public DateTimeOffset ExpiresAt { get; set; }
    /// <summary>Set by signing out, "Sign out other devices", or a site admin.</summary>
    public DateTimeOffset? RevokedAt { get; set; }
    /// <summary>The browser's User-Agent at sign-in, without control characters, at most 300 characters.</summary>
    public string? UserAgent { get; set; }
}

public enum WorkspaceRole { Owner = 0, Admin = 1, Member = 2, Guest = 3 }

public class Workspace
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<WorkspaceMember> Members { get; set; } = [];
    public List<Space> Spaces { get; set; } = [];
}

public class WorkspaceMember
{
    public Guid WorkspaceId { get; set; }
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public WorkspaceRole Role { get; set; } = WorkspaceRole.Member;
    public DateTimeOffset JoinedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>The 13 swatches of the Loom palette (spec 7.2). Stored colours are "#RRGGBB" strings.</summary>
public static class Palette
{
    public const string Slate = "#7A868C", Blue = "#3D7DF5", Indigo = "#6E6CF0", Violet = "#9B5DE5",
        Pink = "#DB4C8B", Red = "#E0484E", Orange = "#D2661B", Amber = "#B07A0A", Lime = "#6F9A1B",
        Green = "#2E9F5E", Teal = "#14998A", Cyan = "#1C93B8", Brown = "#A87750";
}

/// <summary>A Space groups Lists inside a Workspace (folders come later).</summary>
public class Space
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkspaceId { get; set; }
    public required string Name { get; set; }
    public string Color { get; set; } = Palette.Teal;
    public double Position { get; set; }
    /// <summary>
    /// Only the people in <see cref="Members"/> can see a private space and anything in it; to everyone else, workspace
    /// Owner and Admins included, it doesn't exist. Everyone in the workspace can see a space that isn't private.
    /// </summary>
    public bool IsPrivate { get; set; }
    /// <summary>The people of a private space (empty while the space isn't private).</summary>
    public List<SpaceMember> Members { get; set; } = [];
    public List<TaskList> Lists { get; set; } = [];
}

/// <summary>
/// A person who can see a private space. They must be a member of the space's workspace, and their workspace role still
/// decides what they can change in it (a Guest only views and comments).
/// </summary>
public class SpaceMember
{
    public Guid SpaceId { get; set; }
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class TaskList
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SpaceId { get; set; }
    public Space? Space { get; set; }
    public required string Name { get; set; }
    public double Position { get; set; }
    /// <summary>"#RRGGBB" for the list's icon, or null for none (the icon is then grey).</summary>
    public string? Color { get; set; }
    /// <summary>One of <see cref="ListIcons.All"/>, or null for the plain list icon.</summary>
    public string? Icon { get; set; }
    public List<ListStatus> Statuses { get; set; } = [];
}

/// <summary>
/// The icons a list can have, by name (the web app's lib/listIcons.ts draws each one; keep both in step). The first,
/// "list", is what a list without an icon shows.
/// </summary>
public static class ListIcons
{
    public const string Default = "list";

    public static readonly IReadOnlyList<string> All =
    [
        "list", "list-checks", "star", "flag", "target", "rocket", "zap",
        "lightbulb", "bookmark", "book-open", "briefcase", "calendar", "clipboard-list", "file-text",
        "code", "bug", "wrench", "palette", "megaphone", "message-circle", "mail",
        "users", "heart", "house", "shopping-cart", "wallet", "chart-column", "graduation-cap",
        "plane", "coffee", "headphones", "camera", "globe", "leaf", "dumbbell",
        "gift", "trophy", "package", "inbox", "shield", "building", "sparkles",
    ];

    private static readonly HashSet<string> Known = new(All, StringComparer.Ordinal);

    public static bool IsKnown(string? name) => name is not null && Known.Contains(name);
}

public enum StatusCategory { NotStarted = 0, Active = 1, Done = 2 }

/// <summary>The status sets a new list can start with.</summary>
public enum StatusPreset { Simple, Review, Requests }

/// <summary>Statuses are defined per List, like ClickUp.</summary>
public class ListStatus
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ListId { get; set; }
    public required string Name { get; set; }
    public string Color { get; set; } = Palette.Slate;
    public StatusCategory Category { get; set; }
    public double Position { get; set; }

    /// <summary>The statuses of a preset, at positions 1024, 2048, 3072 and 4096.</summary>
    public static List<ListStatus> ForPreset(Guid listId, StatusPreset preset)
    {
        (string Name, string Color, StatusCategory Category)[] rows = preset switch
        {
            StatusPreset.Simple =>
            [
                ("To do", Palette.Slate, StatusCategory.NotStarted),
                ("In progress", Palette.Blue, StatusCategory.Active),
                ("Done", Palette.Green, StatusCategory.Done),
            ],
            StatusPreset.Review =>
            [
                ("To do", Palette.Slate, StatusCategory.NotStarted),
                ("In progress", Palette.Blue, StatusCategory.Active),
                ("Review", Palette.Violet, StatusCategory.Active),
                ("Done", Palette.Green, StatusCategory.Done),
            ],
            StatusPreset.Requests =>
            [
                ("New", Palette.Slate, StatusCategory.NotStarted),
                ("In progress", Palette.Blue, StatusCategory.Active),
                ("Waiting", Palette.Amber, StatusCategory.Active),
                ("Done", Palette.Green, StatusCategory.Done),
            ],
            _ => throw new ArgumentOutOfRangeException(nameof(preset), preset, null),
        };
        return rows.Select((r, i) => new ListStatus
        {
            ListId = listId, Name = r.Name, Color = r.Color, Category = r.Category, Position = 1024 * (i + 1),
        }).ToList();
    }

    /// <summary>The starter workspace's and the API's default: the Review preset.</summary>
    public static List<ListStatus> Defaults(Guid listId) => ForPreset(listId, StatusPreset.Review);
}

public enum TaskPriority { None = 0, Urgent = 1, High = 2, Normal = 3, Low = 4 }

/// <summary>The unit of a repeat rule's interval.</summary>
public enum RepeatUnit { Day = 0, Week = 1, Month = 2, Year = 3 }

/// <summary>What a repeating task's next due date counts from.</summary>
public enum RepeatBasis
{
    /// <summary>The rule's own dates, counted from the due date ("every Monday", "the 1st of each month").</summary>
    Schedule = 0,
    /// <summary>The day the task is completed ("a week after it's done").</summary>
    Completion = 1,
}

/// <summary>Days of the week, for weekly rules.</summary>
[Flags]
public enum WeekdaySet
{
    None = 0,
    Sunday = 1 << 0, Monday = 1 << 1, Tuesday = 1 << 2, Wednesday = 1 << 3, Thursday = 1 << 4, Friday = 1 << 5, Saturday = 1 << 6,
}

/// <summary>
/// How a task repeats; immutable, stored as JSON on the task. Only the parts that fit the unit and basis are set
/// (Taskloom.Application Recurrence.Normalize): weekdays for weekly rules on schedule, a day of the month or a
/// weekday of the month for monthly ones.
/// </summary>
public sealed record RepeatRule
{
    public RepeatUnit Unit { get; init; }
    /// <summary>Every 1 to 365 units.</summary>
    public int Every { get; init; } = 1;
    public RepeatBasis Basis { get; init; }
    /// <summary>Weekly on schedule: the days it falls on (at least one).</summary>
    public WeekdaySet Weekdays { get; init; }
    /// <summary>Monthly on schedule by date: 1 to 31 (the month's last day when shorter), or -1 for the last day.</summary>
    public int? MonthDay { get; init; }
    /// <summary>Monthly on schedule by weekday: the 1st to 4th, or -1 for the last, <see cref="MonthWeekday"/> of the month.</summary>
    public int? MonthWeek { get; init; }
    public DayOfWeek? MonthWeekday { get; init; }
    /// <summary>The last day a new task may be due; none is created after it.</summary>
    public DateOnly? Until { get; init; }
}

public class TaskItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ListId { get; set; }
    public Guid StatusId { get; set; }
    /// <summary>Set for subtasks.</summary>
    public Guid? ParentTaskId { get; set; }
    public required string Title { get; set; }
    /// <summary>Sanitized HTML (Taskloom.Api RichText.Sanitize).</summary>
    public string Description { get; set; } = "";
    /// <summary>Plain-text copy of <see cref="Description"/> for search and previews, kept in step by the API.</summary>
    public string DescriptionText { get; set; } = "";
    public TaskPriority Priority { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? DueDate { get; set; }
    /// <summary>Sort order within a status column; fractional so a move touches one row.</summary>
    public double Position { get; set; }
    public Guid CreatedById { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
    /// <summary>
    /// Set on a top-level task that repeats. Completing the task creates the next one, which takes the rule over, so only
    /// the newest task of a series carries it.
    /// </summary>
    public RepeatRule? Repeat { get; set; }
    /// <summary>
    /// The task this one's completion created (the next of its series). Reopening this task while that one is still
    /// untouched deletes it and takes the rule back, so an accidental tick and its undo leave no trace.
    /// </summary>
    public Guid? NextRepeatId { get; set; }

    public List<TaskAssignee> Assignees { get; set; } = [];
    public List<TaskTag> Tags { get; set; } = [];
    // Navigation-only collections over existing foreign keys, so projections can count them.
    public List<TaskItem> Subtasks { get; set; } = [];
    public List<TaskComment> Comments { get; set; } = [];
    public List<TaskAttachment> Attachments { get; set; } = [];
}

/// <summary>One person a task is assigned to.</summary>
public class TaskAssignee
{
    public Guid TaskId { get; set; }
    public Guid UserId { get; set; }
    /// <summary>When the person was assigned; orders the avatar stack.</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>A workspace-wide label.</summary>
public class Tag
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkspaceId { get; set; }
    public required string Name { get; set; }
    /// <summary><c>Name.Trim().ToLowerInvariant()</c>; unique per workspace.</summary>
    public required string NormalizedName { get; set; }
    public string Color { get; set; } = Palette.Slate;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public static string Normalize(string name) => name.Trim().ToLowerInvariant();
}

public class TaskTag
{
    public Guid TaskId { get; set; }
    public Guid TagId { get; set; }
    public Tag? Tag { get; set; }
}

public enum TaskActivityKind
{
    Renamed, StatusChanged, AssigneeAdded, AssigneeRemoved, DueDateChanged, StartDateChanged,
    PriorityChanged, TagAdded, TagRemoved, DescriptionChanged, MovedList, AttachmentAdded,
    AttachmentRemoved, SubtaskAdded,
    /// <summary>The repeat rule was set, changed or removed; values are the rule as JSON (ActivityValues.Repeat).</summary>
    RepeatChanged,
    /// <summary>Completing the task created the next one; the value is its due date.</summary>
    Repeated,
}

/// <summary>One change to a task. Values are display values copied at the time (spec 5.6).</summary>
public class TaskActivity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TaskId { get; set; }
    public Guid ActorId { get; set; }
    public User? Actor { get; set; }
    public TaskActivityKind Kind { get; set; }
    public string? FromValue { get; set; }
    public string? ToValue { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class TaskComment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TaskId { get; set; }
    public Guid AuthorId { get; set; }
    public User? Author { get; set; }
    public required string Body { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    /// <summary>Set when the author edits the body.</summary>
    public DateTimeOffset? EditedAt { get; set; }
}

public class TaskAttachment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TaskId { get; set; }
    public required string FileName { get; set; }
    public required string ContentType { get; set; }
    public long SizeBytes { get; set; }
    /// <summary>Key in the file store (local disk or Azure Blob Storage).</summary>
    public required string StorageKey { get; set; }
    public Guid UploadedById { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
