using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
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

public class NotFoundException : Exception;
public class ForbiddenException : Exception;

/// <summary>
/// Every read and write goes through here, so a user only ever touches
/// workspaces they are a member of, with the right role.
/// </summary>
public class Access(TaskloomDbContext db, IHttpContextAccessor http)
{
    public Guid UserId => http.HttpContext!.User.UserId();

    public async Task<WorkspaceRole> RequireWorkspaceAsync(Guid workspaceId, WorkspaceRole atLeast = WorkspaceRole.Guest)
    {
        var member = await db.WorkspaceMembers.AsNoTracking()
            .FirstOrDefaultAsync(m => m.WorkspaceId == workspaceId && m.UserId == UserId);
        if (member is null) throw new NotFoundException();
        // Lower enum value = more rights (Owner = 0).
        if (member.Role > atLeast) throw new ForbiddenException();
        return member.Role;
    }

    public async Task<Space> RequireSpaceAsync(Guid spaceId, WorkspaceRole atLeast = WorkspaceRole.Guest)
    {
        var space = await db.Spaces.FirstOrDefaultAsync(s => s.Id == spaceId) ?? throw new NotFoundException();
        await RequireWorkspaceAsync(space.WorkspaceId, atLeast);
        return space;
    }

    public async Task<(TaskList List, Guid WorkspaceId)> RequireListAsync(Guid listId, WorkspaceRole atLeast = WorkspaceRole.Guest)
    {
        var row = await db.Lists.Where(l => l.Id == listId)
            .Select(l => new { List = l, l.Space!.WorkspaceId }).FirstOrDefaultAsync()
            ?? throw new NotFoundException();
        await RequireWorkspaceAsync(row.WorkspaceId, atLeast);
        return (row.List, row.WorkspaceId);
    }

    public async Task<(TaskItem Task, Guid WorkspaceId)> RequireTaskAsync(Guid taskId, WorkspaceRole atLeast = WorkspaceRole.Guest)
    {
        var task = await db.Tasks.FirstOrDefaultAsync(t => t.Id == taskId) ?? throw new NotFoundException();
        var (_, workspaceId) = await RequireListAsync(task.ListId, atLeast);
        return (task, workspaceId);
    }
}
