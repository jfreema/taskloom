using Microsoft.Extensions.Logging;
using Taskloom.Application;

namespace Taskloom.Infrastructure;

/// <summary>Writes emails to the log instead of sending them. Swap for a real sender later.</summary>
public class LoggingEmailSender(ILogger<LoggingEmailSender> logger) : IEmailSender
{
    public Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default)
    {
        logger.LogInformation("Email to {To}: {Subject}", to, subject);
        return Task.CompletedTask;
    }
}
