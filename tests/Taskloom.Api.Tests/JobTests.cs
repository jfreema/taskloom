using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using Taskloom.Api.Jobs;
using Taskloom.Api.Tests.Support;
using Taskloom.Domain;
using Taskloom.Infrastructure;

namespace Taskloom.Api.Tests;

/// <summary>
/// Features 2, the job runner: jobs run on a timer inside the API, one copy at a time across instances, a failing job
/// doesn't stop the others, and a scheduler can run them over HTTP with a key.
/// </summary>
public class JobTests
{
    /// <summary>A job that always throws, registered beside the real ones.</summary>
    private sealed class BrokenJob : IJob
    {
        public static int Runs;
        public string Name => "broken";
        public TimeSpan Every => TimeSpan.FromHours(1);

        public Task<string?> RunAsync(CancellationToken ct)
        {
            Interlocked.Increment(ref Runs);
            throw new InvalidOperationException("This job is broken.");
        }
    }

    private sealed class BrokenSite : InboxSite
    {
        public LogCapture Logs { get; } = new();

        protected override void Register(IServiceCollection services)
        {
            base.Register(services);
            services.AddScoped<IJob, BrokenJob>();
            services.AddLogging(l => l.AddProvider(Logs));
        }
    }

    [Fact]
    public async Task Every_job_runs_and_a_broken_one_does_not_stop_the_rest()
    {
        await using var site = new BrokenSite();
        var owner = await site.NewUserAsync();
        var (_, ana) = await site.JoinAsync(owner, WorkspaceRole.Member, "Ana");
        await owner.Client.CreateTaskAsync(owner.List.Id, new { title = "Run me", assigneeIds = new[] { ana.UserId } });
        site.Clock.Advance(TimeSpan.FromSeconds(61));

        var results = await site.Services.GetRequiredService<JobRunner>().RunAllAsync();
        // Reminders before emails, so a morning's reminders go out in the pass that writes them.
        Assert.Equal(["reminders", "emails", "prune", "broken"], results.Select(r => r.Name).ToArray());
        Assert.All(results, r => Assert.True(r.Ran));
        var emails = results[1];
        Assert.Equal(("sent 1 email for 1 notification", null), (emails.Summary, emails.Error));
        Assert.Equal((null, "This job is broken."), (results[3].Summary, results[3].Error));
        Assert.Single(site.Outbox.To(ana.Email));
        // The failure is logged with the job's name; the email's address is not.
        Assert.Contains(site.Logs.Entries, e => e.Level == LogLevel.Error && e.Message == "Job broken failed; it runs again on its next turn.");
        Assert.Contains(site.Logs.Entries, e => e.Level == LogLevel.Information && e.Message.StartsWith("Job emails: sent 1 email for 1 notification"));
        Assert.DoesNotContain(site.Logs.Entries, e => e.Category.Contains("Job") && e.Message.Contains(ana.Email));

        var runner = site.Services.GetRequiredService<JobRunner>();
        await Assert.ThrowsAsync<InvalidOperationException>(() => runner.RunAsync("no such job"));
    }

    [Fact]
    public async Task A_job_is_skipped_while_another_copy_of_the_app_runs_it()
    {
        await using var site = new InboxSite();
        _ = await site.NewUserAsync();
        var connectionString = await site.DbAsync(db => Task.FromResult(db.Database.GetConnectionString()!));

        // Another copy of the app holds the email job's lock.
        await using var other = new NpgsqlConnection(connectionString);
        await other.OpenAsync();
        await using (var take = new NpgsqlCommand($"SELECT pg_advisory_lock({JobRunner.LockKey("emails")})", other)) await take.ExecuteNonQueryAsync();

        var skipped = await site.RunJobAsync("emails");
        Assert.Equal(("emails", false, null, null), (skipped.Name, skipped.Ran, skipped.Summary, skipped.Error));
        // Each job has its own lock: the others still run.
        Assert.True((await site.RunJobAsync("reminders")).Ran);

        await using (var release = new NpgsqlCommand($"SELECT pg_advisory_unlock({JobRunner.LockKey("emails")})", other)) await release.ExecuteNonQueryAsync();
        Assert.True((await site.RunJobAsync("emails")).Ran);
        // And the runner let its own lock go: the other copy can take it straight away.
        await using var again = new NpgsqlCommand($"SELECT pg_try_advisory_lock({JobRunner.LockKey("emails")})", other);
        Assert.True((bool)(await again.ExecuteScalarAsync())!);

        Assert.NotEqual(JobRunner.LockKey("emails"), JobRunner.LockKey("reminders"));
    }

    [Fact]
    public async Task The_timer_runs_jobs_without_being_asked()
    {
        await using var site = new InboxSite();
        site.Settings["Jobs:Enabled"] = "true";
        site.Settings["Jobs:PollSeconds"] = "1";
        site.Settings["Email:DelaySeconds"] = "0";
        var owner = await site.NewUserAsync();
        var (_, ana) = await site.JoinAsync(owner, WorkspaceRole.Member, "Ana");
        await owner.Client.CreateTaskAsync(owner.List.Id, new { title = "On the timer", assigneeIds = new[] { ana.UserId } });

        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (site.Outbox.To(ana.Email).Count == 0 && DateTime.UtcNow < deadline) await Task.Delay(100);
        Assert.Equal("On the timer", Assert.Single(site.Outbox.To(ana.Email)).Subject.Split('“', '”')[1]);
    }

    [Fact]
    public async Task A_scheduler_can_run_the_jobs_with_the_key_and_nobody_can_without()
    {
        const string key = "a-long-random-key";
        await using var site = new InboxSite();
        site.Settings["Jobs:TriggerKey"] = key;
        var owner = await site.NewUserAsync();
        var (_, ana) = await site.JoinAsync(owner, WorkspaceRole.Member, "Ana");
        await owner.Client.CreateTaskAsync(owner.List.Id, new { title = "By HTTP", assigneeIds = new[] { ana.UserId } });
        site.Clock.Advance(TimeSpan.FromSeconds(61));
        // No cookie and no X-Taskloom header: a scheduler has neither.
        using var scheduler = site.CreateClient(new() { AllowAutoRedirect = false });

        async Task<HttpResponseMessage> RunAsync(string? sent)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "/jobs/run");
            if (sent is not null) req.Headers.Add("X-Taskloom-Jobs-Key", sent);
            return await scheduler.SendAsync(req);
        }

        // Without the key, or with a wrong one, the path looks like it isn't there; signed-in people get the same.
        foreach (var wrong in new[] { null, "", "a-long-random-ke", key + "x", "A-LONG-RANDOM-KEY" })
            Assert.Equal(HttpStatusCode.NotFound, (await RunAsync(wrong)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.Client.PostAsync("/jobs/run", null)).StatusCode);
        Assert.Empty(site.Outbox.Sent);

        var res = await RunAsync(key);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        using var body = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        Assert.Equal(["reminders", "emails", "prune"], body.RootElement.EnumerateArray().Select(j => j.GetProperty("name").GetString()).ToArray());
        Assert.Equal("sent 1 email for 1 notification", body.RootElement[1].GetProperty("summary").GetString());
        Assert.All(body.RootElement.EnumerateArray(), j => Assert.True(j.GetProperty("ran").GetBoolean()));
        Assert.Single(site.Outbox.To(ana.Email));
    }

    [Fact]
    public async Task Without_a_key_the_trigger_does_not_exist_and_old_rows_are_pruned()
    {
        await using var site = new InboxSite();
        site.Settings["Notifications:KeepDays"] = "30";
        var owner = await site.NewUserAsync();
        var (_, anaInfo) = await site.JoinAsync(owner, WorkspaceRole.Member, "Ana");
        using var scheduler = site.CreateClient(new() { AllowAutoRedirect = false });
        // With no key set there is no such path at all (an empty key opens nothing).
        using (var req = new HttpRequestMessage(HttpMethod.Post, "/jobs/run"))
        {
            req.Headers.Add("X-Taskloom-Jobs-Key", "");
            Assert.False((await scheduler.SendAsync(req)).IsSuccessStatusCode);
        }
        Assert.Equal(0, site.Outbox.Attempts);

        var old = await owner.Client.CreateTaskAsync(owner.List.Id, new { title = "Old news", assigneeIds = new[] { anaInfo.UserId } });
        site.Clock.Advance(TimeSpan.FromDays(29));
        Assert.Null((await site.RunJobAsync("prune")).Summary);
        Assert.Single(await site.NotificationRowsAsync(anaInfo.UserId));
        // A day later the row is past 30 days and goes, read or not; a fresh one stays.
        site.Clock.Advance(TimeSpan.FromDays(1.1));
        await site.DbAsync(async db =>
        {
            var task = await db.Tasks.SingleAsync(t => t.Id == old.Id);
            db.Notifications.Add(new Notification { UserId = anaInfo.UserId, TaskId = task.Id, Kind = NotificationKind.Overdue, CreatedAt = site.Clock.GetUtcNow() });
            await db.SaveChangesAsync();
        });
        Assert.Equal("deleted 1 notification older than 30 days", (await site.RunJobAsync("prune")).Summary);
        Assert.Equal(NotificationKind.Overdue, Assert.Single(await site.NotificationRowsAsync(anaInfo.UserId)).Kind);
    }
}
