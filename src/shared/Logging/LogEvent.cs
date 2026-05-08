using Microsoft.Extensions.Logging;

namespace Cmdb2MonitoringServiceSuppression.Shared.Logging;

internal sealed record LogEvent(
    DateTimeOffset Timestamp,
    string Level,
    string Category,
    int EventId,
    string? EventName,
    string Message,
    string? Exception,
    string Service,
    string Environment)
{
    public static LogEvent From(
        string category,
        string service,
        string environment,
        LogLevel level,
        EventId eventId,
        string message,
        Exception? exception)
    {
        return new LogEvent(
            DateTimeOffset.UtcNow,
            level.ToString(),
            category,
            eventId.Id,
            eventId.Name,
            message,
            exception?.ToString(),
            service,
            environment);
    }
}

internal sealed class NullLogger : ILogger
{
    public static readonly NullLogger Instance = new();

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull
    {
        return null;
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return false;
    }

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
    }
}
