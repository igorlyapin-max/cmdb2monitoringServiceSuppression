using Microsoft.Extensions.Logging;

namespace Cmdb2MonitoringServiceSuppression.Shared.Configuration;

public sealed class ElkLoggingOptions
{
    public const string SectionName = "ElkLogging";

    public bool Enabled { get; init; }

    public string Endpoint { get; init; } = "";

    public string Index { get; init; } = "";

    public string ApiKey { get; init; } = "";

    public string MinimumLevel { get; init; } = "Information";

    public string ServiceName { get; init; } = "";

    public string Environment { get; init; } = "Production";

    public int TimeoutMs { get; init; } = 5000;

    public int QueueCapacity { get; init; } = 1000;

    public int FlushTimeoutMs { get; init; } = 5000;

    public bool IsActive()
    {
        return Enabled && !string.IsNullOrWhiteSpace(Endpoint);
    }

    public bool HasValidMinimumLevel()
    {
        return Enum.TryParse<LogLevel>(MinimumLevel, ignoreCase: true, out _);
    }

    public LogLevel GetMinimumLevel()
    {
        return Enum.Parse<LogLevel>(MinimumLevel, ignoreCase: true);
    }

    public bool HasValidEndpoint()
    {
        return !IsActive() || Uri.TryCreate(Endpoint, UriKind.Absolute, out _);
    }
}
