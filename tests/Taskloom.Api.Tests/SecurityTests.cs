using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.EntityFrameworkCore;
using Taskloom.Api.Auth;
using Taskloom.Api.Tests.Support;
using Taskloom.Application;

namespace Taskloom.Api.Tests;

/// <summary>
/// Regression tests for the findings of the security review: each one failed (or took the server down) before its fix.
/// </summary>
public class SecurityTests(TaskloomFactory factory) : IClassFixture<TaskloomFactory>
{
    private static string Repeat(string s, int n) => string.Concat(Enumerable.Repeat(s, n));

    // ---- Positions ----

    [Fact]
    public async Task Positions_near_the_largest_double_never_become_infinite()
    {
        // The midpoint of two huge positions overflowed to Infinity, which JSON can't write: the move and every later
        // read of the list (and of the workspace's tasks) answered 500, for every member.
        Assert.True(double.IsFinite(Positions.Between(double.MaxValue, double.MaxValue)));
        Assert.InRange(Positions.Between(1e308, 1.7e308), 1.0000001e308, 1.6999999e308);
        Assert.Equal(1536, Positions.Between(1024, 2048));
        Assert.Equal(-1.5, Positions.Between(-2, -1));

        var user = await factory.NewUserAsync();
        var a = await user.Client.CreateTaskAsync(user.List.Id, new { title = "A" });
        var b = await user.Client.CreateTaskAsync(user.List.Id, new { title = "B" });
        var c = await user.Client.CreateTaskAsync(user.List.Id, new { title = "C" });
        await user.Client.PatchTaskAsync(a.Id, new { position = 1e308 });
        await user.Client.PatchTaskAsync(b.Id, new { position = 1.7e308 });

        var moved = await user.Client.MoveTaskAsync(c.Id, new { statusId = c.StatusId, beforeTaskId = a.Id, afterTaskId = b.Id });
        Assert.True(double.IsFinite(moved.Position));
        Assert.Equal(["A", "C", "B"], (await user.Client.ListTasksAsync(user.List.Id)).Select(t => t.Title));
        Assert.Equal(3, (await user.Client.WorkspaceTasksAsync(user.Workspace.Id, "?scope=all")).Tasks.Count);
    }

    // ---- Descriptions ----

    public static TheoryData<string, string> ParserBombs => new()
    {
        // 39,000 nested divs overflowed the stack while the sanitizer walked them: the whole process ended.
        { "deep nesting", Repeat("<div>", 39_000) + "x" },
        // Every <li> searches the open elements: 15 seconds of CPU.
        { "nested lists", Repeat("<ul><li>", 20_000) + "x" },
        // Bold left open in a closed paragraph is cloned into each next one: 12 KB of this became 266 MB of nodes.
        { "reopened formatting", "<p>" + string.Concat(Enumerable.Range(0, 1_000).Select(i => $"<b x{i}>")) + "</p>" + Repeat("<p>x</p>", 1_000) },
        // Each attribute is compared with every other one: 10 seconds for one tag.
        { "many attributes", "<p " + string.Join(" ", Enumerable.Range(0, 25_000).Select(i => $"a{i}")) + ">x</p>" },
        { "an attribute value hiding the tag's end", "<p title=\">\" " + string.Join(" ", Enumerable.Range(0, 25_000).Select(i => $"a{i}")) + ">x</p>" },
        // An end tag inside a comment closes nothing for the parser.
        { "end tags hidden in comments", Repeat("<div>", 100) + Repeat("<b><!-- > </b> -->", 500) + Repeat("x</div>", 100) },
        { "misnested formatting", Repeat("<b><i>a</b>", 18_000) },
    };

    [Theory]
    [MemberData(nameof(ParserBombs))]
    public async Task Description_markup_that_would_overwhelm_the_parser_is_refused(string shape, string html)
    {
        Assert.True(html.Length <= RichText.MaxHtmlLength, shape);
        var user = await factory.NewUserAsync();
        var task = await user.Client.CreateTaskAsync(user.List.Id, new { title = "Victim", description = "<p>Before</p>" });

        var res = await user.Client.PatchAsJsonAsync($"/api/tasks/{task.Id}", new { description = html });
        Assert.Equal(RichText.TooComplexMessage, await res.ProblemAsync(HttpStatusCode.BadRequest));
        Assert.Equal("<p>Before</p>", (await user.Client.TaskDetailAsync(task.Id)).Description);

        res = await user.Client.PostAsJsonAsync($"/api/lists/{user.List.Id}/tasks", new { title = "New", description = html });
        Assert.Equal(RichText.TooComplexMessage, await res.ProblemAsync(HttpStatusCode.BadRequest));
        Assert.Single(await user.Client.ListTasksAsync(user.List.Id));
    }

    [Fact]
    public void Short_markup_of_any_shape_is_still_cleaned_and_long_editor_markup_is_kept()
    {
        // Under 1,024 characters nothing is refused: the sanitizer cleans odd markup as before.
        Assert.Equal("<p><b>bold</b></p><p><b>next</b></p>", RichText.Sanitize("<p><b>bold<p>next"));
        Assert.Equal(Repeat("<div>", 150) + "x" + Repeat("</div>", 150), RichText.Sanitize(Repeat("<div>", 150) + "x"));

        // Long documents in the editor's markup are kept, and scripts, styles and images in them are still removed.
        var unit =
            """<h2>Plan</h2><p>Some <strong>bold</strong>, <em>italic <code>code</code></em> and <a href="https://example.com/?a=1&amp;b=2">a link</a>.<br>Next</p>""" +
            """<ul data-type="taskList"><li data-type="taskItem" data-checked="true"><label><input type="checkbox" checked="checked"><span></span></label><div><p>Done</p></div></li></ul>""" +
            """<ul><li><p>one</p><ul><li><p>nested</p></li></ul></li></ul><blockquote><p>quote</p></blockquote><pre><code>if (a &lt; b) {}</code></pre><hr>""";
        var doc = Repeat(unit, 150_000 / unit.Length) +
                  """<p onclick="steal()">Hi<script>if (a < b) steal("</p>");</script><style>p > b { }</style><img src=x onerror=steal()></p>""";
        var clean = RichText.Sanitize(doc);
        Assert.DoesNotContain("steal", clean);
        Assert.DoesNotContain("<script", clean);
        Assert.DoesNotContain("<style", clean);
        Assert.EndsWith("<pre><code>if (a &lt; b) {}</code></pre><hr><p>Hi</p>", clean);
        Assert.Contains("""<a href="https://example.com/?a=1&amp;b=2" target="_blank" rel="noopener noreferrer nofollow">a link</a>""", clean);
        Assert.Equal(150_000 / unit.Length, clean.Split("<h2>Plan</h2>").Length - 1);

        // The plain-text copy walks the tree without recursion: no depth overflows it.
        Assert.Equal("x", RichText.ToPlainText(Repeat("<span>", 50_000) + "x"));
    }

    [Fact]
    public async Task Descriptions_are_limited_after_sanitizing_too()
    {
        // Every link gains target and rel, so 200,000 characters of links came back nearly three times as long and were stored.
        var links = "<p>" + Repeat("""<a href="https://e.com">x</a>""", 6_500) + "</p>";
        Assert.True(links.Length <= RichText.MaxHtmlLength);
        Assert.Equal(RichText.TooLongMessage, Assert.Throws<ProblemException>(() => RichText.Sanitize(links)).Message);

        var user = await factory.NewUserAsync();
        var task = await user.Client.CreateTaskAsync(user.List.Id, new { title = "Links" });
        var res = await user.Client.PatchAsJsonAsync($"/api/tasks/{task.Id}", new { description = links });
        Assert.Equal("Descriptions can be up to 200,000 characters.", await res.ProblemAsync(HttpStatusCode.BadRequest));
        Assert.Equal("", (await user.Client.TaskDetailAsync(task.Id)).Description);
    }

    // ---- Request sizes ----

    [Fact]
    public void Id_lists_are_read_in_linear_time()
    {
        // A list of ids was deduplicated with List.Contains: 40,000 ids took 1.5 s, and 30 MB of them minutes.
        var ids = Enumerable.Range(0, 200_000).Select(_ => Guid.NewGuid()).ToList();
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(ids.Concat(ids.Take(1_000))));
        var watch = Stopwatch.StartNew();
        var read = Json.ReadIds(doc.RootElement, "ids");
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), $"took {watch.Elapsed}");
        Assert.Equal(ids, read);
    }

    [Fact]
    public async Task Request_bodies_over_their_limit_are_refused_before_they_are_read()
    {
        var user = await factory.NewUserAsync();
        var task = await user.Client.CreateTaskAsync(user.List.Id, new { title = "Sizes" });

        // JSON bodies: 4 MiB at most (a full-length description is far less), not the server's default of 30 MB.
        using var json = new StringContent(JsonSerializer.Serialize(new { title = new string('x', 4 * 1024 * 1024) }), Encoding.UTF8, "application/json");
        var res = await user.Client.PatchAsync($"/api/tasks/{task.Id}", json);
        Assert.Equal(RequestLimits.TooLargeMessage, await res.ProblemAsync(HttpStatusCode.RequestEntityTooLarge));

        // Uploads: 25 MB plus room for the form's own lines. A larger one was buffered whole before the handler saw it.
        using (var form = new MultipartFormDataContent { { new ByteArrayContent(new byte[RequestLimits.UploadBodyBytes]), "file", "big.bin" } })
        {
            res = await user.Client.PostAsync($"/api/tasks/{task.Id}/attachments", form);
            Assert.Equal(RequestLimits.FileTooLargeMessage, await res.ProblemAsync(HttpStatusCode.RequestEntityTooLarge));
        }
        // Just over 25 MB fits the room and still gets the handler's own answer.
        using (var form = new MultipartFormDataContent { { new ByteArrayContent(new byte[Limits.AttachmentBytes + 1]), "file", "big.bin" } })
        {
            res = await user.Client.PostAsync($"/api/tasks/{task.Id}/attachments", form);
            Assert.Equal("Files can be up to 25 MB.", await res.ProblemAsync(HttpStatusCode.BadRequest));
        }
        Assert.Empty((await user.Client.TaskDetailAsync(task.Id)).Attachments);
        Assert.Equal(0, await factory.DbAsync(db => db.Attachments.CountAsync(a => a.TaskId == task.Id)));
    }

    // ---- Attachments ----

    /// <summary>A multipart body written byte by byte, so the part's name and type reach the server exactly as given.</summary>
    private static async Task<AttachmentDto> UploadRawAsync(HttpClient client, Guid taskId, string fileName, string contentType)
    {
        var boundary = "taskloom-" + Guid.NewGuid().ToString("N");
        var body = Encoding.UTF8.GetBytes(
            $"--{boundary}\r\nContent-Disposition: form-data; name=\"file\"; filename=\"{fileName}\"\r\nContent-Type: {contentType}\r\n\r\nhello\r\n--{boundary}--\r\n");
        using var content = new ByteArrayContent(body);
        content.Headers.TryAddWithoutValidation("Content-Type", $"multipart/form-data; boundary={boundary}");
        return await (await client.PostAsync($"/api/tasks/{taskId}/attachments", content)).ReadAsync<AttachmentDto>();
    }

    [Fact]
    public async Task Attachment_types_that_would_break_downloads_and_names_that_spoof_are_cleaned()
    {
        var user = await factory.NewUserAsync();
        var task = await user.Client.CreateTaskAsync(user.List.Id, new { title = "Files" });

        // A type beyond ASCII was stored and sent back as Content-Type: every download of the file was a 500.
        var accented = await UploadRawAsync(user.Client, task.Id, "notes.txt", "text/plain; charset=ü");
        Assert.Equal("application/octet-stream", accented.ContentType);
        var download = await user.Client.GetAsync($"/api/attachments/{accented.Id}");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal("hello", await download.Content.ReadAsStringAsync());

        // Something that isn't one media type isn't kept either; a real one is.
        Assert.Equal("application/octet-stream", (await UploadRawAsync(user.Client, task.Id, "x.png", "image/png, text/html")).ContentType);
        Assert.Equal("application/octet-stream", (await UploadRawAsync(user.Client, task.Id, "x.bin", "not a type")).ContentType);
        Assert.Equal("image/png", (await UploadRawAsync(user.Client, task.Id, "x.png", "image/png")).ContentType);
        Assert.Equal("text/plain; charset=utf-8", (await UploadRawAsync(user.Client, task.Id, "x.txt", "text/plain; charset=utf-8")).ContentType);

        // A right-to-left override shows "invoice[RLO]gpj.exe" as "invoiceexe.jpg"; the joiner inside an emoji stays.
        Assert.Equal("invoicegpj.exe", (await UploadRawAsync(user.Client, task.Id, "invoice\u202Egpj.exe", "application/octet-stream")).FileName);
        Assert.Equal("abc.txt", (await UploadRawAsync(user.Client, task.Id, "a\u2066b\u2069c.txt", "text/plain")).FileName);
        Assert.Equal("\U0001F469\u200D\U0001F4BB.txt", (await UploadRawAsync(user.Client, task.Id, "\U0001F469\u200D\U0001F4BB.txt", "text/plain")).FileName);
    }

    // ---- Headers ----

    [Fact]
    public async Task Every_response_forbids_content_sniffing()
    {
        var user = await factory.NewUserAsync();
        using var anonymous = factory.CreateClient();
        foreach (var res in new[]
                 {
                     await user.Client.GetAsync("/api/me"), await user.Client.GetAsync("/api/nope"), await user.Client.PostAsJsonAsync("/api/workspaces", new { name = "" }),
                     await anonymous.GetAsync("/api/me"), await anonymous.GetAsync("/healthz"), await anonymous.GetAsync("/auth/providers"),
                 })
            Assert.Equal("nosniff", res.Headers.GetValues("X-Content-Type-Options").Single());
    }

    // ---- Sign-in ----

    [Theory]
    [InlineData("/lists/\u00E9", "/lists/%C3%A9")]
    [InlineData("/t/1?q=\u65E5\u672C#top", "/t/1?q=%E6%97%A5%E6%9C%AC#top")]
    [InlineData("/x/\U0001F600", "/x/%F0%9F%98%80")]
    [InlineData("/a\u2028b", "/a%E2%80%A8b")]
    [InlineData("/plain/path?x=1&y=%2F", "/plain/path?x=1&y=%2F")]
    [InlineData("//evil.example/\u00E9", "/")]
    public void Return_urls_beyond_ascii_are_percent_encoded_so_the_redirect_header_can_carry_them(string raw, string expected) =>
        Assert.Equal(expected, ReturnUrls.Safe(raw));

    [Fact]
    public void A_return_url_with_half_a_surrogate_pair_becomes_the_root() =>
        Assert.Equal("/", ReturnUrls.Safe("/a" + '\ud800' + "b"));

    // ---- CSRF ----

    [Fact]
    public async Task Every_endpoint_that_changes_state_refuses_a_request_without_the_csrf_header()
    {
        // Not a list of today's endpoints: every endpoint mapped now or later, so a new one can't miss the filter.
        var user = await factory.NewUserAsync();
        user.Client.DefaultRequestHeaders.Remove(RequireTaskloomHeader.Header);
        var checkedRoutes = new List<string>();
        foreach (var endpoint in factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>())
        {
            var methods = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? [];
            foreach (var method in methods.Where(m => !HttpMethods.IsGet(m) && !HttpMethods.IsHead(m)))
            {
                var path = "/" + string.Join('/', endpoint.RoutePattern.PathSegments.Select(segment => segment.Parts.Single() switch
                {
                    RoutePatternLiteralPart literal => literal.Content,
                    RoutePatternParameterPart => Guid.NewGuid().ToString(),
                    var other => throw new InvalidOperationException($"Unexpected route part {other} in {endpoint.RoutePattern.RawText}"),
                }));
                using var req = new HttpRequestMessage(new HttpMethod(method), path);
                req.Content = endpoint.Metadata.GetMetadata<IAcceptsMetadata>()?.ContentTypes.Contains("multipart/form-data") == true
                    ? new MultipartFormDataContent { { new ByteArrayContent("x"u8.ToArray()), "file", "x.txt" } }
                    : JsonContent.Create(new { });
                var res = await user.Client.SendAsync(req);
                Assert.True(res.StatusCode == HttpStatusCode.BadRequest && (await res.Content.ReadAsStringAsync()).Contains("Missing X-Taskloom header."),
                    $"{method} {endpoint.RoutePattern.RawText} answered {(int)res.StatusCode}: {await res.Content.ReadAsStringAsync()}");
                checkedRoutes.Add($"{method} {endpoint.RoutePattern.RawText}");
            }
        }
        Assert.Contains("POST /api/tasks/{id:guid}/attachments", checkedRoutes);
        Assert.Contains("DELETE /api/tasks/{id:guid}", checkedRoutes);
        Assert.Contains("POST /auth/logout", checkedRoutes);
        Assert.True(checkedRoutes.Count >= 33, string.Join(", ", checkedRoutes));
    }
}
