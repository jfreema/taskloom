using Microsoft.EntityFrameworkCore;
using Taskloom.Api.Auth;
using Taskloom.Domain;
using Taskloom.Infrastructure;

namespace Taskloom.Api.Endpoints;

/// <summary>
/// Site admins (Auth:SiteAdmins, Security 1 spec D) see everyone who has signed in or been invited, turn accounts off
/// and on, and sign people out everywhere. To anyone else these routes answer 404, as if they didn't exist. A turned-off
/// person keeps their memberships, assignments and comments; only signing in stops.
/// </summary>
public static class AdminEndpoints
{
    public const string SelfMessage = "You can't turn off your own account.";
    public const string SiteAdminMessage = "Site admins can't be turned off here. Remove them from Auth:SiteAdmins first.";

    public static void MapAdminEndpoints(this RouteGroupBuilder api)
    {
        var users = api.MapGroup("/admin/users").AddEndpointFilter(async (ctx, next) =>
        {
            await ctx.HttpContext.RequestServices.GetRequiredService<Access>().RequireSiteAdminAsync();
            return await next(ctx);
        });

        users.MapGet("", async (TaskloomDbContext db, AuthOptions options, TimeProvider time) =>
            await Rows(db, options, time.GetUtcNow(), db.Users));

        users.MapPost("/{id:guid}/disable", async (Guid id, Access access, TaskloomDbContext db, SessionService sessions,
            AuthOptions options, TimeProvider time, ILoggerFactory logs) =>
        {
            var admin = await access.RequireSiteAdminAsync();
            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id) ?? throw new NotFoundException();
            if (user.Id == admin.Id) throw ProblemException.BadRequest(SelfMessage);
            if (options.IsSiteAdmin(user.Email)) throw ProblemException.BadRequest(SiteAdminMessage);
            // Turning off an account that is already off keeps the first time.
            user.DisabledAt ??= Clock.Now();
            await db.SaveChangesAsync();
            await sessions.RevokeAllAsync(user.Id);
            Log(logs).LogInformation(
                "Site admin {AdminId} ({AdminEmail}) turned off the account of {UserId} ({UserEmail}) and signed them out everywhere",
                admin.Id, admin.Email, user.Id, user.Email);
            return Results.Ok(await Row(db, options, time, user.Id));
        });

        users.MapPost("/{id:guid}/enable", async (Guid id, Access access, TaskloomDbContext db, AuthOptions options,
            TimeProvider time, ILoggerFactory logs) =>
        {
            var admin = await access.RequireSiteAdminAsync();
            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id) ?? throw new NotFoundException();
            user.DisabledAt = null;
            await db.SaveChangesAsync();
            Log(logs).LogInformation("Site admin {AdminId} ({AdminEmail}) turned on the account of {UserId} ({UserEmail})",
                admin.Id, admin.Email, user.Id, user.Email);
            return Results.Ok(await Row(db, options, time, user.Id));
        });

        // Allowed for yourself too: it signs you out everywhere, this browser included (at its next check).
        users.MapPost("/{id:guid}/sign-out", async (Guid id, Access access, TaskloomDbContext db, SessionService sessions,
            ILoggerFactory logs) =>
        {
            var admin = await access.RequireSiteAdminAsync();
            var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id) ?? throw new NotFoundException();
            var ended = await sessions.RevokeAllAsync(user.Id);
            Log(logs).LogInformation("Site admin {AdminId} ({AdminEmail}) signed {UserId} ({UserEmail}) out everywhere ({Sessions} sessions ended)",
                admin.Id, admin.Email, user.Id, user.Email, ended);
            return Results.NoContent();
        });
    }

    /// <summary>Who did what to whom, at Information level, so the host's logs keep an audit trail.</summary>
    private static ILogger Log(ILoggerFactory logs) => logs.CreateLogger(typeof(AdminEndpoints));

    private static async Task<AdminUserDto> Row(TaskloomDbContext db, AuthOptions options, TimeProvider time, Guid id) =>
        (await Rows(db, options, time.GetUtcNow(), db.Users.Where(u => u.Id == id))).Single();

    /// <summary>
    /// The users as admin rows, by display name ignoring case, then id. "Last active" is the latest check of any of their
    /// sessions; active sessions are those neither signed out nor expired.
    /// </summary>
    private static async Task<List<AdminUserDto>> Rows(TaskloomDbContext db, AuthOptions options, DateTimeOffset now, IQueryable<User> users)
    {
        var rows = await users
            .OrderBy(u => u.DisplayName.ToLower()).ThenBy(u => u.Id)
            .Select(u => new
            {
                u.Id, u.Email, u.DisplayName, u.CreatedAt,
                Pending = !u.Identities.Any(),
                Disabled = u.DisabledAt != null,
                LastSeenAt = db.UserSessions.Where(s => s.UserId == u.Id).Max(s => (DateTimeOffset?)s.LastSeenAt),
                ActiveSessions = db.UserSessions.Count(s => s.UserId == u.Id && s.RevokedAt == null && s.ExpiresAt > now),
                Workspaces = db.WorkspaceMembers.Count(m => m.UserId == u.Id),
            })
            .ToListAsync();
        return rows.Select(r => new AdminUserDto(r.Id, r.Email, r.DisplayName, r.Pending, r.Disabled, options.IsSiteAdmin(r.Email),
            r.CreatedAt, r.LastSeenAt, r.ActiveSessions, r.Workspaces)).ToList();
    }
}
