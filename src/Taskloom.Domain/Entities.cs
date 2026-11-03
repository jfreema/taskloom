namespace Taskloom.Domain;

/// <summary>A person who can sign in. Linked to one or more sign-in identities (Google, Entra ID, dev).</summary>
public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Email { get; set; }
    public required string DisplayName { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
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

/// <summary>A Space groups Lists inside a Workspace (folders come later).</summary>
public class Space
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkspaceId { get; set; }
    public required string Name { get; set; }
    public string Color { get; set; } = "#7b68ee";
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

/// <summary>Statuses are defined per List, like ClickUp.</summary>
public class ListStatus
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ListId { get; set; }
    public required string Name { get; set; }
    public string Color { get; set; } = "#87909e";
    public StatusCategory Category { get; set; }
    public double Position { get; set; }

    public static List<ListStatus> Defaults(Guid listId) =>
    [
        new() { ListId = listId, Name = "To do", Color = "#87909e", Category = StatusCategory.NotStarted, Position = 1 },
        new() { ListId = listId, Name = "In progress", Color = "#4194f6", Category = StatusCategory.Active, Position = 2 },
        new() { ListId = listId, Name = "Review", Color = "#a875ff", Category = StatusCategory.Active, Position = 3 },
        new() { ListId = listId, Name = "Done", Color = "#3db88b", Category = StatusCategory.Done, Position = 4 },
    ];
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
    public string Description { get; set; } = "";
    public TaskPriority Priority { get; set; }
    public Guid? AssigneeId { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? DueDate { get; set; }
    /// <summary>Sort order within a status column; fractional so a move touches one row.</summary>
    public double Position { get; set; }
    public Guid CreatedById { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
}

public class TaskComment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TaskId { get; set; }
    public Guid AuthorId { get; set; }
    public User? Author { get; set; }
    public required string Body { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
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
