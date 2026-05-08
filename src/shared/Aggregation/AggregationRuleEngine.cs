using System.Text.RegularExpressions;
using Cmdb2MonitoringServiceSuppression.Shared.ConversionRules;

namespace Cmdb2MonitoringServiceSuppression.Shared.Aggregation;

public sealed class AggregationRuleEngine
{
    public IReadOnlyList<AggregationCommand> BuildCommands(
        CmdbRawEvent rawEvent,
        ConversionRulesDocument rulesDocument)
    {
        ArgumentNullException.ThrowIfNull(rawEvent);
        ArgumentNullException.ThrowIfNull(rulesDocument);

        return rulesDocument.Rules
            .Where(rule => rule.Enabled)
            .OrderBy(rule => rule.Priority)
            .ThenBy(rule => rule.RuleId, StringComparer.Ordinal)
            .Where(rule => RuleMatches(rawEvent, rule))
            .Select(rule => BuildCommand(rawEvent, rule))
            .ToArray();
    }

    private static AggregationCommand BuildCommand(CmdbRawEvent rawEvent, ConversionRule rule)
    {
        var isDelete = rawEvent.EventType.Equals("DELETE", StringComparison.OrdinalIgnoreCase);
        var keyAttribute = rule.Source.KeyAttribute
            ?? rule.When.FieldExists
            ?? "_id";
        var targetAttributes = RenderMappings(rawEvent, rule.Target.AttributeMappings)
            .Concat(RenderMappings(rawEvent, rule.Target.InitialUserValues))
            .GroupBy(item => item.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => CoerceValue(group.Last().Value), StringComparer.Ordinal);
        var idempotencyKey = RenderTemplate(rawEvent, rule.Target.IdempotencyKey);
        var keyValue = keyAttribute.Equals("_id", StringComparison.OrdinalIgnoreCase)
            ? rawEvent.CardId
            : ReadField(rawEvent, keyAttribute);

        return new AggregationCommand
        {
            CommandId = Guid.NewGuid().ToString("N"),
            CorrelationId = rawEvent.EventId,
            SourceEventId = rawEvent.EventId,
            CommandType = isDelete ? AggregationCommandTypes.RemoveMembership : AggregationCommandTypes.EnsureMembership,
            Layer = rule.Layer,
            RuleId = rule.RuleId,
            RuleName = rule.Name,
            EventType = rawEvent.EventType,
            Source = new AggregationSourceObject
            {
                ClassCode = rawEvent.ClassCode,
                CardId = rawEvent.CardId,
                KeyAttribute = keyAttribute,
                KeyValue = keyValue
            },
            Target = new AggregationTargetObject
            {
                ClassCode = rule.Target.ClassCode,
                CardId = rule.Target.CardId,
                CardDescription = rule.Target.CardDescription,
                CreateInstance = string.IsNullOrWhiteSpace(rule.Target.CardId) && rule.Target.CreateInstance,
                IdempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey)
                    ? $"cmdbuild:{rule.Target.ClassCode}:{rule.Target.CardId}"
                    : idempotencyKey,
                Attributes = targetAttributes
            }
        };
    }

    private static bool RuleMatches(CmdbRawEvent rawEvent, ConversionRule rule)
    {
        if (!string.IsNullOrWhiteSpace(rule.Source.ClassCode)
            && !rawEvent.ClassCode.Equals(rule.Source.ClassCode, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(rule.When.FieldExists)
            && string.IsNullOrWhiteSpace(ReadField(rawEvent, rule.When.FieldExists)))
        {
            return false;
        }

        if (!rule.When.AllRegex.All(matcher => RegexMatches(rawEvent, matcher)))
        {
            return false;
        }

        if (rule.When.AnyRegex.Count > 0 && !rule.When.AnyRegex.Any(matcher => RegexMatches(rawEvent, matcher)))
        {
            return false;
        }

        if (rule.When.NoneRegex.Any(matcher => RegexMatches(rawEvent, matcher)))
        {
            return false;
        }

        foreach (var condition in rule.Source.Conditions.Concat(rule.Source.Filters))
        {
            if (!ConditionMatches(rawEvent, condition))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ConditionMatches(CmdbRawEvent rawEvent, SourceCondition condition)
    {
        var value = ReadField(rawEvent, condition.Attribute);
        return condition.Operator.ToLowerInvariant() switch
        {
            "equals" => value.Equals(condition.Value, StringComparison.OrdinalIgnoreCase),
            "not_equals" => !value.Equals(condition.Value, StringComparison.OrdinalIgnoreCase),
            "exists" => !string.IsNullOrWhiteSpace(value),
            "not_exists" => string.IsNullOrWhiteSpace(value),
            "regex" or "matches" => Regex.IsMatch(value, condition.Value),
            _ => false
        };
    }

    private static bool RegexMatches(CmdbRawEvent rawEvent, RegexMatcher matcher)
    {
        return Regex.IsMatch(ReadField(rawEvent, matcher.Field), matcher.Pattern);
    }

    private static IEnumerable<KeyValuePair<string, string>> RenderMappings(
        CmdbRawEvent rawEvent,
        IReadOnlyDictionary<string, string> mappings)
    {
        foreach (var (key, value) in mappings)
        {
            yield return new KeyValuePair<string, string>(key, RenderTemplate(rawEvent, value));
        }
    }

    private static string RenderTemplate(CmdbRawEvent rawEvent, string template)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            return "";
        }

        return Regex.Replace(template, "\\$\\{([^}]+)\\}", match =>
        {
            var token = match.Groups[1].Value.Trim();
            if (token.StartsWith("source.", StringComparison.OrdinalIgnoreCase))
            {
                return ReadField(rawEvent, token["source.".Length..]);
            }

            return token.ToLowerInvariant() switch
            {
                "event.card_id" => rawEvent.CardId,
                "event.class_code" => rawEvent.ClassCode,
                "event.event_id" => rawEvent.EventId,
                "event.event_type" => rawEvent.EventType,
                _ => match.Value
            };
        });
    }

    private static string ReadField(CmdbRawEvent rawEvent, string field)
    {
        if (field.Equals("className", StringComparison.OrdinalIgnoreCase)
            || field.Equals("class_code", StringComparison.OrdinalIgnoreCase)
            || field.Equals("classCode", StringComparison.OrdinalIgnoreCase))
        {
            return rawEvent.ClassCode;
        }

        if (field.Equals("eventType", StringComparison.OrdinalIgnoreCase)
            || field.Equals("event_type", StringComparison.OrdinalIgnoreCase))
        {
            return rawEvent.EventType;
        }

        if (field.Equals("_id", StringComparison.OrdinalIgnoreCase)
            || field.Equals("card_id", StringComparison.OrdinalIgnoreCase)
            || field.Equals("cardId", StringComparison.OrdinalIgnoreCase))
        {
            return rawEvent.CardId;
        }

        return rawEvent.Attributes.TryGetValue(field, out var value) ? value : "";
    }

    private static object? CoerceValue(string value)
    {
        if (bool.TryParse(value, out var boolValue))
        {
            return boolValue;
        }

        if (long.TryParse(value, out var longValue))
        {
            return longValue;
        }

        if (decimal.TryParse(value.Replace(',', '.'), out var decimalValue))
        {
            return decimalValue;
        }

        return value;
    }
}
