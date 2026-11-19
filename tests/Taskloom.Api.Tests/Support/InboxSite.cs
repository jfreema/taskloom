using System.Collections.Concurrent;
using Taskloom.Api.Jobs;
using Taskloom.Application;

namespace Taskloom.Api.Tests.Support;

/// <summary>An email sender that keeps what it is handed, and can be told to fail.</summary>
public sealed class Outbox : IEmailSender
{
    private readonly ConcurrentQueue<EmailMessage> _sent = new();

    /// <summary>When set and it returns true for a message, sending that message throws.</summary>
    public Func<EmailMessage, bool>? Fails { get; set; }
    public int Attempts;

    public IReadOnlyList<EmailMessage> Sent => [.. _sent];
    public List<EmailMessage> To(string email) => _sent.Where(m => m.To == email).ToList();

    public Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        Interlocked.Increment(ref Attempts);
        if (Fails?.Invoke(message) == true) throw new IOException("The mail server said no.");
        _sent.Enqueue(message);
        return Task.CompletedTask;
    }
}

/// <summary>
/// The API with a clock the test moves, an <see cref="Outbox"/> instead of a mail server, and the job timer off: tests
/// run a job by name. Requests stamp rows with the real time; the jobs read this clock, which starts at the real time
/// and only moves forward. Email links point at https://tasks.example.test.
/// </summary>
public class InboxSite : TaskloomFactory
{
    public const string PublicUrl = "https://tasks.example.test";

    public TestClock Clock { get; } = new();
    public Outbox Outbox { get; } = new();

    public InboxSite()
    {
        Settings["App:PublicUrl"] = PublicUrl;
        TestServices = Register;
    }

    protected virtual void Register(IServiceCollection services)
    {
        services.AddSingleton<TimeProvider>(Clock);
        services.AddSingleton<IEmailSender>(Outbox);
    }

    public Task<JobResult> RunJobAsync(string name) => Services.GetRequiredService<JobRunner>().RunAsync(name);

    /// <summary>Lets the short wait before an assignment or mention is emailed pass, then runs the email job.</summary>
    public Task<JobResult> SendEmailsAsync()
    {
        Clock.Advance(TimeSpan.FromSeconds(61));
        return RunJobAsync("emails");
    }
}
