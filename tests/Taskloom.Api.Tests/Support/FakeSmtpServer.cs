using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Taskloom.Api.Tests.Support;

/// <summary>
/// A mail server on a free local port that speaks just enough SMTP to take a message, without TLS, and keeps what it
/// was sent: each message's envelope and its text as it came over the wire.
/// </summary>
public sealed class FakeSmtpServer : IAsyncDisposable
{
    public sealed record Received(string From, List<string> To, string Data);

    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _loop;
    private readonly List<Received> _received = [];

    /// <summary>When set, the server refuses every recipient with this reply ("550 No such user").</summary>
    public string? RefuseRecipients { get; set; }
    /// <summary>The user name and password AUTH PLAIN was given, when the client signed in.</summary>
    public string? Credentials { get; private set; }
    /// <summary>Offer AUTH, so a client with a user name signs in.</summary>
    public bool OffersAuth { get; init; }

    public int Port { get; }
    public IReadOnlyList<Received> Messages
    {
        get { lock (_received) return [.. _received]; }
    }

    public FakeSmtpServer()
    {
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _loop = Task.Run(AcceptAsync);
    }

    private async Task AcceptAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                _ = Task.Run(() => ServeAsync(client));
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task ServeAsync(TcpClient client)
    {
        using var _ = client;
        await using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.Latin1);
        await using var writer = new StreamWriter(stream, Encoding.Latin1) { NewLine = "\r\n", AutoFlush = true };
        await writer.WriteLineAsync("220 fake.local ESMTP");
        string from = "";
        var to = new List<string>();
        while (await reader.ReadLineAsync() is { } line)
        {
            var verb = line.Split(' ')[0].ToUpperInvariant();
            switch (verb)
            {
                case "EHLO":
                    await writer.WriteLineAsync("250-fake.local");
                    if (OffersAuth) await writer.WriteLineAsync("250-AUTH PLAIN");
                    await writer.WriteLineAsync("250 8BITMIME");
                    break;
                case "HELO":
                    await writer.WriteLineAsync("250 fake.local");
                    break;
                case "AUTH":
                    var parts = line.Split(' ');
                    var blob = parts.Length > 2 ? parts[2] : "";
                    if (blob.Length == 0)
                    {
                        await writer.WriteLineAsync("334 ");
                        blob = await reader.ReadLineAsync() ?? "";
                    }
                    Credentials = Encoding.UTF8.GetString(Convert.FromBase64String(blob)).Replace('\0', ':').TrimStart(':');
                    await writer.WriteLineAsync("235 2.7.0 Signed in");
                    break;
                case "MAIL":
                    from = Address(line);
                    to = [];
                    await writer.WriteLineAsync("250 OK");
                    break;
                case "RCPT":
                    if (RefuseRecipients is { } refusal) await writer.WriteLineAsync(refusal);
                    else
                    {
                        to.Add(Address(line));
                        await writer.WriteLineAsync("250 OK");
                    }
                    break;
                case "DATA":
                    await writer.WriteLineAsync("354 Go ahead");
                    var data = new StringBuilder();
                    while (await reader.ReadLineAsync() is { } dataLine && dataLine != ".")
                        data.Append(dataLine.StartsWith("..") ? dataLine[1..] : dataLine).Append("\r\n");
                    lock (_received) _received.Add(new Received(from, to, data.ToString()));
                    await writer.WriteLineAsync("250 OK queued");
                    break;
                case "RSET":
                case "NOOP":
                    await writer.WriteLineAsync("250 OK");
                    break;
                case "QUIT":
                    await writer.WriteLineAsync("221 Bye");
                    return;
                default:
                    await writer.WriteLineAsync("502 Not implemented");
                    break;
            }
        }
    }

    private static string Address(string line)
    {
        int open = line.IndexOf('<'), close = line.IndexOf('>');
        return open >= 0 && close > open ? line[(open + 1)..close] : "";
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        _listener.Stop();
        try { await _loop; } catch (Exception) { /* the listener was stopped */ }
        _stop.Dispose();
    }
}
