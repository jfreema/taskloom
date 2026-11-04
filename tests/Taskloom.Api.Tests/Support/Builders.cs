using Microsoft.EntityFrameworkCore;
using Taskloom.Domain;
using Taskloom.Infrastructure;

namespace Taskloom.Api.Tests.Support;

/// <summary>A signed-in user with their starter workspace (Personal / Tasks).</summary>
public record TestUser(HttpClient Client, UserDto Me, WorkspaceTreeDto Workspace)
{
    public SpaceDto Space => Workspace.Spaces[0];
    public ListSummaryDto List => Workspace.Spaces[0].Lists[0];
    public StatusDto Status(string name) => List.Statuses.Single(s => s.Name == name);
}

/// <summary>Sign-in, seed and builders shared by the test classes (spec 11.2).</summary>
public static class Builders
{
    public static string NewEmail() => $"{Guid.NewGuid():N}@example.com";

    public static async Task<TestUser> NewUserAsync(this TaskloomFactory factory, string? email = null)
    {
        var client = await factory.SignInAsync(email ?? NewEmail());
        var me = await (await client.GetAsync("/api/me")).ReadAsync<UserDto>();
        var workspace = (await (await client.GetAsync("/api/workspaces")).ReadAsync<List<WorkspaceTreeDto>>()).Single();
        return new TestUser(client, me, workspace);
    }

    /// <summary>Invites a new address to the user's workspace with the role, and signs that person in.</summary>
    public static async Task<(HttpClient Client, MemberDto Member)> JoinAsync(this TaskloomFactory factory, TestUser owner, WorkspaceRole role, string? displayName = null)
    {
        var email = NewEmail();
        var res = await owner.Client.PostAsJsonAsync($"/api/workspaces/{owner.Workspace.Id}/members", new { email, role = role.ToString(), displayName });
        var member = await res.ReadAsync<MemberDto>();
        return (await factory.SignInAsync(email), member);
    }

    public static async Task<List<WorkspaceTreeDto>> TreeAsync(this HttpClient client) =>
        await (await client.GetAsync("/api/workspaces")).ReadAsync<List<WorkspaceTreeDto>>();

    public static async Task<ListSummaryDto> ListAsync(this HttpClient client, Guid listId) =>
        (await client.TreeAsync()).SelectMany(w => w.Spaces).SelectMany(s => s.Lists).Single(l => l.Id == listId);

    public static async Task<TaskDto> CreateTaskAsync(this HttpClient client, Guid listId, object body) =>
        await (await client.PostAsJsonAsync($"/api/lists/{listId}/tasks", body)).ReadAsync<TaskDto>();

    public static async Task<List<TaskDto>> ListTasksAsync(this HttpClient client, Guid listId) =>
        await (await client.GetAsync($"/api/lists/{listId}/tasks")).ReadAsync<List<TaskDto>>();

    public static async Task<TaskDetailDto> TaskDetailAsync(this HttpClient client, Guid taskId) =>
        await (await client.GetAsync($"/api/tasks/{taskId}")).ReadAsync<TaskDetailDto>();

    public static async Task<TaskDto> PatchTaskAsync(this HttpClient client, Guid taskId, object body) =>
        await (await client.PatchAsJsonAsync($"/api/tasks/{taskId}", body)).ReadAsync<TaskDto>();

    public static async Task<TaskDto> MoveTaskAsync(this HttpClient client, Guid taskId, object body) =>
        await (await client.PostAsJsonAsync($"/api/tasks/{taskId}/move", body)).ReadAsync<TaskDto>();

    public static Task<HttpResponseMessage> BulkAsync(this HttpClient client, params object[] updates) =>
        client.PostAsJsonAsync("/api/tasks/bulk", new { updates });

    public static Task<HttpResponseMessage> BulkDeleteAsync(this HttpClient client, params Guid[] ids) =>
        client.PostAsJsonAsync("/api/tasks/bulk-delete", new { ids });

    public static async Task<WorkspaceTasksDto> WorkspaceTasksAsync(this HttpClient client, Guid workspaceId, string query = "") =>
        await (await client.GetAsync($"/api/workspaces/{workspaceId}/tasks{query}")).ReadAsync<WorkspaceTasksDto>();

    public static async Task<ListSummaryDto> CreateListAsync(this HttpClient client, Guid spaceId, string name, object? extra = null) =>
        await (await client.PostAsJsonAsync($"/api/spaces/{spaceId}/lists", extra ?? new { name })).ReadAsync<ListSummaryDto>();

    public static async Task<MemberDto> InviteAsync(this HttpClient client, Guid workspaceId, string email, WorkspaceRole role) =>
        await (await client.PostAsJsonAsync($"/api/workspaces/{workspaceId}/members", new { email, role = role.ToString() })).ReadAsync<MemberDto>();

    public static async Task<List<MemberDto>> MembersAsync(this HttpClient client, Guid workspaceId) =>
        await (await client.GetAsync($"/api/workspaces/{workspaceId}/members")).ReadAsync<List<MemberDto>>();

    public static async Task<List<TagDto>> TagsAsync(this HttpClient client, Guid workspaceId) =>
        await (await client.GetAsync($"/api/workspaces/{workspaceId}/tags")).ReadAsync<List<TagDto>>();

    public static async Task<TagDto> CreateTagAsync(this HttpClient client, Guid workspaceId, string name, string? color = null) =>
        await (await client.PostAsJsonAsync($"/api/workspaces/{workspaceId}/tags", new { name, color })).ReadAsync<TagDto>();

    public static async Task<AttachmentDto> UploadAsync(this HttpClient client, Guid taskId, string fileName, byte[] content, string contentType = "text/plain")
    {
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        using var form = new MultipartFormDataContent { { file, "file", fileName } };
        return await (await client.PostAsync($"/api/tasks/{taskId}/attachments", form)).ReadAsync<AttachmentDto>();
    }

    /// <summary>POST /api/dev/seed; returns the response so callers can check failures.</summary>
    public static Task<HttpResponseMessage> SeedAsync(this HttpClient client, Guid workspaceId, int? fillers = null) =>
        client.PostAsJsonAsync("/api/dev/seed", fillers is null ? new { workspaceId } : (object)new { workspaceId, fillers });

    /// <summary>Runs <paramref name="work"/> on a fresh DbContext of the API under test (to arrange or inspect rows the API can't).</summary>
    public static async Task<T> DbAsync<T>(this TaskloomFactory factory, Func<TaskloomDbContext, Task<T>> work)
    {
        using var scope = factory.Services.CreateScope();
        return await work(scope.ServiceProvider.GetRequiredService<TaskloomDbContext>());
    }

    public static Task DbAsync(this TaskloomFactory factory, Func<TaskloomDbContext, Task> work) =>
        factory.DbAsync(async db => { await work(db); return 0; });

    /// <summary>The task's activity rows, oldest first, straight from the database.</summary>
    public static Task<List<TaskActivity>> ActivityRowsAsync(this TaskloomFactory factory, Guid taskId) =>
        factory.DbAsync(db => db.Activities.AsNoTracking().Where(a => a.TaskId == taskId).OrderBy(a => a.CreatedAt).ThenBy(a => a.Id).ToListAsync());
}
