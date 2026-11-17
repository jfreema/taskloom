using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Taskloom.Domain;

namespace Taskloom.Infrastructure;

/// <summary>
/// How <see cref="RepeatRule"/> is stored: a jsonb column, camelCase with enums by name, for example
/// {"unit":"Week","every":1,"basis":"Schedule","weekdays":"Monday, Wednesday",...}. The rule is an immutable record with
/// value equality, so EF's default comparison of the property is enough.
/// </summary>
public static class RepeatRuleJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Write(RepeatRule rule) => JsonSerializer.Serialize(rule, Options);

    public static RepeatRule Read(string json) => JsonSerializer.Deserialize<RepeatRule>(json, Options)!;

    public static readonly ValueConverter<RepeatRule?, string?> Converter = new(
        rule => rule == null ? null : Write(rule),
        json => json == null ? null : Read(json));
}
