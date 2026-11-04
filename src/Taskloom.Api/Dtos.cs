using Taskloom.Domain;

namespace Taskloom.Api;

// Wire shapes of spec 5.2 and 5.3. JSON is camelCase with enums as their C# names (Program.cs).
// web/src/types.ts mirrors these one to one: change both sides together.

// ---- Responses ----

public record UserDto(Guid Id, string Email, string DisplayName);

/// <param name="Pending">The user has no sign-in identity yet (invited, never signed in).</param>
public record MemberDto(Guid UserId, string Email, string DisplayName, WorkspaceRole Role, bool Pending);

public record StatusDto(Guid Id, Guid ListId, string Name, string Color, StatusCategory Category, double Position);
public record ListSummaryDto(Guid Id, Guid SpaceId, string Name, double Position, List<StatusDto> Statuses);
public record SpaceDto(Guid Id, string Name, string Color, double Position, List<ListSummaryDto> Lists);
public record WorkspaceTreeDto(Guid Id, string Name, WorkspaceRole Role, List<SpaceDto> Spaces);
/// <summary>GET /api/lists/{id}; the web app no longer calls it.</summary>
public record ListDetailDto(Guid Id, Guid SpaceId, Guid WorkspaceId, string Name, List<StatusDto> Statuses, List<UserDto> Members);

public record TagDto(Guid Id, Guid WorkspaceId, string Name, string Color, DateTimeOffset CreatedAt);

/// <summary>Built only by <see cref="TaskProjections.ToDto"/>.</summary>
public record TaskDto(
    Guid Id, Guid ListId, Guid StatusId, Guid? ParentTaskId,
    string Title, bool HasDescription, TaskPriority Priority,
    List<Guid> AssigneeIds, List<Guid> TagIds,
    DateOnly? StartDate, DateOnly? DueDate, double Position,
    Guid CreatedById, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, DateTimeOffset? CompletedAt,
    int SubtaskCount, int SubtaskDoneCount, int CommentCount, int AttachmentCount);

public record TaskRefDto(Guid Id, string Title, Guid ListId);
public record CommentDto(Guid Id, Guid TaskId, UserDto Author, string Body, DateTimeOffset CreatedAt, DateTimeOffset? EditedAt);
public record AttachmentDto(Guid Id, Guid TaskId, string FileName, string ContentType, long SizeBytes, Guid UploadedById, DateTimeOffset CreatedAt);
public record ActivityDto(Guid Id, Guid TaskId, UserDto Actor, TaskActivityKind Kind, string? From, string? To, DateTimeOffset CreatedAt);

/// <param name="Description">Sanitized HTML; "" when empty.</param>
/// <param name="Activity">The latest 200 rows, oldest first.</param>
public record TaskDetailDto(
    TaskDto Task, string Description, Guid WorkspaceId, UserDto CreatedBy, TaskRefDto? Parent,
    List<TaskDto> Subtasks, List<CommentDto> Comments, List<AttachmentDto> Attachments, List<ActivityDto> Activity);

public record WorkspaceTasksDto(List<TaskDto> Tasks, List<TaskRefDto> Parents);
public record BulkUpdateResultDto(List<TaskDto> Tasks);
public record AuthProviderDto(string Key, string DisplayName);
public record AuthProvidersDto(bool DevLogin, List<AuthProviderDto> Providers);

// ---- Requests ----
// Ids are optional so clients can create rows with their own UUIDs. Strings are nullable because a missing
// property must give a 400, never a NullReferenceException. PATCH bodies for tasks (TaskPatch) and bulk updates
// are parsed as JsonElement so "absent" and "null" differ.

public enum Placement { Top, Bottom }

public record UpdateMeRequest(string? DisplayName);
public record CreateWorkspaceRequest(Guid? Id, string? Name);
public record RenameRequest(string? Name);
public record AddMemberRequest(string? Email, WorkspaceRole Role, string? DisplayName);
public record UpdateMemberRequest(WorkspaceRole Role);
public record CreateTagRequest(Guid? Id, string? Name, string? Color);
public record UpdateTagRequest(string? Name, string? Color);
public record CreateSpaceRequest(Guid? Id, string? Name, string? Color);
public record UpdateSpaceRequest(string? Name, string? Color, double? Position);
public record CreateListRequest(Guid? Id, string? Name, StatusPreset? StatusPreset, Guid? StatusesFromListId);
public record UpdateListRequest(string? Name, double? Position, Guid? SpaceId);
public record CreateStatusRequest(Guid? Id, string? Name, string? Color, StatusCategory? Category);
public record UpdateStatusRequest(string? Name, string? Color, StatusCategory? Category, double? Position);
public record CreateTaskRequest(
    Guid? Id, string? Title, Guid? StatusId, Guid? ParentTaskId, string? Description,
    TaskPriority? Priority, List<Guid>? AssigneeIds, List<Guid>? TagIds,
    DateOnly? StartDate, DateOnly? DueDate, Placement? Placement);
/// <summary>
/// Move a task to a status between two neighbours: <paramref name="BeforeTaskId"/> is the neighbour above the drop
/// point, <paramref name="AfterTaskId"/> the one below (either may be null).
/// </summary>
public record MoveTaskRequest(Guid StatusId, Guid? BeforeTaskId, Guid? AfterTaskId);
public record BulkDeleteRequest(List<Guid>? Ids);
public record CreateCommentRequest(Guid? Id, string? Body);
public record UpdateCommentRequest(string? Body);
/// <param name="Fillers">Filler tasks to add: default 48, 0 to 2000.</param>
public record DevSeedRequest(Guid WorkspaceId, int? Fillers);
