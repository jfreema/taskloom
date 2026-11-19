using System.Linq.Expressions;
using System.Security.Claims;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Taskloom.Api.Auth;
using Taskloom.Api.Notifications;
using Taskloom.Domain;
using Taskloom.Infrastructure;

namespace Taskloom.Api;

public static class ClaimsExtensions
{
    public const string UserIdClaim = "taskloom:uid";

    public static Guid UserId(this ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue(UserIdClaim), out var id)
            ? id
            : throw new UnauthorizedAccessException();
}

public static class HttpContextExtensions
{
    /// <summary>A 401 has no body (spec 5.1), so the status code pages middleware must not add a ProblemDetails one.</summary>
    public static void RespondUnauthorized(this HttpContext ctx)
    {
        ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
        if (ctx.Features.Get<IStatusCodePagesFeature>() is { } pages) pages.Enabled = false;
    }
}

public class NotFoundException : Exception;
public class ForbiddenException : Exception;

/// <summary>
/// Every read and write goes through here, so a user only ever touches workspaces they are a member of, with the right
/// role, and only the spaces they can see (<see cref="SpaceVisibility"/>). A space, list or task the caller can't see
/// is a 404, exactly like one that doesn't exist.
/// </summary>
public class Access(TaskloomDbContext db, IHttpContextAccessor http, AuthOptions options)
{
    private User? _siteAdmin;

    public Guid UserId => http.HttpContext!.User.UserId();

    /// <summary>
    /// The caller, when their email is in Auth:SiteAdmins; anyone else gets 404, so the site-admin routes look like they
    /// don't exist. Read once per request.
    /// </summary>
    public async Task<User> RequireSiteAdminAsync()
    {
        if (_siteAdmin is not null) return _siteAdmin;
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == UserId);
        if (user is null || !options.IsSiteAdmin(user.Email)) throw new NotFoundException();
        return _siteAdmin = user;
    }

    public async Task<WorkspaceRole> RequireWorkspaceAsync(Guid workspaceId, WorkspaceRole atLeast = WorkspaceRole.Guest)
    {
        var member = await db.WorkspaceMembers.AsNoTracking()
            .FirstOrDefaultAsync(m => m.WorkspaceId == workspaceId && m.UserId == UserId);
        if (member is null) throw new NotFoundException();
        // Lower enum value = more rights (Owner = 0).
        if (member.Role > atLeast) throw new ForbiddenException();
        return member.Role;
    }

    /// <summary>The space (tracked) when the caller can see it and has the role; one query.</summary>
    public async Task<Space> RequireSpaceAsync(Guid spaceId, WorkspaceRole atLeast = WorkspaceRole.Guest)
    {
        var uid = UserId;
        var row = await db.Spaces.Where(s => s.Id == spaceId)
            .Select(s => new
            {
                Space = s,
                Role = db.WorkspaceMembers.Where(m => m.WorkspaceId == s.WorkspaceId && m.UserId == uid).Select(m => (WorkspaceRole?)m.Role).FirstOrDefault(),
                InSpace = db.SpaceMembers.Any(m => m.SpaceId == s.Id && m.UserId == uid),
            })
            .FirstOrDefaultAsync() ?? throw new NotFoundException();
        Check(row.Role, row.Space.IsPrivate, row.InSpace, atLeast);
        return row.Space;
    }

    /// <summary>The list (tracked) and its workspace when the caller can see its space and has the role; one query.</summary>
    public async Task<(TaskList List, Guid WorkspaceId)> RequireListAsync(Guid listId, WorkspaceRole atLeast = WorkspaceRole.Guest)
    {
        var uid = UserId;
        var row = await db.Lists.Where(l => l.Id == listId)
            .Select(l => new
            {
                List = l,
                l.Space!.WorkspaceId,
                l.Space.IsPrivate,
                Role = db.WorkspaceMembers.Where(m => m.WorkspaceId == l.Space.WorkspaceId && m.UserId == uid).Select(m => (WorkspaceRole?)m.Role).FirstOrDefault(),
                InSpace = db.SpaceMembers.Any(m => m.SpaceId == l.SpaceId && m.UserId == uid),
            })
            .FirstOrDefaultAsync() ?? throw new NotFoundException();
        Check(row.Role, row.IsPrivate, row.InSpace, atLeast);
        return (row.List, row.WorkspaceId);
    }

    /// <summary>The task (tracked) and its workspace when the caller can see its space and has the role; one query.</summary>
    public Task<(TaskItem Task, Guid WorkspaceId)> RequireTaskAsync(Guid taskId, WorkspaceRole atLeast = WorkspaceRole.Guest,
        CancellationToken ct = default) =>
        RequireTaskAsync(taskId, t => t, atLeast, ct);

    /// <summary>
    /// <paramref name="data"/> read from the task, and its workspace, when the caller can see its space and has the role.
    /// The check and the data come back in one query, so an endpoint can read what it needs of the task (a tracked
    /// entity, its assignees, its creator) in the same trip to the database.
    /// </summary>
    public async Task<(T Data, Guid WorkspaceId)> RequireTaskAsync<T>(Guid taskId, Expression<Func<TaskItem, T>> data,
        WorkspaceRole atLeast = WorkspaceRole.Guest, CancellationToken ct = default)
    {
        var row = await CheckTaskAsync(taskId, data, null, atLeast, ct);
        return (row.Data, row.WorkspaceId);
    }

    /// <summary>
    /// The same, and in the same query the people who should hear about what the caller does to the task
    /// (<paramref name="audience"/>): only members of the workspace who can see the task's space, never the caller.
    /// </summary>
    public async Task<(T Data, Guid WorkspaceId, List<TaskPerson> People)> RequireTaskAsync<T>(Guid taskId, Expression<Func<TaskItem, T>> data,
        Audience audience, WorkspaceRole atLeast = WorkspaceRole.Guest, CancellationToken ct = default)
    {
        var row = await CheckTaskAsync(taskId, data, audience, atLeast, ct);
        return (row.Data, row.WorkspaceId, row.People);
    }

    private async Task<TaskCheck<T>> CheckTaskAsync<T>(Guid taskId, Expression<Func<TaskItem, T>> data, Audience? audience,
        WorkspaceRole atLeast, CancellationToken ct)
    {
        var uid = UserId;
        Expression<Func<TaskInList, T, TaskCheck<T>>> shape;
        // Without anyone to read, the plain check: no join for people who can't be there.
        if (audience is { } a && (a.Watchers || a.Named is { Count: > 0 }))
        {
            var watchers = a.Watchers;
            var named = a.Named?.ToList() ?? [];
            shape = (x, d) => new TaskCheck<T>
            {
                Data = d,
                WorkspaceId = x.List.Space!.WorkspaceId,
                IsPrivate = x.List.Space.IsPrivate,
                Role = db.WorkspaceMembers.Where(m => m.WorkspaceId == x.List.Space.WorkspaceId && m.UserId == uid).Select(m => (WorkspaceRole?)m.Role).FirstOrDefault(),
                InSpace = db.SpaceMembers.Any(m => m.SpaceId == x.List.SpaceId && m.UserId == uid),
                // The same question as SpaceVisibility.VisibleTo, asked of each watcher and each person named.
                People = db.WorkspaceMembers
                    .Where(m => m.WorkspaceId == x.List.Space.WorkspaceId && m.UserId != uid
                        && ((watchers && (m.UserId == x.Task.CreatedById || db.TaskAssignees.Any(s => s.TaskId == x.Task.Id && s.UserId == m.UserId)))
                            || named.Contains(m.UserId))
                        && (!x.List.Space.IsPrivate || db.SpaceMembers.Any(s => s.SpaceId == x.List.SpaceId && s.UserId == m.UserId)))
                    .Select(m => new TaskPerson(m.UserId, m.User!.DisplayName,
                        m.UserId == x.Task.CreatedById || db.TaskAssignees.Any(s => s.TaskId == x.Task.Id && s.UserId == m.UserId)))
                    .ToList(),
            };
        }
        else
        {
            shape = (x, d) => new TaskCheck<T>
            {
                Data = d,
                WorkspaceId = x.List.Space!.WorkspaceId,
                IsPrivate = x.List.Space.IsPrivate,
                Role = db.WorkspaceMembers.Where(m => m.WorkspaceId == x.List.Space.WorkspaceId && m.UserId == uid).Select(m => (WorkspaceRole?)m.Role).FirstOrDefault(),
                InSpace = db.SpaceMembers.Any(m => m.SpaceId == x.List.SpaceId && m.UserId == uid),
            };
        }
        // shape with d replaced by data's body read from x.Task.
        var x = shape.Parameters[0];
        var dataBody = Replace(data.Body, data.Parameters[0], Expression.Property(x, nameof(TaskInList.Task)));
        var select = Expression.Lambda<Func<TaskInList, TaskCheck<T>>>(Replace(shape.Body, shape.Parameters[1], dataBody), x);

        var row = await db.Tasks.Where(t => t.Id == taskId)
            .Join(db.Lists, t => t.ListId, l => l.Id, (t, l) => new TaskInList { Task = t, List = l })
            .Select(select)
            .FirstOrDefaultAsync(ct) ?? throw new NotFoundException();
        Check(row.Role, row.IsPrivate, row.InSpace, atLeast);
        return row;
    }

    /// <summary>
    /// The workspace of tasks in <paramref name="listIds"/> (distinct) when every list exists, all are in one workspace,
    /// the caller can see each one's space and has the role there; one query. Otherwise 404, or 403 for the role.
    /// The same query says who among <paramref name="people"/> can see each list (members of the workspace, in its
    /// space when that is private), for the notifications of a bulk change.
    /// </summary>
    public async Task<(Guid WorkspaceId, Dictionary<Guid, HashSet<Guid>> CanSee)> RequireListsAsync(IReadOnlyCollection<Guid> listIds,
        WorkspaceRole atLeast, IReadOnlyCollection<Guid> people)
    {
        var uid = UserId;
        var candidates = people.Where(p => p != uid).Distinct().ToList();
        var rows = await db.Lists.Where(l => listIds.Contains(l.Id))
            .Select(l => new
            {
                l.Id,
                l.Space!.WorkspaceId,
                l.Space.IsPrivate,
                Role = db.WorkspaceMembers.Where(m => m.WorkspaceId == l.Space.WorkspaceId && m.UserId == uid).Select(m => (WorkspaceRole?)m.Role).FirstOrDefault(),
                InSpace = db.SpaceMembers.Any(m => m.SpaceId == l.SpaceId && m.UserId == uid),
                CanSee = db.WorkspaceMembers
                    .Where(m => m.WorkspaceId == l.Space.WorkspaceId && candidates.Contains(m.UserId)
                        && (!l.Space.IsPrivate || db.SpaceMembers.Any(s => s.SpaceId == l.SpaceId && s.UserId == m.UserId)))
                    .Select(m => m.UserId).ToList(),
            })
            .ToListAsync();
        if (rows.Count != listIds.Count || rows.Select(r => r.WorkspaceId).Distinct().Count() != 1) throw new NotFoundException();
        // Every list must be visible before the role counts, so a task the caller can't see is a 404 whatever else is sent.
        foreach (var r in rows) Check(r.Role, r.IsPrivate, r.InSpace, WorkspaceRole.Guest);
        Check(rows[0].Role, false, false, atLeast);
        return (rows[0].WorkspaceId, rows.ToDictionary(r => r.Id, r => r.CanSee.ToHashSet()));
    }

    /// <summary>The ids of the lists of a workspace that the caller can see (a query to compose, not yet run).</summary>
    public IQueryable<Guid> VisibleListIds(Guid workspaceId)
    {
        var spaceIds = db.Spaces.Where(s => s.WorkspaceId == workspaceId).VisibleTo(db, UserId).Select(s => s.Id);
        return db.Lists.Where(l => spaceIds.Contains(l.SpaceId)).Select(l => l.Id);
    }

    /// <summary>Not a member, or a private space they aren't in: 404. A member below the role: 403.</summary>
    private static void Check(WorkspaceRole? role, bool isPrivate, bool inSpace, WorkspaceRole atLeast)
    {
        if (role is null || (isPrivate && !inSpace)) throw new NotFoundException();
        if (role > atLeast) throw new ForbiddenException();
    }

    private static Expression Replace(Expression tree, ParameterExpression parameter, Expression with) =>
        new ParameterReplacer(parameter, with).Visit(tree);

    private sealed class ParameterReplacer(ParameterExpression parameter, Expression with) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => node == parameter ? with : node;
    }

    private sealed class TaskInList
    {
        public required TaskItem Task { get; init; }
        public required TaskList List { get; init; }
    }

    private sealed class TaskCheck<T>
    {
        public required T Data { get; init; }
        public Guid WorkspaceId { get; init; }
        public bool IsPrivate { get; init; }
        public WorkspaceRole? Role { get; init; }
        public bool InSpace { get; init; }
        public List<TaskPerson> People { get; init; } = [];
    }
}

/// <summary>
/// Who can see a space: everyone in its workspace when it isn't private, else only its people (who must also still be in
/// the workspace). Kept in one place so every query asks the same question.
/// </summary>
public static class SpaceVisibility
{
    /// <summary>The spaces <paramref name="userId"/> can see.</summary>
    public static IQueryable<Space> VisibleTo(this IQueryable<Space> spaces, TaskloomDbContext db, Guid userId) =>
        spaces.Where(s => db.WorkspaceMembers.Any(m => m.WorkspaceId == s.WorkspaceId && m.UserId == userId)
                          && (!s.IsPrivate || db.SpaceMembers.Any(m => m.SpaceId == s.Id && m.UserId == userId)));

    /// <summary>The workspace members among <paramref name="userIds"/> and, of those, the ones who can see the space.</summary>
    public static async Task<(int Members, int CanSee)> CountAsync(TaskloomDbContext db, Guid spaceId, IReadOnlyCollection<Guid> userIds)
    {
        if (userIds.Count == 0) return (0, 0);
        var rows = await db.Spaces.Where(s => s.Id == spaceId)
            .SelectMany(s => db.WorkspaceMembers.Where(m => m.WorkspaceId == s.WorkspaceId && userIds.Contains(m.UserId))
                .Select(m => !s.IsPrivate || db.SpaceMembers.Any(x => x.SpaceId == s.Id && x.UserId == m.UserId)))
            .ToListAsync();
        return (rows.Count, rows.Count(x => x));
    }
}
