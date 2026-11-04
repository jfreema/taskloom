using System.Net;
using System.Net.Http.Headers;
using Taskloom.Api.Tests.Support;
using Taskloom.Domain;

namespace Taskloom.Api.Tests;

/// <summary>Attachment downloads, caching and clean-up (spec 5.4 #40–#42, 5.5 Attachments).</summary>
public class AttachmentTests(TaskloomFactory factory) : IClassFixture<TaskloomFactory>
{
    private const string CacheControl = "private, max-age=31536000, immutable";

    [Fact]
    public async Task Raster_images_are_inline_with_safe_headers_and_revalidate_with_etag()
    {
        var user = await factory.NewUserAsync();
        var task = await user.Client.CreateTaskAsync(user.List.Id, new { title = "Pictures" });
        var bytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3 };

        foreach (var (fileName, contentType) in new[]
                 { ("photo.png", "image/png"), ("photo.jpg", "image/jpeg"), ("anim.gif", "image/gif"), ("pic.webp", "image/webp"), ("upper.png", "IMAGE/PNG") })
        {
            var a = await user.Client.UploadAsync(task.Id, fileName, bytes, contentType);
            Assert.Equal((fileName, contentType, bytes.LongLength, user.Me.Id), (a.FileName, a.ContentType, a.SizeBytes, a.UploadedById));

            var res = await user.Client.GetAsync($"/api/attachments/{a.Id}");
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            Assert.Equal(bytes, await res.Content.ReadAsByteArrayAsync());
            Assert.Equal(contentType, res.Content.Headers.ContentType?.MediaType, ignoreCase: true);
            var disposition = res.Content.Headers.ContentDisposition!;
            Assert.Equal(("inline", fileName), (disposition.DispositionType, disposition.FileName?.Trim('"')));
            AssertSafeAndCacheable(res, a.Id);

            // Revalidation: the id is a strong ETag; a matching If-None-Match (weak, strong or *) is a 304 with no body.
            foreach (var match in new[] { $"\"{a.Id}\"", $"W/\"{a.Id}\"", "*", $"\"{Guid.NewGuid()}\", \"{a.Id}\"" })
            {
                using var conditional = new HttpRequestMessage(HttpMethod.Get, $"/api/attachments/{a.Id}");
                conditional.Headers.TryAddWithoutValidation("If-None-Match", match);
                var notModified = await user.Client.SendAsync(conditional);
                Assert.Equal(HttpStatusCode.NotModified, notModified.StatusCode);
                Assert.Empty(await notModified.Content.ReadAsByteArrayAsync());
                Assert.Equal(CacheControl, notModified.Headers.NonValidated["Cache-Control"].ToString());
                Assert.Equal($"\"{a.Id}\"", notModified.Headers.NonValidated["ETag"].ToString());
            }
            // Another ETag gets the file again.
            using var stale = new HttpRequestMessage(HttpMethod.Get, $"/api/attachments/{a.Id}");
            stale.Headers.IfNoneMatch.Add(new EntityTagHeaderValue($"\"{Guid.NewGuid()}\""));
            var fresh = await user.Client.SendAsync(stale);
            Assert.Equal(HttpStatusCode.OK, fresh.StatusCode);
            Assert.Equal(bytes, await fresh.Content.ReadAsByteArrayAsync());
        }

        // A 304 is still only for members: someone else gets 404 whatever they send.
        var stranger = await factory.NewUserAsync();
        var first = (await user.Client.TaskDetailAsync(task.Id)).Attachments[0];
        using var probe = new HttpRequestMessage(HttpMethod.Get, $"/api/attachments/{first.Id}");
        probe.Headers.TryAddWithoutValidation("If-None-Match", "*");
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.Client.SendAsync(probe)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await user.Client.GetAsync($"/api/attachments/{Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task Other_files_download_and_deleting_a_task_removes_its_subtasks_and_stored_files()
    {
        var user = await factory.NewUserAsync();
        var parent = await user.Client.CreateTaskAsync(user.List.Id, new { title = "Parent" });
        var sub = await user.Client.CreateTaskAsync(user.List.Id, new { title = "Sub", parentTaskId = parent.Id });
        var keep = await user.Client.CreateTaskAsync(user.List.Id, new { title = "Keep" });

        // Everything that isn't a raster image downloads, SVG and HTML included, with the original name.
        var uploads = new List<AttachmentDto>();
        foreach (var (fileName, contentType) in new[]
                 {
                     ("notes.txt", "text/plain"), ("logo.svg", "image/svg+xml"), ("page.html", "text/html"),
                     ("report.pdf", "application/pdf"), ("Résumé ✓.txt", "text/plain"),
                 })
        {
            var content = System.Text.Encoding.UTF8.GetBytes($"<svg onload=alert(1)>{fileName}</svg>");
            var a = await user.Client.UploadAsync(parent.Id, fileName, content, contentType);
            uploads.Add(a);
            var res = await user.Client.GetAsync($"/api/attachments/{a.Id}");
            Assert.Equal(content, await res.Content.ReadAsByteArrayAsync());
            Assert.Equal(contentType, res.Content.Headers.ContentType?.MediaType);
            var disposition = res.Content.Headers.ContentDisposition!;
            Assert.Equal("attachment", disposition.DispositionType);
            Assert.Equal(fileName, disposition.FileNameStar ?? disposition.FileName?.Trim('"'));
            AssertSafeAndCacheable(res, a.Id);
        }
        // A path in the name is dropped, and an empty file is refused.
        var pathed = await user.Client.UploadAsync(sub.Id, "../../secrets/../sub-file.txt", "sub"u8.ToArray());
        Assert.Equal("sub-file.txt", pathed.FileName);
        var kept = await user.Client.UploadAsync(keep.Id, "kept.txt", "kept"u8.ToArray());
        var empty = new ByteArrayContent([]);
        empty.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        using (var form = new MultipartFormDataContent { { empty, "file", "empty.txt" } })
            Assert.Equal("The file is empty.", await (await user.Client.PostAsync($"/api/tasks/{parent.Id}/attachments", form)).ProblemAsync(HttpStatusCode.BadRequest));

        // The detail lists them oldest first; uploads and deletes are recorded.
        var detail = await user.Client.TaskDetailAsync(parent.Id);
        Assert.Equal(uploads, detail.Attachments);
        Assert.Equal(uploads.Count, detail.Task.AttachmentCount);
        Assert.Equal(uploads.Select(a => (TaskActivityKind.AttachmentAdded, (string?)null, (string?)a.FileName)),
            detail.Activity.Where(x => x.Kind == TaskActivityKind.AttachmentAdded).Select(x => (x.Kind, x.From, x.To)));

        // Deleting one attachment removes its row and its stored file.
        var first = uploads[0];
        Assert.True(File.Exists(StoredFile(first)));
        Assert.Equal(HttpStatusCode.NoContent, (await user.Client.DeleteAsync($"/api/attachments/{first.Id}")).StatusCode);
        Assert.False(File.Exists(StoredFile(first)));
        Assert.Equal(HttpStatusCode.NotFound, (await user.Client.GetAsync($"/api/attachments/{first.Id}")).StatusCode);
        detail = await user.Client.TaskDetailAsync(parent.Id);
        Assert.Equal(uploads.Count - 1, detail.Task.AttachmentCount);
        var removed = detail.Activity.Last();
        Assert.Equal((TaskActivityKind.AttachmentRemoved, first.FileName, (string?)null), (removed.Kind, removed.From, removed.To));

        // Deleting the task removes its subtasks and every stored file of both; other tasks keep theirs.
        var stored = uploads.Skip(1).Append(pathed).Select(StoredFile).ToList();
        Assert.All(stored, path => Assert.True(File.Exists(path), path));
        Assert.Equal(HttpStatusCode.NoContent, (await user.Client.DeleteAsync($"/api/tasks/{parent.Id}")).StatusCode);
        Assert.All(stored, path => Assert.False(File.Exists(path), path));
        Assert.Equal(HttpStatusCode.NotFound, (await user.Client.GetAsync($"/api/tasks/{parent.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await user.Client.GetAsync($"/api/tasks/{sub.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await user.Client.GetAsync($"/api/attachments/{pathed.Id}")).StatusCode);
        Assert.Equal([keep.Id], (await user.Client.ListTasksAsync(user.List.Id)).Select(t => t.Id));
        Assert.True(File.Exists(StoredFile(kept)));
        Assert.Equal("kept", await user.Client.GetStringAsync($"/api/attachments/{kept.Id}"));
        Assert.Equal(HttpStatusCode.NotFound, (await user.Client.DeleteAsync($"/api/tasks/{parent.Id}")).StatusCode);
    }

    [Fact]
    public async Task Deleting_a_list_space_or_workspace_removes_their_stored_files()
    {
        var user = await factory.NewUserAsync();
        async Task<AttachmentDto> FileOn(Guid listId, Guid? parentTaskId = null)
        {
            var task = await user.Client.CreateTaskAsync(listId, new { title = "With a file", parentTaskId });
            return await user.Client.UploadAsync(task.Id, "file.txt", "content"u8.ToArray());
        }
        var second = await user.Client.CreateListAsync(user.Space.Id, "Second");
        var space = await (await user.Client.PostAsJsonAsync($"/api/workspaces/{user.Workspace.Id}/spaces", new { name = "Gone" })).ReadAsync<SpaceDto>();
        var inSpace = await user.Client.CreateListAsync(space.Id, "In space");
        var workspace = await (await user.Client.PostAsJsonAsync("/api/workspaces", new { name = "Gone too" })).ReadAsync<WorkspaceTreeDto>();
        var otherSpace = await (await user.Client.PostAsJsonAsync($"/api/workspaces/{workspace.Id}/spaces", new { name = "There" })).ReadAsync<SpaceDto>();
        var inWorkspace = await user.Client.CreateListAsync(otherSpace.Id, "In workspace");

        var kept = await FileOn(user.List.Id);
        var onList = await FileOn(second.Id);
        var onSpace = await FileOn(inSpace.Id);
        var onSpaceSubtask = await FileOn(inSpace.Id, onSpace.TaskId);
        var onWorkspace = await FileOn(inWorkspace.Id);
        var all = new[] { kept, onList, onSpace, onSpaceSubtask, onWorkspace };
        Assert.All(all, a => Assert.True(File.Exists(StoredFile(a))));

        Assert.Equal(HttpStatusCode.NoContent, (await user.Client.DeleteAsync($"/api/lists/{second.Id}")).StatusCode);
        Assert.Equal([false, true, true, true], new[] { onList, onSpace, onSpaceSubtask, onWorkspace }.Select(a => File.Exists(StoredFile(a))));
        Assert.Equal(HttpStatusCode.NoContent, (await user.Client.DeleteAsync($"/api/spaces/{space.Id}")).StatusCode);
        Assert.Equal([false, false, true], new[] { onSpace, onSpaceSubtask, onWorkspace }.Select(a => File.Exists(StoredFile(a))));
        Assert.Equal(HttpStatusCode.NoContent, (await user.Client.DeleteAsync($"/api/workspaces/{workspace.Id}")).StatusCode);
        Assert.False(File.Exists(StoredFile(onWorkspace)));

        Assert.True(File.Exists(StoredFile(kept)));
        Assert.Equal("content", await user.Client.GetStringAsync($"/api/attachments/{kept.Id}"));
        foreach (var gone in all.Skip(1))
            Assert.Equal(HttpStatusCode.NotFound, (await user.Client.GetAsync($"/api/attachments/{gone.Id}")).StatusCode);
    }

    private string StoredFile(AttachmentDto a) => Path.Combine(factory.FilesRoot, "tasks", a.TaskId.ToString(), a.Id.ToString());

    private static void AssertSafeAndCacheable(HttpResponseMessage res, Guid attachmentId)
    {
        Assert.Equal("nosniff", res.Headers.NonValidated["X-Content-Type-Options"].ToString());
        Assert.Equal("sandbox", res.Headers.NonValidated["Content-Security-Policy"].ToString());
        Assert.Equal(CacheControl, res.Headers.NonValidated["Cache-Control"].ToString());
        Assert.Equal($"\"{attachmentId}\"", res.Headers.NonValidated["ETag"].ToString());
    }
}
