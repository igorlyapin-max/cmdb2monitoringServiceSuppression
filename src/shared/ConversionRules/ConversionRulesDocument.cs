using System.Text.Json.Serialization;

namespace Cmdb2MonitoringServiceSuppression.Shared.ConversionRules;

public sealed record ConversionRulesDocument
{
    [JsonPropertyName("version")]
    public string Version { get; init; } = "1";

    [JsonPropertyName("rules")]
    public IReadOnlyList<ConversionRule> Rules { get; init; } = [];
}

public sealed record ConversionRule
{
    [JsonPropertyName("rule_id")]
    public required string RuleId { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("layer")]
    public required string Layer { get; init; }

    [JsonPropertyName("source")]
    public required SourceSelector Source { get; init; }

    [JsonPropertyName("target")]
    public required TargetObject Target { get; init; }

    [JsonPropertyName("relations")]
    public IReadOnlyList<TargetRelation> Relations { get; init; } = [];

    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; } = true;
}

public sealed record SourceSelector
{
    [JsonPropertyName("class_code")]
    public required string ClassCode { get; init; }

    [JsonPropertyName("key_attribute")]
    public string? KeyAttribute { get; init; }

    [JsonPropertyName("conditions")]
    public IReadOnlyList<SourceCondition> Conditions { get; init; } = [];
}

public sealed record SourceCondition
{
    [JsonPropertyName("attribute")]
    public required string Attribute { get; init; }

    [JsonPropertyName("operator")]
    public required string Operator { get; init; }

    [JsonPropertyName("value")]
    public required string Value { get; init; }
}

public sealed record TargetObject
{
    [JsonPropertyName("class_code")]
    public required string ClassCode { get; init; }

    [JsonPropertyName("idempotency_key")]
    public required string IdempotencyKey { get; init; }

    [JsonPropertyName("attribute_mappings")]
    public IReadOnlyDictionary<string, string> AttributeMappings { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
}

public sealed record TargetRelation
{
    [JsonPropertyName("domain_code")]
    public required string DomainCode { get; init; }

    [JsonPropertyName("target_class_code")]
    public required string TargetClassCode { get; init; }

    [JsonPropertyName("target_lookup")]
    public required string TargetLookup { get; init; }

    [JsonPropertyName("attribute_mappings")]
    public IReadOnlyDictionary<string, string> AttributeMappings { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
}
