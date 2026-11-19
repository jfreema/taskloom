using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using Taskloom.Application;

namespace Taskloom.Infrastructure;

/// <summary>How to reach the mail server (the Email section of the configuration).</summary>
public sealed class SmtpOptions
{
    /// <summary>The server's name, such as smtp.gmail.com or smtp.office365.com.</summary>
    public string Host { get; set; } = "";
    /// <summary>587 (STARTTLS) for most services; 465 for TLS from the first byte; 25 or 1025 for a local test server.</summary>
    public int Port { get; set; } = 587;
    /// <summary>
    /// "Auto" (the default) uses TLS from the first byte on port 465 and STARTTLS on any other port, and refuses a
    /// server that offers neither. "None" sends in the clear, for a test server on your own machine only.
    /// </summary>
    public string Security { get; set; } = "Auto";
    /// <summary>Leave empty for a server that takes mail without sign-in.</summary>
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public int TimeoutSeconds { get; set; } = 20;

    public SecureSocketOptions SocketOptions => Security.Trim().ToLowerInvariant() switch
    {
        "none" => SecureSocketOptions.None,
        "starttls" => SecureSocketOptions.StartTls,
        "ssl" or "tls" or "sslonconnect" => SecureSocketOptions.SslOnConnect,
        "auto" or "" => Port == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls,
        _ => throw new InvalidOperationException("Email:Smtp:Security must be Auto, StartTls, Ssl or None."),
    };
}

/// <summary>
/// Sends through any SMTP server (Email:Provider "Smtp"): Gmail with an app password, Microsoft 365, Azure
/// Communication Services, SendGrid, Amazon SES, or a test server such as Mailpit. One connection per email, which is
/// plenty for notifications; the certificate is always checked.
/// </summary>
public sealed class SmtpEmailSender(SmtpOptions options, MailboxAddress from) : IEmailSender
{
    /// <summary>The sender for these settings; throws, naming the setting, when one is missing or malformed.</summary>
    public static SmtpEmailSender Create(SmtpOptions options, string? from)
    {
        if (string.IsNullOrWhiteSpace(options.Host)) throw new InvalidOperationException("Email:Provider is Smtp, so set Email:Smtp:Host to the mail server's name.");
        if (options.Port is < 1 or > 65535) throw new InvalidOperationException("Email:Smtp:Port must be a port number, usually 587.");
        if (string.IsNullOrWhiteSpace(from) || !MailboxAddress.TryParse(from, out var address))
            throw new InvalidOperationException("Email:Provider is Smtp, so set Email:From to the address emails come from, like \"Taskloom <taskloom@example.com>\".");
        _ = options.SocketOptions;
        return new SmtpEmailSender(options, address);
    }

    public async Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        var mime = new MimeMessage();
        mime.From.Add(from);
        mime.To.Add(new MailboxAddress(message.ToName ?? "", message.To));
        mime.Subject = message.Subject;
        // Marks it as machine-sent, so out-of-office replies and the like stay away.
        mime.Headers.Add("Auto-Submitted", "auto-generated");
        mime.Body = new BodyBuilder { TextBody = message.Text, HtmlBody = message.Html }.ToMessageBody();

        using var client = new SmtpClient { Timeout = options.TimeoutSeconds * 1000 };
        await client.ConnectAsync(options.Host, options.Port, options.SocketOptions, ct);
        if (options.Username.Length > 0) await client.AuthenticateAsync(options.Username, options.Password, ct);
        await client.SendAsync(mime, ct);
        await client.DisconnectAsync(true, ct);
    }
}
