namespace Taskloom.Api.Notifications;

/// <summary>
/// People's time zones, by IANA name ("America/Chicago"), as their browsers report them. The names come from the
/// machine's own time zone data; a name it doesn't know counts as UTC, so a reminder is never lost, only early or late.
/// </summary>
public static class TimeZones
{
    public const int MaxLength = 64;
    public const string UnknownMessage = "Send an IANA time zone name such as America/Chicago.";

    public static bool IsKnown(string? id) => id is { Length: > 0 and <= MaxLength } && TimeZoneInfo.TryFindSystemTimeZoneById(id, out _);

    public static TimeZoneInfo Find(string? id) =>
        id is { Length: > 0 } && TimeZoneInfo.TryFindSystemTimeZoneById(id, out var zone) ? zone : TimeZoneInfo.Utc;

    /// <summary>The date and the hour (0 to 23) it is for someone in the zone.</summary>
    public static (DateOnly Date, int Hour) LocalNow(string? id, DateTimeOffset now)
    {
        var local = TimeZoneInfo.ConvertTime(now, Find(id));
        return (DateOnly.FromDateTime(local.DateTime), local.Hour);
    }
}
