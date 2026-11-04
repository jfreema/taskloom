using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Taskloom.Api;

/// <summary>Size limits of spec 5.1.</summary>
public static class Limits
{
    /// <summary>Workspace, space, list and display names.</summary>
    public const int Name = 200;
    public const int StatusName = 100;
    public const int TagName = 50;
    public const int TaskTitle = 500;
    public const int CommentBody = 10_000;
    public const int DescriptionHtml = RichText.MaxHtmlLength;
    public const long AttachmentBytes = 26_214_400;
    public const int BulkItems = 500;
    /// <summary>The <c>q</c> of GET /api/workspaces/{id}/tasks.</summary>
    public const int SearchQuery = 200;
}

/// <summary>
/// The current time at the precision PostgreSQL keeps (microseconds), so a response built from memory shows the same
/// timestamp as every later read of the row.
/// </summary>
public static class Clock
{
    public static DateTimeOffset Now()
    {
        var now = DateTimeOffset.UtcNow;
        return now.AddTicks(-(now.Ticks % TimeSpan.TicksPerMicrosecond));
    }
}

/// <summary>A 400 or 409 with a user-facing <c>detail</c>; Program.cs turns it into ProblemDetails (spec 5.1).</summary>
public sealed class ProblemException(int status, string detail) : Exception(detail)
{
    public int Status { get; } = status;

    public static ProblemException BadRequest(string detail) => new(StatusCodes.Status400BadRequest, detail);
    public static ProblemException Conflict(string detail) => new(StatusCodes.Status409Conflict, detail);
}

public static class Text
{
    /// <summary>The trimmed value, or null when it is missing, blank or longer than <paramref name="max"/>.</summary>
    public static string? Required(string? raw, int max)
    {
        var value = raw?.Trim();
        return string.IsNullOrEmpty(value) || value.Length > max ? null : value;
    }

    /// <summary>Trims a required string, or gives the 400 to return: one message when it is blank, another when it is too long.</summary>
    public static (string? Value, IResult? Error) Check(string? raw, int max, string blankMessage, string tooLongMessage)
    {
        var value = raw?.Trim();
        if (string.IsNullOrEmpty(value)) return (null, Problems.BadRequest(blankMessage));
        if (value.Length > max) return (null, Problems.BadRequest(tooLongMessage));
        return (value, null);
    }

    /// <summary>Like <see cref="Check"/>, but throws the 400.</summary>
    public static string Require(string? raw, int max, string blankMessage, string tooLongMessage)
    {
        var value = raw?.Trim();
        if (string.IsNullOrEmpty(value)) throw ProblemException.BadRequest(blankMessage);
        if (value.Length > max) throw ProblemException.BadRequest(tooLongMessage);
        return value;
    }

    /// <summary>
    /// The first <paramref name="max"/> characters, one fewer when the cut would split a surrogate pair: half an emoji
    /// is not valid text, and PostgreSQL refuses it.
    /// </summary>
    public static string Truncate(string value, int max)
    {
        if (value.Length <= max) return value;
        return value[..(max > 0 && char.IsHighSurrogate(value[max - 1]) ? max - 1 : max)];
    }
}

/// <summary>Colours are "#RRGGBB" strings (spec 5.5 Tags; also used for spaces and statuses).</summary>
public static class Colors
{
    public const string InvalidMessage = "Colours must be a hex value like #7A868C.";

    public static bool IsHex(string? value) =>
        value is { Length: 7 } && value[0] == '#' && value.AsSpan(1).IndexOfAnyExcept("0123456789abcdefABCDEF") < 0;

    /// <summary>The colour, or <paramref name="fallback"/> when none was sent; a malformed colour is a 400.</summary>
    public static string OrDefault(string? color, string fallback) => color is null ? fallback : Require(color);

    public static string Require(string color) => IsHex(color) ? color : throw ProblemException.BadRequest(InvalidMessage);
}

/// <summary>Email addresses as the API stores them: trimmed and lower case.</summary>
public static class Emails
{
    public const int MaxLength = 320;
    public const string InvalidMessage = "Enter a valid email address.";

    /// <summary>The normalized address, or null when it doesn't look like one (one "@" with text on both sides, no spaces).</summary>
    public static string? Normalize(string? raw)
    {
        var email = raw?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(email) || email.Length > MaxLength) return null;
        var at = email.IndexOf('@');
        if (at <= 0 || at == email.Length - 1 || email.IndexOf('@', at + 1) >= 0) return null;
        foreach (var c in email)
            if (char.IsWhiteSpace(c) || char.IsControl(c)) return null;
        return email;
    }
}

/// <summary>Display names guessed from an email address (spec 5.5 Members).</summary>
public static class NameGuess
{
    private static readonly char[] Separators = ['.', '_', '-'];

    /// <summary>
    /// The part before "@", without anything from "+", split on ".", "_" and "-", each word capitalised:
    /// "ana.moreno@" gives "Ana Moreno", "jsmith@" gives "Jsmith", "j_doe+test@" gives "J Doe".
    /// </summary>
    public static string FromEmail(string email)
    {
        var trimmed = email.Trim();
        var local = trimmed;
        var at = local.IndexOf('@');
        if (at >= 0) local = local[..at];
        var plus = local.IndexOf('+');
        if (plus >= 0) local = local[..plus];
        var words = local.Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(w => char.ToUpperInvariant(w[0]) + w[1..]);
        var name = string.Join(' ', words);
        if (name.Length == 0) name = trimmed;
        return name.Length > Limits.Name ? Text.Truncate(name, Limits.Name).TrimEnd() : name;
    }
}

/// <summary>Sign-in return paths (spec 5.8): only local paths, anything else becomes "/".</summary>
public static class ReturnUrls
{
    /// <remarks>
    /// Characters beyond ASCII are percent-encoded (as UTF-8, the way a browser writes them in a URL): the path ends up
    /// in the Location header of the redirect after sign-in, and a header can't carry them, so the server would fail
    /// the sign-in with a 500. A lone surrogate can't be encoded, so it gives "/".
    /// </remarks>
    public static string Safe(string? raw)
    {
        if (string.IsNullOrEmpty(raw) || raw[0] != '/') return "/";
        if (raw.Length > 1 && (raw[1] == '/' || raw[1] == '\\')) return "/";
        foreach (var c in raw)
            if (c == '\\' || char.IsControl(c)) return "/";
        if (!raw.Any(c => c > '\x7e')) return raw;
        var encoded = new StringBuilder(raw.Length * 2);
        for (var i = 0; i < raw.Length; i++)
        {
            if (raw[i] <= '\x7e')
            {
                encoded.Append(raw[i]);
                continue;
            }
            var length = char.IsSurrogatePair(raw, i) ? 2 : 1;
            if (char.IsSurrogate(raw[i]) && length == 1) return "/";
            encoded.Append(Uri.EscapeDataString(raw.Substring(i, length)));
            i += length - 1;
        }
        return encoded.ToString();
    }
}

/// <summary>Reading the JSON bodies parsed as <see cref="JsonElement"/> (PATCH and bulk), where "absent" and "null" differ.</summary>
public static class Json
{
    /// <summary>
    /// A 400 when a string or property name is half a surrogate pair (a lone escape like "\ud83d"): System.Text.Json
    /// only finds out when the string is read, and throws. Bodies bound to records get the same 400 from the binder.
    /// </summary>
    public static void RequireReadable(JsonElement body)
    {
        try { Read(body); }
        catch (InvalidOperationException) { throw ProblemException.BadRequest(Problems.UnreadableMessage); }

        static void Read(JsonElement v)
        {
            switch (v.ValueKind)
            {
                case JsonValueKind.String:
                    v.GetString();
                    break;
                case JsonValueKind.Array:
                    foreach (var item in v.EnumerateArray()) Read(item);
                    break;
                case JsonValueKind.Object:
                    foreach (var p in v.EnumerateObject())
                    {
                        _ = p.Name;
                        Read(p.Value);
                    }
                    break;
            }
        }
    }

    public static bool TryGetGuidValue(this JsonElement v, out Guid id)
    {
        id = default;
        return v.ValueKind == JsonValueKind.String && v.TryGetGuid(out id);
    }

    /// <summary>The property with this name, ignoring case (the last one wins when it appears twice).</summary>
    public static bool TryGetPropertyIgnoreCase(this JsonElement obj, string name, out JsonElement value)
    {
        value = default;
        var found = false;
        if (obj.ValueKind != JsonValueKind.Object) return false;
        foreach (var p in obj.EnumerateObject())
        {
            if (!string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
            value = p.Value;
            found = true;
        }
        return found;
    }

    /// <summary>
    /// The distinct ids of a JSON array of UUID strings, in order, or a 400 with <paramref name="message"/>.
    /// Linear in the array's length (a set finds repeats), however long a client makes it.
    /// </summary>
    public static List<Guid> ReadIds(JsonElement v, string message)
    {
        if (v.ValueKind != JsonValueKind.Array) throw ProblemException.BadRequest(message);
        var ids = new List<Guid>();
        var seen = new HashSet<Guid>();
        foreach (var item in v.EnumerateArray())
        {
            if (!item.TryGetGuidValue(out var id)) throw ProblemException.BadRequest(message);
            if (seen.Add(id)) ids.Add(id);
        }
        return ids;
    }

    public static Guid ReadGuid(JsonElement v, string message) =>
        v.TryGetGuidValue(out var id) ? id : throw ProblemException.BadRequest(message);

    public const string DateMessage = "Dates must look like 2026-11-04.";

    /// <summary>A "yyyy-MM-dd" string, or null for JSON null.</summary>
    public static DateOnly? ReadDate(JsonElement v)
    {
        if (v.ValueKind == JsonValueKind.Null) return null;
        if (v.ValueKind == JsonValueKind.String && Dates.TryParse(v.GetString(), out var d)) return d;
        throw ProblemException.BadRequest(DateMessage);
    }

    /// <summary>A finite JSON number.</summary>
    public static double ReadPosition(JsonElement v) =>
        v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d) && double.IsFinite(d)
            ? d
            : throw ProblemException.BadRequest("Positions must be numbers.");
}

public static class Dates
{
    /// <summary>Exactly "yyyy-MM-dd" (spec 5.1), never other formats.</summary>
    public static bool TryParse(string? s, out DateOnly date) =>
        DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
}

public static class Enums
{
    /// <summary>A defined enum name (ignoring case); numbers are refused so undefined values can't be stored.</summary>
    public static bool TryParseName<T>(string? s, out T value) where T : struct, Enum
    {
        value = default;
        if (string.IsNullOrWhiteSpace(s) || !char.IsLetter(s.TrimStart()[0])) return false;
        return Enum.TryParse(s, ignoreCase: true, out value) && Enum.IsDefined(value);
    }
}

/// <summary>Error responses of spec 5.1: ProblemDetails whose <c>detail</c> the UI may show as is.</summary>
public static class Problems
{
    /// <summary>
    /// The 400 for a body that couldn't be read: malformed JSON, wrong field types, or text PostgreSQL can't store
    /// (a NUL character, half a surrogate pair).
    /// </summary>
    public const string UnreadableMessage = "The request couldn't be read. Check the body and its field types.";

    public static IResult BadRequest(string detail) => Results.Problem(detail: detail, statusCode: StatusCodes.Status400BadRequest);
    public static IResult Conflict(string detail) => Results.Problem(detail: detail, statusCode: StatusCodes.Status409Conflict);
}
