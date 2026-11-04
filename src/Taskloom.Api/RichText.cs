using System.Text;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Ganss.Xss;

namespace Taskloom.Api;

/// <summary>
/// Task descriptions are HTML written by the Tiptap editor (spec 5.7). Everything the API stores goes through
/// <see cref="Sanitize"/>, and <see cref="ToPlainText"/> derives the plain-text copy used for search and previews.
/// </summary>
public static partial class RichText
{
    public const int MaxHtmlLength = 200_000;
    public const int MaxTextLength = 10_000;

    private static readonly string[] Tags =
        ["p", "br", "strong", "b", "em", "i", "s", "del", "u", "code", "pre", "blockquote", "h2", "h3",
         "ul", "ol", "li", "a", "hr", "label", "input", "span", "div"];

    /// <summary>The sanitizer's attribute set is global, so it holds the union; this map says which tags may carry each.</summary>
    private static readonly Dictionary<string, HashSet<string>> AttributeTags = new(StringComparer.OrdinalIgnoreCase)
    {
        ["href"] = ["a"],
        ["data-type"] = ["ul", "li"],
        ["data-checked"] = ["li"],
        ["type"] = ["input"],
        ["checked"] = ["input"],
        ["disabled"] = ["input"],
    };

    private static readonly string[] Schemes = ["http", "https", "mailto"];

    /// <summary>Elements that start a new line in the plain-text copy.</summary>
    private static readonly HashSet<string> BlockTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "p", "div", "h1", "h2", "h3", "h4", "h5", "h6", "ul", "ol", "li", "blockquote", "pre", "hr",
        "table", "thead", "tbody", "tr", "td", "th", "section", "article", "header", "footer", "figure",
    };

    // Thread-safe once configured: Sanitize may be called concurrently on one instance.
    private static readonly HtmlSanitizer Sanitizer = CreateSanitizer();

    private static HtmlSanitizer CreateSanitizer()
    {
        var s = new HtmlSanitizer();
        s.AllowedTags.Clear();
        s.AllowedTags.UnionWith(Tags);
        s.AllowedAttributes.Clear();
        s.AllowedAttributes.UnionWith(AttributeTags.Keys);
        s.AllowDataAttributes = false;
        s.AllowedSchemes.Clear();
        s.AllowedSchemes.UnionWith(Schemes);
        s.AllowedCssProperties.Clear();
        s.AllowedAtRules.Clear();
        s.PostProcessNode += (_, e) => PostProcess(e.Node);
        return s;
    }

    /// <summary>Per-tag rules the global allow-lists can't express.</summary>
    private static void PostProcess(INode node)
    {
        if (node is not IElement element) return;
        var tag = element.LocalName;
        foreach (var attribute in element.Attributes.ToList())
        {
            if (!AttributeTags.TryGetValue(attribute.Name, out var tags) || !tags.Contains(tag))
                element.RemoveAttribute(attribute.Name);
        }
        switch (tag)
        {
            case "a":
                element.SetAttribute("target", "_blank");
                element.SetAttribute("rel", "noopener noreferrer nofollow");
                break;
            case "input" when !string.Equals(element.GetAttribute("type"), "checkbox", StringComparison.OrdinalIgnoreCase):
                element.Remove();
                break;
        }
    }

    /// <summary>
    /// Returns safe HTML: only the tags and attributes of spec 5.7 survive, links open in a new tab, and inputs
    /// can only be checkboxes. An empty editor document (<c>&lt;p&gt;&lt;/p&gt;</c>) is stored as "".
    /// </summary>
    public static string Sanitize(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return "";
        var clean = Sanitizer.Sanitize(html).Trim();
        return clean == "<p></p>" ? "" : clean;
    }

    /// <summary>
    /// The text of the HTML: one line per block element (an empty paragraph is an empty line), runs of
    /// whitespace collapsed to one space, trimmed, and at most <see cref="MaxTextLength"/> characters.
    /// </summary>
    public static string ToPlainText(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return "";
        var document = new HtmlParser().ParseDocument(html);
        var state = new TextState();
        if (document.Body is { } body) Collect(body, state, inPre: false);
        state.EndLine(force: false);

        var lines = state.Lines
            .SelectMany(line => line.Split('\n'))
            .Select(line => Whitespace().Replace(line, " ").Trim());
        var text = string.Join('\n', lines).Trim();
        if (text.Length > MaxTextLength)
        {
            var cut = char.IsHighSurrogate(text[MaxTextLength - 1]) ? MaxTextLength - 1 : MaxTextLength;
            text = text[..cut].TrimEnd();
        }
        return text;
    }

    private sealed class TextState
    {
        public readonly List<string> Lines = [];
        private readonly StringBuilder _current = new();
        private bool _hasCurrent;

        public void Append(string text)
        {
            _current.Append(text);
            _hasCurrent = true;
        }

        /// <summary>Ends the line being collected; with <paramref name="force"/> also when it is empty.</summary>
        public void EndLine(bool force)
        {
            if (!_hasCurrent && !force) return;
            Lines.Add(_current.ToString());
            _current.Clear();
            _hasCurrent = false;
        }

        public bool HasCurrent => _hasCurrent;
    }

    private static void Collect(INode node, TextState state, bool inPre)
    {
        foreach (var child in node.ChildNodes)
        {
            switch (child)
            {
                case IText text:
                    // Outside <pre>, a line break in the source is just whitespace.
                    state.Append(inPre ? text.Data.Replace("\r\n", "\n") : text.Data.Replace('\r', ' ').Replace('\n', ' '));
                    break;
                case IElement { LocalName: "br" }:
                    state.EndLine(force: true);
                    break;
                case IElement element when BlockTags.Contains(element.LocalName):
                    state.EndLine(force: false);
                    var linesBefore = state.Lines.Count;
                    Collect(element, state, inPre || element.LocalName == "pre");
                    if (state.HasCurrent) state.EndLine(force: true);
                    else if (state.Lines.Count == linesBefore) state.Lines.Add("");
                    break;
                case IElement element:
                    Collect(element, state, inPre);
                    break;
            }
        }
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
