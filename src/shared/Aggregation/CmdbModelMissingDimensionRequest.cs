using System.Text.Json.Serialization;

namespace Cmdb2MonitoringServiceSuppression.Shared.Aggregation;

public sealed record CmdbModelMissingDimensionRequest
{
    [JsonPropertyName("schema_version")]
    public int SchemaVersion { get; init; } = 1;

    [JsonPropertyName("idempotency_key")]
    public string IdempotencyKey { get; init; } = "";

    [JsonPropertyName("layer")]
    public string Layer { get; init; } = "";

    [JsonPropertyName("template_id")]
    public string TemplateId { get; init; } = "";

    [JsonPropertyName("template_name")]
    public string TemplateName { get; init; } = "";

    [JsonPropertyName("source_class")]
    public string SourceClass { get; init; } = "";

    [JsonPropertyName("source_card_id")]
    public string SourceCardId { get; init; } = "";

    [JsonPropertyName("source_event_id")]
    public string SourceEventId { get; init; } = "";

    [JsonPropertyName("event_type")]
    public string EventType { get; init; } = "";

    [JsonPropertyName("field")]
    public string Field { get; init; } = "";

    [JsonPropertyName("field_value")]
    public string FieldValue { get; init; } = "";

    [JsonPropertyName("dimension_key")]
    public string DimensionKey { get; init; } = "";

    [JsonPropertyName("dimension_value")]
    public string DimensionValue { get; init; } = "";

    [JsonPropertyName("dimension_name")]
    public string DimensionName { get; init; } = "";

    [JsonPropertyName("target_key")]
    public string TargetKey { get; init; } = "";

    [JsonPropertyName("variables")]
    public IReadOnlyDictionary<string, string> Variables { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    [JsonPropertyName("reason")]
    public string Reason { get; init; } = "";

    [JsonPropertyName("detected_at")]
    public DateTimeOffset DetectedAt { get; init; } = DateTimeOffset.UtcNow;
}
