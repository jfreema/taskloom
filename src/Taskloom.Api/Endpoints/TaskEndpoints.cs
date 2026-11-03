using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Taskloom.Application;
using Taskloom.Domain;
using Taskloom.Infrastructure;

namespace Taskloom.Api.Endpoints;

public static class TaskEndpoints
{
    public const long MaxAttachmentBytes = 25 * 1024 * 1024;

    public static void MapTaskEndpoints(this RouteGroupBuilder api)
    {
        // All tasks in a list, subtasks included (flat; clients nest by ParentTaskId).
        api.MapGet("/lists/{id:guid}/tasks", async (Guid id, Access access, TaskloomDbContext db) =>
        {
            await access.RequireListAsync(id);
            return await db.Tasks.Where(t => t.ListId == id).OrderBy(t => t.Position)
                .Select(t => TaskDto.From(t)).ToListAsync();
        });

        api.MapPost("/lists/{id:guid}/tasks", async (Guid id, CreateTaskRequest req, Access access, TaskloomDbContext db) =>
        {
            await access.RequireListAsync(id, WorkspaceRole.Member);
            if (string.IsNullOrWhiteSpace(req.Title)) return Results.BadRequest();

            var statuses = await db.Statuses.Where(s => s.ListId == id).OrderBy(s => s.Position).ToListAsync();
            var status = req.StatusId is null ? statuses.First() : statuses.FirstOrDefault(s => s.Id == req.StatusId);
            if (status is null) return Results.BadRequest();
            if (req.ParentTaskId is not null && !await db.Tasks.AnyAsync(t => t.Id == req.ParentTaskId && t.ListId == id))
                return Results.BadRequest();
            if (req.AssigneeId is not null && !await IsListMemberAsync(db, id, req.AssigneeId.Value))
                return Results.BadRequest();

            var last = await db.Tasks.Where(t => t.ListId == id && t.StatusId == status.Id && t.ParentTaskId == req.ParentTaskId)
                .MaxAsync(t => (double?)t.Position);
            var task = new TaskItem
            {
                Id = req.Id ?? Guid.NewGuid(),
                ListId = id,
                StatusId = status.Id,
                ParentTaskId = req.ParentTaskId,
                Title = req.Title.Trim(),
                Description = req.Description ?? "",
                Priority = req.Priority ?? TaskPriority.None,
                AssigneeId = req.AssigneeId,
                StartDate = req.StartDate,
                DueDate = req.DueDate,
                Position = Positions.Between(last, null),
                CreatedById = access.UserId,
                CompletedAt = status.Category == StatusCategory.Done ? DateTimeOffset.UtcNow : null,
            };
            db.Tasks.Add(task);
            await db.SaveChangesAsync();
            return Results.Created($"/api/tasks/{task.Id}", TaskDto.From(task));
        });

        api.MapGet("/tasks/{id:guid}", async (Guid id, Access access, TaskloomDbContext db) =>
        {
            var (task, _) = await access.RequireTaskAsync(id);
            var subtasks = await db.Tasks.Where(t => t.ParentTaskId == id).OrderBy(t => t.Position).Select(t => TaskDto.From(t)).ToListAsync();
            var comments = await db.Comments.Where(c => c.TaskId == id).OrderBy(c => c.CreatedAt)
                .Select(c => new CommentDto(c.Id, c.TaskId, new UserDto(c.AuthorId, c.Author!.Email, c.Author.DisplayName), c.Body, c.CreatedAt))
                .ToListAsync();
            var attachments = await db.Attachments.Where(a => a.TaskId == id).OrderBy(a => a.CreatedAt)
                .Select(a => new AttachmentDto(a.Id, a.TaskId, a.FileName, a.ContentType, a.SizeBytes, a.CreatedAt)).ToListAsync();
            return new TaskDetailDto(TaskDto.From(task), subtasks, comments, attachments);
        });

        // Partial update: only the fields present in the body change; null clears a nullable field.
        api.MapPatch("/tasks/{id:guid}", async (Guid id, JsonElement body, Access access, TaskloomDbContext db) =>
        {
            var (task, _) = await access.RequireTaskAsync(id, WorkspaceRole.Member);
            if (body.ValueKind != JsonValueKind.Object) return Results.BadRequest();

            foreach (var prop in body.EnumerateObject())
            {
                var v = prop.Value;
                var isNull = v.ValueKind == JsonValueKind.Null;
                switch (prop.Name.ToLowerInvariant())
                {
                    case "title":
                        if (isNull || string.IsNullOrWhiteSpace(v.GetString())) return Results.BadRequest();
                        task.Title = v.GetString()!.Trim();
                        break;
                    case "description":
                        task.Description = isNull ? "" : v.GetString() ?? "";
                        break;
                    case "priority":
                        if (isNull) task.Priority = TaskPriority.None;
                        else if (Enum.TryParse<TaskPriority>(v.ToString(), true, out var p)) task.Priority = p;
                        else return Results.BadRequest();
                        break;
                    case "assigneeid":
                        if (isNull) task.AssigneeId = null;
                        else if (v.TryGetGuid(out var a) && await IsListMemberAsync(db, task.ListId, a)) task.AssigneeId = a;
                        else return Results.BadRequest();
                        break;
                    case "startdate":
                        if (isNull) task.StartDate = null;
                        else if (DateOnly.TryParse(v.GetString(), out var sd)) task.StartDate = sd;
                        else return Results.BadRequest();
                        break;
                    case "duedate":
                        if (isNull) task.DueDate = null;
                        else if (DateOnly.TryParse(v.GetString(), out var dd)) task.DueDate = dd;
                        else return Results.BadRequest();
                        break;
                    case "statusid":
                        if (!v.TryGetGuid(out var sid)) return Results.BadRequest();
                        var status = await db.Statuses.FirstOrDefaultAsync(s => s.Id == sid && s.ListId == task.ListId);
                        if (status is null) return Results.BadRequest();
                        SetStatus(task, status);
                        break;
                }
            }
            task.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
            return Results.Ok(TaskDto.From(task));
        });

        // Board drag-and-drop and list reordering.
        api.MapPost("/tasks/{id:guid}/move", async (Guid id, MoveTaskRequest req, Access access, TaskloomDbContext db) =>
        {
            var (task, _) = await access.RequireTaskAsync(id, WorkspaceRole.Member);
            var status = await db.Statuses.FirstOrDefaultAsync(s => s.Id == req.StatusId && s.ListId == task.ListId);
            if (status is null) return Results.BadRequest();

            double? before = req.BeforeTaskId is null ? null
                : await db.Tasks.Where(t => t.Id == req.BeforeTaskId && t.ListId == task.ListId).Select(t => (double?)t.Position).FirstOrDefaultAsync();
            double? after = req.AfterTaskId is null ? null
                : await db.Tasks.Where(t => t.Id == req.AfterTaskId && t.ListId == task.ListId).Select(t => (double?)t.Position).FirstOrDefaultAsync();
            if (before is null && after is null)
            {
                // Dropped into an empty column, or with no neighbours given: go to the bottom.
                before = await db.Tasks.Where(t => t.ListId == task.ListId && t.StatusId == status.Id && t.ParentTaskId == task.ParentTaskId && t.Id != id)
                    .MaxAsync(t => (double?)t.Position);
            }
            task.Position = Positions.Between(before, after);
            SetStatus(task, status);
            task.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
            return Results.Ok(TaskDto.From(task));
        });

        api.MapDelete("/tasks/{id:guid}", async (Guid id, Access access, TaskloomDbContext db, IFileStore files) =>
        {
            await access.RequireTaskAsync(id, WorkspaceRole.Member);
            var taskIds = await db.Tasks.Where(t => t.Id == id || t.ParentTaskId == id).Select(t => t.Id).ToListAsync();
            var keys = await db.Attachments.Where(a => taskIds.Contains(a.TaskId)).Select(a => a.StorageKey).ToListAsync();
            await db.Tasks.Where(t => t.Id == id).ExecuteDeleteAsync();
            foreach (var key in keys) await files.DeleteAsync(key);
            return Results.NoContent();
        });

        // ---- Comments ----
        api.MapPost("/tasks/{id:guid}/comments", async (Guid id, CreateCommentRequest req, Access access, TaskloomDbContext db) =>
        {
            await access.RequireTaskAsync(id, WorkspaceRole.Guest);
            if (string.IsNullOrWhiteSpace(req.Body)) return Results.BadRequest();
            var c = new TaskComment { TaskId = id, AuthorId = access.UserId, Body = req.Body.Trim() };
            db.Comments.Add(c);
            await db.SaveChangesAsync();
            var author = await db.Users.FirstAsync(u => u.Id == access.UserId);
            return Results.Ok(new CommentDto(c.Id, id, new UserDto(author.Id, author.Email, author.DisplayName), c.Body, c.CreatedAt));
        });

        api.MapDelete("/comments/{id:guid}", async (Guid id, Access access, TaskloomDbContext db) =>
        {
            var c = await db.Comments.FirstOrDefaultAsync(x => x.Id == id);
            if (c is null) return Results.NotFound();
            await access.RequireTaskAsync(c.TaskId);
            if (c.AuthorId != access.UserId) return Results.Forbid();
            db.Comments.Remove(c);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        // ---- Attachments (stored through IFileStore: local disk or Azure Blob Storage) ----
        api.MapPost("/tasks/{id:guid}/attachments", async (Guid id, IFormFile file, Access access, TaskloomDbContext db, IFileStore files) =>
        {
            await access.RequireTaskAsync(id, WorkspaceRole.Member);
            if (file.Length == 0 || file.Length > MaxAttachmentBytes) return Results.BadRequest();
            var a = new TaskAttachment
            {
                TaskId = id,
                FileName = Path.GetFileName(file.FileName),
                ContentType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType,
                SizeBytes = file.Length,
                StorageKey = "",
                UploadedById = access.UserId,
            };
            a.StorageKey = $"tasks/{id}/{a.Id}";
            await using (var stream = file.OpenReadStream())
                await files.SaveAsync(a.StorageKey, stream, a.ContentType);
            db.Attachments.Add(a);
            await db.SaveChangesAsync();
            return Results.Ok(new AttachmentDto(a.Id, a.TaskId, a.FileName, a.ContentType, a.SizeBytes, a.CreatedAt));
        }).DisableAntiforgery();

        api.MapGet("/attachments/{id:guid}", async (Guid id, Access access, TaskloomDbContext db, IFileStore files) =>
        {
            var a = await db.Attachments.FirstOrDefaultAsync(x => x.Id == id);
            if (a is null) return Results.NotFound();
            await access.RequireTaskAsync(a.TaskId);
            var stream = await files.OpenReadAsync(a.StorageKey);
            return stream is null ? Results.NotFound() : Results.File(stream, a.ContentType, a.FileName);
        });

        api.MapDelete("/attachments/{id:guid}", async (Guid id, Access access, TaskloomDbContext db, IFileStore files) =>
        {
            var a = await db.Attachments.FirstOrDefaultAsync(x => x.Id == id);
            if (a is null) return Results.NotFound();
            await access.RequireTaskAsync(a.TaskId, WorkspaceRole.Member);
            db.Attachments.Remove(a);
            await db.SaveChangesAsync();
            await files.DeleteAsync(a.StorageKey);
            return Results.NoContent();
        });
    }

    private static void SetStatus(TaskItem task, ListStatus status)
    {
        task.StatusId = status.Id;
        task.CompletedAt = status.Category == StatusCategory.Done ? task.CompletedAt ?? DateTimeOffset.UtcNow : null;
    }

    private static Task<bool> IsListMemberAsync(TaskloomDbContext db, Guid listId, Guid userId) =>
        db.Lists.Where(l => l.Id == listId)
            .SelectMany(l => db.WorkspaceMembers.Where(m => m.WorkspaceId == l.Space!.WorkspaceId && m.UserId == userId))
            .AnyAsync();
}
