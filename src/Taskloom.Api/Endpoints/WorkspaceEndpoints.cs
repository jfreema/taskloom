using Microsoft.EntityFrameworkCore;
using Taskloom.Domain;
using Taskloom.Infrastructure;

namespace Taskloom.Api.Endpoints;

/// <summary>The signed-in user, workspaces and members (spec 5.4 #5–#14). Spaces, lists and statuses are in <see cref="HierarchyEndpoints"/>.</summary>
public static class WorkspaceEndpoints
{
    public const string RoleMessage = "Choose Admin, Member or Guest.";

    public static void MapWorkspaceEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/me", async (Access access, TaskloomDbContext db) =>
        {
            var u = await db.Users.FirstOrDefaultAsync(x => x.Id == access.UserId);
            // A session whose user is gone is signed out: 401, without a body.
            return u is null ? throw new UnauthorizedAccessException() : Results.Ok(new UserDto(u.Id, u.Email, u.DisplayName));
        });

        api.MapPatch("/me", async (UpdateMeRequest req, Access access, TaskloomDbContext db) =>
        {
            var name = Text.Require(req.DisplayName, Limits.Name, "Enter a display name.", "Display names can be up to 200 characters.");
            var u = await db.Users.FirstOrDefaultAsync(x => x.Id == access.UserId);
            if (u is null) throw new UnauthorizedAccessException();
            u.DisplayName = name;
            await db.SaveChangesAsync();
            return Results.Ok(new UserDto(u.Id, u.Email, u.DisplayName));
        });

        // ---- Workspaces ----
        // The whole tree the caller can see: workspaces by creation, spaces and lists by position, each list with its statuses.
        api.MapGet("/workspaces", async (Access access, TaskloomDbContext db) =>
        {
            var uid = access.UserId;
            return await (
                    from m in db.WorkspaceMembers
                    where m.UserId == uid
                    join w in db.Workspaces on m.WorkspaceId equals w.Id
                    orderby w.CreatedAt, w.Id
                    select new WorkspaceTreeDto(w.Id, w.Name, m.Role,
                        w.Spaces.OrderBy(s => s.Position).ThenBy(s => s.Id).Select(s => new SpaceDto(s.Id, s.Name, s.Color, s.Position,
                            s.Lists.OrderBy(l => l.Position).ThenBy(l => l.Id).Select(l => new ListSummaryDto(l.Id, l.SpaceId, l.Name, l.Position,
                                l.Statuses.OrderBy(x => x.Position).ThenBy(x => x.Id)
                                    .Select(x => new StatusDto(x.Id, x.ListId, x.Name, x.Color, x.Category, x.Position)).ToList()))
                                .ToList()))
                            .ToList()))
                .AsSplitQuery().ToListAsync();
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

        api.MapDelete("/workspaces/{id:guid}", async (Guid id, Access access, TaskloomDbContext db) =>
        {
            await access.RequireWorkspaceAsync(id, WorkspaceRole.Owner);
            await db.Workspaces.Where(x => x.Id == id).ExecuteDeleteAsync();
            return Results.NoContent();
        });

        // ---- Members (spec 5.5 Members) ----
        api.MapGet("/workspaces/{id:guid}/members", async (Guid id, Access access, TaskloomDbContext db) =>
        {
            await access.RequireWorkspaceAsync(id);
            return await db.WorkspaceMembers.Where(m => m.WorkspaceId == id)
                .OrderBy(m => m.User!.DisplayName.ToLower()).ThenBy(m => m.User!.DisplayName).ThenBy(m => m.UserId)
                .Select(m => new MemberDto(m.UserId, m.User!.Email, m.User.DisplayName, m.Role, !m.User.Identities.Any()))
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
            return Results.Ok(new MemberDto(user.Id, user.Email, user.DisplayName, req.Role, pending));
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

        // Removing someone also unassigns them from the workspace's tasks.
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
            db.WorkspaceMembers.Remove(m);
            await db.SaveChangesAsync();
            await tx.CommitAsync();
            return Results.NoContent();
        });
    }

    private static bool IsAssignable(WorkspaceRole role) => role is WorkspaceRole.Admin or WorkspaceRole.Member or WorkspaceRole.Guest;
}
