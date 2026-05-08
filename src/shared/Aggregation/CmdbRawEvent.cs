using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cmdb2MonitoringServiceSuppression.Shared.Aggregation;

public sealed record CmdbRawEvent
{
    [JsonPropertyName("event_id")]
    public string EventId { get; init; } = "";

    [JsonPropertyName("source")]
    public string Source { get; init; } = "";

    [JsonPropertyName("event_type")]
    public string EventType { get; init; } = "";

    [JsonPropertyName("class_code")]
    public string ClassCode { get; init; } = "";

    [JsonPropertyName("card_id")]
    public string CardId { get; init; } = "";

    [JsonPropertyName("occurred_at")]
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;

    [JsonPropertyName("attributes")]
    public IReadOnlyDictionary<string, string> Attributes { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    [JsonPropertyName("raw_payload")]
    public JsonElement RawPayload { get; init; }
}

public sealed class CmdbWebhookNormalizationOptions
{
    public const string SectionName = "CmdbWebhookNormalization";

    public IReadOnlyList<string> EventTypeFields { get; init; } =
        ["event_type", "eventType", "operation", "action", "type"];

    public IReadOnlyList<string> ClassCodeFields { get; init; } =
        ["class_code", "classCode", "class", "_class", "className"];

    public IReadOnlyList<string> CardIdFields { get; init; } =
        ["card_id", "cardId", "_id", "id"];

    public IReadOnlyList<string> AttributeObjectFields { get; init; } =
        ["attributes", "values", "card", "data"];
}
