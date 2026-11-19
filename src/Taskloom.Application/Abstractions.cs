namespace Taskloom.Application;

/// <summary>
/// Blob storage for attachments. Implementations live in adapter projects
/// (Taskloom.Infrastructure for local disk, Taskloom.Cloud.Azure for Blob Storage),
/// so the app never references a cloud SDK directly.
/// </summary>
public interface IFileStore
{
    Task SaveAsync(string key, Stream content, string contentType, CancellationToken ct = default);
    Task<Stream?> OpenReadAsync(string key, CancellationToken ct = default);
    Task DeleteAsync(string key, CancellationToken ct = default);
}

/// <summary>One email to one person, as HTML with the same content in plain text.</summary>
/// <param name="To">The recipient's address.</param>
/// <param name="ToName">Their display name, for the To header.</param>
public sealed record EmailMessage(string To, string? ToName, string Subject, string Html, string Text);

/// <summary>
/// Outbound email (notifications and due-date reminders). Implementations live in adapter projects
/// (Taskloom.Infrastructure has one that only logs and one for any SMTP server), picked by Email:Provider, so the app
/// never references a mail service directly. A send that fails throws; the caller retries later.
/// </summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken ct = default);
}
