using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Taskloom.Domain;
using Taskloom.Infrastructure;

namespace Taskloom.Api.Endpoints;

/// <summary>Workspace tags (spec 5.4 #15–#18). Names are unique per workspace ignoring case.</summary>
public static class TagEndpoints
{
    /// <summary>The unique index on (WorkspaceId, NormalizedName).</summary>
    public const string UniqueNameIndex = "IX_tags_WorkspaceId_NormalizedName";

    public static readonly Expression<Func<Tag, TagDto>> ToDto = t => new TagDto(t.Id, t.WorkspaceId, t.Name, t.Color, t.CreatedAt);
    private static readonly Func<Tag, TagDto> ToDtoCompiled = ToDto.Compile();

    public static void MapTagEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/workspaces/{id:guid}/tags", async (Guid id, Access access, TaskloomDbContext db, CancellationToken ct) =>
        {
            await access.RequireWorkspaceAsync(id);
            return await db.Tags.Where(t => t.WorkspaceId == id)
                .OrderBy(t => t.NormalizedName).ThenBy(t => t.Name).ThenBy(t => t.Id)
                .Select(ToDto).ToListAsync(ct);
        });

        api.MapPost("/workspaces/{id:guid}/tags", async (Guid id, CreateTagRequest req, Access access, TaskloomDbContext db) =>
        {
            await access.RequireWorkspaceAsync(id, WorkspaceRole.Member);
            var name = RequireName(req.Name);
            var color = Colors.OrDefault(req.Color, Palette.Slate);
            var tag = new Tag
            {
                Id = req.Id ?? Guid.NewGuid(), WorkspaceId = id, Name = name, NormalizedName = Tag.Normalize(name), Color = color,
                CreatedAt = Clock.Now(),
            };
            await EnsureNameIsFreeAsync(db, tag);
            db.Tags.Add(tag);
            await SaveAsync(db, tag);
            return Results.Created($"/api/tags/{tag.Id}", ToDtoCompiled(tag));
        });

        api.MapPatch("/tags/{id:guid}", async (Guid id, UpdateTagRequest req, Access access, TaskloomDbContext db) =>
        {
            var tag = await db.Tags.FirstOrDefaultAsync(t => t.Id == id) ?? throw new NotFoundException();
            await access.RequireWorkspaceAsync(tag.WorkspaceId, WorkspaceRole.Member);
            if (req.Name is not null)
            {
                tag.Name = RequireName(req.Name);
                tag.NormalizedName = Tag.Normalize(tag.Name);
                await EnsureNameIsFreeAsync(db, tag);
            }
            if (req.Color is not null) tag.Color = Colors.Require(req.Color);
            await SaveAsync(db, tag);
            return Results.Ok(ToDtoCompiled(tag));
        });

        // Removing a tag removes it from every task (task_tags CASCADE).
        api.MapDelete("/tags/{id:guid}", async (Guid id, Access access, TaskloomDbContext db) =>
        {
            var tag = await db.Tags.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id) ?? throw new NotFoundException();
            await access.RequireWorkspaceAsync(tag.WorkspaceId, WorkspaceRole.Admin);
            await db.Tags.Where(t => t.Id == id).ExecuteDeleteAsync();
            return Results.NoContent();
        });
    }

    private static string RequireName(string? name) =>
        Text.Require(name, Limits.TagName, "Enter a tag name.", "Tag names can be up to 50 characters.");

    private static ProblemException Duplicate(string existingName) => ProblemException.Conflict($"A tag called “{existingName}” already exists.");

    /// <summary>Another tag of the workspace with the same name ignoring case is a 409.</summary>
    private static async Task EnsureNameIsFreeAsync(TaskloomDbContext db, Tag tag)
    {
        var existing = await db.Tags.AsNoTracking()
            .Where(t => t.WorkspaceId == tag.WorkspaceId && t.NormalizedName == tag.NormalizedName && t.Id != tag.Id)
            .Select(t => t.Name).FirstOrDefaultAsync();
        if (existing is not null) throw Duplicate(existing);
    }

    /// <summary>Saves, turning a name collision with a concurrent request into the same 409.</summary>
    private static async Task SaveAsync(TaskloomDbContext db, Tag tag)
    {
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: UniqueNameIndex })
        {
            throw Duplicate(tag.Name);
        }
    }
}
