namespace Cmdb2MonitoringServiceSuppression.Shared.Configuration;

public sealed class SemanticDeduplicationOptions
{
    public const string SectionName = "SemanticDeduplication";

    public bool Enabled { get; init; } = true;

    public int WindowSeconds { get; init; } = 3600;

    public int MaxEntries { get; init; } = 50000;

    public bool HasValidWindow()
    {
        return WindowSeconds > 0;
    }

    public bool HasValidMaxEntries()
    {
        return MaxEntries > 0;
    }
}
