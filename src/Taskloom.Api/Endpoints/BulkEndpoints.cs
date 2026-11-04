using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Taskloom.Application;
using Taskloom.Domain;
using Taskloom.Infrastructure;

namespace Taskloom.Api.Endpoints;

/// <summary>
/// POST /api/tasks/bulk and /api/tasks/bulk-delete (spec 5.4 #34, #35 and 5.5). The client sends at most 500 items
/// per call and splits bigger selections into chunks.
/// </summary>
public static class BulkEndpoints
{
    public static void MapBulkEndpoints(this RouteGroupBuilder api)
    {
        api.MapPost("/tasks/bulk", UpdateAsync);
        api.MapPost("/tasks/bulk-delete", DeleteAsync);
    }

    /// <summary>
    /// Every update follows the PATCH rules (title and description are ignored). One transaction: if any update is
    /// invalid the response is a 400 naming the problem and nothing changes. Results keep the request order.
    /// </summary>
    private static async Task<IResult> UpdateAsync(JsonElement body, Access access, TaskloomDbContext db)
    {
        Json.RequireReadable(body);
        if (!body.TryGetPropertyIgnoreCase("updates", out var updates) || updates.ValueKind != JsonValueKind.Array)
            throw ProblemException.BadRequest("Send the changes as a list of updates.");
        var items = updates.EnumerateArray().ToList();
        if (items.Count is < 1 or > Limits.BulkItems) throw ProblemException.BadRequest("Send between 1 and 500 updates at a time.");
        var ids = new List<Guid>(items.Count);
        foreach (var item in items)
        {
            if (!item.TryGetPropertyIgnoreCase("id", out var idValue) || !idValue.TryGetGuidValue(out var taskId))
                throw ProblemException.BadRequest("Each update needs the id of a task.");
            ids.Add(taskId);
        }
        if (ids.Distinct().Count() != ids.Count) throw ProblemException.BadRequest("Each task can only appear once in a bulk update.");

        await using var tx = await db.Database.BeginTransactionAsync();
        var tasks = await db.Tasks.Include(t => t.Assignees).Include(t => t.Tags)
            .Where(t => ids.Contains(t.Id)).AsSplitQuery().ToDictionaryAsync(t => t.Id);
        // All tasks must exist and share one workspace in which the caller is Member or above.
        if (tasks.Count != ids.Count) throw new NotFoundException();
        var listIds = tasks.Values.Select(t => t.ListId).Distinct().ToList();
        var workspaceIds = await db.Lists.Where(l => listIds.Contains(l.Id)).Select(l => l.Space!.WorkspaceId).Distinct().ToListAsync();
        if (workspaceIds.Count != 1) throw new NotFoundException();
        await access.RequireWorkspaceAsync(workspaceIds[0], WorkspaceRole.Member);

        var pairs = ids.Select((id, i) => (tasks[id], TaskChange.Parse(items[i], TaskChangeKind.Bulk))).ToList();
        var now = Clock.Now();
        var patcher = await TaskPatcher.CreateAsync(db, workspaceIds[0], new ActivityRecorder(db, access.UserId, now), now, pairs);
        foreach (var (task, change) in pairs) await patcher.ApplyAsync(task, change);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return Results.Ok(new BulkUpdateResultDto(await TaskProjections.LoadManyAsync(db, ids)));
    }

    /// <summary>
    /// Deletes up to 500 tasks with their subtasks and stored files. Ids that no longer exist are ignored (an undo
    /// toast may commit a delete twice). Nothing is deleted when any task is in a workspace where the caller isn't a
    /// member (404) or is only a Guest (403).
    /// </summary>
    private static async Task<IResult> DeleteAsync(BulkDeleteRequest req, Access access, TaskloomDbContext db, IFileStore files)
    {
        if (req.Ids is not { Count: >= 1 and <= Limits.BulkItems }) throw ProblemException.BadRequest("Send between 1 and 500 task ids at a time.");
        var ids = req.Ids.Distinct().ToList();
        var found = await db.Tasks.Where(t => ids.Contains(t.Id)).Select(t => new { t.Id, t.ListId }).ToListAsync();
        if (found.Count == 0) return Results.NoContent();

        var listIds = found.Select(t => t.ListId).Distinct().ToList();
        var workspaceIds = await db.Lists.Where(l => listIds.Contains(l.Id)).Select(l => l.Space!.WorkspaceId).Distinct().ToListAsync();
        var me = access.UserId;
        var roles = await db.WorkspaceMembers.Where(m => m.UserId == me && workspaceIds.Contains(m.WorkspaceId)).Select(m => m.Role).ToListAsync();
        if (roles.Count != workspaceIds.Count) throw new NotFoundException();
        if (roles.Any(r => r > WorkspaceRole.Member)) throw new ForbiddenException();

        var taskIds = found.Select(t => t.Id).ToList();
        var keys = await db.Attachments
            .Where(a => db.Tasks.Any(t => t.Id == a.TaskId && (taskIds.Contains(t.Id) || (t.ParentTaskId != null && taskIds.Contains(t.ParentTaskId.Value)))))
            .Select(a => a.StorageKey).ToListAsync();
        // Subtasks and everything hanging off the tasks go with them (CASCADE).
        await db.Tasks.Where(t => taskIds.Contains(t.Id)).ExecuteDeleteAsync();
        foreach (var key in keys) await files.DeleteAsync(key);
        return Results.NoContent();
    }
}
