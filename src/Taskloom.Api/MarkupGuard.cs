namespace Taskloom.Api;

/// <summary>
/// Refuses description HTML that would cost the HTML parser far more than its length, before
/// <see cref="RichText.Sanitize"/> parses it. Any client can send any markup, and an HTML5 parser is not linear in
/// it: each tag checks the stack of open elements (quadratic in nesting), every block boundary clones the formatting
/// elements still open (12 KB of crafted markup became 266 MB of nodes), each attribute of a tag is compared with
/// every other one, and the sanitizer walks the tree recursively, so 39,000 nested divs overflowed the stack and
/// ended the process.
/// <para>
/// Short input can't do much of that and passes unchecked, so the sanitizer still cleans any odd markup. Longer input
/// must be shaped like what the editor (Tiptap) writes: only the tags the sanitizer keeps (plus scripts, styles and
/// the like, whose content is skipped as the parser skips it, and images), every end tag closing the innermost open
/// element, no comments or other markup declarations, at most <see cref="MaxDepth"/> elements open at once and
/// <see cref="MaxAttributes"/> attributes on a tag, and inside formatting (bold, links, code…) only more formatting,
/// spans and line breaks. On that input the parser never closes a formatting element implicitly, so it never clones
/// one, and its work stays linear.
/// </para>
/// </summary>
public static class MarkupGuard
{
    /// <summary>Up to this length anything is accepted: its worst case costs less than a full-length editor document.</summary>
    public const int UncheckedLength = 1_024;
    public const int MaxDepth = 256;
    public const int MaxAttributes = 32;

    private static readonly HashSet<string> Voids = ["br", "hr", "input", "img", "wbr"];

    /// <summary>Elements whose content the tokenizer reads as text up to their end tag (RAWTEXT, RCDATA and script data).</summary>
    private static readonly HashSet<string> RawText = ["script", "style", "xmp", "iframe", "noembed", "noframes", "noscript", "textarea", "title"];

    /// <summary>The kept tags that the HTML parser treats as formatting elements (it reopens them after a block ends).</summary>
    private static readonly HashSet<string> Formatting = ["a", "b", "code", "em", "i", "s", "strong", "u"];

    /// <summary>What may be opened inside a formatting element.</summary>
    private static readonly HashSet<string> Inline = ["a", "b", "code", "em", "i", "s", "strong", "u", "span", "del", "br"];

    public static bool Accepts(string html, IReadOnlySet<string> keptTags)
    {
        if (html.Length <= UncheckedLength) return true;
        var open = new List<string>();
        int formatting = 0, links = 0, i = 0, n = html.Length;
        while (i < n)
        {
            var lt = html.IndexOf('<', i);
            if (lt < 0) return true;
            i = lt + 1;
            if (i == n) return false;
            if (html[i] == '/')
            {
                // An end tag, exactly "</name>" (spaces allowed before ">"), closing the innermost open element.
                var name = ReadName(html, ++i, out i);
                while (i < n && IsSpace(html[i])) i++;
                if (name is null || i == n || html[i] != '>') return false;
                i++;
                if (open.Count == 0 || open[^1] != name) return false;
                open.RemoveAt(open.Count - 1);
                if (Formatting.Contains(name)) formatting--;
                if (name == "a") links--;
                continue;
            }

            // A start tag. Anything else after "<" (comments, doctypes, a stray "<") never comes from the editor.
            var tag = ReadName(html, i, out i);
            if (tag is null || !(keptTags.Contains(tag) || RawText.Contains(tag) || Voids.Contains(tag))) return false;
            if (i == n || !IsDelimiter(html[i]) || !SkipAttributes(html, ref i)) return false;
            if (formatting > 0 && !Inline.Contains(tag)) return false;
            if (RawText.Contains(tag))
            {
                // Skipped whole, like the tokenizer does: its content is text, and its end tag closes it.
                var end = FindEndTag(html, tag, i);
                if (end < 0) return true;
                // Script data turns "<!--" into an escaped section in which "</script>" may not end it: not worth modelling.
                if (tag == "script" && html.IndexOf("<!--", i, end - i, StringComparison.Ordinal) >= 0) return false;
                i = end + 2 + tag.Length;
                if (!SkipAttributes(html, ref i)) return false;
                continue;
            }
            if (tag == "a" && links > 0) return false;
            if (Voids.Contains(tag)) continue;
            if (open.Count == MaxDepth) return false;
            open.Add(tag);
            if (Formatting.Contains(tag)) formatting++;
            if (tag == "a") links++;
        }
        return true;
    }

    /// <summary>An ASCII tag name (letter, then letters and digits) in lower case, or null.</summary>
    private static string? ReadName(string s, int start, out int end)
    {
        end = start;
        if (end == s.Length || !char.IsAsciiLetter(s[end])) return null;
        while (end < s.Length && char.IsAsciiLetterOrDigit(s[end])) end++;
        return s[start..end].ToLowerInvariant();
    }

    /// <summary>Where "&lt;/name" followed by a space, "/" or "&gt;" first appears from <paramref name="from"/> (any case), or -1.</summary>
    private static int FindEndTag(string s, string name, int from)
    {
        for (var j = s.IndexOf("</", from, StringComparison.Ordinal); j >= 0; j = s.IndexOf("</", j + 2, StringComparison.Ordinal))
        {
            var after = j + 2 + name.Length;
            if (after < s.Length && s.AsSpan(j + 2, name.Length).Equals(name, StringComparison.OrdinalIgnoreCase) && IsDelimiter(s[after]))
                return j;
        }
        return -1;
    }

    /// <summary>
    /// Moves past a tag's attributes and its closing "&gt;" the way the HTML tokenizer does (spec 13.2.5, "before
    /// attribute name" onwards), so a "&gt;" inside a quoted value doesn't end the tag. False at the end of the input
    /// or past <see cref="MaxAttributes"/>.
    /// </summary>
    private static bool SkipAttributes(string s, ref int i)
    {
        var n = s.Length;
        var count = 0;
        while (true)
        {
            while (i < n && IsSpace(s[i])) i++;
            if (i == n) return false;
            if (s[i] == '>')
            {
                i++;
                return true;
            }
            if (s[i] == '/')
            {
                // "/>" ends the tag; a "/" before anything else is ignored.
                i++;
                continue;
            }
            // The name: its first character whatever it is (even "="), then up to a space, "/", ">" or "=".
            if (++count > MaxAttributes) return false;
            i++;
            while (i < n && !IsSpace(s[i]) && s[i] != '/' && s[i] != '>' && s[i] != '=') i++;
            while (i < n && IsSpace(s[i])) i++;
            if (i == n) return false;
            if (s[i] != '=') continue;
            i++;
            while (i < n && IsSpace(s[i])) i++;
            if (i == n) return false;
            if (s[i] is '"' or '\'')
            {
                var close = s.IndexOf(s[i], i + 1);
                if (close < 0) return false;
                i = close + 1;
            }
            else
            {
                // Unquoted: up to a space or ">" (a ">" right after "=" is an empty value and ends the tag).
                while (i < n && !IsSpace(s[i]) && s[i] != '>') i++;
            }
        }
    }

    /// <summary>What ends a tag name.</summary>
    private static bool IsDelimiter(char c) => IsSpace(c) || c is '/' or '>';

    /// <summary>The tokenizer's whitespace; a carriage return counts because the parser turns it into a line feed first.</summary>
    private static bool IsSpace(char c) => c is ' ' or '\t' or '\n' or '\f' or '\r';
}
