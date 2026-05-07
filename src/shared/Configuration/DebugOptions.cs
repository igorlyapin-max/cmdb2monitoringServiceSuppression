namespace Cmdb2MonitoringServiceSuppression.Shared.Configuration;

public sealed class DebugOptions
{
    public const string SectionName = "Debug";

    public bool Enabled { get; init; }

    public string Level { get; init; } = "Basic";

    public bool HasValidLevel()
    {
        return string.Equals(Level, "Basic", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Level, "Verbose", StringComparison.OrdinalIgnoreCase);
    }
}
