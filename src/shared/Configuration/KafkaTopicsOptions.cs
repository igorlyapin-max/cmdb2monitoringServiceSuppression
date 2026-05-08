namespace Cmdb2MonitoringServiceSuppression.Shared.Configuration;

public sealed class KafkaTopicsOptions
{
    public const string SectionName = "KafkaTopics";

    public string ManagedIdentifier { get; init; } = "cmdb2monitoring-service-suppression";

    public string ManagedPrefix { get; init; } = "service-suppression.";

    public string CmdbWebhookEvents { get; init; } = "service-suppression.cmdb.events.raw";

    public string AggregationCommands { get; init; } = "service-suppression.monitoring.aggregation.commands";

    public string ConfigBuildRequests { get; init; } = "service-suppression.config.build-requests";

    public string ZabbixApplyPlans { get; init; } = "service-suppression.zabbix.apply-plans";

    public string DebugLogs { get; init; } = "service-suppression.logs";

    public string DerivedObjectCommands { get; init; } = "";

    public string EffectiveAggregationCommands()
    {
        return !string.IsNullOrWhiteSpace(AggregationCommands)
            ? AggregationCommands
            : DerivedObjectCommands;
    }
}
