using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Taskloom.Domain;
using Taskloom.Infrastructure;

namespace Taskloom.Api;

/// <summary>
/// The one way to build a <see cref="TaskDto"/> (spec 5.2): an EF projection, so the counts, assignees and tags come
/// from the database in the same query. Endpoints that change a task re-query it with this after SaveChanges.
/// </summary>
public static class TaskProjections
{
    public static readonly Expression<Func<TaskItem, TaskDto>> ToDto = t => new TaskDto(
        t.Id, t.ListId, t.StatusId, t.ParentTaskId,
        t.Title, !string.IsNullOrWhiteSpace(t.DescriptionText), t.Priority,
        t.Assignees.OrderBy(a => a.CreatedAt).ThenBy(a => a.UserId).Select(a => a.UserId).ToList(),
        t.Tags.OrderBy(x => x.Tag!.NormalizedName).ThenBy(x => x.TagId).Select(x => x.TagId).ToList(),
        t.StartDate, t.DueDate, t.Position,
        t.CreatedById, t.CreatedAt, t.UpdatedAt, t.CompletedAt,
        t.Subtasks.Count(), t.Subtasks.Count(s => s.CompletedAt != null), t.Comments.Count(), t.Attachments.Count());

    /// <summary>Tasks in their stable order: position, then creation time, then id.</summary>
    public static IOrderedQueryable<TaskItem> InOrder(this IQueryable<TaskItem> tasks) =>
        tasks.OrderBy(t => t.Position).ThenBy(t => t.CreatedAt).ThenBy(t => t.Id);

    /// <summary>One task as a DTO (after SaveChanges, so the response shows what was stored).</summary>
    public static Task<TaskDto> LoadAsync(TaskloomDbContext db, Guid taskId, CancellationToken ct = default) =>
        db.Tasks.Where(t => t.Id == taskId).Select(ToDto).FirstAsync(ct);

    /// <summary>Several tasks as DTOs, in the order of <paramref name="taskIds"/> (bulk results keep the request order).</summary>
    public static async Task<List<TaskDto>> LoadManyAsync(TaskloomDbContext db, IReadOnlyList<Guid> taskIds, CancellationToken ct = default)
    {
        var rows = await db.Tasks.Where(t => taskIds.Contains(t.Id)).Select(ToDto).AsSplitQuery().ToListAsync(ct);
        var byId = rows.ToDictionary(t => t.Id);
        return taskIds.Where(byId.ContainsKey).Select(id => byId[id]).ToList();
    }
}
