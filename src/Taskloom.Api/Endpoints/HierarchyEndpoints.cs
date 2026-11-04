using Microsoft.EntityFrameworkCore;
using Taskloom.Application;
using Taskloom.Domain;
using Taskloom.Infrastructure;

namespace Taskloom.Api.Endpoints;

/// <summary>Spaces, lists and statuses (spec 5.4 #19–#27 and the list and status rules of 5.5).</summary>
public static class HierarchyEndpoints
{
    public const string NeedsDone = "A list needs at least one Done status.";
    public const string NeedsNotDone = "A list needs at least one status that isn't Done.";

    public static void MapHierarchyEndpoints(this RouteGroupBuilder api)
    {
        // ---- Spaces ----
        api.MapPost("/workspaces/{id:guid}/spaces", async (Guid id, CreateSpaceRequest req, Access access, TaskloomDbContext db) =>
        {
            await access.RequireWorkspaceAsync(id, WorkspaceRole.Member);
            var name = SpaceName(req.Name);
            var color = Colors.OrDefault(req.Color, Palette.Teal);
            var last = await db.Spaces.Where(s => s.WorkspaceId == id).MaxAsync(s => (double?)s.Position);
            var s = new Space { Id = req.Id ?? Guid.NewGuid(), WorkspaceId = id, Name = name, Color = color, Position = Positions.Between(last, null) };
            db.Spaces.Add(s);
            await db.SaveChangesAsync();
            return Results.Created($"/api/spaces/{s.Id}", new SpaceDto(s.Id, s.Name, s.Color, s.Position, []));
        });

        api.MapPatch("/spaces/{id:guid}", async (Guid id, UpdateSpaceRequest req, Access access, TaskloomDbContext db) =>
        {
            var s = await access.RequireSpaceAsync(id, WorkspaceRole.Member);
            if (req.Name is not null) s.Name = SpaceName(req.Name);
            if (req.Color is not null) s.Color = Colors.Require(req.Color);
            if (req.Position is { } position) s.Position = Finite(position);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        // Its lists and their tasks go with it (CASCADE), and so do the tasks' stored files.
        api.MapDelete("/spaces/{id:guid}", async (Guid id, Access access, TaskloomDbContext db, IFileStore files) =>
        {
            await access.RequireSpaceAsync(id, WorkspaceRole.Admin);
            var keys = await db.Attachments
                .Where(a => db.Tasks.Any(t => t.Id == a.TaskId && db.Lists.Any(l => l.Id == t.ListId && l.SpaceId == id)))
                .Select(a => a.StorageKey).ToListAsync();
            await db.Spaces.Where(s => s.Id == id).ExecuteDeleteAsync();
            foreach (var key in keys) await files.DeleteAsync(key);
            return Results.NoContent();
        });

        // ---- Lists ----
        // Statuses come from statusesFromListId (copied) or statusPreset (default Review).
        api.MapPost("/spaces/{id:guid}/lists", async (Guid id, CreateListRequest req, Access access, TaskloomDbContext db) =>
        {
            var space = await access.RequireSpaceAsync(id, WorkspaceRole.Member);
            var name = ListName(req.Name);
            var list = new TaskList { Id = req.Id ?? Guid.NewGuid(), SpaceId = id, Name = name };
            List<ListStatus> statuses;
            if (req.StatusesFromListId is { } fromId)
            {
                if (!await db.Lists.AnyAsync(l => l.Id == fromId && l.Space!.WorkspaceId == space.WorkspaceId))
                    throw ProblemException.BadRequest("Choose a list in this workspace to copy statuses from.");
                var source = await db.Statuses.AsNoTracking().Where(s => s.ListId == fromId).OrderBy(s => s.Position).ThenBy(s => s.Id).ToListAsync();
                statuses = source.Select(s => new ListStatus { ListId = list.Id, Name = s.Name, Color = s.Color, Category = s.Category, Position = s.Position }).ToList();
            }
            else statuses = ListStatus.ForPreset(list.Id, req.StatusPreset ?? StatusPreset.Review);

            var last = await db.Lists.Where(l => l.SpaceId == id).MaxAsync(l => (double?)l.Position);
            list.Position = Positions.Between(last, null);
            db.Lists.Add(list);
            db.Statuses.AddRange(statuses);
            await db.SaveChangesAsync();
            return Results.Created($"/api/lists/{list.Id}", new ListSummaryDto(list.Id, list.SpaceId, list.Name, list.Position,
                statuses.OrderBy(s => s.Position).ThenBy(s => s.Id).Select(ToStatusDto).ToList()));
        });

        api.MapGet("/lists/{id:guid}", async (Guid id, Access access, TaskloomDbContext db) =>
        {
            var (list, workspaceId) = await access.RequireListAsync(id);
            var statuses = await db.Statuses.Where(s => s.ListId == id).OrderBy(s => s.Position).ThenBy(s => s.Id)
                .Select(s => new StatusDto(s.Id, s.ListId, s.Name, s.Color, s.Category, s.Position)).ToListAsync();
            var members = await db.WorkspaceMembers.Where(m => m.WorkspaceId == workspaceId)
                .OrderBy(m => m.User!.DisplayName.ToLower()).ThenBy(m => m.User!.DisplayName).ThenBy(m => m.UserId)
                .Select(m => new UserDto(m.UserId, m.User!.Email, m.User.DisplayName)).ToListAsync();
            return new ListDetailDto(list.Id, list.SpaceId, workspaceId, list.Name, statuses, members);
        });

        // spaceId moves the list to another space of the workspace, last unless position is also sent.
        api.MapPatch("/lists/{id:guid}", async (Guid id, UpdateListRequest req, Access access, TaskloomDbContext db) =>
        {
            var (list, workspaceId) = await access.RequireListAsync(id, WorkspaceRole.Member);
            if (req.Name is not null) list.Name = ListName(req.Name);
            if (req.SpaceId is { } spaceId && spaceId != list.SpaceId)
            {
                if (!await db.Spaces.AnyAsync(s => s.Id == spaceId && s.WorkspaceId == workspaceId))
                    throw ProblemException.BadRequest("Choose a space in this workspace.");
                list.SpaceId = spaceId;
                if (req.Position is null)
                    list.Position = Positions.Between(await db.Lists.Where(l => l.SpaceId == spaceId).MaxAsync(l => (double?)l.Position), null);
            }
            if (req.Position is { } position) list.Position = Finite(position);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        // Its tasks go with it (CASCADE), and so do their stored files.
        api.MapDelete("/lists/{id:guid}", async (Guid id, Access access, TaskloomDbContext db, IFileStore files) =>
        {
            await access.RequireListAsync(id, WorkspaceRole.Member);
            var keys = await db.Attachments.Where(a => db.Tasks.Any(t => t.Id == a.TaskId && t.ListId == id)).Select(a => a.StorageKey).ToListAsync();
            await db.Lists.Where(l => l.Id == id).ExecuteDeleteAsync();
            foreach (var key in keys) await files.DeleteAsync(key);
            return Results.NoContent();
        });

        // ---- Statuses ----
        api.MapPost("/lists/{id:guid}/statuses", async (Guid id, CreateStatusRequest req, Access access, TaskloomDbContext db) =>
        {
            await access.RequireListAsync(id, WorkspaceRole.Member);
            var name = StatusName(req.Name);
            var color = Colors.OrDefault(req.Color, Palette.Slate);
            var category = req.Category ?? throw ProblemException.BadRequest("Choose a status type: NotStarted, Active or Done.");
            var last = await db.Statuses.Where(s => s.ListId == id).MaxAsync(s => (double?)s.Position);
            var s = new ListStatus { Id = req.Id ?? Guid.NewGuid(), ListId = id, Name = name, Color = color, Category = category, Position = Positions.Between(last, null) };
            db.Statuses.Add(s);
            await db.SaveChangesAsync();
            return Results.Ok(ToStatusDto(s));
        });

        // A category change keeps one Done and one non-Done status in the list and completes or reopens the status's tasks.
        api.MapPatch("/statuses/{id:guid}", async (Guid id, UpdateStatusRequest req, Access access, TaskloomDbContext db) =>
        {
            var s = await db.Statuses.FirstOrDefaultAsync(x => x.Id == id) ?? throw new NotFoundException();
            await access.RequireListAsync(s.ListId, WorkspaceRole.Member);
            if (req.Name is not null) s.Name = StatusName(req.Name);
            if (req.Color is not null) s.Color = Colors.Require(req.Color);
            if (req.Position is { } position) s.Position = Finite(position);

            await using var tx = await db.Database.BeginTransactionAsync();
            if (req.Category is { } category && category != s.Category)
            {
                var others = await db.Statuses.Where(x => x.ListId == s.ListId && x.Id != id).Select(x => x.Category).ToListAsync();
                var wasDone = s.Category == StatusCategory.Done;
                var isDone = category == StatusCategory.Done;
                if (wasDone && !isDone && !others.Contains(StatusCategory.Done)) throw ProblemException.BadRequest(NeedsDone);
                if (!wasDone && isDone && others.All(c => c == StatusCategory.Done)) throw ProblemException.BadRequest(NeedsNotDone);
                s.Category = category;
                var now = Clock.Now();
                if (isDone && !wasDone)
                    await db.Tasks.Where(t => t.StatusId == id).ExecuteUpdateAsync(u => u.SetProperty(t => t.CompletedAt, now));
                else if (wasDone && !isDone)
                    await db.Tasks.Where(t => t.StatusId == id).ExecuteUpdateAsync(u => u.SetProperty(t => t.CompletedAt, (DateTimeOffset?)null));
            }
            await db.SaveChangesAsync();
            await tx.CommitAsync();
            return Results.Ok(ToStatusDto(s));
        });

        // Tasks move to moveTo (another status of the same list), else to the first remaining status by position.
        api.MapDelete("/statuses/{id:guid}", async (Guid id, string? moveTo, Access access, TaskloomDbContext db) =>
        {
            var s = await db.Statuses.FirstOrDefaultAsync(x => x.Id == id) ?? throw new NotFoundException();
            await access.RequireListAsync(s.ListId, WorkspaceRole.Member);
            await using var tx = await db.Database.BeginTransactionAsync();
            var others = await db.Statuses.AsNoTracking().Where(x => x.ListId == s.ListId && x.Id != id)
                .OrderBy(x => x.Position).ThenBy(x => x.Id).ToListAsync();
            if (others.Count == 0) throw ProblemException.BadRequest("A list needs at least one status.");
            if (s.Category == StatusCategory.Done && others.All(x => x.Category != StatusCategory.Done)) throw ProblemException.BadRequest(NeedsDone);
            if (s.Category != StatusCategory.Done && others.All(x => x.Category == StatusCategory.Done)) throw ProblemException.BadRequest(NeedsNotDone);
            var target = others[0];
            if (!string.IsNullOrEmpty(moveTo))
                target = Guid.TryParse(moveTo, out var moveToId) && others.FirstOrDefault(x => x.Id == moveToId) is { } chosen
                    ? chosen
                    : throw ProblemException.BadRequest("Choose another status of this list to move its tasks to.");

            // Completion follows the target: set when the tasks enter Done, kept when both are Done, cleared otherwise.
            var tasks = db.Tasks.Where(t => t.StatusId == id);
            var now = Clock.Now();
            if (target.Category != StatusCategory.Done)
                await tasks.ExecuteUpdateAsync(u => u.SetProperty(t => t.StatusId, target.Id).SetProperty(t => t.CompletedAt, (DateTimeOffset?)null));
            else if (s.Category == StatusCategory.Done)
                await tasks.ExecuteUpdateAsync(u => u.SetProperty(t => t.StatusId, target.Id).SetProperty(t => t.CompletedAt, t => t.CompletedAt ?? now));
            else
                await tasks.ExecuteUpdateAsync(u => u.SetProperty(t => t.StatusId, target.Id).SetProperty(t => t.CompletedAt, now));
            db.Statuses.Remove(s);
            await db.SaveChangesAsync();
            await tx.CommitAsync();
            return Results.NoContent();
        });
    }

    public static StatusDto ToStatusDto(ListStatus s) => new(s.Id, s.ListId, s.Name, s.Color, s.Category, s.Position);

    private static string SpaceName(string? name) => Text.Require(name, Limits.Name, "Enter a space name.", "Space names can be up to 200 characters.");
    private static string ListName(string? name) => Text.Require(name, Limits.Name, "Enter a list name.", "List names can be up to 200 characters.");
    private static string StatusName(string? name) => Text.Require(name, Limits.StatusName, "Enter a status name.", "Status names can be up to 100 characters.");

    private static double Finite(double position) => double.IsFinite(position) ? position : throw ProblemException.BadRequest("Positions must be numbers.");
}
