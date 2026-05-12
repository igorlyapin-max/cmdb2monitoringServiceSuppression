namespace Cmdb2MonitoringServiceSuppression.Shared.Configuration;

public sealed class ReadinessOptions
{
    public const string SectionName = "Readiness";

    public string ZabbixHostIdAttribute { get; init; } = "zabbix_main_hostid";

    public bool HasValidZabbixHostIdAttribute()
    {
        return !string.IsNullOrWhiteSpace(ZabbixHostIdAttribute);
    }
}
