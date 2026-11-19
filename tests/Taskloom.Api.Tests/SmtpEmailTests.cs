using System.Reflection;
using MailKit.Net.Smtp;
using MimeKit;
using Taskloom.Api.Tests.Support;
using Taskloom.Application;
using Taskloom.Domain;
using Taskloom.Infrastructure;

namespace Taskloom.Api.Tests;

/// <summary>
/// Features 2, real email: Email:Provider "Smtp" sends through a mail server, and settings that can't work stop the app
/// at startup with a message that names them.
/// </summary>
public class SmtpEmailTests
{
    private static SmtpOptions Local(FakeSmtpServer server) => new() { Host = "127.0.0.1", Port = server.Port, Security = "None", TimeoutSeconds = 5 };

    [Fact]
    public async Task An_email_goes_through_the_mail_server_as_text_and_html()
    {
        await using var server = new FakeSmtpServer();
        var sender = SmtpEmailSender.Create(Local(server), "Taskloom <taskloom@example.com>");
        await sender.SendAsync(new EmailMessage("ana@example.com", "Ana Ångström", "Olive assigned you “Book venue”",
            "<html><body><p>Book <b>venue</b></p></body></html>", "Book venue\n.\nA line that is only a dot is still text.\n"));

        var received = Assert.Single(server.Messages);
        Assert.Equal(("taskloom@example.com", "ana@example.com"), (received.From, Assert.Single(received.To)));
        var mime = await MimeMessage.LoadAsync(new MemoryStream(System.Text.Encoding.Latin1.GetBytes(received.Data)));
        Assert.Equal(("Taskloom", "taskloom@example.com"), (mime.From.Mailboxes.Single().Name, mime.From.Mailboxes.Single().Address));
        Assert.Equal(("Ana Ångström", "ana@example.com"), (mime.To.Mailboxes.Single().Name, mime.To.Mailboxes.Single().Address));
        Assert.Equal("Olive assigned you “Book venue”", mime.Subject);
        Assert.Equal("auto-generated", mime.Headers["Auto-Submitted"]);
        Assert.Equal("Book venue\n.\nA line that is only a dot is still text.\n", mime.TextBody.ReplaceLineEndings("\n"));
        Assert.Equal("<html><body><p>Book <b>venue</b></p></body></html>", mime.HtmlBody.Trim());
        Assert.Null(server.Credentials);

        // A recipient without a name, and a second message on a new connection.
        await sender.SendAsync(new EmailMessage("ben@example.com", null, "Second", "<p>2</p>", "2"));
        Assert.Equal("ben@example.com", server.Messages[1].To.Single());
    }

    [Fact]
    public async Task It_signs_in_when_given_a_user_name_and_reports_a_refusal()
    {
        await using var server = new FakeSmtpServer { OffersAuth = true };
        var options = Local(server);
        options.Username = "apikey";
        options.Password = "s3cret";
        var sender = SmtpEmailSender.Create(options, "taskloom@example.com");
        await sender.SendAsync(new EmailMessage("ana@example.com", "Ana", "Hello", "<p>Hi</p>", "Hi"));
        Assert.Equal("apikey:s3cret", server.Credentials);
        Assert.Single(server.Messages);

        // A refusal is an exception (the email job retries on it); nothing is delivered.
        server.RefuseRecipients = "550 5.1.1 No such user";
        await Assert.ThrowsAsync<SmtpCommandException>(() => sender.SendAsync(new EmailMessage("nobody@example.com", null, "Hello", "<p>Hi</p>", "Hi")));
        Assert.Single(server.Messages);

        // So is a server that isn't there.
        var nowhere = SmtpEmailSender.Create(new SmtpOptions { Host = "127.0.0.1", Port = 1, Security = "None", TimeoutSeconds = 2 }, "taskloom@example.com");
        await Assert.ThrowsAnyAsync<Exception>(() => nowhere.SendAsync(new EmailMessage("ana@example.com", null, "Hello", "<p>Hi</p>", "Hi")));
    }

    [Fact]
    public void Settings_that_cannot_work_are_named()
    {
        static string Message(Action create) => Assert.Throws<InvalidOperationException>(create).Message;

        Assert.Equal("Email:Provider is Smtp, so set Email:Smtp:Host to the mail server's name.",
            Message(() => SmtpEmailSender.Create(new SmtpOptions(), "taskloom@example.com")));
        Assert.Equal("Email:Smtp:Port must be a port number, usually 587.",
            Message(() => SmtpEmailSender.Create(new SmtpOptions { Host = "smtp.example.com", Port = 0 }, "taskloom@example.com")));
        foreach (var from in new[] { null, "", "  ", "not an address" })
        {
            Assert.Equal("Email:Provider is Smtp, so set Email:From to the address emails come from, like \"Taskloom <taskloom@example.com>\".",
                Message(() => SmtpEmailSender.Create(new SmtpOptions { Host = "smtp.example.com" }, from)));
        }
        Assert.Equal("Email:Smtp:Security must be Auto, StartTls, Ssl or None.",
            Message(() => SmtpEmailSender.Create(new SmtpOptions { Host = "smtp.example.com", Security = "Maybe" }, "taskloom@example.com")));

        // "Auto" never sends in the clear: TLS from the first byte on 465, STARTTLS anywhere else.
        Assert.Equal(MailKit.Security.SecureSocketOptions.SslOnConnect, new SmtpOptions { Port = 465 }.SocketOptions);
        Assert.Equal(MailKit.Security.SecureSocketOptions.StartTls, new SmtpOptions { Port = 587 }.SocketOptions);
        Assert.Equal(MailKit.Security.SecureSocketOptions.StartTls, new SmtpOptions { Port = 25, Security = " auto " }.SocketOptions);
        Assert.Equal(MailKit.Security.SecureSocketOptions.None, new SmtpOptions { Port = 1025, Security = "None" }.SocketOptions);
    }

    /// <summary>Starts the app and returns the message of the exception that stopped it, or null when it started.</summary>
    private static string? StartupFailure(TaskloomFactory factory)
    {
        factory.Settings["Database:MigrateOnStartup"] = "false";
        try
        {
            _ = factory.Services;
            return null;
        }
        catch (Exception e)
        {
            while (e is TargetInvocationException or AggregateException && e.InnerException is not null) e = e.InnerException;
            return e.Message;
        }
    }

    [Fact]
    public async Task The_app_refuses_to_start_with_email_settings_that_cannot_work()
    {
        (string Key, string Value)[][] broken =
        [
            [("Email:Provider", "Smtp")],
            [("Email:Provider", "smtp"), ("Email:Smtp:Host", "smtp.example.com")],
            [("Email:Provider", "Pigeon")],
            [("App:PublicUrl", "tasks.example.com")],
            [("App:PublicUrl", "ftp://tasks.example.com")],
            [("Reminders:Hour", "24")],
        ];
        string[] messages =
        [
            "Email:Provider is Smtp, so set Email:Smtp:Host to the mail server's name.",
            "Email:Provider is Smtp, so set Email:From to the address emails come from, like \"Taskloom <taskloom@example.com>\".",
            "Email:Provider is \"Pigeon\"; use Log or Smtp.",
            "App:PublicUrl must be the address people open Taskloom at, like https://tasks.example.com.",
            "App:PublicUrl must be the address people open Taskloom at, like https://tasks.example.com.",
            "Reminders:Hour must be an hour from 0 to 23.",
        ];
        for (var i = 0; i < broken.Length; i++)
        {
            await using var factory = new NoDevLoginFactory();
            foreach (var (key, value) in broken[i]) factory.Settings[key] = value;
            Assert.Equal(messages[i], StartupFailure(factory));
        }

        // Blank values (what docker compose passes for unset variables) mean the defaults.
        await using var blank = new NoDevLoginFactory();
        foreach (var key in new[] { "Email:Provider", "Email:From", "App:PublicUrl", "Jobs:TriggerKey", "Email:Smtp:Host" }) blank.Settings[key] = "";
        Assert.Null(StartupFailure(blank));
    }

    [Fact]
    public async Task With_smtp_configured_the_app_sends_real_email_and_says_so()
    {
        await using var server = new FakeSmtpServer();
        await using var site = new TaskloomFactory();
        site.Settings["Email:Provider"] = "Smtp";
        site.Settings["Email:From"] = "Taskloom <taskloom@example.com>";
        site.Settings["Email:Smtp:Host"] = "127.0.0.1";
        site.Settings["Email:Smtp:Port"] = server.Port.ToString();
        site.Settings["Email:Smtp:Security"] = "None";
        site.Settings["Email:DelaySeconds"] = "0";
        site.Settings["App:PublicUrl"] = "https://tasks.example.test/";
        var owner = await site.NewUserAsync();
        Assert.True((await (await owner.Client.GetAsync("/api/me")).ReadAsync<MeDto>()).EmailConfigured);
        var (_, ana) = await site.JoinAsync(owner, WorkspaceRole.Member, "Ana");
        var task = await owner.Client.CreateTaskAsync(owner.List.Id, new { title = "Over the wire", assigneeIds = new[] { ana.UserId } });

        var result = await site.Services.GetRequiredService<Jobs.JobRunner>().RunAsync("emails");
        Assert.Equal("sent 1 email for 1 notification", result.Summary);
        var received = Assert.Single(server.Messages);
        Assert.Equal(ana.Email, Assert.Single(received.To));
        var mime = await MimeMessage.LoadAsync(new MemoryStream(System.Text.Encoding.Latin1.GetBytes(received.Data)));
        Assert.EndsWith("assigned you “Over the wire”", mime.Subject);
        // The trailing slash of App:PublicUrl is dropped.
        Assert.Contains($"https://tasks.example.test/t/{task.Id}", mime.TextBody);
    }
}
