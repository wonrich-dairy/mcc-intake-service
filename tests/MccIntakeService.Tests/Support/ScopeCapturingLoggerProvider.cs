using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace MccIntakeService.Tests.Support;

/// <summary>
/// Records every log entry with the scope values in force when it was written, so a test can see
/// what a JSON console line would carry without parsing console output.
/// </summary>
internal sealed class ScopeCapturingLoggerProvider : ILoggerProvider, ISupportExternalScope
{
    private IExternalScopeProvider _scopes = new LoggerExternalScopeProvider();

    public ConcurrentQueue<CapturedLogEntry> Entries { get; } = new();

    public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopes = scopeProvider;

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(this, categoryName);

    public void Dispose()
    {
    }

    private sealed class CapturingLogger(ScopeCapturingLoggerProvider owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => owner._scopes.Push(state);

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var scope = new Dictionary<string, object?>();

            owner._scopes.ForEachScope(
                (value, values) =>
                {
                    if (value is IEnumerable<KeyValuePair<string, object>> pairs)
                    {
                        foreach (var pair in pairs)
                        {
                            values[pair.Key] = pair.Value;
                        }
                    }
                },
                scope);

            owner.Entries.Enqueue(new CapturedLogEntry(category, formatter(state, exception), scope));
        }
    }
}

internal sealed record CapturedLogEntry(
    string Category,
    string Message,
    IReadOnlyDictionary<string, object?> Scope);
