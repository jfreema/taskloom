using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Taskloom.Api.Tests.Support;

/// <summary>A logger provider that keeps every entry the app writes (after the app's own level filters), for tests to read.</summary>
public sealed class LogCapture : ILoggerProvider
{
    private readonly ConcurrentQueue<LogEntry> _entries = new();

    public IReadOnlyList<LogEntry> Entries => [.. _entries];

    public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);

    public void Dispose() { }

    private sealed class Logger(LogCapture capture, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            capture._entries.Enqueue(new LogEntry(category, logLevel, formatter(state, exception)));
    }
}

public sealed record LogEntry(string Category, LogLevel Level, string Message);
