namespace Taskloom.Domain;

/// <summary>A person who can sign in. Linked to one or more sign-in identities (Google, Entra ID, dev).</summary>
public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Email { get; set; }
    public required string DisplayName { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    /// <summary>Empty for a pending user: invited, never signed in.</summary>
    public List<UserIdentity> Identities { get; set; } = [];
}

/// <summary>An external sign-in identity: (provider, subject) is unique.</summary>
public class UserIdentity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public required string Provider { get; set; }
    public required string Subject { get; set; }
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
    public List<TaskList> Lists { get; set; } = [];
}

public class TaskList
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SpaceId { get; set; }
    public Space? Space { get; set; }
    public required string Name { get; set; }
    public double Position { get; set; }
    public List<ListStatus> Statuses { get; set; } = [];
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
    AttachmentRemoved, SubtaskAdded
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
