using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Taskloom.Domain;

namespace Taskloom.Api;

/// <summary>
/// The fixed formats of activity values (spec 5.6), so the client can render them: dates as yyyy-MM-dd, priorities
/// as enum names, repeat rules as the JSON of <see cref="RepeatDto"/> (without its null fields), everything else as the
/// name displayed at the time, at most 500 characters.
/// </summary>
public static class ActivityValues
{
    public const int MaxLength = 500;

    private static readonly JsonSerializerOptions RepeatJson = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string? Repeat(RepeatRule? rule) => RepeatDto.From(rule) is { } dto ? JsonSerializer.Serialize(dto, RepeatJson) : null;

    public static string? Date(DateOnly? date) => date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>Truncates a value longer than 500 characters to 499 plus "…".</summary>
    public static string? Clip(string? value) => value is { Length: > MaxLength } ? Text.Truncate(value, MaxLength - 1) + "…" : value;
}
