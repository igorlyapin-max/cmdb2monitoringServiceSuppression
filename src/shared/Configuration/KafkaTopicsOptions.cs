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

    public string ZabbixServiceApplyPlans { get; init; } = "service-suppression.zabbix.service.apply-plans";

    public string ZabbixSuppressionApplyPlans { get; init; } = "service-suppression.zabbix.suppression.apply-plans";

    public string CmdbModelMissingDimensions { get; init; } = "service-suppression.cmdb.model.missing-dimensions";

    public string DeadLetterTopic { get; init; } = "service-suppression.dlq";

    public string DebugLogs { get; init; } = "service-suppression.logs";

    public string DerivedObjectCommands { get; init; } = "";

    public string EffectiveAggregationCommands()
    {
        return !string.IsNullOrWhiteSpace(AggregationCommands)
            ? AggregationCommands
            : DerivedObjectCommands;
    }

    public string EffectiveZabbixApplyPlans(string layer)
    {
        if (string.Equals(layer, "service", StringComparison.OrdinalIgnoreCase))
        {
            return !string.IsNullOrWhiteSpace(ZabbixServiceApplyPlans)
                ? ZabbixServiceApplyPlans
                : ZabbixApplyPlans;
        }

        if (string.Equals(layer, "suppression", StringComparison.OrdinalIgnoreCase))
        {
            return !string.IsNullOrWhiteSpace(ZabbixSuppressionApplyPlans)
                ? ZabbixSuppressionApplyPlans
                : ZabbixApplyPlans;
        }

        return ZabbixApplyPlans;
    }
}
