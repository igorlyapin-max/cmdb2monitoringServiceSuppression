using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Cmdb2MonitoringServiceSuppression.Shared.ConversionRules;

namespace Cmdb2MonitoringServiceSuppression.Shared.Aggregation;

public sealed class AggregationRuleEngine
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly string[] DefaultSourceZabbixHostIdFields =
    [
        "zabbix_main_hostid",
        "zabbixMainHostId",
        "zabbix_main_host_id",
        "zabbix_hostid",
        "zabbixHostId",
        "zabbix_host_id",
        "hostid",
        "host_id"
    ];

    private readonly string[] sourceZabbixHostIdFields;

    public AggregationRuleEngine(string? zabbixHostIdAttribute = null)
    {
        var configuredAttribute = string.IsNullOrWhiteSpace(zabbixHostIdAttribute)
            ? "zabbix_main_hostid"
            : zabbixHostIdAttribute.Trim();
        sourceZabbixHostIdFields = new[] { configuredAttribute }
            .Concat(DefaultSourceZabbixHostIdFields)
            .Where(field => !string.IsNullOrWhiteSpace(field))
            .Select(field => field.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public IReadOnlyList<AggregationCommand> BuildCommands(
        CmdbRawEvent rawEvent,
        ConversionRulesDocument rulesDocument)
    {
        return BuildCommandPlans(rawEvent, rulesDocument)
            .Select(plan => plan.Command)
            .ToArray();
    }

    public IReadOnlyList<AggregationCommandPlan> BuildCommandPlans(
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
            .Select(rule =>
            {
                var command = BuildCommand(rawEvent, rule);
                return new AggregationCommandPlan
                {
                    Command = command,
                    SemanticKey = BuildSemanticKey(rawEvent, rule),
                    SemanticFingerprint = BuildSemanticFingerprint(rawEvent, rule, command, rulesDocument.Version)
                };
            })
            .ToArray();
    }

    private AggregationCommand BuildCommand(CmdbRawEvent rawEvent, ConversionRule rule)
    {
        var isDelete = rawEvent.EventType.Equals("DELETE", StringComparison.OrdinalIgnoreCase);
        var keyAttribute = rule.Source.KeyAttribute
            ?? rule.When.FieldExists
            ?? "_id";
        var targetAttributes = RenderMappings(rawEvent, rule.Target.AttributeMappings)
            .Concat(RenderMappings(rawEvent, rule.Target.InitialUserValues))
            .GroupBy(item => item.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => CoerceValue(group.Last().Value), StringComparer.Ordinal);
        var targetRelations = rule.Relations
            .Select(relation => new AggregationTargetRelation
            {
                DomainCode = relation.DomainCode,
                TargetClassCode = relation.TargetClassCode,
                TargetLookup = RenderTemplate(rawEvent, relation.TargetLookup),
                AttributeMappings = RenderMappings(rawEvent, relation.AttributeMappings)
                    .GroupBy(item => item.Key, StringComparer.Ordinal)
                    .ToDictionary(group => group.Key, group => CoerceValue(group.Last().Value), StringComparer.Ordinal)
            })
            .Where(relation =>
                !string.IsNullOrWhiteSpace(relation.DomainCode)
                && !string.IsNullOrWhiteSpace(relation.TargetClassCode)
                && !string.IsNullOrWhiteSpace(relation.TargetLookup))
            .ToArray();
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
                KeyValue = keyValue,
                ZabbixHostId = SourceZabbixHostId(rawEvent),
                Attributes = new Dictionary<string, string>(rawEvent.Attributes, StringComparer.OrdinalIgnoreCase)
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
                Attributes = targetAttributes,
                Relations = targetRelations
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

    private string SourceZabbixHostId(CmdbRawEvent rawEvent)
    {
        foreach (var field in sourceZabbixHostIdFields)
        {
            var value = ReadField(rawEvent, field);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return "";
    }

    private static string BuildSemanticKey(CmdbRawEvent rawEvent, ConversionRule rule)
    {
        var eventKind = rawEvent.EventType.Equals("DELETE", StringComparison.OrdinalIgnoreCase)
            ? "DELETE"
            : "UPSERT";
        return string.Join('\u001f', rule.RuleId, rawEvent.ClassCode, rawEvent.CardId, eventKind);
    }

    private static string BuildSemanticFingerprint(
        CmdbRawEvent rawEvent,
        ConversionRule rule,
        AggregationCommand command,
        string documentVersion)
    {
        var fields = SemanticSourceFields(rule)
            .Select(field => new KeyValuePair<string, string>(field, ReadField(rawEvent, field)))
            .OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var payload = new
        {
            documentVersion,
            ruleFingerprint = Hash(JsonSerializer.Serialize(rule, JsonOptions)),
            eventKind = rawEvent.EventType.Equals("DELETE", StringComparison.OrdinalIgnoreCase) ? "DELETE" : "UPSERT",
            sourceClass = rawEvent.ClassCode,
            sourceCard = rawEvent.CardId,
            sourceKey = command.Source.KeyValue,
            sourceZabbixHostId = command.Source.ZabbixHostId,
            fields,
            command.CommandType,
            command.Layer,
            command.Target.ClassCode,
            command.Target.CardId,
            command.Target.IdempotencyKey,
            targetAttributes = command.Target.Attributes
                .OrderBy(item => item.Key, StringComparer.Ordinal)
                .ToArray(),
            targetRelations = command.Target.Relations
                .OrderBy(item => item.DomainCode, StringComparer.Ordinal)
                .ThenBy(item => item.TargetClassCode, StringComparer.Ordinal)
                .ThenBy(item => item.TargetLookup, StringComparer.Ordinal)
                .Select(item => new
                {
                    item.DomainCode,
                    item.TargetClassCode,
                    item.TargetLookup,
                    attributes = item.AttributeMappings
                        .OrderBy(attribute => attribute.Key, StringComparer.Ordinal)
                        .ToArray()
                })
                .ToArray()
        };

        return Hash(JsonSerializer.Serialize(payload, JsonOptions));
    }

    private static IEnumerable<string> SemanticSourceFields(ConversionRule rule)
    {
        var fields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "className",
            "_id",
            "eventType"
        };

        AddField(fields, rule.Source.KeyAttribute);
        AddField(fields, rule.When.FieldExists);
        foreach (var matcher in rule.When.AllRegex.Concat(rule.When.AnyRegex).Concat(rule.When.NoneRegex))
        {
            AddField(fields, matcher.Field);
        }

        foreach (var condition in rule.Source.Conditions.Concat(rule.Source.Filters))
        {
            AddField(fields, condition.Attribute);
        }

        AddSourceTemplateFields(fields, rule.Target.IdempotencyKey);
        AddSourceTemplateFields(fields, rule.Target.CardId);
        AddSourceTemplateFields(fields, rule.Target.CardDescription);
        foreach (var value in rule.Target.AttributeMappings.Values.Concat(rule.Target.InitialUserValues.Values))
        {
            AddSourceTemplateFields(fields, value);
        }

        foreach (var relation in rule.Relations)
        {
            AddSourceTemplateFields(fields, relation.TargetLookup);
            foreach (var value in relation.AttributeMappings.Values)
            {
                AddSourceTemplateFields(fields, value);
            }
        }

        return fields.Where(field => !string.IsNullOrWhiteSpace(field));
    }

    private static void AddField(ISet<string> fields, string? field)
    {
        if (!string.IsNullOrWhiteSpace(field))
        {
            fields.Add(field.Trim());
        }
    }

    private static void AddSourceTemplateFields(ISet<string> fields, string? template)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            return;
        }

        foreach (Match match in Regex.Matches(template, "\\$\\{\\s*source\\.([A-Za-z_][A-Za-z0-9_]*)\\s*\\}", RegexOptions.IgnoreCase))
        {
            fields.Add(match.Groups[1].Value);
        }
    }

    private static string Hash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
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
