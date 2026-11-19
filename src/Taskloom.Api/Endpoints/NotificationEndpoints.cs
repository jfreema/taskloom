using Microsoft.EntityFrameworkCore;
using Taskloom.Api.Notifications;
using Taskloom.Domain;
using Taskloom.Infrastructure;

namespace Taskloom.Api.Endpoints;

/// <summary>
/// The Inbox (Features 2): what happened to the caller's tasks, mentions of them and their due-date reminders, per
/// workspace. A row only shows while the caller can see its task (<see cref="Access.VisibleListIds"/>), so leaving a
/// private space takes its rows out of the Inbox and out of the unread count. Everyone reads only their own rows.
/// </summary>
public static class NotificationEndpoints
{
    public const int DefaultLimit = 50, MaxLimit = 100;

    public static void MapNotificationEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/workspaces/{id:guid}/notifications", ListAsync);

        // The sidebar's badge, asked for every minute by every open tab: membership and the count in one query.
        api.MapGet("/workspaces/{id:guid}/notifications/count", async (Guid id, Access access, TaskloomDbContext db, CancellationToken ct) =>
            new NotificationCountDto(await UnreadAsync(id, access, db, ct)));

        api.MapPost("/workspaces/{id:guid}/notifications/read-all", async (Guid id, ReadAllNotificationsRequest req, Access access, TaskloomDbContext db) =>
        {
            await access.RequireWorkspaceAsync(id);
            var now = Clock.Now();
            var rows = Mine(id, access, db).Where(n => n.ReadAt == null);
            // Rows that arrived after the page was loaded stay unread: the person hasn't seen them.
            if (req.Before is { } before) rows = rows.Where(n => n.CreatedAt <= before);
            await rows.ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, now));
            return Results.NoContent();
        });

        api.MapPost("/notifications/read", async (MarkNotificationsRequest req, Access access, TaskloomDbContext db) =>
        {
            var me = access.UserId;
            var read = req.Read ?? true;
            IQueryable<Notification> rows;
            if (req.TaskId is { } taskId && req.Ids is null) rows = db.Notifications.Where(n => n.UserId == me && n.TaskId == taskId);
            else if (req.Ids is { Count: >= 1 and <= Limits.BulkItems } ids && req.TaskId is null)
            {
                var distinct = ids.Distinct().ToList();
                rows = db.Notifications.Where(n => n.UserId == me && distinct.Contains(n.Id));
            }
            else throw ProblemException.BadRequest("Send the ids of 1 to 500 notifications, or a taskId.");

            DateTimeOffset? at = read ? Clock.Now() : null;
            // Only rows that change: reading again keeps the first time.
            await rows.Where(n => (n.ReadAt == null) == read).ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, at));
            return Results.NoContent();
        });
    }

    /// <summary>The caller's rows about tasks of the workspace they can see (a query to compose).</summary>
    private static IQueryable<Notification> Mine(Guid workspaceId, Access access, TaskloomDbContext db)
    {
        var me = access.UserId;
        var listIds = access.VisibleListIds(workspaceId);
        return db.Notifications.Where(n => n.UserId == me && listIds.Contains(n.Task!.ListId));
    }

    /// <summary>The caller's unread count in the workspace; 404 when they aren't a member. One query.</summary>
    private static async Task<int> UnreadAsync(Guid workspaceId, Access access, TaskloomDbContext db, CancellationToken ct)
    {
        var me = access.UserId;
        var unread = Mine(workspaceId, access, db).Where(n => n.ReadAt == null);
        var row = await db.WorkspaceMembers.Where(m => m.WorkspaceId == workspaceId && m.UserId == me)
            .Select(m => new { Unread = unread.Count() })
            .FirstOrDefaultAsync(ct) ?? throw new NotFoundException();
        return row.Unread;
    }

    // GET /api/workspaces/{id}/notifications?unread=true&before=<createdAt>&limit=50, newest first. Two queries: the
    // membership check with the unread count, then the page.
    private static async Task<NotificationsDto> ListAsync(Guid id, HttpRequest request, Access access, TaskloomDbContext db, CancellationToken ct)
    {
        var query = request.Query;
        var unreadOnly = false;
        if (Single(query, "unread") is { Length: > 0 } rawUnread && !bool.TryParse(rawUnread, out unreadOnly))
            throw ProblemException.BadRequest("unread must be true or false.");
        DateTimeOffset? before = null;
        if (Single(query, "before") is { Length: > 0 } rawBefore)
        {
            before = DateTimeOffset.TryParse(rawBefore, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var parsed)
                ? parsed
                : throw ProblemException.BadRequest("before must be a time like 2026-11-18T14:30:00Z.");
        }
        var limit = DefaultLimit;
        if (Single(query, "limit") is { Length: > 0 } rawLimit &&
            (!int.TryParse(rawLimit, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out limit) || limit is < 1 or > MaxLimit))
            throw ProblemException.BadRequest("limit must be a whole number from 1 to 100.");

        var unread = await UnreadAsync(id, access, db, ct);
        var rows = Mine(id, access, db);
        if (unreadOnly) rows = rows.Where(n => n.ReadAt == null);
        if (before is { } b) rows = rows.Where(n => n.CreatedAt < b);
        var page = await rows.OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.Id).Take(limit + 1)
            .Select(n => new NotificationDto(n.Id, n.Kind, n.TaskId, n.Task!.Title, n.Task.ListId,
                n.ActorId == null ? null : new UserDto(n.ActorId.Value, n.Actor!.Email, n.Actor.DisplayName, UserPhotos.UrlFor(n.ActorId.Value, n.Actor.PhotoVersion)),
                n.FromValue, n.ToValue, n.CommentId, n.CreatedAt, n.ReadAt))
            .ToListAsync(ct);
        var hasMore = page.Count > limit;
        if (hasMore) page.RemoveAt(limit);
        return new NotificationsDto(page, unread, hasMore);
    }

    private static string? Single(IQueryCollection query, string key) =>
        query.TryGetValue(key, out var values) && values.Count > 0 ? values[0] : null;
}
