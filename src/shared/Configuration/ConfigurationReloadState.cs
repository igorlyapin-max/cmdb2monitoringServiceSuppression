namespace Cmdb2MonitoringServiceSuppression.Shared.Configuration;

public sealed class ConfigurationReloadState
{
    private readonly object sync = new();
    private long version = 1;
    private DateTimeOffset? lastReloadedAtUtc;

    public DateTimeOffset StartedAtUtc { get; } = DateTimeOffset.UtcNow;

    public ConfigurationReloadSnapshot Snapshot()
    {
        lock (sync)
        {
            return new ConfigurationReloadSnapshot(version, StartedAtUtc, lastReloadedAtUtc);
        }
    }

    public ConfigurationReloadSnapshot MarkReloaded()
    {
        lock (sync)
        {
            version++;
            lastReloadedAtUtc = DateTimeOffset.UtcNow;
            return new ConfigurationReloadSnapshot(version, StartedAtUtc, lastReloadedAtUtc);
        }
    }
}

public sealed record ConfigurationReloadSnapshot(
    long Version,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? LastReloadedAtUtc);
