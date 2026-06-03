namespace Cmdb2MonitoringServiceSuppression.Shared.Configuration;

public sealed class ReadinessOptions
{
    public const string SectionName = "Readiness";

    public string Route { get; init; } = "/ready";

    public string ZabbixHostIdAttribute { get; init; } = "zabbix_main_hostid";

    public bool CheckExternalDependencies { get; init; }

    public int CheckTimeoutMs { get; init; } = 2000;

    public bool HasValidRoute()
    {
        return !string.IsNullOrWhiteSpace(Route) && Route.StartsWith('/');
    }

    public bool HasValidZabbixHostIdAttribute()
    {
        return !string.IsNullOrWhiteSpace(ZabbixHostIdAttribute);
    }

    public bool HasValidCheckTimeout()
    {
        return CheckTimeoutMs > 0;
    }
}
