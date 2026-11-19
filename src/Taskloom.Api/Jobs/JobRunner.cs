using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Taskloom.Api.Notifications;
using Taskloom.Infrastructure;

namespace Taskloom.Api.Jobs;

/// <summary>
/// Work that runs on a schedule rather than in a request (Features 2): sending emails, the morning reminders, clearing
/// out old Inbox rows. One instance per run, from its own scope.
/// </summary>
public interface IJob
{
    /// <summary>A short, stable name: it appears in logs and names the job's lock.</summary>
    string Name { get; }
    /// <summary>How long to leave between runs.</summary>
    TimeSpan Every { get; }
    /// <summary>Does the work that is due. Returns what it did in a few words, or null when there was nothing to do.</summary>
    Task<string?> RunAsync(CancellationToken ct);
}

/// <summary>What became of one job in a pass.</summary>
/// <param name="Ran">False when another copy of the app was running it (it holds the job's lock).</param>
public sealed record JobResult(string Name, bool Ran, string? Summary, string? Error);

/// <summary>
/// The small job runner inside the API. A timer (Jobs:PollSeconds) runs each <see cref="IJob"/> when its interval has
/// passed. Each run takes a PostgreSQL advisory lock named after the job, so with several copies of the app only one
/// runs a job at a time; the others skip that pass. A job that throws is logged and tried again on its next turn; it
/// never stops the others or the app. Hosts that freeze the process between requests can call POST /jobs/run from a
/// scheduler instead (<see cref="JobEndpoints"/>).
/// </summary>
public sealed class JobRunner(IServiceScopeFactory scopes, NotificationOptions options, TimeProvider time, ILogger<JobRunner> log) : BackgroundService
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastRun = new();
    private readonly SemaphoreSlim _pass = new(1, 1);

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        if (!options.JobsEnabled) return;
        // Let the app finish starting (migrations run before the host starts, but the first requests come first).
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.JobsPollSeconds));
        try
        {
            while (await timer.WaitForNextTickAsync(stop))
            {
                try
                {
                    await RunAsync(onlyDue: true, stop);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    log.LogError(e, "The job runner's pass failed; it tries again in {Seconds} s.", options.JobsPollSeconds);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // The app is stopping.
        }
    }

    /// <summary>Runs every job now, whatever its interval (POST /jobs/run, and tests).</summary>
    public Task<List<JobResult>> RunAllAsync(CancellationToken ct = default) => RunAsync(onlyDue: false, ct);

    /// <summary>Runs one job now, by name.</summary>
    public async Task<JobResult> RunAsync(string name, CancellationToken ct = default)
    {
        await _pass.WaitAsync(ct);
        try
        {
            return await RunOneAsync(name, ct);
        }
        finally
        {
            _pass.Release();
        }
    }

    private async Task<List<JobResult>> RunAsync(bool onlyDue, CancellationToken ct)
    {
        // One pass at a time in this process: a slow mail server must not stack passes up.
        await _pass.WaitAsync(ct);
        try
        {
            List<(string Name, TimeSpan Every)> jobs;
            await using (var scope = scopes.CreateAsyncScope())
                jobs = scope.ServiceProvider.GetServices<IJob>().Select(j => (j.Name, j.Every)).ToList();
            var results = new List<JobResult>();
            foreach (var (name, every) in jobs)
            {
                ct.ThrowIfCancellationRequested();
                if (onlyDue && _lastRun.TryGetValue(name, out var last) && time.GetUtcNow() - last < every) continue;
                results.Add(await RunOneAsync(name, ct));
            }
            return results;
        }
        finally
        {
            _pass.Release();
        }
    }

    private async Task<JobResult> RunOneAsync(string name, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var job = scope.ServiceProvider.GetServices<IJob>().FirstOrDefault(j => j.Name == name)
                  ?? throw new InvalidOperationException($"There is no job named {name}.");
        var db = scope.ServiceProvider.GetRequiredService<TaskloomDbContext>();
        var key = LockKey(name);
        var started = Stopwatch.GetTimestamp();
        // The lock belongs to the connection, so the connection stays open for the whole run.
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            if (!await db.Database.SqlQuery<bool>($"SELECT pg_try_advisory_lock({key}) AS \"Value\"").SingleAsync(ct))
                return new JobResult(name, false, null, null);
            try
            {
                var summary = await job.RunAsync(ct);
                _lastRun[name] = time.GetUtcNow();
                if (summary is not null)
                    log.LogInformation("Job {Job}: {Summary} ({Milliseconds} ms)", name, summary, (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                return new JobResult(name, true, summary, null);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                // Counted as a run, so a job that keeps failing waits its interval rather than failing every pass.
                _lastRun[name] = time.GetUtcNow();
                log.LogError(e, "Job {Job} failed; it runs again on its next turn.", name);
                return new JobResult(name, true, null, e.Message);
            }
            finally
            {
                await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_unlock({key})", CancellationToken.None);
            }
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    /// <summary>A lock number that is the same in every copy of the app: the first 8 bytes of SHA-256 of the job's name.</summary>
    public static long LockKey(string name) => BitConverter.ToInt64(SHA256.HashData(Encoding.UTF8.GetBytes("taskloom:job:" + name)), 0);
}

public static class JobEndpoints
{
    public const string KeyHeader = "X-Taskloom-Jobs-Key";

    /// <summary>The jobs and their runner. Tests turn the timer off (Jobs:Enabled) and run jobs by hand.</summary>
    public static IServiceCollection AddTaskloomJobs(this IServiceCollection services)
    {
        // In this order, so a morning's reminders are emailed in the pass that writes them.
        services.AddScoped<IJob, ReminderJob>();
        services.AddScoped<IJob, EmailJob>();
        services.AddScoped<IJob, PruneJob>();
        services.AddSingleton<JobRunner>();
        services.AddHostedService(sp => sp.GetRequiredService<JobRunner>());
        return services;
    }

    /// <summary>
    /// POST /jobs/run: runs every job now and says what each did. For a scheduler on hosts where the timer can't be
    /// relied on. It exists only when Jobs:TriggerKey is set, and answers 404 to any other key, like a path that
    /// isn't there. It takes no cookie, so it is outside /api.
    /// </summary>
    public static void MapJobEndpoints(this WebApplication app)
    {
        var options = app.Services.GetRequiredService<NotificationOptions>();
        if (options.JobsTriggerKey.Length == 0) return;
        var expected = Encoding.UTF8.GetBytes(options.JobsTriggerKey);
        app.MapPost("/jobs/run", async (HttpContext http, JobRunner runner, CancellationToken ct) =>
        {
            var sent = Encoding.UTF8.GetBytes(http.Request.Headers[KeyHeader].ToString());
            if (!CryptographicOperations.FixedTimeEquals(sent, expected)) return Results.NotFound();
            var results = await runner.RunAllAsync(ct);
            return Results.Ok(results);
        });
    }
}
