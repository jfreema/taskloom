using System.Text;
using System.Text.RegularExpressions;

namespace Taskloom.Api.Notifications;

/// <summary>
/// @mentions in comments (Features 2). A comment's body stays plain text; a mention is the token <c>&lt;@user id&gt;</c>
/// inside it, with the id as a lower-case UUID, so a name that changes later still shows right. The web app writes the
/// token when someone is picked from the @ list and draws it as "@Name".
/// </summary>
public static partial class Mentions
{
    /// <summary>More than anyone means to mention; beyond it the comment is refused.</summary>
    public const int Max = 50;

    public const string TooManyMessage = "A comment can mention up to 50 people.";
    public const string MustSeeTaskMessage = "Only people who can see this task can be mentioned.";

    [GeneratedRegex(@"<@([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})>", RegexOptions.CultureInvariant)]
    private static partial Regex Token();

    public static string TokenFor(Guid userId) => $"<@{userId:D}>";

    /// <summary>The distinct people a body mentions, in order; a 400 when there are more than <see cref="Max"/>.</summary>
    public static List<Guid> In(string body)
    {
        var ids = new List<Guid>();
        if (!body.Contains("<@", StringComparison.Ordinal)) return ids;
        var seen = new HashSet<Guid>();
        foreach (Match m in Token().Matches(body))
        {
            var id = Guid.Parse(m.Groups[1].ValueSpan);
            if (!seen.Add(id)) continue;
            if (ids.Count == Max) throw ProblemException.BadRequest(TooManyMessage);
            ids.Add(id);
        }
        return ids;
    }

    /// <summary>The body with each mention written as "@Name" ("@Former member" for someone without a name here).</summary>
    public static string ToPlainText(string body, IReadOnlyDictionary<Guid, string> names)
    {
        if (!body.Contains("<@", StringComparison.Ordinal)) return body;
        return Token().Replace(body, m => "@" + (names.TryGetValue(Guid.Parse(m.Groups[1].ValueSpan), out var name) ? name : "Former member"));
    }

    /// <summary>
    /// A comment on one line for an Inbox row: mentions as names, runs of white space as one space, at most
    /// <paramref name="max"/> characters (ending in "…" when cut).
    /// </summary>
    public static string Preview(string body, IReadOnlyDictionary<Guid, string> names, int max = 280)
    {
        var text = ToPlainText(body, names);
        var line = new StringBuilder(Math.Min(text.Length, max + 1));
        var space = false;
        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c) || char.IsControl(c))
            {
                space = line.Length > 0;
                continue;
            }
            if (space) line.Append(' ');
            space = false;
            line.Append(c);
            if (line.Length > max) break;
        }
        return line.Length > max ? Text.Truncate(line.ToString(), max - 1).TrimEnd() + "…" : line.ToString();
    }
}
