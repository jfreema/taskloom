using Taskloom.Domain;

namespace Taskloom.Api;

public record UserDto(Guid Id, string Email, string DisplayName);

public record WorkspaceTreeDto(Guid Id, string Name, WorkspaceRole Role, List<SpaceDto> Spaces);
public record SpaceDto(Guid Id, string Name, string Color, double Position, List<ListSummaryDto> Lists);
public record ListSummaryDto(Guid Id, string Name, double Position);
public record StatusDto(Guid Id, string Name, string Color, StatusCategory Category, double Position);
public record ListDetailDto(Guid Id, Guid SpaceId, Guid WorkspaceId, string Name, List<StatusDto> Statuses, List<UserDto> Members);
public record MemberDto(Guid UserId, string Email, string DisplayName, WorkspaceRole Role);

public record TaskDto(
    Guid Id, Guid ListId, Guid StatusId, Guid? ParentTaskId, string Title, string Description,
    TaskPriority Priority, Guid? AssigneeId, DateOnly? StartDate, DateOnly? DueDate, double Position,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, DateTimeOffset? CompletedAt)
{
    public static TaskDto From(TaskItem t) => new(t.Id, t.ListId, t.StatusId, t.ParentTaskId, t.Title, t.Description,
        t.Priority, t.AssigneeId, t.StartDate, t.DueDate, t.Position, t.CreatedAt, t.UpdatedAt, t.CompletedAt);
}

public record CommentDto(Guid Id, Guid TaskId, UserDto Author, string Body, DateTimeOffset CreatedAt);
public record AttachmentDto(Guid Id, Guid TaskId, string FileName, string ContentType, long SizeBytes, DateTimeOffset CreatedAt);
public record TaskDetailDto(TaskDto Task, List<TaskDto> Subtasks, List<CommentDto> Comments, List<AttachmentDto> Attachments);

// Requests. Ids are optional so clients can create rows with their own UUIDs (offline-friendly).
public record CreateWorkspaceRequest(Guid? Id, string Name);
public record RenameRequest(string Name);
public record CreateSpaceRequest(Guid? Id, string Name, string? Color);
public record UpdateSpaceRequest(string? Name, string? Color, double? Position);
public record CreateListRequest(Guid? Id, string Name);
public record UpdateListRequest(string? Name, double? Position);
public record CreateStatusRequest(Guid? Id, string Name, string? Color, StatusCategory Category);
public record UpdateStatusRequest(string? Name, string? Color, StatusCategory? Category, double? Position);
public record AddMemberRequest(string Email, WorkspaceRole Role);
public record UpdateMemberRequest(WorkspaceRole Role);
public record CreateTaskRequest(
    Guid? Id, string Title, Guid? StatusId, Guid? ParentTaskId, string? Description,
    TaskPriority? Priority, Guid? AssigneeId, DateOnly? StartDate, DateOnly? DueDate);
/// <summary>Move a task to a status, between two neighbours (either may be null for top/bottom).</summary>
public record MoveTaskRequest(Guid StatusId, Guid? BeforeTaskId, Guid? AfterTaskId);
public record CreateCommentRequest(string Body);
