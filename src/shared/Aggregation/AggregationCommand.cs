using System.Text.Json.Serialization;

namespace Cmdb2MonitoringServiceSuppression.Shared.Aggregation;

public sealed record AggregationCommand
{
    [JsonPropertyName("command_id")]
    public string CommandId { get; init; } = "";

    [JsonPropertyName("correlation_id")]
    public string CorrelationId { get; init; } = "";

    [JsonPropertyName("source_event_id")]
    public string SourceEventId { get; init; } = "";

    [JsonPropertyName("command_type")]
    public string CommandType { get; init; } = "";

    [JsonPropertyName("layer")]
    public string Layer { get; init; } = "";

    [JsonPropertyName("rule_id")]
    public string RuleId { get; init; } = "";

    [JsonPropertyName("rule_name")]
    public string RuleName { get; init; } = "";

    [JsonPropertyName("event_type")]
    public string EventType { get; init; } = "";

    [JsonPropertyName("source")]
    public AggregationSourceObject Source { get; init; } = new();

    [JsonPropertyName("target")]
    public AggregationTargetObject Target { get; init; } = new();

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record AggregationCommandPlan
{
    [JsonPropertyName("command")]
    public required AggregationCommand Command { get; init; }

    [JsonPropertyName("semantic_key")]
    public required string SemanticKey { get; init; }

    [JsonPropertyName("semantic_fingerprint")]
    public required string SemanticFingerprint { get; init; }
}

public sealed record AggregationSourceObject
{
    [JsonPropertyName("class_code")]
    public string ClassCode { get; init; } = "";

    [JsonPropertyName("card_id")]
    public string CardId { get; init; } = "";

    [JsonPropertyName("key_attribute")]
    public string KeyAttribute { get; init; } = "";

    [JsonPropertyName("key_value")]
    public string KeyValue { get; init; } = "";

    [JsonPropertyName("zabbix_hostid")]
    public string ZabbixHostId { get; init; } = "";

    [JsonPropertyName("attributes")]
    public IReadOnlyDictionary<string, string> Attributes { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}

public sealed record AggregationTargetObject
{
    [JsonPropertyName("class_code")]
    public string ClassCode { get; init; } = "";

    [JsonPropertyName("card_id")]
    public string CardId { get; init; } = "";

    [JsonPropertyName("card_description")]
    public string CardDescription { get; init; } = "";

    [JsonPropertyName("create_instance")]
    public bool CreateInstance { get; init; }

    [JsonPropertyName("idempotency_key")]
    public string IdempotencyKey { get; init; } = "";

    [JsonPropertyName("attributes")]
    public IReadOnlyDictionary<string, object?> Attributes { get; init; } =
        new Dictionary<string, object?>(StringComparer.Ordinal);

    [JsonPropertyName("relations")]
    public IReadOnlyList<AggregationTargetRelation> Relations { get; init; } = [];
}

public sealed record AggregationTargetRelation
{
    [JsonPropertyName("domain_code")]
    public string DomainCode { get; init; } = "";

    [JsonPropertyName("target_class_code")]
    public string TargetClassCode { get; init; } = "";

    [JsonPropertyName("target_lookup")]
    public string TargetLookup { get; init; } = "";

    [JsonPropertyName("attribute_mappings")]
    public IReadOnlyDictionary<string, object?> AttributeMappings { get; init; } =
        new Dictionary<string, object?>(StringComparer.Ordinal);
}

public static class AggregationCommandTypes
{
    public const string EnsureMembership = "ensure_membership";

    public const string RemoveMembership = "remove_membership";

    public const string RemoveSourceMembership = "remove_source_membership";
}
