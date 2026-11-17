using System.Security.Claims;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Taskloom.Api.Auth;
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

    public async Task<(TaskItem Task, Guid WorkspaceId)> RequireTaskAsync(Guid taskId, WorkspaceRole atLeast = WorkspaceRole.Guest)
    {
        var task = await db.Tasks.FirstOrDefaultAsync(t => t.Id == taskId) ?? throw new NotFoundException();
        var (_, workspaceId) = await RequireListAsync(task.ListId, atLeast);
        return (task, workspaceId);
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
