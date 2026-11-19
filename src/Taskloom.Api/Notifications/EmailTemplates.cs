using System.Globalization;
using System.Net;
using System.Text;
using Taskloom.Application;
using Taskloom.Domain;

namespace Taskloom.Api.Notifications;

/// <summary>One Inbox row as an email needs it.</summary>
/// <param name="Comment">The whole comment of a Mentioned row, mentions written as names.</param>
public sealed record EmailItem(
    NotificationKind Kind, Guid TaskId, string TaskTitle, string ListName, DateOnly? DueDate, string? ActorName, string? Comment);

/// <summary>
/// The emails Taskloom sends (Features 2): one for a single assignment or mention, a short list for several, and the
/// morning reminder. Each is HTML with the same content as plain text. Everything a person typed (titles, names,
/// comments) is HTML-encoded, and subjects are kept to one line. Links appear only when App:PublicUrl is set.
/// </summary>
public static class EmailTemplates
{
    private const string Accent = "#0D7469", Ink = "#161B1D", Muted = "#646E72", Line = "#E3E7E8";

    /// <summary>What happened to the person's tasks: assignments and mentions (at least one).</summary>
    /// <param name="more">How many more there are than <paramref name="items"/> lists.</param>
    public static EmailMessage Activity(string to, string toName, IReadOnlyList<EmailItem> items, NotificationOptions options, int more = 0)
    {
        string subject;
        var html = new StringBuilder();
        var text = new StringBuilder();
        if (items.Count == 1 && more == 0)
        {
            var item = items[0];
            subject = $"{Actor(item)} {(item.Kind == NotificationKind.Mentioned ? "mentioned you in" : "assigned you")} “{item.TaskTitle}”";
            AppendItem(html, text, item, options, heading: true);
        }
        else
        {
            var actors = items.Select(Actor).Distinct().ToList();
            var count = items.Count + more;
            subject = actors.Count == 1 && more == 0 ? $"{actors[0]}: {count} updates for you in Taskloom" : $"{count} updates for you in Taskloom";
            foreach (var item in items) AppendItem(html, text, item, options, heading: true);
            More(html, text, more);
        }
        return Wrap(to, toName, subject, html, text, options,
            "You get this email when someone assigns you a task or mentions you.");
    }

    /// <summary>The morning reminder: tasks due today, then tasks that became overdue (at least one of either).</summary>
    /// <param name="more">How many more there are than <paramref name="items"/> lists.</param>
    public static EmailMessage Reminder(string to, string toName, IReadOnlyList<EmailItem> items, NotificationOptions options, int more = 0)
    {
        var today = items.Where(i => i.Kind == NotificationKind.DueToday).ToList();
        var overdue = items.Where(i => i.Kind == NotificationKind.Overdue).OrderBy(i => i.DueDate).ToList();
        var parts = new List<string>();
        if (today.Count > 0) parts.Add($"{Count(today.Count)} due today");
        if (overdue.Count > 0) parts.Add(today.Count > 0 ? $"{overdue.Count} overdue" : $"{Count(overdue.Count)} overdue");
        if (more > 0) parts.Add($"{more} more");
        var subject = string.Join(", ", parts);

        var html = new StringBuilder();
        var text = new StringBuilder();
        Section(html, text, "Due today", today, options, showDate: false);
        Section(html, text, "Overdue", overdue, options, showDate: true);
        More(html, text, more);
        return Wrap(to, toName, subject, html, text, options, "You get this email on mornings when a task of yours is due or has become overdue.");
    }

    private static void More(StringBuilder html, StringBuilder text, int more)
    {
        if (more <= 0) return;
        html.Append($"<p style=\"margin:0 0 20px;color:{Muted};font-size:14px\">And {more} more in your Inbox.</p>");
        text.Append($"And {more} more in your Inbox.\n\n");
    }

    private static string Count(int n) => n == 1 ? "1 task" : $"{n} tasks";

    private static string Actor(EmailItem item) => string.IsNullOrWhiteSpace(item.ActorName) ? "Someone" : item.ActorName;

    private static void AppendItem(StringBuilder html, StringBuilder text, EmailItem item, NotificationOptions options, bool heading)
    {
        var link = options.Link($"/t/{item.TaskId}");
        var what = item.Kind == NotificationKind.Mentioned ? "mentioned you in" : "assigned you";
        var meta = item.DueDate is { } due ? $"{item.ListName} · due {Day(due)}" : item.ListName;

        html.Append("<div style=\"margin:0 0 20px\">");
        if (heading)
            html.Append($"<p style=\"margin:0 0 6px;color:{Muted};font-size:14px\"><b style=\"color:{Ink}\">{E(Actor(item))}</b> {what}</p>");
        html.Append($"<p style=\"margin:0;font-size:17px;font-weight:600;line-height:1.35\">{TitleLink(item.TaskTitle, link)}</p>");
        html.Append($"<p style=\"margin:4px 0 0;color:{Muted};font-size:13px\">{E(meta)}</p>");
        if (item.Comment is { Length: > 0 } comment)
            html.Append($"<p style=\"margin:12px 0 0;padding:10px 14px;border-left:3px solid {Accent};background:#F3F5F5;border-radius:4px;white-space:pre-wrap;font-size:15px\">{E(comment)}</p>");
        html.Append("</div>");

        text.Append($"{Actor(item)} {what} \"{item.TaskTitle}\" ({meta})\n");
        if (item.Comment is { Length: > 0 }) text.Append('\n').Append(item.Comment).Append('\n');
        if (link is not null) text.Append(link).Append('\n');
        text.Append('\n');
    }

    private static void Section(StringBuilder html, StringBuilder text, string title, List<EmailItem> items, NotificationOptions options, bool showDate)
    {
        if (items.Count == 0) return;
        html.Append($"<p style=\"margin:0 0 8px;font-size:12px;font-weight:600;letter-spacing:.05em;text-transform:uppercase;color:{Muted}\">{title}</p>");
        html.Append("<table role=\"presentation\" cellpadding=\"0\" cellspacing=\"0\" style=\"width:100%;margin:0 0 20px;border-collapse:collapse\">");
        text.Append(title.ToUpperInvariant()).Append('\n');
        foreach (var item in items)
        {
            var link = options.Link($"/t/{item.TaskId}");
            var meta = showDate && item.DueDate is { } due ? $"{item.ListName} · was due {Day(due)}" : item.ListName;
            html.Append($"<tr><td style=\"padding:9px 0;border-top:1px solid {Line}\">");
            html.Append($"<div style=\"font-size:15px;font-weight:600;line-height:1.35\">{TitleLink(item.TaskTitle, link)}</div>");
            html.Append($"<div style=\"margin-top:2px;color:{Muted};font-size:13px\">{E(meta)}</div></td></tr>");
            text.Append($"- {item.TaskTitle} ({meta})\n");
            if (link is not null) text.Append("  ").Append(link).Append('\n');
        }
        html.Append("</table>");
        text.Append('\n');
    }

    private static string TitleLink(string title, string? link) =>
        link is null ? $"<span style=\"color:{Ink}\">{E(title)}</span>" : $"<a href=\"{E(link)}\" style=\"color:{Accent};text-decoration:none\">{E(title)}</a>";

    private static EmailMessage Wrap(string to, string toName, string subject, StringBuilder body, StringBuilder text, NotificationOptions options, string why)
    {
        var inbox = options.Link("/inbox");
        var settings = options.Link("/settings/notifications");
        var html = new StringBuilder();
        html.Append("<!doctype html><html><body style=\"margin:0;padding:0;background:#F3F5F5\">");
        html.Append($"<div style=\"max-width:560px;margin:0 auto;padding:28px 20px;font-family:-apple-system,'Segoe UI',Roboto,Helvetica,Arial,sans-serif;color:{Ink};font-size:15px;line-height:1.5\">");
        html.Append($"<p style=\"margin:0 0 18px;font-size:13px;font-weight:600;color:{Accent}\">Taskloom</p>");
        html.Append($"<div style=\"background:#FFFFFF;border:1px solid {Line};border-radius:12px;padding:22px 22px 4px\">").Append(body).Append("</div>");
        if (inbox is not null)
            html.Append($"<p style=\"margin:18px 0 0\"><a href=\"{E(inbox)}\" style=\"display:inline-block;padding:9px 16px;border-radius:8px;background:{Accent};color:#FFFFFF;font-weight:600;font-size:14px;text-decoration:none\">Open your Inbox</a></p>");
        html.Append($"<p style=\"margin:18px 0 0;color:{Muted};font-size:12.5px\">{E(why)} ");
        html.Append(settings is null
            ? "You can turn it off in Taskloom under Settings, Notifications."
            : $"<a href=\"{E(settings)}\" style=\"color:{Muted}\">Change what Taskloom emails you</a>.");
        html.Append("</p></div></body></html>");

        text.Append(why).Append(' ');
        text.Append(settings is null ? "You can turn it off in Taskloom under Settings, Notifications." : $"Change what Taskloom emails you: {settings}");
        text.Append('\n');
        return new EmailMessage(to, OneLine(toName, 200), OneLine(subject, 200), html.ToString(), text.ToString());
    }

    /// <summary>"Wed, Nov 18", always in English like the rest of the app.</summary>
    private static string Day(DateOnly date) => date.ToString("ddd, MMM d", CultureInfo.InvariantCulture);

    private static string E(string value) => WebUtility.HtmlEncode(value);

    /// <summary>One line without control characters (a header can't carry a line break), cut to <paramref name="max"/>.</summary>
    public static string OneLine(string value, int max)
    {
        var line = string.Concat(value.Select(c => char.IsControl(c) ? ' ' : c)).Trim();
        return line.Length > max ? Text.Truncate(line, max - 1).TrimEnd() + "…" : line;
    }
}
