using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Taskloom.Api.Tests.Support;
using Taskloom.Domain;

namespace Taskloom.Api.Tests;

/// <summary>
/// Text PostgreSQL can't store (a NUL character, half a surrogate pair) is a 400 wherever it is sent, never a 500;
/// the API's own cuts (activity values, guessed names, file names) never split a surrogate pair; and an
/// upload whose row isn't saved leaves no file behind.
/// </summary>
public class InputSafetyTests(TaskloomFactory factory) : IClassFixture<TaskloomFactory>
{
    private const string Unreadable = "The request couldn't be read. Check the body and its field types.";
    private const string Emoji = "\U0001F600"; // two UTF-16 units
    private const string Nul = "a\u0000b";

    private static readonly UTF8Encoding Strict = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private static bool IsWellFormed(string s)
    {
        try
        {
            Strict.GetByteCount(s);
            return true;
        }
        catch (EncoderFallbackException)
        {
            return false;
        }
    }

    private static string Emojis(int count) => string.Concat(Enumerable.Repeat(Emoji, count));

    private static StringContent RawJson(string json) => new(json, Encoding.UTF8, "application/json");

    /// <summary>A multipart body written by hand, so the part's headers can hold what HttpClient would refuse to send.</summary>
    private static ByteArrayContent RawUpload(string contentDisposition, string contentType)
    {
        var body = Encoding.UTF8.GetBytes(
            $"--b\r\nContent-Disposition: form-data; name=\"file\"; {contentDisposition}\r\nContent-Type: {contentType}\r\n\r\nhello\r\n--b--\r\n");
        var content = new ByteArrayContent(body);
        content.Headers.TryAddWithoutValidation("Content-Type", "multipart/form-data; boundary=b");
        return content;
    }

    [Fact]
    public async Task Nul_characters_are_a_400_and_nothing_is_saved()
    {
        var user = await factory.NewUserAsync();
        var c = user.Client;
        var task = await c.CreateTaskAsync(user.List.Id, new { title = "Kept" });
        var anon = factory.CreateClient();
        anon.DefaultRequestHeaders.Add("X-Taskloom", "1");
        Func<Task<HttpResponseMessage>>[] requests =
        [
            () => c.PostAsJsonAsync($"/api/lists/{user.List.Id}/tasks", new { title = Nul }),
            () => c.PatchAsJsonAsync($"/api/tasks/{task.Id}", new { title = Nul }),
            () => c.PostAsJsonAsync($"/api/tasks/{task.Id}/comments", new { body = Nul }),
            () => c.PostAsJsonAsync($"/api/workspaces/{user.Workspace.Id}/tags", new { name = Nul }),
            () => c.PatchAsJsonAsync($"/api/lists/{user.List.Id}", new { name = Nul }),
            () => c.PatchAsJsonAsync("/api/me", new { displayName = Nul }),
            () => c.GetAsync($"/api/workspaces/{user.Workspace.Id}/tasks?q=a%00b"),
            () => anon.PostAsJsonAsync("/auth/dev-login", new { email = Builders.NewEmail(), displayName = Nul }),
        ];
        foreach (var send in requests)
            Assert.Equal(Unreadable, await (await send()).ProblemAsync(HttpStatusCode.BadRequest));

        var detail = await c.TaskDetailAsync(task.Id);
        Assert.Equal("Kept", detail.Task.Title);
        Assert.Empty(detail.Comments);
        Assert.Single(await c.ListTasksAsync(user.List.Id));
        Assert.Empty(await c.TagsAsync(user.Workspace.Id));
        Assert.Equal("Tasks", (await c.ListAsync(user.List.Id)).Name);
        Assert.Equal(user.Me.DisplayName, (await (await c.GetAsync("/api/me")).ReadAsync<UserDto>()).DisplayName);

        // A description is HTML, where the parser drops NUL, so it saves.
        await c.PatchTaskAsync(task.Id, new { description = "<p>a\u0000b</p>" });
        Assert.Equal("<p>ab</p>", (await c.TaskDetailAsync(task.Id)).Description);
    }

    [Fact]
    public async Task Half_a_surrogate_pair_is_a_400_and_nothing_is_saved()
    {
        var user = await factory.NewUserAsync();
        var c = user.Client;
        var task = await c.CreateTaskAsync(user.List.Id, new { title = "Kept" });
        // Raw strings: "\ud83d" reaches the API as a JSON escape of half a pair.
        foreach (var json in new[] { """{"title":"\ud83d"}""", """{"description":"\ude00"}""", """{"priority":"\ud83d"}""", """{"\ud83d":1}""" })
            Assert.Equal(Unreadable, await (await c.PatchAsync($"/api/tasks/{task.Id}", RawJson(json))).ProblemAsync(HttpStatusCode.BadRequest));
        var bulk = $$"""{"updates":[{"id":"{{task.Id}}","priority":"\ud83d","dueDate":"2026-11-04"}]}""";
        Assert.Equal(Unreadable, await (await c.PostAsync("/api/tasks/bulk", RawJson(bulk))).ProblemAsync(HttpStatusCode.BadRequest));
        var create = await c.PostAsync($"/api/lists/{user.List.Id}/tasks", RawJson("""{"title":"\ud83d"}"""));
        Assert.Equal(Unreadable, await create.ProblemAsync(HttpStatusCode.BadRequest));

        var left = Assert.Single(await c.ListTasksAsync(user.List.Id));
        Assert.Equal(("Kept", TaskPriority.None, (DateOnly?)null), (left.Title, left.Priority, left.DueDate));
    }

    [Theory]
    [InlineData("abc", 5, "abc")]
    [InlineData("abcdef", 3, "abc")]
    [InlineData("a\U0001F600b", 2, "a")] // the cut would fall inside the emoji
    [InlineData("a\U0001F600b", 3, "a\U0001F600")]
    [InlineData("\U0001F600", 0, "")]
    public void Truncate_never_splits_a_surrogate_pair(string value, int max, string expected) =>
        Assert.Equal(expected, Text.Truncate(value, max));

    [Fact]
    public void Activity_values_and_guessed_names_are_cut_between_pairs()
    {
        var clipped = ActivityValues.Clip("a" + Emojis(300))!;
        Assert.True(IsWellFormed(clipped));
        Assert.Equal("a" + Emojis(249) + "…", clipped);

        var guessed = NameGuess.FromEmail("a" + Emojis(101) + "@example.com");
        Assert.True(IsWellFormed(guessed));
        Assert.Equal("A" + Emojis(99), guessed);
    }

    [Fact]
    public async Task Names_cut_by_the_api_stay_valid_text()
    {
        var user = await factory.NewUserAsync();
        // An invite guesses the name from the address: "A" and 101 emoji is 203 characters, cut to 199, not 200.
        var member = await user.Client.InviteAsync(user.Workspace.Id, "a" + Emojis(101) + "@example.com", WorkspaceRole.Member);
        Assert.Equal("A" + Emojis(99), member.DisplayName);

        // A 405-character file name keeps its extension and loses whole emoji: 1 + 2 × 125 + 4 = 255.
        var task = await user.Client.CreateTaskAsync(user.List.Id, new { title = "Files" });
        var att = await user.Client.UploadAsync(task.Id, "a" + Emojis(200) + ".txt", "hello"u8.ToArray());
        Assert.Equal("a" + Emojis(125) + ".txt", att.FileName);
        var added = Assert.Single((await user.Client.TaskDetailAsync(task.Id)).Activity, a => a.Kind == TaskActivityKind.AttachmentAdded);
        Assert.Equal(att.FileName, added.To);
    }

    [Fact]
    public async Task Attachment_names_and_types_lose_control_characters()
    {
        var user = await factory.NewUserAsync();
        var task = await user.Client.CreateTaskAsync(user.List.Id, new { title = "Files" });
        var res = await user.Client.PostAsync($"/api/tasks/{task.Id}/attachments", RawUpload("filename*=UTF-8''a%00b%0A.txt", "text/plain"));
        var att = await res.ReadAsync<AttachmentDto>();
        Assert.Equal(("ab.txt", "text/plain"), (att.FileName, att.ContentType));

        res = await user.Client.PostAsync($"/api/tasks/{task.Id}/attachments", RawUpload("filename=\"x.txt\"", "text/pl\u0001ain"));
        att = await res.ReadAsync<AttachmentDto>();
        Assert.Equal(("x.txt", "application/octet-stream"), (att.FileName, att.ContentType));
        Assert.Equal("hello", await user.Client.GetStringAsync($"/api/attachments/{att.Id}"));
    }

    [Fact]
    public async Task An_upload_whose_row_is_not_saved_leaves_no_file()
    {
        var user = await factory.NewUserAsync();
        var task = await user.Client.CreateTaskAsync(user.List.Id, new { title = "Files" });
        var root = factory.Services.GetRequiredService<IConfiguration>()["FileStore:Local:Root"]!;
        var folder = Path.Combine(root, "tasks", task.Id.ToString());
        // The database refuses the row after the file was stored.
        await factory.DbAsync(db => db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION test_refuse_attachment() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'refused by the test'; END $$;
            CREATE TRIGGER test_refuse_attachment BEFORE INSERT ON task_attachments FOR EACH ROW EXECUTE FUNCTION test_refuse_attachment();
            """));
        try
        {
            using var form = new MultipartFormDataContent { { new ByteArrayContent("hello"u8.ToArray()), "file", "lost.txt" } };
            var res = await user.Client.PostAsync($"/api/tasks/{task.Id}/attachments", form);
            Assert.Equal(HttpStatusCode.InternalServerError, res.StatusCode);
        }
        finally
        {
            await factory.DbAsync(db => db.Database.ExecuteSqlRawAsync("""
                DROP TRIGGER test_refuse_attachment ON task_attachments;
                DROP FUNCTION test_refuse_attachment();
                """));
        }
        Assert.Empty(Directory.Exists(folder) ? Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories) : []);
        Assert.Equal(0, (await user.Client.TaskDetailAsync(task.Id)).Task.AttachmentCount);

        // Uploads work again once the database takes the row.
        var att = await user.Client.UploadAsync(task.Id, "kept.txt", "hello"u8.ToArray());
        Assert.Single(Directory.EnumerateFiles(folder));
        Assert.Equal("hello", await user.Client.GetStringAsync($"/api/attachments/{att.Id}"));
    }
}
