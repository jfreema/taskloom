using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;
using Taskloom.Api.Auth;
using Taskloom.Api.Notifications;
using Taskloom.Infrastructure;

namespace Taskloom.Api.Endpoints;

/// <summary>
/// People's photos (Look 2). Anyone can see the photo of someone they share a workspace with, like their name; to
/// anyone else it doesn't exist. A person uploads or removes their own photo in Settings › Profile, and from then on
/// signing in leaves it alone (otherwise each sign-in brings the provider's photo, see <see cref="ProviderPhotos"/>).
/// </summary>
public static class PhotoEndpoints
{
    /// <summary>A form part this size holds any photo of <see cref="UserPhotos.MaxBytes"/> with room for its headers.</summary>
    public const long UploadBodyBytes = UserPhotos.MaxBytes + 16 * 1024;

    public static void MapPhotoEndpoints(this RouteGroupBuilder api)
    {
        // The URL names the photo's version (UserPhotos.UrlFor): that one never changes, so browsers keep it for good.
        // Any other version (an old link) answers the current photo and is checked again on every use.
        api.MapGet("/users/{id:guid}/photo", async (Guid id, string? v, HttpContext http, Access access, TaskloomDbContext db, CancellationToken ct) =>
        {
            var me = access.UserId;
            var visible = id == me || await db.WorkspaceMembers.AnyAsync(m => m.UserId == id &&
                db.WorkspaceMembers.Any(x => x.WorkspaceId == m.WorkspaceId && x.UserId == me), ct);
            if (!visible) return Results.NotFound();
            var version = await db.Users.Where(u => u.Id == id).Select(u => u.PhotoVersion).FirstOrDefaultAsync(ct);
            if (version is null) return Results.NotFound();

            var headers = http.Response.Headers;
            headers.CacheControl = v == version ? "private, max-age=31536000, immutable" : "private, no-cache";
            headers.XContentTypeOptions = "nosniff";
            headers.ContentSecurityPolicy = "sandbox";
            var etag = new EntityTagHeaderValue($"\"{version}\"");
            if (http.Request.GetTypedHeaders().IfNoneMatch is { Count: > 0 } ifNoneMatch &&
                ifNoneMatch.Any(t => t.Equals(EntityTagHeaderValue.Any) || t.Compare(etag, useStrongComparison: false)))
            {
                headers.ETag = etag.ToString();
                return Results.StatusCode(StatusCodes.Status304NotModified);
            }
            var photo = await db.UserPhotos.AsNoTracking().Where(p => p.UserId == id)
                .Select(p => new { p.ContentType, p.Data }).FirstOrDefaultAsync(ct);
            if (photo is null) return Results.NotFound();
            return Results.Bytes(photo.Data, photo.ContentType, entityTag: etag);
        }).WithMetadata(new CacheableResponse());

        // The web app sends a 256-pixel square JPEG it made from the chosen picture; any JPEG, PNG, WebP or GIF within
        // the limits is taken as it is.
        api.MapPost("/me/photo", async (IFormFile file, Access access, TaskloomDbContext db, AuthOptions options, NotificationOptions notifications, CancellationToken ct) =>
        {
            if (file.Length == 0) return Problems.BadRequest(UserPhotos.NotAnImageMessage);
            if (file.Length > UserPhotos.MaxBytes) return Problems.BadRequest(UserPhotos.TooLargeMessage);
            var data = new byte[file.Length];
            await using (var stream = file.OpenReadStream()) await stream.ReadExactlyAsync(data, ct);
            var (image, error) = UserPhotos.Check(data);
            if (image is null) return Problems.BadRequest(error!);

            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == access.UserId, ct) ?? throw new UnauthorizedAccessException();
            await UserPhotos.SetAsync(db, user, data, image, UserPhotos.UploadSource, Clock.Now(), ct);
            user.PhotoChosen = true;
            await db.SaveChangesAsync(ct);
            return Results.Ok(WorkspaceEndpoints.Me(user, options, notifications));
        }).DisableAntiforgery().WithMetadata(new LargeBody(UploadBodyBytes, UserPhotos.TooLargeMessage));

        // Back to initials, which signing in then keeps.
        api.MapDelete("/me/photo", async (Access access, TaskloomDbContext db, AuthOptions options, NotificationOptions notifications, CancellationToken ct) =>
        {
            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == access.UserId, ct) ?? throw new UnauthorizedAccessException();
            await UserPhotos.RemoveAsync(db, user, ct);
            user.PhotoChosen = true;
            await db.SaveChangesAsync(ct);
            return Results.Ok(WorkspaceEndpoints.Me(user, options, notifications));
        });
    }
}
