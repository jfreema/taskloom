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

/// <summary>Outbound email (notifications, invites). Not wired to a provider yet.</summary>
public interface IEmailSender
{
    Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default);
}
