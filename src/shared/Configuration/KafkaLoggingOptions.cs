using Microsoft.Extensions.Logging;

namespace Cmdb2MonitoringServiceSuppression.Shared.Configuration;

public sealed class KafkaLoggingOptions
{
    public const string SectionName = "KafkaLogging";

    public bool Enabled { get; init; }

    public string Topic { get; init; } = "";

    public string MinimumLevel { get; init; } = "Information";

    public string ServiceName { get; init; } = "";

    public string Environment { get; init; } = "Production";

    public int QueueCapacity { get; init; } = 1000;

    public int FlushTimeoutMs { get; init; } = 5000;

    public bool HasValidMinimumLevel()
    {
        return Enum.TryParse<LogLevel>(MinimumLevel, ignoreCase: true, out _);
    }

    public LogLevel GetMinimumLevel()
    {
        return Enum.Parse<LogLevel>(MinimumLevel, ignoreCase: true);
    }

    public bool HasValidTopic()
    {
        return !Enabled || !string.IsNullOrWhiteSpace(Topic);
    }
}
