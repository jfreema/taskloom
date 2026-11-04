using Microsoft.EntityFrameworkCore;
using Taskloom.Api.Tests.Support;
using Taskloom.Infrastructure;

namespace Taskloom.Api.Tests;

/// <summary>Spec 5.7: what the description sanitizer keeps and removes, and the plain-text copy.</summary>
public class RichTextTests(TaskloomFactory factory) : IClassFixture<TaskloomFactory>
{
    private const string LinkAttributes = """target="_blank" rel="noopener noreferrer nofollow" """;

    [Fact]
    public void Sanitizer_strips_scripts_handlers_styles_images_and_javascript_links()
    {
        var dirty =
            """<p onclick="alert(1)" style="color:red" class="lead" id="x">Hi <script>alert(2)</script>""" +
            """<img src="https://example.com/a.png" onerror="alert(3)"><iframe src="https://example.com"></iframe>""" +
            """<a href="javascript:alert(4)" onmouseover="alert(5)">one</a> <a href=" JaVaScRiPt:alert(6)">two</a>""" +
            """<a href="data:text/html;base64,PHNjcmlwdD4=">three</a><style>p { color: red }</style></p>""" +
            """<svg><script>alert(7)</script></svg><object data="x.swf"></object><form action="https://example.com"><button>Go</button></form>""" +
            """<input type="text" value="x"><input><input type="checkbox" onchange="alert(8)"><span style="background:url(javascript:alert(9))">s</span>""";

        var clean = RichText.Sanitize(dirty);

        foreach (var banned in new[] { "script", "alert", "onclick", "onerror", "onmouseover", "onchange", "style", "class", "id=",
                     "<img", "<iframe", "javascript", "data:", "<svg", "<object", "<form", "<button", "value=", "type=\"text\"" })
            Assert.DoesNotContain(banned, clean, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            $"""<p>Hi <a {LinkAttributes.TrimEnd()}>one</a> <a {LinkAttributes.TrimEnd()}>two</a><a {LinkAttributes.TrimEnd()}>three</a></p>""" +
            """<input type="checkbox"><span>s</span>""",
            clean);

        Assert.Equal("", RichText.Sanitize(null));
        Assert.Equal("", RichText.Sanitize("   "));
        Assert.Equal("", RichText.Sanitize("<p></p>"));
        Assert.Equal("", RichText.Sanitize("<script>alert(1)</script>"));
    }

    [Fact]
    public async Task Sanitizer_keeps_tiptap_markup_enforces_per_tag_attributes_and_derives_capped_text()
    {
        // What Tiptap's StarterKit, Link and TaskList write survives unchanged (boolean attributes are written as ="").
        var tiptap =
            """<h2>Outline</h2><h3>Details</h3><p><strong>b</strong> <b>b2</b> <em>i</em> <i>i2</i> <s>s</s> <del>d</del> <u>u</u> <code>c</code><br>next</p>""" +
            """<p><a href="https://example.com/a?b=1&amp;c=2" target="_blank" rel="noopener noreferrer nofollow">web</a> <a href="mailto:ana@example.com">mail</a></p>""" +
            """<ul><li><p>bullet</p></li></ul><ol><li><p>numbered</p></li></ol><blockquote><p>quote</p></blockquote><pre><code>x = 1</code></pre><hr>""" +
            """<ul data-type="taskList"><li data-type="taskItem" data-checked="true"><label><input type="checkbox" checked="checked"><span></span></label><div><p>Welcome page</p></div></li>""" +
            """<li data-type="taskItem" data-checked="false"><label><input type="checkbox"><span></span></label><div><p>First task walkthrough</p></div></li></ul>""";
        Assert.Equal(
            """<h2>Outline</h2><h3>Details</h3><p><strong>b</strong> <b>b2</b> <em>i</em> <i>i2</i> <s>s</s> <del>d</del> <u>u</u> <code>c</code><br>next</p>""" +
            $"""<p><a href="https://example.com/a?b=1&amp;c=2" {LinkAttributes.TrimEnd()}>web</a> <a href="mailto:ana@example.com" {LinkAttributes.TrimEnd()}>mail</a></p>""" +
            """<ul><li><p>bullet</p></li></ul><ol><li><p>numbered</p></li></ol><blockquote><p>quote</p></blockquote><pre><code>x = 1</code></pre><hr>""" +
            """<ul data-type="taskList"><li data-type="taskItem" data-checked="true"><label><input type="checkbox" checked="checked"><span></span></label><div><p>Welcome page</p></div></li>""" +
            """<li data-type="taskItem" data-checked="false"><label><input type="checkbox"><span></span></label><div><p>First task walkthrough</p></div></li></ul>""",
            RichText.Sanitize(tiptap));

        // Each allowed attribute only on its own tags: href on a; data-type on ul and li; data-checked on li; type, checked, disabled on input.
        var misplaced =
            """<p href="https://example.com" data-type="taskList" data-checked="true" type="checkbox">p</p>""" +
            """<ul data-type="taskList" data-checked="true" href="https://example.com"><li data-type="taskItem" data-checked="true" type="checkbox" checked disabled>li</li></ul>""" +
            """<a href="https://example.com" data-type="x" type="button" checked target="_self" rel="opener">a</a>""" +
            """<input type="checkbox" checked disabled href="https://example.com" data-checked="true" data-type="x" name="n">""" +
            """<div data-type="x" data-checked="true">d</div><span data-checked="false" data-other="1" title="t">s</span><label for="x">l</label>""";
        Assert.Equal(
            """<p>p</p><ul data-type="taskList"><li data-type="taskItem" data-checked="true">li</li></ul>""" +
            $"""<a href="https://example.com" {LinkAttributes.TrimEnd()}>a</a><input type="checkbox" checked="" disabled="">""" +
            """<div>d</div><span>s</span><label>l</label>""",
            RichText.Sanitize(misplaced));

        // The plain-text copy: one line per block, whitespace collapsed, entities decoded, trimmed, capped at 10,000.
        Assert.Equal("Outline\nWelcome page\nFirst task walkthrough", RichText.ToPlainText(RichText.Sanitize(DevSeed.OnboardingDescription)));
        Assert.Equal("a b\nc & d <e>\nline\nbreak", RichText.ToPlainText("<p>  a \n  b </p><p>c&nbsp;&amp; d &lt;e&gt;</p><p>line<br>break</p>"));
        Assert.Equal("first\n\nthird", RichText.ToPlainText("<p>first</p><p></p><p>third</p>"));
        Assert.Equal("", RichText.ToPlainText("<p> </p><hr>"));
        Assert.Equal(10_000, RichText.ToPlainText($"<p>{new string('a', 12_000)}</p>").Length);

        // Through the API: create stores sanitized HTML and derives the plain-text copy; hasDescription follows the text.
        var user = await factory.NewUserAsync();
        var task = await user.Client.CreateTaskAsync(user.List.Id,
            new { title = "Described", description = """<p onclick="x()">Hello <script>bad()</script><a href="javascript:bad()">there</a></p>""" + $"<p>{new string('z', 12_000)}</p>" });
        Assert.True(task.HasDescription);
        var detail = await user.Client.TaskDetailAsync(task.Id);
        Assert.StartsWith($"""<p>Hello <a {LinkAttributes.TrimEnd()}>there</a></p><p>zzz""", detail.Description);
        using (var scope = factory.Services.CreateScope())
        {
            var text = await scope.ServiceProvider.GetRequiredService<TaskloomDbContext>().Tasks
                .Where(t => t.Id == task.Id).Select(t => t.DescriptionText).SingleAsync();
            Assert.Equal(10_000, text.Length);
            Assert.StartsWith("Hello there\nzzz", text);
        }

        var empty = await user.Client.CreateTaskAsync(user.List.Id, new { title = "Only markup", description = "<p></p><p> </p>" });
        Assert.False(empty.HasDescription);
    }
}
