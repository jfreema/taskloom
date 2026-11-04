using System.Net;
using Taskloom.Api.Auth;
using Taskloom.Api.Tests.Support;
using Taskloom.Domain;

namespace Taskloom.Api.Tests;

/// <summary>
/// Every /api endpoint of spec 5.4 with its minimum role (5.1): sign-in is required, a non-member gets 404, each role
/// below the minimum gets 403, a write without X-Taskloom gets 400, refusals change nothing, and the minimum role succeeds.
/// </summary>
public class AuthorizationMatrixTests(TaskloomFactory factory) : IClassFixture<TaskloomFactory>
{
    /// <summary>The lowest role an endpoint allows. SignedIn needs no workspace; Author means the comment's author, whatever their role.</summary>
    public enum Min { SignedIn, Guest, Member, Admin, Owner, Author }

    /// <param name="Request">The request, built fresh for every send; it is valid, so only the caller decides the answer.</param>
    public sealed record Row(Min Min, HttpStatusCode Success, Func<World, HttpRequestMessage> Request);

    private static HttpRequestMessage Get(string path) => new(HttpMethod.Get, path);
    private static HttpRequestMessage Delete(string path) => new(HttpMethod.Delete, path);
    private static HttpRequestMessage Post(string path, object body) => new(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
    private static HttpRequestMessage Patch(string path, object body) => new(HttpMethod.Patch, path) { Content = JsonContent.Create(body) };

    private static HttpRequestMessage Upload(string path) => new(HttpMethod.Post, path)
    {
        Content = new MultipartFormDataContent { { new ByteArrayContent("new file"u8.ToArray()), "file", "new.txt" } },
    };

    public static readonly Dictionary<string, Row> Rows = new()
    {
        ["#5 GET /api/me"] = new(Min.SignedIn, HttpStatusCode.OK, _ => Get("/api/me")),
        ["#6 PATCH /api/me"] = new(Min.SignedIn, HttpStatusCode.OK, _ => Patch("/api/me", new { displayName = "Renamed Person" })),
        ["#7 GET /api/workspaces"] = new(Min.SignedIn, HttpStatusCode.OK, _ => Get("/api/workspaces")),
        ["#8 POST /api/workspaces"] = new(Min.SignedIn, HttpStatusCode.Created, _ => Post("/api/workspaces", new { name = "Another" })),
        ["#9 PATCH /api/workspaces/{id}"] = new(Min.Admin, HttpStatusCode.NoContent, w => Patch($"/api/workspaces/{w.WorkspaceId}", new { name = "Renamed" })),
        ["#10 DELETE /api/workspaces/{id}"] = new(Min.Owner, HttpStatusCode.NoContent, w => Delete($"/api/workspaces/{w.WorkspaceId}")),
        ["#11 GET /api/workspaces/{id}/members"] = new(Min.Guest, HttpStatusCode.OK, w => Get($"/api/workspaces/{w.WorkspaceId}/members")),
        ["#12 POST /api/workspaces/{id}/members"] = new(Min.Admin, HttpStatusCode.OK,
            w => Post($"/api/workspaces/{w.WorkspaceId}/members", new { email = w.NewcomerEmail, role = "Member" })),
        ["#13 PATCH /api/workspaces/{id}/members/{userId}"] = new(Min.Admin, HttpStatusCode.NoContent,
            w => Patch($"/api/workspaces/{w.WorkspaceId}/members/{w.MemberId}", new { role = "Guest" })),
        ["#14 DELETE /api/workspaces/{id}/members/{userId}"] = new(Min.Admin, HttpStatusCode.NoContent,
            w => Delete($"/api/workspaces/{w.WorkspaceId}/members/{w.MemberId}")),
        ["#15 GET /api/workspaces/{id}/tags"] = new(Min.Guest, HttpStatusCode.OK, w => Get($"/api/workspaces/{w.WorkspaceId}/tags")),
        ["#16 POST /api/workspaces/{id}/tags"] = new(Min.Member, HttpStatusCode.Created, w => Post($"/api/workspaces/{w.WorkspaceId}/tags", new { name = "new tag" })),
        ["#17 PATCH /api/tags/{id}"] = new(Min.Member, HttpStatusCode.OK, w => Patch($"/api/tags/{w.Tag.Id}", new { name = "renamed", color = Palette.Blue })),
        ["#18 DELETE /api/tags/{id}"] = new(Min.Admin, HttpStatusCode.NoContent, w => Delete($"/api/tags/{w.Tag.Id}")),
        ["#19 POST /api/workspaces/{id}/spaces"] = new(Min.Member, HttpStatusCode.Created, w => Post($"/api/workspaces/{w.WorkspaceId}/spaces", new { name = "New space" })),
        ["#20 PATCH /api/spaces/{id}"] = new(Min.Member, HttpStatusCode.NoContent, w => Patch($"/api/spaces/{w.SpaceId}", new { name = "Renamed", color = Palette.Pink })),
        ["#21 DELETE /api/spaces/{id}"] = new(Min.Admin, HttpStatusCode.NoContent, w => Delete($"/api/spaces/{w.SpaceId}")),
        ["#22 POST /api/spaces/{id}/lists"] = new(Min.Member, HttpStatusCode.Created, w => Post($"/api/spaces/{w.SpaceId}/lists", new { name = "New list", statusPreset = "Simple" })),
        ["#23 PATCH /api/lists/{id}"] = new(Min.Member, HttpStatusCode.NoContent, w => Patch($"/api/lists/{w.ListId}", new { name = "Renamed" })),
        ["#24 DELETE /api/lists/{id}"] = new(Min.Member, HttpStatusCode.NoContent, w => Delete($"/api/lists/{w.ListId}")),
        ["#25 POST /api/lists/{id}/statuses"] = new(Min.Member, HttpStatusCode.OK, w => Post($"/api/lists/{w.ListId}/statuses", new { name = "Blocked", category = "Active" })),
        ["#26 PATCH /api/statuses/{id}"] = new(Min.Member, HttpStatusCode.OK, w => Patch($"/api/statuses/{w.Status("Review")}", new { name = "In review" })),
        ["#27 DELETE /api/statuses/{id}?moveTo="] = new(Min.Member, HttpStatusCode.NoContent,
            w => Delete($"/api/statuses/{w.Status("Review")}?moveTo={w.Status("To do")}")),
        ["#28 GET /api/lists/{id}/tasks"] = new(Min.Guest, HttpStatusCode.OK, w => Get($"/api/lists/{w.ListId}/tasks")),
        ["#29 POST /api/lists/{id}/tasks"] = new(Min.Member, HttpStatusCode.Created, w => Post($"/api/lists/{w.ListId}/tasks", new { title = "New task" })),
        ["#30 GET /api/tasks/{id}"] = new(Min.Guest, HttpStatusCode.OK, w => Get($"/api/tasks/{w.Task.Id}")),
        ["#31 PATCH /api/tasks/{id}"] = new(Min.Member, HttpStatusCode.OK, w => Patch($"/api/tasks/{w.Task.Id}", new { priority = "High", title = "Renamed" })),
        ["#32 POST /api/tasks/{id}/move"] = new(Min.Member, HttpStatusCode.OK, w => Post($"/api/tasks/{w.Task.Id}/move", new { statusId = w.Status("Done") })),
        ["#33 DELETE /api/tasks/{id}"] = new(Min.Member, HttpStatusCode.NoContent, w => Delete($"/api/tasks/{w.Task.Id}")),
        ["#34 POST /api/tasks/bulk"] = new(Min.Member, HttpStatusCode.OK,
            w => Post("/api/tasks/bulk", new { updates = new[] { new { id = w.Task.Id, priority = "Low" } } })),
        ["#35 POST /api/tasks/bulk-delete"] = new(Min.Member, HttpStatusCode.NoContent, w => Post("/api/tasks/bulk-delete", new { ids = new[] { w.Task.Id } })),
        ["#36 GET /api/workspaces/{id}/tasks"] = new(Min.Guest, HttpStatusCode.OK, w => Get($"/api/workspaces/{w.WorkspaceId}/tasks?scope=all")),
        ["#37 POST /api/tasks/{id}/comments"] = new(Min.Guest, HttpStatusCode.OK, w => Post($"/api/tasks/{w.Task.Id}/comments", new { body = "Another comment" })),
        ["#38 PATCH /api/comments/{id}"] = new(Min.Author, HttpStatusCode.OK, w => Patch($"/api/comments/{w.Comment.Id}", new { body = "Edited" })),
        ["#39 DELETE /api/comments/{id}"] = new(Min.Author, HttpStatusCode.NoContent, w => Delete($"/api/comments/{w.Comment.Id}")),
        ["#40 POST /api/tasks/{id}/attachments"] = new(Min.Member, HttpStatusCode.OK, w => Upload($"/api/tasks/{w.Task.Id}/attachments")),
        ["#41 GET /api/attachments/{id}"] = new(Min.Guest, HttpStatusCode.OK, w => Get($"/api/attachments/{w.Attachment.Id}")),
        ["#42 DELETE /api/attachments/{id}"] = new(Min.Member, HttpStatusCode.NoContent, w => Delete($"/api/attachments/{w.Attachment.Id}")),
        ["#43 POST /api/dev/seed"] = new(Min.Owner, HttpStatusCode.NoContent, w => Post("/api/dev/seed", new { workspaceId = w.WorkspaceId, fillers = 0 })),
        ["GET /api/lists/{id}"] = new(Min.Guest, HttpStatusCode.OK, w => Get($"/api/lists/{w.ListId}")),
    };

    public static TheoryData<string> Endpoints => new(Rows.Keys);

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task Each_endpoint_allows_its_minimum_role_and_refuses_everyone_else(string endpoint)
    {
        var row = Rows[endpoint];
        var w = await World.CreateAsync(factory);
        var before = await w.SnapshotAsync();

        async Task Expect(HttpClient client, HttpStatusCode expected, string who)
        {
            using var req = row.Request(w);
            var res = await client.SendAsync(req);
            Assert.True(res.StatusCode == expected, $"{endpoint} as {who}: expected {(int)expected}, got {(int)res.StatusCode}: {await res.Content.ReadAsStringAsync()}");
        }

        // Signed out: 401 with no body, for every endpoint.
        using (var req = row.Request(w))
        {
            req.Headers.Add(RequireTaskloomHeader.Header, "1");
            var res = await w.Anonymous.SendAsync(req);
            Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
            Assert.Equal("", await res.Content.ReadAsStringAsync());
        }
        if (row.Min != Min.SignedIn) await Expect(w.Outsider, HttpStatusCode.NotFound, "a non-member");
        if (row.Min is Min.Member or Min.Admin or Min.Owner) await Expect(w.Guest, HttpStatusCode.Forbidden, "a Guest");
        if (row.Min is Min.Admin or Min.Owner) await Expect(w.Member, HttpStatusCode.Forbidden, "a Member");
        if (row.Min is Min.Owner) await Expect(w.Admin, HttpStatusCode.Forbidden, "an Admin");
        // Only the author edits or deletes a comment: not even the Owner may.
        if (row.Min is Min.Author) await Expect(w.Owner.Client, HttpStatusCode.Forbidden, "the Owner, not the author");

        var caller = row.Min switch
        {
            Min.SignedIn => w.Outsider,
            Min.Guest or Min.Author => w.Guest,
            Min.Member => w.Member,
            Min.Admin => w.Admin,
            _ => w.Owner.Client,
        };
        using (var probe = row.Request(w))
        {
            if (probe.Method != HttpMethod.Get)
            {
                // The caller has the role, but a write without the CSRF header is refused first.
                caller.DefaultRequestHeaders.Remove(RequireTaskloomHeader.Header);
                try
                {
                    var res = await caller.SendAsync(probe);
                    Assert.Equal("Missing X-Taskloom header.", await res.ProblemAsync(HttpStatusCode.BadRequest));
                }
                finally
                {
                    caller.DefaultRequestHeaders.Add(RequireTaskloomHeader.Header, "1");
                }
            }
        }

        // None of the refused calls changed anything.
        Assert.Equal(before, await w.SnapshotAsync());

        await Expect(caller, row.Success, $"the minimum role ({row.Min})");
    }

    /// <summary>
    /// A workspace with a person of each role, an outsider with a workspace of their own, and one of everything to act
    /// on: a tag, a task, a comment written by the Guest, and an attachment.
    /// </summary>
    public sealed class World
    {
        public required TestUser Owner { get; init; }
        public required HttpClient Admin { get; init; }
        public required HttpClient Member { get; init; }
        public required HttpClient Guest { get; init; }
        public required HttpClient Outsider { get; init; }
        public required HttpClient Anonymous { get; init; }
        public required Guid MemberId { get; init; }
        public required TagDto Tag { get; init; }
        public required TaskDto Task { get; init; }
        public required CommentDto Comment { get; init; }
        public required AttachmentDto Attachment { get; init; }
        public string NewcomerEmail { get; } = Builders.NewEmail();

        public Guid WorkspaceId => Owner.Workspace.Id;
        public Guid SpaceId => Owner.Space.Id;
        public Guid ListId => Owner.List.Id;
        public Guid Status(string name) => Owner.Status(name).Id;

        public static async Task<World> CreateAsync(TaskloomFactory factory)
        {
            var owner = await factory.NewUserAsync();
            var (admin, _) = await factory.JoinAsync(owner, WorkspaceRole.Admin, "Ada Admin");
            var (member, memberInfo) = await factory.JoinAsync(owner, WorkspaceRole.Member, "Max Member");
            var (guest, _) = await factory.JoinAsync(owner, WorkspaceRole.Guest, "Gus Guest");
            var outsider = await factory.NewUserAsync();
            var tag = await owner.Client.CreateTagAsync(owner.Workspace.Id, "matrix");
            var task = await owner.Client.CreateTaskAsync(owner.List.Id, new { title = "Matrix task", tagIds = new[] { tag.Id }, assigneeIds = new[] { memberInfo.UserId } });
            var comment = await (await guest.PostAsJsonAsync($"/api/tasks/{task.Id}/comments", new { body = "From the guest" })).ReadAsync<CommentDto>();
            var attachment = await owner.Client.UploadAsync(task.Id, "matrix.txt", "matrix"u8.ToArray());
            return new World
            {
                Owner = owner, Admin = admin, Member = member, Guest = guest, Outsider = outsider.Client,
                Anonymous = factory.CreateClient(new() { AllowAutoRedirect = false }),
                MemberId = memberInfo.UserId, Tag = tag, Task = task, Comment = comment, Attachment = attachment,
            };
        }

        /// <summary>Everything the refused calls could have touched, as the API returns it.</summary>
        public async Task<string> SnapshotAsync()
        {
            var reads = new (HttpClient Client, string Path)[]
            {
                (Owner.Client, "/api/workspaces"), (Owner.Client, $"/api/workspaces/{WorkspaceId}/members"),
                (Owner.Client, $"/api/workspaces/{WorkspaceId}/tags"), (Owner.Client, $"/api/lists/{ListId}/tasks"),
                (Owner.Client, $"/api/tasks/{Task.Id}"), (Owner.Client, $"/api/attachments/{Attachment.Id}"),
                (Owner.Client, "/api/me"), (Outsider, "/api/me"), (Outsider, "/api/workspaces"), (Guest, "/api/me"),
            };
            var parts = new List<string>();
            foreach (var (client, path) in reads)
            {
                var res = await client.GetAsync(path);
                parts.Add($"{path} {(int)res.StatusCode} {await res.Content.ReadAsStringAsync()}");
            }
            return string.Join("\n", parts);
        }
    }
}
