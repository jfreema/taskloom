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
        // A private space starts with its creator and memberIds; nobody else in the workspace sees it.
        api.MapPost("/workspaces/{id:guid}/spaces", async (Guid id, CreateSpaceRequest req, Access access, TaskloomDbContext db) =>
        {
            await access.RequireWorkspaceAsync(id, WorkspaceRole.Member);
            var name = SpaceName(req.Name);
            var color = Colors.OrDefault(req.Color, Palette.Teal);
            var isPrivate = req.IsPrivate ?? false;
            var memberIds = PeopleIds(req.MemberIds, required: false);
            if (!isPrivate && memberIds.Count > 0) throw ProblemException.BadRequest(PeopleNeedPrivate);
            if (isPrivate)
            {
                memberIds.Remove(access.UserId);
                memberIds.Insert(0, access.UserId);
                await RequireWorkspaceMembersAsync(db, id, memberIds);
            }
            var last = await db.Spaces.Where(s => s.WorkspaceId == id).MaxAsync(s => (double?)s.Position);
            var s = new Space { Id = req.Id ?? Guid.NewGuid(), WorkspaceId = id, Name = name, Color = color, Position = Positions.Between(last, null), IsPrivate = isPrivate };
            db.Spaces.Add(s);
            db.SpaceMembers.AddRange(NewMembers(s.Id, memberIds, Clock.Now()));
            await db.SaveChangesAsync();
            return Results.Created($"/api/spaces/{s.Id}", new SpaceDto(s.Id, s.Name, s.Color, s.Position, s.IsPrivate, memberIds, []));
        });

        // isPrivate true keeps the space to its people, starting with the caller; false shows it to everyone in the
        // workspace and forgets its people. Assignments are never touched: someone who can't see a task keeps it but
        // doesn't see it until they can again.
        api.MapPatch("/spaces/{id:guid}", async (Guid id, UpdateSpaceRequest req, Access access, TaskloomDbContext db) =>
        {
            var s = await access.RequireSpaceAsync(id, WorkspaceRole.Member);
            if (req.Name is not null) s.Name = SpaceName(req.Name);
            if (req.Color is not null) s.Color = Colors.Require(req.Color);
            if (req.Position is { } position) s.Position = Finite(position);

            await using var tx = await db.Database.BeginTransactionAsync();
            if (req.IsPrivate is { } isPrivate && isPrivate != s.IsPrivate)
            {
                s.IsPrivate = isPrivate;
                if (!isPrivate) await db.SpaceMembers.Where(m => m.SpaceId == id).ExecuteDeleteAsync();
                else if (!await db.SpaceMembers.AnyAsync(m => m.SpaceId == id && m.UserId == access.UserId))
                    db.SpaceMembers.AddRange(NewMembers(id, [access.UserId], Clock.Now()));
            }
            await db.SaveChangesAsync();
            await tx.CommitAsync();
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

        // ---- People of a private space ----
        // Anyone Member+ who can see a private space adds workspace members to it (people already in it are ignored).
        api.MapPost("/spaces/{id:guid}/members", async (Guid id, AddSpaceMembersRequest req, Access access, TaskloomDbContext db) =>
        {
            var s = await access.RequireSpaceAsync(id, WorkspaceRole.Member);
            if (!s.IsPrivate) throw ProblemException.BadRequest(PeopleNeedPrivate);
            var ids = PeopleIds(req.UserIds, required: true);
            await RequireWorkspaceMembersAsync(db, s.WorkspaceId, ids);
            var existing = await db.SpaceMembers.Where(m => m.SpaceId == id).Select(m => m.UserId).ToListAsync();
            db.SpaceMembers.AddRange(NewMembers(id, ids.Where(u => !existing.Contains(u)).ToList(), Clock.Now()));
            await db.SaveChangesAsync();
            return Results.Ok(new SpaceMembersDto(id, true, await MemberIdsAsync(db, id)));
        });

        // Member+ takes anyone out; anyone may take themselves out. Someone has to stay.
        api.MapDelete("/spaces/{id:guid}/members/{userId:guid}", async (Guid id, Guid userId, Access access, TaskloomDbContext db) =>
        {
            await access.RequireSpaceAsync(id, userId == access.UserId ? WorkspaceRole.Guest : WorkspaceRole.Member);
            var row = await db.SpaceMembers.FirstOrDefaultAsync(m => m.SpaceId == id && m.UserId == userId) ?? throw new NotFoundException();
            if (await db.SpaceMembers.CountAsync(m => m.SpaceId == id) == 1) throw ProblemException.BadRequest(LastPerson);
            db.SpaceMembers.Remove(row);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        // Owner and Admins see every private space's name and size, so one that has nobody left can be recovered.
        api.MapGet("/workspaces/{id:guid}/private-spaces", async (Guid id, Access access, TaskloomDbContext db) =>
        {
            await access.RequireWorkspaceAsync(id, WorkspaceRole.Admin);
            var uid = access.UserId;
            return await db.Spaces.Where(s => s.WorkspaceId == id && s.IsPrivate)
                .OrderBy(s => s.Position).ThenBy(s => s.Id)
                .Select(s => new PrivateSpaceDto(s.Id, s.Name, s.Color, s.Members.Count, s.Members.Any(m => m.UserId == uid)))
                .ToListAsync();
        });

        // Owner and Admins may add themselves to any private space. They then show among its people, like anyone else.
        api.MapPost("/spaces/{id:guid}/join", async (Guid id, Access access, TaskloomDbContext db) =>
        {
            var uid = access.UserId;
            var row = await db.Spaces.Where(s => s.Id == id)
                .Select(s => new
                {
                    s.IsPrivate,
                    Role = db.WorkspaceMembers.Where(m => m.WorkspaceId == s.WorkspaceId && m.UserId == uid).Select(m => (WorkspaceRole?)m.Role).FirstOrDefault(),
                    InSpace = s.Members.Any(m => m.UserId == uid),
                })
                .FirstOrDefaultAsync();
            // Below Admin, a private space you aren't in doesn't exist.
            if (row?.Role is not { } role || (role > WorkspaceRole.Admin && row.IsPrivate && !row.InSpace)) throw new NotFoundException();
            if (role > WorkspaceRole.Admin) throw new ForbiddenException();
            if (!row.IsPrivate) throw ProblemException.BadRequest("Everyone in the workspace can already see this space.");
            if (!row.InSpace)
            {
                db.SpaceMembers.AddRange(NewMembers(id, [uid], Clock.Now()));
                await db.SaveChangesAsync();
            }
            return Results.NoContent();
        });

        // ---- Lists ----
        // Statuses come from statusesFromListId (copied) or statusPreset (default Review).
        api.MapPost("/spaces/{id:guid}/lists", async (Guid id, CreateListRequest req, Access access, TaskloomDbContext db) =>
        {
            var space = await access.RequireSpaceAsync(id, WorkspaceRole.Member);
            var name = ListName(req.Name);
            var list = new TaskList { Id = req.Id ?? Guid.NewGuid(), SpaceId = id, Name = name, Color = ListColor(req.Color), Icon = ListIcon(req.Icon) };
            List<ListStatus> statuses;
            if (req.StatusesFromListId is { } fromId)
            {
                if (!await access.VisibleListIds(space.WorkspaceId).AnyAsync(l => l == fromId))
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
            return Results.Created($"/api/lists/{list.Id}", new ListSummaryDto(list.Id, list.SpaceId, list.Name, list.Position, list.Color, list.Icon,
                statuses.OrderBy(s => s.Position).ThenBy(s => s.Id).Select(ToStatusDto).ToList()));
        });

        api.MapGet("/lists/{id:guid}", async (Guid id, Access access, TaskloomDbContext db) =>
        {
            var (list, workspaceId) = await access.RequireListAsync(id);
            var statuses = await db.Statuses.Where(s => s.ListId == id).OrderBy(s => s.Position).ThenBy(s => s.Id)
                .Select(s => new StatusDto(s.Id, s.ListId, s.Name, s.Color, s.Category, s.Position)).ToListAsync();
            // The people who can see the list.
            var spaceId = list.SpaceId;
            var members = await db.WorkspaceMembers
                .Where(m => m.WorkspaceId == workspaceId &&
                            db.Spaces.Any(s => s.Id == spaceId && (!s.IsPrivate || s.Members.Any(x => x.UserId == m.UserId))))
                .OrderBy(m => m.User!.DisplayName.ToLower()).ThenBy(m => m.User!.DisplayName).ThenBy(m => m.UserId)
                .Select(m => new UserDto(m.UserId, m.User!.Email, m.User.DisplayName, UserPhotos.UrlFor(m.UserId, m.User.PhotoVersion))).ToListAsync();
            return new ListDetailDto(list.Id, list.SpaceId, workspaceId, list.Name, statuses, members);
        });

        // spaceId moves the list to another space of the workspace that the caller can see, last unless position is also
        // sent. Into or out of a private space, the list's tasks are then seen by that space's people only.
        api.MapPatch("/lists/{id:guid}", async (Guid id, UpdateListRequest req, Access access, TaskloomDbContext db) =>
        {
            var (list, workspaceId) = await access.RequireListAsync(id, WorkspaceRole.Member);
            if (req.Name is not null) list.Name = ListName(req.Name);
            if (req.Color is not null) list.Color = ListColor(req.Color);
            if (req.Icon is not null) list.Icon = ListIcon(req.Icon);
            if (req.SpaceId is { } spaceId && spaceId != list.SpaceId)
            {
                if (!await db.Spaces.Where(s => s.WorkspaceId == workspaceId).VisibleTo(db, access.UserId).AnyAsync(s => s.Id == spaceId))
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

    public const string PeopleNeedPrivate = "Only a private space has its own people: everyone in the workspace can see this one.";
    public const string PeopleMustBeMembers = "Everyone added must be a member of this workspace.";
    public const string LastPerson = "A private space needs at least one person. Make it visible to everyone, or delete it, instead.";

    /// <summary>Distinct ids, in the order sent; 1 to 500 when <paramref name="required"/>.</summary>
    private static List<Guid> PeopleIds(List<Guid>? ids, bool required)
    {
        var list = (ids ?? []).Distinct().ToList();
        if (required && list.Count == 0) throw ProblemException.BadRequest("Choose who to add.");
        if (list.Count > Limits.BulkItems) throw ProblemException.BadRequest("Add up to 500 people at a time.");
        return list;
    }

    private static async Task RequireWorkspaceMembersAsync(TaskloomDbContext db, Guid workspaceId, List<Guid> userIds)
    {
        if (!await TaskEndpoints.AreMembersAsync(db, workspaceId, userIds)) throw ProblemException.BadRequest(PeopleMustBeMembers);
    }

    /// <summary>A microsecond apart, so the people list keeps the order they were sent in.</summary>
    private static IEnumerable<SpaceMember> NewMembers(Guid spaceId, List<Guid> userIds, DateTimeOffset now) =>
        userIds.Select((u, i) => new SpaceMember { SpaceId = spaceId, UserId = u, AddedAt = now.AddTicks(10 * i) });

    private static Task<List<Guid>> MemberIdsAsync(TaskloomDbContext db, Guid spaceId) =>
        db.SpaceMembers.Where(m => m.SpaceId == spaceId).OrderBy(m => m.AddedAt).ThenBy(m => m.UserId).Select(m => m.UserId).ToListAsync();

    public static StatusDto ToStatusDto(ListStatus s) => new(s.Id, s.ListId, s.Name, s.Color, s.Category, s.Position);

    private static string SpaceName(string? name) => Text.Require(name, Limits.Name, "Enter a space name.", "Space names can be up to 200 characters.");
    private static string ListName(string? name) => Text.Require(name, Limits.Name, "Enter a list name.", "List names can be up to 200 characters.");
    public const string ListIconMessage = "Choose one of the list icons.";

    /// <summary>A list's colour as sent: "" (or null) for none, else "#RRGGBB".</summary>
    private static string? ListColor(string? color) => string.IsNullOrEmpty(color) ? null : Colors.Require(color);

    /// <summary>A list's icon as sent: "" (or null, or the plain list icon) for none, else one of <see cref="ListIcons.All"/>.</summary>
    private static string? ListIcon(string? icon) =>
        string.IsNullOrEmpty(icon) || icon == ListIcons.Default ? null
        : ListIcons.IsKnown(icon) ? icon : throw ProblemException.BadRequest(ListIconMessage);

    private static string StatusName(string? name) => Text.Require(name, Limits.StatusName, "Enter a status name.", "Status names can be up to 100 characters.");

    private static double Finite(double position) => double.IsFinite(position) ? position : throw ProblemException.BadRequest("Positions must be numbers.");
}
