namespace Cmdb2MonitoringServiceSuppression.Shared.Configuration;

public sealed class KafkaTopicsOptions
{
    public const string SectionName = "KafkaTopics";

    public string CmdbWebhookEvents { get; init; } = "service-suppression.cmdb.webhooks";

    public string DerivedObjectCommands { get; init; } = "service-suppression.cmdb.derived-object-commands";

    public string ConfigBuildRequests { get; init; } = "service-suppression.config.build-requests";

    public string ZabbixApplyPlans { get; init; } = "service-suppression.zabbix.apply-plans";
}
