namespace Cmdb2MonitoringServiceSuppression.Shared.Configuration;

public sealed class DebugOptions
{
    public const string SectionName = "Debug";

    public bool Enabled { get; set; }

    public string Level { get; set; } = "Basic";

    public bool HasValidLevel()
    {
        return string.Equals(Level, "Basic", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Level, "Verbose", StringComparison.OrdinalIgnoreCase);
    }

    public bool IsBasicEnabled()
    {
        return Enabled;
    }

    public bool IsVerboseEnabled()
    {
        return Enabled && string.Equals(Level, "Verbose", StringComparison.OrdinalIgnoreCase);
    }

    public string NormalizedLevel()
    {
        return IsVerboseEnabled() ? "Verbose" : "Basic";
    }
}
