using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace DisputePortal.IntegrationTests;

/// <summary>Captures every log entry a host writes (category, event, rendered message), for audit-log assertions.</summary>
public sealed class RecordingLoggerProvider : ILoggerProvider
{
    public ConcurrentQueue<(string Category, EventId EventId, LogLevel Level, string Message)> Entries { get; } = new();

    public ILogger CreateLogger(string categoryName) => new RecordingLogger(categoryName, Entries);

    public void Dispose()
    {
    }

    private sealed class RecordingLogger(string category, ConcurrentQueue<(string, EventId, LogLevel, string)> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            entries.Enqueue((category, eventId, logLevel, formatter(state, exception)));
    }
}
