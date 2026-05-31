using Cmdb2MonitoringServiceSuppression.Shared.Configuration;

namespace Cmdb2MonitoringServiceSuppression.Shared.Observability;

public static class CorrelationContext
{
    private static readonly AsyncLocal<string?> Current = new();

    public static string? CurrentId => Current.Value;

    public static IDisposable Begin(string? correlationId)
    {
        var previous = Current.Value;
        Current.Value = string.IsNullOrWhiteSpace(correlationId)
            ? null
            : correlationId.Trim();
        return new Scope(previous);
    }

    public static string NewId()
    {
        return Guid.NewGuid().ToString("N");
    }

    public static string HeaderName(string? configuredHeaderName)
    {
        return string.IsNullOrWhiteSpace(configuredHeaderName)
            ? CorrelationOptions.DefaultHeaderName
            : configuredHeaderName.Trim();
    }

    private sealed class Scope(string? previous) : IDisposable
    {
        private bool disposed;

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            Current.Value = previous;
            disposed = true;
        }
    }
}
