using Taskloom.Infrastructure;

namespace Taskloom.Api.Notifications;

/// <summary>
/// Settings for notifications, their emails and the jobs that send them (the App, Email, Reminders, Notifications and
/// Jobs sections of the configuration).
/// </summary>
public sealed class NotificationOptions
{
    /// <summary>
    /// App:PublicUrl: where people open Taskloom ("https://tasks.example.com"), without a trailing slash, for the links
    /// in emails. Empty leaves the links out: a job has no request to read the address from, and guessing it from a
    /// request's Host header would let anyone who can send one put their own address in other people's email.
    /// </summary>
    public string PublicUrl { get; init; } = "";

    /// <summary>Email:Provider is something that really sends ("Smtp"), not the default "Log".</summary>
    public bool EmailConfigured { get; init; }

    /// <summary>
    /// Email:DelaySeconds (60): how long an assignment or mention waits before it is emailed. Read in Taskloom in that
    /// time, it is never emailed, and several in a row go out as one email.
    /// </summary>
    public TimeSpan EmailDelay { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>Reminders:Hour (8): the hour of each person's own day, 0 to 23, from which their morning reminder goes out.</summary>
    public int ReminderHour { get; init; } = 8;

    /// <summary>Notifications:KeepDays (90): Inbox rows older than this are deleted.</summary>
    public int KeepDays { get; init; } = 90;

    /// <summary>Jobs:Enabled (true): run the jobs inside this process on a timer.</summary>
    public bool JobsEnabled { get; init; } = true;

    /// <summary>Jobs:PollSeconds (30): how often the timer looks for jobs that are due.</summary>
    public int JobsPollSeconds { get; init; } = 30;

    /// <summary>
    /// Jobs:TriggerKey: when set, POST /jobs/run with this value in X-Taskloom-Jobs-Key runs every job now. For hosts
    /// that stop the process between requests (Cloud Run), where a scheduler calls it instead of the timer.
    /// </summary>
    public string JobsTriggerKey { get; init; } = "";

    public static NotificationOptions From(IConfiguration config)
    {
        var url = (config["App:PublicUrl"] ?? "").Trim().TrimEnd('/');
        if (url.Length > 0 && !(Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" && uri.Query.Length == 0 && uri.Fragment.Length == 0))
            throw new InvalidOperationException("App:PublicUrl must be the address people open Taskloom at, like https://tasks.example.com.");
        var hour = config.GetValue("Reminders:Hour", 8);
        if (hour is < 0 or > 23) throw new InvalidOperationException("Reminders:Hour must be an hour from 0 to 23.");
        return new NotificationOptions
        {
            PublicUrl = url,
            EmailConfigured = !string.Equals(EmailProvider(config), "log", StringComparison.OrdinalIgnoreCase),
            EmailDelay = TimeSpan.FromSeconds(Math.Clamp(config.GetValue("Email:DelaySeconds", 60), 0, 3600)),
            ReminderHour = hour,
            KeepDays = Math.Max(1, config.GetValue("Notifications:KeepDays", 90)),
            JobsEnabled = config.GetValue("Jobs:Enabled", true),
            JobsPollSeconds = Math.Clamp(config.GetValue("Jobs:PollSeconds", 30), 1, 3600),
            JobsTriggerKey = (config["Jobs:TriggerKey"] ?? "").Trim(),
        };
    }

    public static string EmailProvider(IConfiguration config)
    {
        var provider = (config["Email:Provider"] ?? "").Trim();
        return provider.Length == 0 ? "Log" : provider;
    }

    /// <summary>The address of a page of the app, or null when App:PublicUrl isn't set.</summary>
    public string? Link(string path) => PublicUrl.Length == 0 ? null : PublicUrl + path;
}

public static class EmailSetup
{
    /// <summary>The email sender Email:Provider names: "Log" (the default) or "Smtp". Anything else stops the app at startup.</summary>
    public static IServiceCollection AddTaskloomEmail(this IServiceCollection services, IConfiguration config)
    {
        var provider = NotificationOptions.EmailProvider(config);
        switch (provider.ToLowerInvariant())
        {
            case "log":
                services.AddSingleton<Application.IEmailSender, LoggingEmailSender>();
                break;
            case "smtp":
                var smtp = config.GetSection("Email:Smtp").Get<SmtpOptions>() ?? new SmtpOptions();
                services.AddSingleton<Application.IEmailSender>(SmtpEmailSender.Create(smtp, config["Email:From"]));
                break;
            default:
                throw new InvalidOperationException($"Email:Provider is \"{provider}\"; use Log or Smtp.");
        }
        return services;
    }
}
