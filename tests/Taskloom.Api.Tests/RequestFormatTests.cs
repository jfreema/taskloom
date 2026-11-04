using System.Net;
using System.Text;
using Taskloom.Api.Tests.Support;

namespace Taskloom.Api.Tests;

/// <summary>Request formats (spec 5.1): dates are exactly yyyy-MM-dd, and /api paths no endpoint takes are 404, 405 or 415.</summary>
public class RequestFormatTests(TaskloomFactory factory) : IClassFixture<TaskloomFactory>
{
    private const string DateMessage = "Dates must look like 2026-11-04.";

    public static TheoryData<string> NotDates =>
    [
        "2026-11-4", "2026-1-04", "26-11-04", "02026-11-04", "11/04/2026", "04.11.2026", "2026/11/04", "20261104",
        "2026-11-04T00:00:00", "2026-11-04T00:00:00Z", "2026-11-04 ", " 2026-11-04", "2026-11-04\n", "2026-02-29", "2026-13-01",
        "2026-00-10", "2026-11-31", "+2026-11-04", "２０２６-11-04", "2026-11-0٤", "", "today", "Nov 4, 2026",
    ];

    [Theory]
    [MemberData(nameof(NotDates))]
    public async Task Dates_are_yyyy_MM_dd_in_creates_patches_and_bulk_updates(string notADate)
    {
        var user = await factory.NewUserAsync();
        var task = await user.Client.CreateTaskAsync(user.List.Id, new { title = "Dated", dueDate = "2026-11-30", startDate = "2026-11-02" });
        var before = await (await user.Client.GetAsync($"/api/lists/{user.List.Id}/tasks")).Content.ReadAsStringAsync();

        Assert.NotNull(await (await user.Client.PostAsJsonAsync($"/api/lists/{user.List.Id}/tasks", new { title = "Bad", dueDate = notADate })).ProblemAsync(HttpStatusCode.BadRequest));
        Assert.NotNull(await (await user.Client.PostAsJsonAsync($"/api/lists/{user.List.Id}/tasks", new { title = "Bad", startDate = notADate })).ProblemAsync(HttpStatusCode.BadRequest));
        Assert.Equal(DateMessage, await (await user.Client.PatchAsJsonAsync($"/api/tasks/{task.Id}", new { dueDate = notADate })).ProblemAsync(HttpStatusCode.BadRequest));
        Assert.Equal(DateMessage, await (await user.Client.PatchAsJsonAsync($"/api/tasks/{task.Id}", new { title = "Renamed", startDate = notADate })).ProblemAsync(HttpStatusCode.BadRequest));
        Assert.Equal(DateMessage, await (await user.Client.BulkAsync(new { id = task.Id, dueDate = notADate })).ProblemAsync(HttpStatusCode.BadRequest));
        Assert.Equal(DateMessage, await (await user.Client.BulkAsync(new { id = task.Id, startDate = notADate, priority = "High" })).ProblemAsync(HttpStatusCode.BadRequest));

        Assert.Equal(before, await (await user.Client.GetAsync($"/api/lists/{user.List.Id}/tasks")).Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Dates_accept_real_days_and_null_clears()
    {
        var user = await factory.NewUserAsync();
        var leap = await user.Client.CreateTaskAsync(user.List.Id, new { title = "Leap", dueDate = "2028-02-29", startDate = "0001-01-01" });
        Assert.Equal((new DateOnly(2028, 2, 29), new DateOnly(1, 1, 1)), (leap.DueDate, leap.StartDate));
        var patched = await user.Client.PatchTaskAsync(leap.Id, new { dueDate = "9999-12-31", startDate = (string?)null });
        Assert.Equal((new DateOnly(9999, 12, 31), (DateOnly?)null), (patched.DueDate, patched.StartDate));
        // A number isn't a date either.
        Assert.Equal(DateMessage, await (await user.Client.PatchAsJsonAsync($"/api/tasks/{leap.Id}", new { dueDate = 20261104 })).ProblemAsync(HttpStatusCode.BadRequest));
        var bulk = await (await user.Client.BulkAsync(new { id = leap.Id, dueDate = (string?)null, startDate = "2026-11-04" })).ReadAsync<BulkUpdateResultDto>();
        Assert.Equal(((DateOnly?)null, new DateOnly(2026, 11, 4)), (bulk.Tasks[0].DueDate, bulk.Tasks[0].StartDate));
    }

    [Fact]
    public async Task Api_paths_no_endpoint_takes_are_404_405_or_415_never_the_app()
    {
        var user = await factory.NewUserAsync();
        var task = await user.Client.CreateTaskAsync(user.List.Id, new { title = "Routed" });

        // Unknown paths, and ids that aren't ids, are 404 problems (not index.html).
        foreach (var path in new[] { "/api/nope", "/api/tasks/not-a-guid", $"/api/tasks/{task.Id}/nope", "/api/workspaces/1/tags", "/api" })
        {
            var res = await user.Client.GetAsync(path);
            Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
            Assert.NotEqual("text/html", res.Content.Headers.ContentType?.MediaType);
        }

        // A known path with another method is 405 with Allow, whatever the case of the path.
        foreach (var (method, path, allow) in new[]
                 {
                     ("DELETE", "/api/me", "GET, PATCH"), ("PUT", $"/api/tasks/{task.Id}", "DELETE, GET, PATCH"),
                     ("GET", "/api/tasks/bulk", "POST"), ("POST", $"/API/Tasks/{task.Id}", "DELETE, GET, PATCH"),
                     ("PATCH", $"/api/workspaces/{user.Workspace.Id}/tags", "GET, POST"),
                 })
        {
            using var req = new HttpRequestMessage(new HttpMethod(method), path) { Content = JsonContent.Create(new { }) };
            var res = await user.Client.SendAsync(req);
            Assert.Equal($"Use {allow} for this address.", await res.ProblemAsync(HttpStatusCode.MethodNotAllowed));
            Assert.Equal(allow, res.Content.Headers.Allow.Count > 0 ? string.Join(", ", res.Content.Headers.Allow) : res.Headers.NonValidated["Allow"].ToString());
        }

        // A known path and method with a body of the wrong type is 415, and nothing is saved.
        var before = await (await user.Client.GetAsync("/api/workspaces")).Content.ReadAsStringAsync();
        Assert.Equal("Send the body as application/json.", await (await user.Client.PostAsync("/api/workspaces",
            new StringContent("""{"name":"Typed"}""", Encoding.UTF8, "text/plain"))).ProblemAsync(HttpStatusCode.UnsupportedMediaType));
        Assert.Equal("Send the body as application/json.", await (await user.Client.PatchAsync($"/api/tasks/{task.Id}",
            new FormUrlEncodedContent([new("title", "Form")]))).ProblemAsync(HttpStatusCode.UnsupportedMediaType));
        Assert.Equal("Send the body as multipart/form-data.", await (await user.Client.PostAsJsonAsync($"/api/tasks/{task.Id}/attachments",
            new { file = "hello" })).ProblemAsync(HttpStatusCode.UnsupportedMediaType));
        // No body at all is the same answer for a JSON endpoint.
        Assert.Equal("Send the body as application/json.", await (await user.Client.PostAsync("/api/workspaces", null)).ProblemAsync(HttpStatusCode.UnsupportedMediaType));
        Assert.Equal(before, await (await user.Client.GetAsync("/api/workspaces")).Content.ReadAsStringAsync());
        Assert.Equal("Routed", (await user.Client.TaskDetailAsync(task.Id)).Task.Title);

        // Signed out, every /api path is 401 with no body, so nothing about the routes leaks.
        var anonymous = factory.CreateClient(new() { AllowAutoRedirect = false });
        anonymous.DefaultRequestHeaders.Add("X-Taskloom", "1");
        foreach (var (method, path) in new[] { ("GET", "/api/nope"), ("DELETE", "/api/me"), ("POST", "/api/workspaces") })
        {
            using var req = new HttpRequestMessage(new HttpMethod(method), path) { Content = new StringContent("x", Encoding.UTF8, "text/plain") };
            var res = await anonymous.SendAsync(req);
            Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
            Assert.Empty(await res.Content.ReadAsStringAsync());
        }
    }
}
