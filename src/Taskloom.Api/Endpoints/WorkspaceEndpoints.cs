using Microsoft.EntityFrameworkCore;
using Taskloom.Api.Auth;
using Taskloom.Api.Notifications;
using Taskloom.Application;
using Taskloom.Domain;
using Taskloom.Infrastructure;

namespace Taskloom.Api.Endpoints;

/// <summary>The signed-in user, workspaces and members (spec 5.4 #5–#14). Spaces, lists and statuses are in <see cref="HierarchyEndpoints"/>.</summary>
public static class WorkspaceEndpoints
{
    public const string RoleMessage = "Choose Admin, Member or Guest.";

    public static void MapWorkspaceEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/me", async (Access access, TaskloomDbContext db, AuthOptions options, NotificationOptions notifications) =>
        {
            var u = await db.Users.FirstOrDefaultAsync(x => x.Id == access.UserId);
            // A session whose user is gone is signed out: 401, without a body.
            return u is null ? throw new UnauthorizedAccessException() : Results.Ok(Me(u, options, notifications));
        });

        api.MapPatch("/me", async (UpdateMeRequest req, Access access, TaskloomDbContext db, AuthOptions options, NotificationOptions notifications) =>
        {
            var name = Text.Require(req.DisplayName, Limits.Name, "Enter a display name.", "Display names can be up to 200 characters.");
            var u = await db.Users.FirstOrDefaultAsync(x => x.Id == access.UserId);
            if (u is null) throw new UnauthorizedAccessException();
            u.DisplayName = name;
            await db.SaveChangesAsync();
            return Results.Ok(Me(u, options, notifications));
        });

        // Features 2: which emails the caller gets, and the time zone their browser is in (it decides when their morning
        // reminder goes out). Only what is sent changes.
        api.MapPatch("/me/notifications", async (UpdateNotificationSettingsRequest req, Access access, TaskloomDbContext db, AuthOptions options,
            NotificationOptions notifications) =>
        {
            var zone = req.TimeZone?.Trim();
            if (zone is not null && !TimeZones.IsKnown(zone)) throw ProblemException.BadRequest(TimeZones.UnknownMessage);
            var u = await db.Users.FirstOrDefaultAsync(x => x.Id == access.UserId);
            if (u is null) throw new UnauthorizedAccessException();
            if (req.EmailMentions is { } mentions) u.EmailMentions = mentions;
            if (req.EmailReminders is { } reminders) u.EmailReminders = reminders;
            if (zone is not null) u.TimeZone = zone;
            await db.SaveChangesAsync();
            return Results.Ok(Me(u, options, notifications));
        });

        // Ends every other session of the caller (a browser they no longer use, a copied cookie); this one stays.
        api.MapPost("/me/sign-out-others", async (Access access, SessionService sessions, HttpContext http) =>
        {
            await sessions.RevokeAllAsync(access.UserId, keep: SessionService.SessionIdOf(http.User), http.RequestAborted);
            return Results.NoContent();
        });

        // ---- Workspaces ----
        // The whole tree the caller can see: workspaces by creation, then the spaces they can see (every space that isn't
        // private, and the private ones they are in) and their lists by position, each list with its statuses.
        api.MapGet("/workspaces", async (Access access, TaskloomDbContext db) =>
        {
            var uid = access.UserId;
            var memberships = await (
                    from m in db.WorkspaceMembers
                    where m.UserId == uid
                    join w in db.Workspaces on m.WorkspaceId equals w.Id
                    orderby w.CreatedAt, w.Id
                    select new { w.Id, w.Name, m.Role })
                .ToListAsync();
            var spaces = await db.Spaces.VisibleTo(db, uid)
                .OrderBy(s => s.Position).ThenBy(s => s.Id)
                .Select(s => new
                {
                    s.WorkspaceId,
                    Dto = new SpaceDto(s.Id, s.Name, s.Color, s.Position, s.IsPrivate,
                        s.Members.OrderBy(m => m.AddedAt).ThenBy(m => m.UserId).Select(m => m.UserId).ToList(),
                        s.Lists.OrderBy(l => l.Position).ThenBy(l => l.Id).Select(l => new ListSummaryDto(l.Id, l.SpaceId, l.Name, l.Position, l.Color, l.Icon,
                            l.Statuses.OrderBy(x => x.Position).ThenBy(x => x.Id)
                                .Select(x => new StatusDto(x.Id, x.ListId, x.Name, x.Color, x.Category, x.Position)).ToList()))
                            .ToList()),
                })
                .AsSplitQuery().ToListAsync();
            var byWorkspace = spaces.ToLookup(s => s.WorkspaceId, s => s.Dto);
            return memberships.Select(m => new WorkspaceTreeDto(m.Id, m.Name, m.Role, byWorkspace[m.Id].ToList())).ToList();
        });

        api.MapPost("/workspaces", async (CreateWorkspaceRequest req, Access access, TaskloomDbContext db) =>
        {
            var name = Text.Required(req.Name, Limits.Name) ?? throw ProblemException.BadRequest("Enter a workspace name.");
            var w = new Workspace { Id = req.Id ?? Guid.NewGuid(), Name = name };
            db.Workspaces.Add(w);
            db.WorkspaceMembers.Add(new WorkspaceMember { WorkspaceId = w.Id, UserId = access.UserId, Role = WorkspaceRole.Owner });
            await db.SaveChangesAsync();
            return Results.Created($"/api/workspaces/{w.Id}", new WorkspaceTreeDto(w.Id, w.Name, WorkspaceRole.Owner, []));
        });

        api.MapPatch("/workspaces/{id:guid}", async (Guid id, RenameRequest req, Access access, TaskloomDbContext db) =>
        {
            await access.RequireWorkspaceAsync(id, WorkspaceRole.Admin);
            var name = Text.Required(req.Name, Limits.Name) ?? throw ProblemException.BadRequest("Enter a workspace name.");
            var w = await db.Workspaces.FirstAsync(x => x.Id == id);
            w.Name = name;
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        // Everything in it goes with it (CASCADE), and so do its tasks' stored files.
        api.MapDelete("/workspaces/{id:guid}", async (Guid id, Access access, TaskloomDbContext db, IFileStore files) =>
        {
            await access.RequireWorkspaceAsync(id, WorkspaceRole.Owner);
            var keys = await db.Attachments
                .Where(a => db.Tasks.Any(t => t.Id == a.TaskId && db.Lists.Any(l => l.Id == t.ListId && l.Space!.WorkspaceId == id)))
                .Select(a => a.StorageKey).ToListAsync();
            await db.Workspaces.Where(x => x.Id == id).ExecuteDeleteAsync();
            foreach (var key in keys) await files.DeleteAsync(key);
            return Results.NoContent();
        });

        // ---- Members (spec 5.5 Members) ----
        api.MapGet("/workspaces/{id:guid}/members", async (Guid id, Access access, TaskloomDbContext db) =>
        {
            await access.RequireWorkspaceAsync(id);
            return await db.WorkspaceMembers.Where(m => m.WorkspaceId == id)
                .OrderBy(m => m.User!.DisplayName.ToLower()).ThenBy(m => m.User!.DisplayName).ThenBy(m => m.UserId)
                .Select(m => new MemberDto(m.UserId, m.User!.Email, m.User.DisplayName, m.Role, !m.User.Identities.Any(), m.User.DisabledAt != null,
                    UserPhotos.UrlFor(m.UserId, m.User.PhotoVersion)))
                .ToListAsync();
        });

        // An invite creates a placeholder (pending) user when nobody has the email; their first sign-in links to it.
        // No email is sent (spec 2.3).
        api.MapPost("/workspaces/{id:guid}/members", async (Guid id, AddMemberRequest req, Access access, TaskloomDbContext db) =>
        {
            await access.RequireWorkspaceAsync(id, WorkspaceRole.Admin);
            if (!IsAssignable(req.Role)) throw ProblemException.BadRequest(RoleMessage);
            var email = Emails.Normalize(req.Email) ?? throw ProblemException.BadRequest(Emails.InvalidMessage);
            var given = string.IsNullOrWhiteSpace(req.DisplayName) ? null
                : Text.Require(req.DisplayName, Limits.Name, "Enter a display name.", "Display names can be up to 200 characters.");

            var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email);
            var pending = true;
            if (user is null)
            {
                // The name from "Name <email>", else one guessed from the address; the provider's name replaces it on first sign-in.
                user = new User { Email = email, DisplayName = given ?? NameGuess.FromEmail(email) };
                db.Users.Add(user);
            }
            else
            {
                if (await db.WorkspaceMembers.AnyAsync(m => m.WorkspaceId == id && m.UserId == user.Id))
                    throw ProblemException.Conflict($"{user.Email} is already a member of this workspace.");
                pending = !await db.UserIdentities.AnyAsync(i => i.UserId == user.Id);
            }
            db.WorkspaceMembers.Add(new WorkspaceMember { WorkspaceId = id, UserId = user.Id, Role = req.Role });
            await db.SaveChangesAsync();
            return Results.Ok(new MemberDto(user.Id, user.Email, user.DisplayName, req.Role, pending, user.DisabledAt != null, UserPhotos.UrlFor(user.Id, user.PhotoVersion)));
        });

        // Admins may change other Admins; nobody changes their own role or the Owner's, and nobody becomes Owner.
        api.MapPatch("/workspaces/{id:guid}/members/{userId:guid}", async (Guid id, Guid userId, UpdateMemberRequest req, Access access, TaskloomDbContext db) =>
        {
            await access.RequireWorkspaceAsync(id, WorkspaceRole.Admin);
            var m = await db.WorkspaceMembers.FirstOrDefaultAsync(x => x.WorkspaceId == id && x.UserId == userId) ?? throw new NotFoundException();
            if (userId == access.UserId) throw ProblemException.BadRequest("You can't change your own role.");
            if (m.Role == WorkspaceRole.Owner) throw ProblemException.BadRequest("The workspace owner can't be changed.");
            if (!IsAssignable(req.Role)) throw ProblemException.BadRequest(RoleMessage);
            m.Role = req.Role;
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        // Removing someone also unassigns them from the workspace's tasks and takes them out of its private spaces, so
        // inviting them again doesn't bring those back.
        api.MapDelete("/workspaces/{id:guid}/members/{userId:guid}", async (Guid id, Guid userId, Access access, TaskloomDbContext db) =>
        {
            await access.RequireWorkspaceAsync(id, WorkspaceRole.Admin);
            var m = await db.WorkspaceMembers.FirstOrDefaultAsync(x => x.WorkspaceId == id && x.UserId == userId) ?? throw new NotFoundException();
            if (m.Role == WorkspaceRole.Owner) throw ProblemException.BadRequest("The workspace owner can't be removed.");
            if (userId == access.UserId) throw ProblemException.BadRequest("You can't remove yourself.");

            await using var tx = await db.Database.BeginTransactionAsync();
            await db.TaskAssignees
                .Where(a => a.UserId == userId && db.Tasks.Any(t => t.Id == a.TaskId && db.Lists.Any(l => l.Id == t.ListId && l.Space!.WorkspaceId == id)))
                .ExecuteDeleteAsync();
            await db.SpaceMembers
                .Where(x => x.UserId == userId && db.Spaces.Any(s => s.Id == x.SpaceId && s.WorkspaceId == id))
                .ExecuteDeleteAsync();
            // Their Inbox rows about this workspace go too, read or not: they can't see the tasks any more.
            await db.Notifications
                .Where(n => n.UserId == userId && db.Lists.Any(l => l.Id == n.Task!.ListId && l.Space!.WorkspaceId == id))
                .ExecuteDeleteAsync();
            db.WorkspaceMembers.Remove(m);
            await db.SaveChangesAsync();
            await tx.CommitAsync();
            return Results.NoContent();
        });
    }

    public static MeDto Me(User u, AuthOptions options, NotificationOptions notifications) =>
        new(u.Id, u.Email, u.DisplayName, options.IsSiteAdmin(u.Email), UserPhotos.UrlFor(u.Id, u.PhotoVersion), u.PhotoChosen,
            u.TimeZone, u.EmailMentions, u.EmailReminders, notifications.EmailConfigured);

    private static bool IsAssignable(WorkspaceRole role) => role is WorkspaceRole.Admin or WorkspaceRole.Member or WorkspaceRole.Guest;
}
