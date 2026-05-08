using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Cmdb2MonitoringServiceSuppression.Shared.Aggregation;

public sealed class CmdbRawEventFactory(IOptions<CmdbWebhookNormalizationOptions> options)
{
    public CmdbRawEvent FromWebhook(JsonElement payload, string source, string eventId)
    {
        var normalizedOptions = options.Value;
        var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in normalizedOptions.AttributeObjectFields)
        {
            if (TryReadElement(payload, field, out var attributeElement)
                && attributeElement.ValueKind == JsonValueKind.Object)
            {
                AddAttributes(attributes, attributeElement);
            }
        }

        if (payload.ValueKind == JsonValueKind.Object)
        {
            AddAttributes(attributes, payload);
        }

        var eventType = ReadFirstString(payload, normalizedOptions.EventTypeFields, attributes);
        var classCode = ReadFirstString(payload, normalizedOptions.ClassCodeFields, attributes);
        var cardId = ReadFirstString(payload, normalizedOptions.CardIdFields, attributes);
        return new CmdbRawEvent
        {
            EventId = eventId,
            Source = source,
            EventType = NormalizeEventType(eventType),
            ClassCode = classCode,
            CardId = cardId,
            OccurredAt = DateTimeOffset.UtcNow,
            Attributes = attributes,
            RawPayload = payload.Clone()
        };
    }

    private static void AddAttributes(IDictionary<string, string> attributes, JsonElement element)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
            {
                continue;
            }

            attributes[property.Name] = ScalarToString(property.Value);
        }
    }

    private static string ReadFirstString(
        JsonElement payload,
        IEnumerable<string> fieldNames,
        IReadOnlyDictionary<string, string> attributes)
    {
        foreach (var field in fieldNames)
        {
            if (TryReadElement(payload, field, out var element))
            {
                var value = ScalarToString(element);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            if (attributes.TryGetValue(field, out var attributeValue)
                && !string.IsNullOrWhiteSpace(attributeValue))
            {
                return attributeValue;
            }
        }

        return "";
    }

    private static bool TryReadElement(JsonElement element, string propertyName, out JsonElement value)
    {
        value = default;
        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        return false;
    }

    private static string ScalarToString(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? "",
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => "",
            JsonValueKind.Undefined => "",
            _ => element.GetRawText()
        };
    }

    private static string NormalizeEventType(string eventType)
    {
        if (eventType.Equals("create", StringComparison.OrdinalIgnoreCase)
            || eventType.Equals("insert", StringComparison.OrdinalIgnoreCase)
            || eventType.Equals("created", StringComparison.OrdinalIgnoreCase))
        {
            return "CREATE";
        }

        if (eventType.Equals("delete", StringComparison.OrdinalIgnoreCase)
            || eventType.Equals("deleted", StringComparison.OrdinalIgnoreCase)
            || eventType.Equals("remove", StringComparison.OrdinalIgnoreCase))
        {
            return "DELETE";
        }

        if (eventType.Equals("update", StringComparison.OrdinalIgnoreCase)
            || eventType.Equals("modify", StringComparison.OrdinalIgnoreCase)
            || eventType.Equals("updated", StringComparison.OrdinalIgnoreCase))
        {
            return "UPDATE";
        }

        return string.IsNullOrWhiteSpace(eventType) ? "UPDATE" : eventType.ToUpperInvariant();
    }
}
