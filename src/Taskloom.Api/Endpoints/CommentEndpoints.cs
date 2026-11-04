using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;
using Taskloom.Application;
using Taskloom.Domain;
using Taskloom.Infrastructure;

namespace Taskloom.Api.Endpoints;

/// <summary>Comments (spec 5.4 #37–#39). Guests can comment; only the author edits or deletes, whatever their role.</summary>
public static class CommentEndpoints
{
    public static void MapCommentEndpoints(this RouteGroupBuilder api)
    {
        api.MapPost("/tasks/{id:guid}/comments", async (Guid id, CreateCommentRequest req, Access access, TaskloomDbContext db) =>
        {
            await access.RequireTaskAsync(id, WorkspaceRole.Guest);
            var body = RequireBody(req.Body);
            var c = new TaskComment { Id = req.Id ?? Guid.NewGuid(), TaskId = id, AuthorId = access.UserId, Body = body, CreatedAt = Clock.Now() };
            db.Comments.Add(c);
            await db.SaveChangesAsync();
            var author = await db.Users.FirstAsync(u => u.Id == access.UserId);
            return Results.Ok(ToDto(c, author));
        });

        api.MapPatch("/comments/{id:guid}", async (Guid id, UpdateCommentRequest req, Access access, TaskloomDbContext db) =>
        {
            var c = await RequireAuthorAsync(id, access, db);
            var body = RequireBody(req.Body);
            if (body != c.Body)
            {
                c.Body = body;
                c.EditedAt = Clock.Now();
                await db.SaveChangesAsync();
            }
            var author = await db.Users.FirstAsync(u => u.Id == c.AuthorId);
            return Results.Ok(ToDto(c, author));
        });

        api.MapDelete("/comments/{id:guid}", async (Guid id, Access access, TaskloomDbContext db) =>
        {
            var c = await RequireAuthorAsync(id, access, db);
            db.Comments.Remove(c);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });
    }

    /// <summary>Membership comes first (a removed member gets 404 even for their own comment), then authorship (403).</summary>
    private static async Task<TaskComment> RequireAuthorAsync(Guid id, Access access, TaskloomDbContext db)
    {
        var c = await db.Comments.FirstOrDefaultAsync(x => x.Id == id) ?? throw new NotFoundException();
        await access.RequireTaskAsync(c.TaskId);
        if (c.AuthorId != access.UserId) throw new ForbiddenException();
        return c;
    }

    private static string RequireBody(string? body) =>
        Text.Require(body, Limits.CommentBody, "Write a comment first.", "Comments can be up to 10,000 characters.");

    private static CommentDto ToDto(TaskComment c, User author) =>
        new(c.Id, c.TaskId, new UserDto(author.Id, author.Email, author.DisplayName), c.Body, c.CreatedAt, c.EditedAt);
}

/// <summary>Attachments (spec 5.4 #40–#42), stored through IFileStore: local disk or Azure Blob Storage.</summary>
public static class AttachmentEndpoints
{
    /// <summary>Raster images the browser may show inline; everything else (SVG included) downloads.</summary>
    private static readonly HashSet<string> InlineTypes = new(StringComparer.OrdinalIgnoreCase)
        { "image/png", "image/jpeg", "image/gif", "image/webp" };

    private const int MaxFileName = 255, MaxContentType = 255;

    public static void MapAttachmentEndpoints(this RouteGroupBuilder api)
    {
        api.MapPost("/tasks/{id:guid}/attachments", async (Guid id, IFormFile file, Access access, TaskloomDbContext db, IFileStore files) =>
        {
            await access.RequireTaskAsync(id, WorkspaceRole.Member);
            if (file.Length == 0) return Problems.BadRequest("The file is empty.");
            if (file.Length > Limits.AttachmentBytes) return Problems.BadRequest("Files can be up to 25 MB.");
            var now = Clock.Now();
            var a = new TaskAttachment
            {
                TaskId = id,
                FileName = CleanFileName(file.FileName),
                ContentType = CleanContentType(file.ContentType),
                SizeBytes = file.Length,
                StorageKey = "",
                UploadedById = access.UserId,
                CreatedAt = now,
            };
            a.StorageKey = $"tasks/{id}/{a.Id}";
            await using (var stream = file.OpenReadStream())
                await files.SaveAsync(a.StorageKey, stream, a.ContentType);
            db.Attachments.Add(a);
            new ActivityRecorder(db, access.UserId, now).Record(id, TaskActivityKind.AttachmentAdded, null, a.FileName);
            try
            {
                await db.SaveChangesAsync();
            }
            catch
            {
                // Without its row nothing could reach the file or ever delete it.
                await files.DeleteAsync(a.StorageKey, CancellationToken.None);
                throw;
            }
            return Results.Ok(ToDto(a));
        }).DisableAntiforgery().WithMetadata(new LargeBody(RequestLimits.UploadBodyBytes));

        api.MapGet("/attachments/{id:guid}", async (Guid id, HttpContext http, Access access, TaskloomDbContext db, IFileStore files, CancellationToken ct) =>
        {
            var a = await db.Attachments.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException();
            await access.RequireTaskAsync(a.TaskId);
            // Attachments never change, so the id is the ETag and caches may keep the file for a year.
            var etag = new EntityTagHeaderValue($"\"{a.Id}\"");
            var headers = http.Response.Headers;
            if (http.Request.GetTypedHeaders().IfNoneMatch is { Count: > 0 } ifNoneMatch &&
                ifNoneMatch.Any(t => t.Equals(EntityTagHeaderValue.Any) || t.Compare(etag, useStrongComparison: false)))
            {
                SetCacheHeaders(headers, etag);
                return Results.StatusCode(StatusCodes.Status304NotModified);
            }
            var stream = await files.OpenReadAsync(a.StorageKey, ct);
            if (stream is null) return Results.NotFound();
            SetCacheHeaders(headers, etag);
            var disposition = new ContentDispositionHeaderValue(IsInlineImage(a.ContentType) ? "inline" : "attachment");
            disposition.SetHttpFileName(a.FileName);
            headers.ContentDisposition = disposition.ToString();
            headers.XContentTypeOptions = "nosniff";
            headers.ContentSecurityPolicy = "sandbox";
            return Results.File(stream, a.ContentType);
        });

        api.MapDelete("/attachments/{id:guid}", async (Guid id, Access access, TaskloomDbContext db, IFileStore files) =>
        {
            var a = await db.Attachments.FirstOrDefaultAsync(x => x.Id == id) ?? throw new NotFoundException();
            await access.RequireTaskAsync(a.TaskId, WorkspaceRole.Member);
            db.Attachments.Remove(a);
            new ActivityRecorder(db, access.UserId, Clock.Now()).Record(a.TaskId, TaskActivityKind.AttachmentRemoved, a.FileName, null);
            await db.SaveChangesAsync();
            await files.DeleteAsync(a.StorageKey);
            return Results.NoContent();
        });
    }

    private static void SetCacheHeaders(IHeaderDictionary headers, EntityTagHeaderValue etag)
    {
        headers.CacheControl = "private, max-age=31536000, immutable";
        headers.ETag = etag.ToString();
    }

    private static bool IsInlineImage(string contentType) =>
        MediaTypeHeaderValue.TryParse(contentType, out var media) && InlineTypes.Contains(media.MediaType.ToString());

    /// <summary>
    /// The type the browser sent when it is one media type written in printable ASCII, else application/octet-stream.
    /// It is sent back as the Content-Type of every download, and a header can't carry other characters: the server
    /// would answer each download with a 500.
    /// </summary>
    private static string CleanContentType(string? raw) =>
        raw is { Length: > 0 and <= MaxContentType } && raw.All(c => c is >= ' ' and <= '~') && MediaTypeHeaderValue.TryParse(raw, out _)
            ? raw
            : "application/octet-stream";

    /// <summary>
    /// Bidirectional formatting characters: a right-to-left override in "invoice[RLO]gpj.exe" shows the name as
    /// "invoiceexe.jpg". Other format characters (such as the joiners inside emoji) stay.
    /// </summary>
    private static bool IsBidiControl(char c) => c is '\u061C' or '\u200E' or '\u200F' or (>= '\u202A' and <= '\u202E') or (>= '\u2066' and <= '\u2069');

    /// <summary>
    /// The file's own name without any path, control or bidirectional formatting characters, at most 255 characters
    /// (keeping the extension, never splitting a surrogate pair).
    /// </summary>
    private static string CleanFileName(string? raw)
    {
        var name = Path.GetFileName(string.Concat((raw ?? "").Where(c => !char.IsControl(c) && !IsBidiControl(c))).Replace('\\', '/')).Trim();
        if (name.Length == 0) return "file";
        if (name.Length <= MaxFileName) return name;
        var extension = Path.GetExtension(name);
        if (extension.Length > 20) extension = "";
        return Text.Truncate(name, MaxFileName - extension.Length) + extension;
    }

    private static AttachmentDto ToDto(TaskAttachment a) =>
        new(a.Id, a.TaskId, a.FileName, a.ContentType, a.SizeBytes, a.UploadedById, a.CreatedAt);
}
