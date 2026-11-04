using System.Globalization;

namespace Taskloom.Api;

/// <summary>
/// The fixed formats of activity values (spec 5.6), so the client can render them: dates as yyyy-MM-dd, priorities
/// as enum names, everything else as the name displayed at the time, at most 500 characters.
/// </summary>
public static class ActivityValues
{
    public const int MaxLength = 500;

    public static string? Date(DateOnly? date) => date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>Truncates a value longer than 500 characters to 499 plus "…".</summary>
    public static string? Clip(string? value) => value is { Length: > MaxLength } ? Text.Truncate(value, MaxLength - 1) + "…" : value;
}
