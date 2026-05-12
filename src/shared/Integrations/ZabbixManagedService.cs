using System.Globalization;
using Cmdb2MonitoringServiceSuppression.Shared.Aggregation;

namespace Cmdb2MonitoringServiceSuppression.Shared.Integrations;

public sealed record ZabbixManagedServiceDefinition
{
    public string Layer { get; init; } = "";

    public string ManagedKey { get; init; } = "";

    public string ClassCode { get; init; } = "";

    public string CardId { get; init; } = "";

    public string RuleId { get; init; } = "";

    public string RuleName { get; init; } = "";

    public string SourceClass { get; init; } = "";

    public string SourceCardId { get; init; } = "";

    public string Name { get; init; } = "";

    public string Description { get; init; } = "";

    public int Algorithm { get; init; } = ZabbixServiceAlgorithms.MostCriticalOfChildren;

    public int SortOrder { get; init; }

    public int Weight { get; init; }

    public IReadOnlyList<ZabbixManagedServiceRelation> Relations { get; init; } = [];

    public IReadOnlyDictionary<string, string> Tags { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
}

public sealed record ZabbixManagedServiceRelation
{
    public string DomainCode { get; init; } = "";

    public string TargetClassCode { get; init; } = "";

    public string TargetLookup { get; init; } = "";
}

public sealed record ZabbixServiceTag(string Tag, string Value);

public sealed record ZabbixServiceInfo
{
    public string ServiceId { get; init; } = "";

    public string Name { get; init; } = "";

    public IReadOnlyDictionary<string, string> Tags { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    public IReadOnlyList<ZabbixServiceInfo> Children { get; init; } = [];

    public IReadOnlyList<ZabbixServiceInfo> Parents { get; init; } = [];
}

public sealed record ZabbixManagedServiceApplyResult
{
    public bool Success { get; init; }

    public string Action { get; init; } = "";

    public string ServiceId { get; init; } = "";

    public int RelationsApplied { get; init; }

    public int RelationsDeferred { get; init; }

    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public static class ZabbixServiceAlgorithms
{
    public const int AlwaysOk = 0;

    public const int MostCriticalIfAllChildrenHaveProblems = 1;

    public const int MostCriticalOfChildren = 2;
}

public static class ZabbixManagedServiceTags
{
    public const string Managed = "cmdb2monitoring:managed";

    public const string Layer = "cmdb2monitoring:layer";

    public const string Class = "cmdb2monitoring:class";

    public const string Key = "cmdb2monitoring:key";

    public const string CardId = "cmdb2monitoring:card_id";

    public const string RuleId = "cmdb2monitoring:rule_id";

    public const string RuleName = "cmdb2monitoring:rule_name";

    public const string SourceClass = "cmdb2monitoring:source_class";

    public const string SourceCardId = "cmdb2monitoring:source_card_id";
}

public static class ZabbixManagedServiceMapper
{
    public static ZabbixManagedServiceDefinition FromAggregationCommand(AggregationCommand command, string layer)
    {
        var managedKey = ManagedKey(command.Target);
        var name = ServiceDisplayName(command, managedKey);
        if (string.IsNullOrWhiteSpace(name))
        {
            name = managedKey;
        }

        var description = FirstAttribute(command.Target.Attributes, "description", "Description")
            ?? command.Target.CardDescription
            ?? "";
        var tags = BuildTags(command, layer, managedKey);

        return new ZabbixManagedServiceDefinition
        {
            Layer = layer,
            ManagedKey = managedKey,
            ClassCode = command.Target.ClassCode,
            CardId = command.Target.CardId,
            RuleId = command.RuleId,
            RuleName = command.RuleName,
            SourceClass = command.Source.ClassCode,
            SourceCardId = command.Source.CardId,
            Name = Trim(name, 255),
            Description = Trim(description, 2048),
            Algorithm = ServiceAlgorithm(command.Target.Attributes),
            SortOrder = ClampInt(FirstAttribute(command.Target.Attributes, "sortorder", "sort_order"), 0, 0, 999),
            Weight = ClampInt(FirstAttribute(command.Target.Attributes, "weight"), 0, 0, 1_000_000),
            Relations = command.Target.Relations
                .Select(relation => new ZabbixManagedServiceRelation
                {
                    DomainCode = relation.DomainCode,
                    TargetClassCode = relation.TargetClassCode,
                    TargetLookup = relation.TargetLookup
                })
                .ToArray(),
            Tags = tags
        };
    }

    public static string ManagedKey(AggregationTargetObject target)
    {
        if (!string.IsNullOrWhiteSpace(target.IdempotencyKey))
        {
            return target.IdempotencyKey;
        }

        if (!string.IsNullOrWhiteSpace(target.CardId) && !string.IsNullOrWhiteSpace(target.ClassCode))
        {
            return $"cmdbuild:{target.ClassCode}:{target.CardId}";
        }

        return string.IsNullOrWhiteSpace(target.ClassCode)
            ? target.CardId
            : $"{target.ClassCode}:{target.CardId}";
    }

    private static string ServiceDisplayName(AggregationCommand command, string managedKey)
    {
        return FirstAttribute(command.Target.Attributes, "zabbix_service_name", "zabbix_name", "monitoring_name")
            ?? (command.Target.CreateInstance ? NonEmpty(command.RuleName) : null)
            ?? FirstAttribute(command.Target.Attributes, "name", "Name", "Description", "description")
            ?? NonEmpty(command.RuleName)
            ?? command.Target.CardDescription
            ?? managedKey;
    }

    public static IReadOnlyList<string> LookupCandidates(string classCode, string lookup)
    {
        if (string.IsNullOrWhiteSpace(lookup))
        {
            return [];
        }

        var candidates = new List<string> { lookup };
        if (!string.IsNullOrWhiteSpace(classCode))
        {
            candidates.Add($"cmdbuild:{classCode}:{lookup}");
        }

        return candidates
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static Dictionary<string, string> BuildTags(
        AggregationCommand command,
        string layer,
        string managedKey)
    {
        var tags = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [ZabbixManagedServiceTags.Managed] = "true",
            [ZabbixManagedServiceTags.Layer] = layer,
            [ZabbixManagedServiceTags.Class] = command.Target.ClassCode,
            [ZabbixManagedServiceTags.Key] = managedKey
        };

        AddTag(tags, ZabbixManagedServiceTags.CardId, command.Target.CardId);
        AddTag(tags, ZabbixManagedServiceTags.RuleId, command.RuleId);
        AddTag(tags, ZabbixManagedServiceTags.RuleName, command.RuleName);
        AddTag(tags, ZabbixManagedServiceTags.SourceClass, command.Source.ClassCode);
        AddTag(tags, ZabbixManagedServiceTags.SourceCardId, command.Source.CardId);
        AddTag(tags, "cmdb2monitoring:aggregation_type", FirstAttribute(command.Target.Attributes, "aggregation_type"));
        AddTag(tags, "cmdb2monitoring:is_critical", FirstAttribute(command.Target.Attributes, "is_critical"));
        AddTag(tags, "cmdb2monitoring:threshold", FirstAttribute(command.Target.Attributes, "threshold"));
        AddTag(tags, "cmdb2monitoring:n", FirstAttribute(command.Target.Attributes, "n"));
        return tags;
    }

    private static int ServiceAlgorithm(IReadOnlyDictionary<string, object?> attributes)
    {
        var aggregationType = FirstAttribute(attributes, "aggregation_type");
        if (string.Equals(aggregationType, "all", StringComparison.OrdinalIgnoreCase))
        {
            return ZabbixServiceAlgorithms.MostCriticalIfAllChildrenHaveProblems;
        }

        if (string.Equals(aggregationType, "none", StringComparison.OrdinalIgnoreCase)
            || string.Equals(aggregationType, "always_ok", StringComparison.OrdinalIgnoreCase))
        {
            return ZabbixServiceAlgorithms.AlwaysOk;
        }

        return ZabbixServiceAlgorithms.MostCriticalOfChildren;
    }

    private static void AddTag(Dictionary<string, string> tags, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            tags[key] = Trim(value, 255);
        }
    }

    private static string? FirstAttribute(IReadOnlyDictionary<string, object?> attributes, params string[] names)
    {
        foreach (var name in names)
        {
            if (attributes.TryGetValue(name, out var exactValue))
            {
                return ScalarString(exactValue);
            }
        }

        foreach (var pair in attributes)
        {
            if (names.Any(name => string.Equals(name, pair.Key, StringComparison.OrdinalIgnoreCase)))
            {
                return ScalarString(pair.Value);
            }
        }

        return null;
    }

    private static string? NonEmpty(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static string? ScalarString(object? value)
    {
        return value switch
        {
            null => null,
            string text => string.IsNullOrWhiteSpace(text) ? null : text,
            bool boolean => boolean ? "true" : "false",
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString()
        };
    }

    private static int ClampInt(string? value, int fallback, int min, int max)
    {
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            return fallback;
        }

        return Math.Clamp(parsed, min, max);
    }

    private static string Trim(string value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}
