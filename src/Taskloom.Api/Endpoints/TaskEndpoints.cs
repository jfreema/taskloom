using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Taskloom.Application;
using Taskloom.Domain;
using Taskloom.Infrastructure;

namespace Taskloom.Api.Endpoints;

/// <summary>Tasks (spec 5.4 #28–#33 and #36). Bulk calls are in <see cref="BulkEndpoints"/>.</summary>
public static class TaskEndpoints
{
    public const string AssigneesMustBeMembers = TaskPatcher.AssigneesMustBeMembers;
    public const string TagsMustBelongToWorkspace = TaskPatcher.TagsMustBelongToWorkspace;

    /// <summary>Neighbours closer than this are renumbered before a move (spec 5.5 Move).</summary>
    public const double MinGap = 0.001;

    public static void MapTaskEndpoints(this RouteGroupBuilder api)
    {
        // All tasks in a list, subtasks included (flat; clients nest by ParentTaskId).
        api.MapGet("/lists/{id:guid}/tasks", async (Guid id, Access access, TaskloomDbContext db, CancellationToken ct) =>
        {
            await access.RequireListAsync(id);
            return await db.Tasks.Where(t => t.ListId == id).InOrder()
                .Select(TaskProjections.ToDto).AsSplitQuery().ToListAsync(ct);
        });

        api.MapPost("/lists/{id:guid}/tasks", CreateAsync);
        api.MapGet("/tasks/{id:guid}", DetailAsync);
        api.MapPatch("/tasks/{id:guid}", PatchAsync);
        api.MapPost("/tasks/{id:guid}/move", MoveAsync);

        api.MapDelete("/tasks/{id:guid}", async (Guid id, Access access, TaskloomDbContext db, IFileStore files) =>
        {
            await access.RequireTaskAsync(id, WorkspaceRole.Member);
            var taskIds = await db.Tasks.Where(t => t.Id == id || t.ParentTaskId == id).Select(t => t.Id).ToListAsync();
            var keys = await db.Attachments.Where(a => taskIds.Contains(a.TaskId)).Select(a => a.StorageKey).ToListAsync();
            // Subtasks, comments, attachments, assignees, tags and activity go with it (CASCADE).
            await db.Tasks.Where(t => t.Id == id).ExecuteDeleteAsync();
            foreach (var key in keys) await files.DeleteAsync(key);
            return Results.NoContent();
        });

        api.MapGet("/workspaces/{id:guid}/tasks", WorkspaceTaskQuery.RunAsync);
    }

    // ---- POST /api/lists/{id}/tasks (spec 5.5 Create task) ----
    private static async Task<IResult> CreateAsync(Guid id, CreateTaskRequest req, Access access, TaskloomDbContext db)
    {
        var (_, workspaceId) = await access.RequireListAsync(id, WorkspaceRole.Member);
        var title = Text.Require(req.Title, Limits.TaskTitle, "Enter a task title.", "Task titles can be up to 500 characters.");
        if (req.Description is { Length: > Limits.DescriptionHtml }) throw ProblemException.BadRequest("Descriptions can be up to 200,000 characters.");

        await using var tx = await db.Database.BeginTransactionAsync();
        var statuses = await db.Statuses.AsNoTracking().Where(s => s.ListId == id).OrderBy(s => s.Position).ThenBy(s => s.Id).ToListAsync();
        var status = req.StatusId is { } statusId
            ? statuses.FirstOrDefault(s => s.Id == statusId) ?? throw ProblemException.BadRequest(TaskChange.StatusMessage)
            : statuses.FirstOrDefault(s => s.Category == StatusCategory.NotStarted) ?? statuses.FirstOrDefault()
              ?? throw ProblemException.BadRequest("This list has no statuses.");

        TaskItem? parent = null;
        if (req.ParentTaskId is { } parentId)
        {
            parent = await db.Tasks.AsNoTracking().FirstOrDefaultAsync(t => t.Id == parentId && t.ListId == id)
                     ?? throw ProblemException.BadRequest("Choose a parent task in this list.");
            if (parent.ParentTaskId is not null) throw ProblemException.BadRequest("Subtasks can't have subtasks.");
        }
        // On create every assignee is being added, so all must be members.
        var assigneeIds = (req.AssigneeIds ?? []).Distinct().ToList();
        if (!await AreMembersAsync(db, workspaceId, assigneeIds)) throw ProblemException.BadRequest(AssigneesMustBeMembers);
        var tagIds = (req.TagIds ?? []).Distinct().ToList();
        if (!await AreWorkspaceTagsAsync(db, workspaceId, tagIds)) throw ProblemException.BadRequest(TagsMustBelongToWorkspace);

        // Placement among tasks with the same list, status and parent: Top = lowest − 1024, Bottom = highest + 1024.
        var siblings = db.Tasks.Where(t => t.ListId == id && t.StatusId == status.Id && t.ParentTaskId == req.ParentTaskId);
        var position = req.Placement == Placement.Top
            ? Positions.Between(null, await siblings.MinAsync(t => (double?)t.Position))
            : Positions.Between(await siblings.MaxAsync(t => (double?)t.Position), null);

        var now = Clock.Now();
        var description = RichText.Sanitize(req.Description);
        var task = new TaskItem
        {
            Id = req.Id ?? Guid.NewGuid(),
            ListId = id,
            StatusId = status.Id,
            ParentTaskId = parent?.Id,
            Title = title,
            Description = description,
            DescriptionText = RichText.ToPlainText(description),
            Priority = req.Priority ?? TaskPriority.None,
            StartDate = req.StartDate,
            DueDate = req.DueDate,
            Position = position,
            CreatedById = access.UserId,
            CreatedAt = now,
            UpdatedAt = now,
            CompletedAt = status.Category == StatusCategory.Done ? now : null,
        };
        db.Tasks.Add(task);
        // A microsecond apart, so the avatar stack keeps the order the people were sent in.
        db.TaskAssignees.AddRange(assigneeIds.Select((u, i) => new TaskAssignee { TaskId = task.Id, UserId = u, CreatedAt = now.AddTicks(10 * i) }));
        db.TaskTags.AddRange(tagIds.Select(tagId => new TaskTag { TaskId = task.Id, TagId = tagId }));
        if (parent is not null)
            new ActivityRecorder(db, access.UserId, now).Record(parent.Id, TaskActivityKind.SubtaskAdded, null, task.Title);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return Results.Created($"/api/tasks/{task.Id}", await TaskProjections.LoadAsync(db, task.Id));
    }

    // ---- GET /api/tasks/{id} ----
    private static async Task<TaskDetailDto> DetailAsync(Guid id, Access access, TaskloomDbContext db, CancellationToken ct)
    {
        var (task, workspaceId) = await access.RequireTaskAsync(id);
        var dto = await TaskProjections.LoadAsync(db, id, ct);
        var createdBy = await db.Users.Where(u => u.Id == task.CreatedById)
            .Select(u => new UserDto(u.Id, u.Email, u.DisplayName)).FirstAsync(ct);
        var parent = task.ParentTaskId is null ? null
            : await db.Tasks.Where(t => t.Id == task.ParentTaskId).Select(t => new TaskRefDto(t.Id, t.Title, t.ListId)).FirstOrDefaultAsync(ct);
        var subtasks = await db.Tasks.Where(t => t.ParentTaskId == id).InOrder()
            .Select(TaskProjections.ToDto).AsSplitQuery().ToListAsync(ct);
        var comments = await db.Comments.Where(c => c.TaskId == id).OrderBy(c => c.CreatedAt).ThenBy(c => c.Id)
            .Select(c => new CommentDto(c.Id, c.TaskId, new UserDto(c.AuthorId, c.Author!.Email, c.Author.DisplayName), c.Body, c.CreatedAt, c.EditedAt))
            .ToListAsync(ct);
        var attachments = await db.Attachments.Where(a => a.TaskId == id).OrderBy(a => a.CreatedAt).ThenBy(a => a.Id)
            .Select(a => new AttachmentDto(a.Id, a.TaskId, a.FileName, a.ContentType, a.SizeBytes, a.UploadedById, a.CreatedAt))
            .ToListAsync(ct);
        // The latest 200 rows, returned oldest first.
        var activity = await db.Activities.Where(a => a.TaskId == id)
            .OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Id).Take(200)
            .Select(a => new ActivityDto(a.Id, a.TaskId, new UserDto(a.ActorId, a.Actor!.Email, a.Actor.DisplayName), a.Kind, a.FromValue, a.ToValue, a.CreatedAt))
            .ToListAsync(ct);
        activity.Reverse();
        return new TaskDetailDto(dto, task.Description, workspaceId, createdBy, parent, subtasks, comments, attachments, activity);
    }

    // ---- PATCH /api/tasks/{id} (spec 5.5 Patch task, through TaskPatcher) ----
    private static async Task<IResult> PatchAsync(Guid id, JsonElement body, Access access, TaskloomDbContext db)
    {
        var (task, workspaceId) = await access.RequireTaskAsync(id, WorkspaceRole.Member);
        Json.RequireReadable(body);
        var change = TaskChange.Parse(body, TaskChangeKind.Patch);

        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Entry(task).Collection(t => t.Assignees).LoadAsync();
        await db.Entry(task).Collection(t => t.Tags).LoadAsync();
        var now = Clock.Now();
        var patcher = await TaskPatcher.CreateAsync(db, workspaceId, new ActivityRecorder(db, access.UserId, now), now, [(task, change)]);
        await patcher.ApplyAsync(task, change);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return Results.Ok(await TaskProjections.LoadAsync(db, id));
    }

    // ---- POST /api/tasks/{id}/move (spec 5.5 Move) ----
    private static async Task<IResult> MoveAsync(Guid id, MoveTaskRequest req, Access access, TaskloomDbContext db)
    {
        var (task, _) = await access.RequireTaskAsync(id, WorkspaceRole.Member);
        await using var tx = await db.Database.BeginTransactionAsync();
        var status = await db.Statuses.AsNoTracking().FirstOrDefaultAsync(s => s.Id == req.StatusId && s.ListId == task.ListId)
                     ?? throw ProblemException.BadRequest(TaskChange.StatusMessage);
        var oldStatus = await db.Statuses.AsNoTracking().FirstAsync(s => s.Id == task.StatusId);

        // The true siblings: same list, target status and parent, without the task itself. The client only knows the
        // neighbours it can see; filters and the Done column's cap can hide others.
        var siblings = await db.Tasks
            .Where(t => t.ListId == task.ListId && t.StatusId == status.Id && t.ParentTaskId == task.ParentTaskId && t.Id != id)
            .InOrder().Select(t => new { t.Id, t.Position }).ToListAsync();
        int IndexOf(Guid neighbour)
        {
            var i = siblings.FindIndex(s => s.Id == neighbour);
            return i >= 0 ? i : throw ProblemException.BadRequest("The task next to the drop point isn't in that status any more.");
        }

        // prev/next index the siblings that end up directly above and below the task.
        int prev, next;
        if (req.BeforeTaskId is { } above)
        {
            // Directly after the task above, whatever afterTaskId says (it must still be a sibling).
            prev = IndexOf(above);
            next = prev + 1;
            if (req.AfterTaskId is { } below) IndexOf(below);
        }
        else if (req.AfterTaskId is { } below)
        {
            next = IndexOf(below);
            prev = next - 1;
        }
        else
        {
            prev = siblings.Count - 1;
            next = siblings.Count;
        }

        var positions = siblings.Select(s => s.Position).ToArray();
        if (prev >= 0 && next < siblings.Count && positions[next] - positions[prev] < MinGap)
        {
            // Neighbours too close to split: renumber this status's siblings 1024, 2048, … in their current order.
            positions = siblings.Select((_, i) => Positions.Step * (i + 1)).ToArray();
            var ids = siblings.Select(s => s.Id).ToArray();
            await db.Database.ExecuteSqlAsync($"""
                UPDATE tasks AS t SET "Position" = v.position
                FROM unnest({ids}, {positions}) AS v(id, position)
                WHERE t."Id" = v.id
                """);
        }
        var position = Positions.Between(prev >= 0 ? positions[prev] : null, next < siblings.Count ? positions[next] : null);

        var now = Clock.Now();
        var changed = position != task.Position;
        if (status.Id != task.StatusId)
        {
            new ActivityRecorder(db, access.UserId, now).Record(task.Id, TaskActivityKind.StatusChanged, oldStatus.Name, status.Name);
            TaskPatcher.SetStatus(task, oldStatus, status, now);
            changed = true;
        }
        task.Position = position;
        if (changed) task.UpdatedAt = now;
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return Results.Ok(await TaskProjections.LoadAsync(db, id));
    }

    /// <summary>True when every user id (distinct) is a member of the workspace.</summary>
    public static async Task<bool> AreMembersAsync(TaskloomDbContext db, Guid workspaceId, IReadOnlyCollection<Guid> userIds) =>
        userIds.Count == 0 ||
        await db.WorkspaceMembers.CountAsync(m => m.WorkspaceId == workspaceId && userIds.Contains(m.UserId)) == userIds.Count;

    /// <summary>True when every tag id (distinct) is a tag of the workspace.</summary>
    public static async Task<bool> AreWorkspaceTagsAsync(TaskloomDbContext db, Guid workspaceId, IReadOnlyCollection<Guid> tagIds) =>
        tagIds.Count == 0 ||
        await db.Tags.CountAsync(t => t.WorkspaceId == workspaceId && tagIds.Contains(t.Id)) == tagIds.Count;
}

/// <summary>
/// GET /api/workspaces/{id}/tasks (spec 5.5): Home, the palette search and other cross-list views. The request's
/// cancellation token reaches EF, so a search abandoned by the next keystroke stops on the server.
/// </summary>
public static class WorkspaceTaskQuery
{
    public const int DefaultLimit = 500, DefaultSearchLimit = 20, MaxLimit = 1000;

    public static async Task<WorkspaceTasksDto> RunAsync(Guid id, HttpRequest request, Access access, TaskloomDbContext db, CancellationToken ct)
    {
        await access.RequireWorkspaceAsync(id);
        var query = request.Query;

        var all = Single(query, "scope")?.Trim().ToLowerInvariant() switch
        {
            null or "" or "mine" => false,
            "all" => true,
            _ => throw ProblemException.BadRequest("scope must be mine or all."),
        };
        var q = Single(query, "q")?.Trim();
        if (string.IsNullOrEmpty(q)) q = null;
        if (q is { Length: > Limits.SearchQuery }) throw ProblemException.BadRequest("Searches can be up to 200 characters.");
        var includeDone = false;
        if (Single(query, "includeDone") is { Length: > 0 } rawInclude && !bool.TryParse(rawInclude, out includeDone))
            throw ProblemException.BadRequest("includeDone must be true or false.");
        DateOnly? doneSince = null;
        if (Single(query, "doneSince") is { Length: > 0 } rawSince)
            doneSince = Dates.TryParse(rawSince, out var since) ? since : throw ProblemException.BadRequest("doneSince must be a date like 2026-11-04.");
        var limit = q is null ? DefaultLimit : DefaultSearchLimit;
        if (Single(query, "limit") is { Length: > 0 } rawLimit &&
            (!int.TryParse(rawLimit, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out limit) || limit is < 1 or > MaxLimit))
            throw ProblemException.BadRequest("limit must be a whole number from 1 to 1000.");

        var me = access.UserId;
        var listIds = db.Lists.Where(l => l.Space!.WorkspaceId == id).Select(l => l.Id);
        var tasks = db.Tasks.Where(t => listIds.Contains(t.ListId));
        // Mine: assigned to me, or created by me and assigned to nobody. Subtasks are included either way.
        if (!all) tasks = tasks.Where(t => t.Assignees.Any(a => a.UserId == me) || (t.CreatedById == me && !t.Assignees.Any()));
        if (!includeDone)
        {
            if (doneSince is { } day)
            {
                var from = new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
                tasks = tasks.Where(t => t.CompletedAt == null || t.CompletedAt >= from);
            }
            else tasks = tasks.Where(t => t.CompletedAt == null);
        }

        IOrderedQueryable<TaskItem> ordered;
        if (q is not null)
        {
            var escaped = EscapeLike(q);
            string contains = $"%{escaped}%", prefix = $"{escaped}%";
            tasks = tasks.Where(t => EF.Functions.ILike(t.Title, contains, @"\") || EF.Functions.ILike(t.DescriptionText, contains, @"\"));
            // Open tasks first, then titles starting with q, titles containing q, description-only matches, then the most recently updated.
            ordered = tasks.OrderBy(t => t.CompletedAt != null)
                .ThenBy(t => EF.Functions.ILike(t.Title, prefix, @"\") ? 0 : EF.Functions.ILike(t.Title, contains, @"\") ? 1 : 2)
                .ThenByDescending(t => t.UpdatedAt).ThenBy(t => t.Id);
        }
        else
        {
            ordered = tasks.OrderBy(t => t.DueDate == null).ThenBy(t => t.DueDate)
                .ThenBy(t => t.Position).ThenBy(t => t.CreatedAt).ThenBy(t => t.Id);
        }
        var result = await ordered.Take(limit).Select(TaskProjections.ToDto).AsSplitQuery().ToListAsync(ct);

        // The parents of returned subtasks that are not themselves returned, so the client can label them.
        var returned = result.Select(t => t.Id).ToHashSet();
        var parentIds = result.Where(t => t.ParentTaskId is { } p && !returned.Contains(p)).Select(t => t.ParentTaskId!.Value).Distinct().ToList();
        var parents = new List<TaskRefDto>();
        if (parentIds.Count > 0)
        {
            var refs = await db.Tasks.Where(t => parentIds.Contains(t.Id)).Select(t => new TaskRefDto(t.Id, t.Title, t.ListId)).ToDictionaryAsync(t => t.Id, ct);
            parents = parentIds.Where(refs.ContainsKey).Select(p => refs[p]).ToList();
        }
        return new WorkspaceTasksDto(result, parents);
    }

    /// <summary>Escapes LIKE wildcards with the escape character "\".</summary>
    public static string EscapeLike(string s) => s.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_");

    private static string? Single(IQueryCollection query, string key) =>
        query.TryGetValue(key, out var values) && values.Count > 0 ? values[0] : null;
}
