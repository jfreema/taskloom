using Microsoft.Extensions.Logging;
using Taskloom.Application;

namespace Taskloom.Infrastructure;

/// <summary>
/// Writes each email's recipient and subject to the log instead of sending it: the default (Email:Provider "Log"), so a
/// fresh install never mails anyone. Set Email:Provider to "Smtp" to send for real (<see cref="SmtpEmailSender"/>).
/// </summary>
public class LoggingEmailSender(ILogger<LoggingEmailSender> logger) : IEmailSender
{
    public Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        logger.LogInformation("Email to {To} (not sent, Email:Provider is Log): {Subject}", message.To, message.Subject);
        logger.LogDebug("{Text}", message.Text);
        return Task.CompletedTask;
    }
}
