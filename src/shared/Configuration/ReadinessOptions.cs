namespace Cmdb2MonitoringServiceSuppression.Shared.Configuration;

public sealed class ReadinessOptions
{
    public const string SectionName = "Readiness";

    public string Route { get; init; } = "/ready";

    public string ZabbixHostIdAttribute { get; init; } = "zabbix_main_hostid";

    public bool HasValidRoute()
    {
        return !string.IsNullOrWhiteSpace(Route) && Route.StartsWith('/');
    }

    public bool HasValidZabbixHostIdAttribute()
    {
        return !string.IsNullOrWhiteSpace(ZabbixHostIdAttribute);
    }
}
