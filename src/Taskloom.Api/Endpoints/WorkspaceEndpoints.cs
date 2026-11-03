using Microsoft.EntityFrameworkCore;
using Taskloom.Application;
using Taskloom.Domain;
using Taskloom.Infrastructure;

namespace Taskloom.Api.Endpoints;

public static class WorkspaceEndpoints
{
    public static void MapWorkspaceEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/me", async (Access access, TaskloomDbContext db) =>
        {
            var u = await db.Users.FirstOrDefaultAsync(x => x.Id == access.UserId);
            return u is null ? Results.Unauthorized() : Results.Ok(new UserDto(u.Id, u.Email, u.DisplayName));
        });

        // ---- Workspaces ----
        api.MapGet("/workspaces", async (Access access, TaskloomDbContext db) =>
        {
            var uid = access.UserId;
            var memberships = await db.WorkspaceMembers.Where(m => m.UserId == uid).ToListAsync();
            var ids = memberships.Select(m => m.WorkspaceId).ToList();
            var workspaces = await db.Workspaces.Where(w => ids.Contains(w.Id))
                .Include(w => w.Spaces).ThenInclude(s => s.Lists)
                .AsSplitQuery().OrderBy(w => w.CreatedAt).ToListAsync();
            return workspaces.Select(w => new WorkspaceTreeDto(w.Id, w.Name,
                memberships.First(m => m.WorkspaceId == w.Id).Role,
                w.Spaces.OrderBy(s => s.Position).Select(s => new SpaceDto(s.Id, s.Name, s.Color, s.Position,
                    s.Lists.OrderBy(l => l.Position).Select(l => new ListSummaryDto(l.Id, l.Name, l.Position)).ToList())).ToList()));
        });

        api.MapPost("/workspaces", async (CreateWorkspaceRequest req, Access access, TaskloomDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(req.Name)) return Results.BadRequest();
            var w = new Workspace { Id = req.Id ?? Guid.NewGuid(), Name = req.Name.Trim() };
            db.Workspaces.Add(w);
            db.WorkspaceMembers.Add(new WorkspaceMember { WorkspaceId = w.Id, UserId = access.UserId, Role = WorkspaceRole.Owner });
            await db.SaveChangesAsync();
            return Results.Created($"/api/workspaces/{w.Id}", new WorkspaceTreeDto(w.Id, w.Name, WorkspaceRole.Owner, []));
        });

        api.MapPatch("/workspaces/{id:guid}", async (Guid id, RenameRequest req, Access access, TaskloomDbContext db) =>
        {
            await access.RequireWorkspaceAsync(id, WorkspaceRole.Admin);
            var w = await db.Workspaces.FirstAsync(x => x.Id == id);
            w.Name = req.Name.Trim();
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        api.MapDelete("/workspaces/{id:guid}", async (Guid id, Access access, TaskloomDbContext db) =>
        {
            await access.RequireWorkspaceAsync(id, WorkspaceRole.Owner);
            await db.Workspaces.Where(x => x.Id == id).ExecuteDeleteAsync();
            return Results.NoContent();
        });

        // ---- Members ----
        api.MapGet("/workspaces/{id:guid}/members", async (Guid id, Access access, TaskloomDbContext db) =>
        {
            await access.RequireWorkspaceAsync(id);
            return await db.WorkspaceMembers.Where(m => m.WorkspaceId == id)
                .Select(m => new MemberDto(m.UserId, m.User!.Email, m.User.DisplayName, m.Role)).ToListAsync();
        });

        api.MapPost("/workspaces/{id:guid}/members", async (Guid id, AddMemberRequest req, Access access, TaskloomDbContext db) =>
        {
            await access.RequireWorkspaceAsync(id, WorkspaceRole.Admin);
            if (req.Role == WorkspaceRole.Owner) return Results.BadRequest();
            var email = req.Email.Trim().ToLowerInvariant();
            if (!email.Contains('@')) return Results.BadRequest();
            // An invite creates a placeholder user; their first sign-in with this email links to it.
            var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email);
            if (user is null)
            {
                user = new User { Email = email, DisplayName = email.Split('@')[0] };
                db.Users.Add(user);
            }
            if (await db.WorkspaceMembers.AnyAsync(m => m.WorkspaceId == id && m.UserId == user.Id)) return Results.Conflict();
            db.WorkspaceMembers.Add(new WorkspaceMember { WorkspaceId = id, UserId = user.Id, Role = req.Role });
            await db.SaveChangesAsync();
            return Results.Ok(new MemberDto(user.Id, user.Email, user.DisplayName, req.Role));
        });

        api.MapPatch("/workspaces/{id:guid}/members/{userId:guid}", async (Guid id, Guid userId, UpdateMemberRequest req, Access access, TaskloomDbContext db) =>
        {
            await access.RequireWorkspaceAsync(id, WorkspaceRole.Admin);
            var m = await db.WorkspaceMembers.FirstOrDefaultAsync(x => x.WorkspaceId == id && x.UserId == userId);
            if (m is null) return Results.NotFound();
            if (m.Role == WorkspaceRole.Owner || req.Role == WorkspaceRole.Owner) return Results.BadRequest();
            m.Role = req.Role;
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        api.MapDelete("/workspaces/{id:guid}/members/{userId:guid}", async (Guid id, Guid userId, Access access, TaskloomDbContext db) =>
        {
            await access.RequireWorkspaceAsync(id, WorkspaceRole.Admin);
            var m = await db.WorkspaceMembers.FirstOrDefaultAsync(x => x.WorkspaceId == id && x.UserId == userId);
            if (m is null) return Results.NotFound();
            if (m.Role == WorkspaceRole.Owner) return Results.BadRequest();
            db.WorkspaceMembers.Remove(m);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        // ---- Spaces ----
        api.MapPost("/workspaces/{id:guid}/spaces", async (Guid id, CreateSpaceRequest req, Access access, TaskloomDbContext db) =>
        {
            await access.RequireWorkspaceAsync(id, WorkspaceRole.Member);
            if (string.IsNullOrWhiteSpace(req.Name)) return Results.BadRequest();
            var last = await db.Spaces.Where(s => s.WorkspaceId == id).MaxAsync(s => (double?)s.Position);
            var s = new Space { Id = req.Id ?? Guid.NewGuid(), WorkspaceId = id, Name = req.Name.Trim(), Color = req.Color ?? "#7b68ee", Position = Positions.Between(last, null) };
            db.Spaces.Add(s);
            await db.SaveChangesAsync();
            return Results.Created($"/api/spaces/{s.Id}", new SpaceDto(s.Id, s.Name, s.Color, s.Position, []));
        });

        api.MapPatch("/spaces/{id:guid}", async (Guid id, UpdateSpaceRequest req, Access access, TaskloomDbContext db) =>
        {
            var s = await access.RequireSpaceAsync(id, WorkspaceRole.Member);
            if (!string.IsNullOrWhiteSpace(req.Name)) s.Name = req.Name.Trim();
            if (req.Color is not null) s.Color = req.Color;
            if (req.Position is not null) s.Position = req.Position.Value;
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        api.MapDelete("/spaces/{id:guid}", async (Guid id, Access access, TaskloomDbContext db) =>
        {
            await access.RequireSpaceAsync(id, WorkspaceRole.Admin);
            await db.Spaces.Where(s => s.Id == id).ExecuteDeleteAsync();
            return Results.NoContent();
        });

        // ---- Lists ----
        api.MapPost("/spaces/{id:guid}/lists", async (Guid id, CreateListRequest req, Access access, TaskloomDbContext db) =>
        {
            await access.RequireSpaceAsync(id, WorkspaceRole.Member);
            if (string.IsNullOrWhiteSpace(req.Name)) return Results.BadRequest();
            var last = await db.Lists.Where(l => l.SpaceId == id).MaxAsync(l => (double?)l.Position);
            var list = new TaskList { Id = req.Id ?? Guid.NewGuid(), SpaceId = id, Name = req.Name.Trim(), Position = Positions.Between(last, null) };
            db.Lists.Add(list);
            db.Statuses.AddRange(ListStatus.Defaults(list.Id));
            await db.SaveChangesAsync();
            return Results.Created($"/api/lists/{list.Id}", new ListSummaryDto(list.Id, list.Name, list.Position));
        });

        api.MapGet("/lists/{id:guid}", async (Guid id, Access access, TaskloomDbContext db) =>
        {
            var (list, workspaceId) = await access.RequireListAsync(id);
            var statuses = await db.Statuses.Where(s => s.ListId == id).OrderBy(s => s.Position)
                .Select(s => new StatusDto(s.Id, s.Name, s.Color, s.Category, s.Position)).ToListAsync();
            var members = await db.WorkspaceMembers.Where(m => m.WorkspaceId == workspaceId)
                .Select(m => new UserDto(m.UserId, m.User!.Email, m.User.DisplayName)).ToListAsync();
            return new ListDetailDto(list.Id, list.SpaceId, workspaceId, list.Name, statuses, members);
        });

        api.MapPatch("/lists/{id:guid}", async (Guid id, UpdateListRequest req, Access access, TaskloomDbContext db) =>
        {
            var (list, _) = await access.RequireListAsync(id, WorkspaceRole.Member);
            if (!string.IsNullOrWhiteSpace(req.Name)) list.Name = req.Name.Trim();
            if (req.Position is not null) list.Position = req.Position.Value;
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        api.MapDelete("/lists/{id:guid}", async (Guid id, Access access, TaskloomDbContext db) =>
        {
            await access.RequireListAsync(id, WorkspaceRole.Member);
            await db.Lists.Where(l => l.Id == id).ExecuteDeleteAsync();
            return Results.NoContent();
        });

        // ---- Statuses ----
        api.MapPost("/lists/{id:guid}/statuses", async (Guid id, CreateStatusRequest req, Access access, TaskloomDbContext db) =>
        {
            await access.RequireListAsync(id, WorkspaceRole.Member);
            if (string.IsNullOrWhiteSpace(req.Name)) return Results.BadRequest();
            var last = await db.Statuses.Where(s => s.ListId == id).MaxAsync(s => (double?)s.Position);
            var s = new ListStatus { Id = req.Id ?? Guid.NewGuid(), ListId = id, Name = req.Name.Trim(), Color = req.Color ?? "#87909e", Category = req.Category, Position = Positions.Between(last, null) };
            db.Statuses.Add(s);
            await db.SaveChangesAsync();
            return Results.Ok(new StatusDto(s.Id, s.Name, s.Color, s.Category, s.Position));
        });

        api.MapPatch("/statuses/{id:guid}", async (Guid id, UpdateStatusRequest req, Access access, TaskloomDbContext db) =>
        {
            var s = await db.Statuses.FirstOrDefaultAsync(x => x.Id == id);
            if (s is null) return Results.NotFound();
            await access.RequireListAsync(s.ListId, WorkspaceRole.Member);
            if (!string.IsNullOrWhiteSpace(req.Name)) s.Name = req.Name.Trim();
            if (req.Color is not null) s.Color = req.Color;
            if (req.Category is not null) s.Category = req.Category.Value;
            if (req.Position is not null) s.Position = req.Position.Value;
            await db.SaveChangesAsync();
            return Results.Ok(new StatusDto(s.Id, s.Name, s.Color, s.Category, s.Position));
        });

        api.MapDelete("/statuses/{id:guid}", async (Guid id, Access access, TaskloomDbContext db) =>
        {
            var s = await db.Statuses.FirstOrDefaultAsync(x => x.Id == id);
            if (s is null) return Results.NotFound();
            await access.RequireListAsync(s.ListId, WorkspaceRole.Member);
            var fallback = await db.Statuses.Where(x => x.ListId == s.ListId && x.Id != id).OrderBy(x => x.Position).FirstOrDefaultAsync();
            if (fallback is null) return Results.Problem("A list needs at least one status.", statusCode: 400);
            // Tasks in the deleted status move to the first remaining one.
            await db.Tasks.Where(t => t.StatusId == id).ExecuteUpdateAsync(u => u.SetProperty(t => t.StatusId, fallback.Id));
            db.Statuses.Remove(s);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });
    }
}
