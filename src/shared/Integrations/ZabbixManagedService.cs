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

    public string SourceKeyAttribute { get; init; } = "";

    public string SourceKeyValue { get; init; } = "";

    public string SourceZabbixHostId { get; init; } = "";

    public string Name { get; init; } = "";

    public string Description { get; init; } = "";

    public int Algorithm { get; init; } = ZabbixServiceAlgorithms.MostCriticalOfChildren;

    public int SortOrder { get; init; }

    public int Weight { get; init; }

    public string Role { get; init; } = ZabbixManagedServiceRoles.Aggregate;

    public string Visibility { get; init; } = ZabbixManagedServiceVisibility.Child;

    public IReadOnlyList<ZabbixManagedServiceRelation> Relations { get; init; } = [];

    public IReadOnlyList<string> ChildManagedKeys { get; init; } = [];

    public IReadOnlyList<string> ParentManagedKeys { get; init; } = [];

    public IReadOnlyList<ZabbixProblemTag> ProblemTags { get; init; } = [];

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

public sealed record ZabbixProblemTag(string Tag, string Value, int Operator = ZabbixProblemTagOperators.Equal);

public sealed record ZabbixServiceInfo
{
    public string ServiceId { get; init; } = "";

    public string Name { get; init; } = "";

    public IReadOnlyDictionary<string, string> Tags { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    public IReadOnlyList<ZabbixServiceInfo> Children { get; init; } = [];

    public IReadOnlyList<ZabbixServiceInfo> Parents { get; init; } = [];
}

public sealed record ZabbixManagedServiceDeleteResult
{
    public string ManagedKey { get; init; } = "";

    public string ServiceId { get; init; } = "";

    public string Name { get; init; } = "";

    public string Action { get; init; } = "";

    public string Message { get; init; } = "";
}

public sealed record ZabbixSlaDefinition
{
    public string PolicyKey { get; init; } = "";

    public string Name { get; init; } = "";

    public decimal Slo { get; init; }

    public int Period { get; init; } = ZabbixSlaPeriods.Monthly;

    public string Timezone { get; init; } = "UTC";

    public long EffectiveDate { get; init; }

    public int Status { get; init; } = ZabbixSlaStatuses.Enabled;

    public string Description { get; init; } = "";

    public string ManagedExcludedDowntimePrefix { get; init; } = "CMDB2M REG:";

    public IReadOnlyList<ZabbixSlaServiceTag> ServiceTags { get; init; } = [];

    public IReadOnlyList<ZabbixSlaSchedulePeriod> Schedule { get; init; } = [];

    public IReadOnlyList<ZabbixSlaExcludedDowntime> ExcludedDowntimes { get; init; } = [];
}

public sealed record ZabbixSlaServiceTag(string Tag, string Value, int Operator = ZabbixSlaServiceTagOperators.Equal);

public sealed record ZabbixSlaSchedulePeriod(int PeriodFrom, int PeriodTo);

public sealed record ZabbixSlaExcludedDowntime(string Name, long PeriodFrom, long PeriodTo);

public sealed record ZabbixSlaInfo
{
    public string SlaId { get; init; } = "";

    public string Name { get; init; } = "";

    public decimal Slo { get; init; }

    public int Period { get; init; }

    public string Timezone { get; init; } = "";

    public int Status { get; init; }

    public IReadOnlyList<ZabbixSlaServiceTag> ServiceTags { get; init; } = [];

    public IReadOnlyList<ZabbixSlaSchedulePeriod> Schedule { get; init; } = [];

    public IReadOnlyList<ZabbixSlaExcludedDowntime> ExcludedDowntimes { get; init; } = [];
}

public sealed record ZabbixSlaApplyResult
{
    public string SlaId { get; init; } = "";

    public string Action { get; init; } = "";

    public int ManagedExcludedDowntimes { get; init; }

    public int PreservedManualExcludedDowntimes { get; init; }
}

public sealed record ZabbixHostInfo
{
    public string HostId { get; init; } = "";

    public string Host { get; init; } = "";

    public string Name { get; init; } = "";

    public IReadOnlyList<ZabbixServiceTag> Tags { get; init; } = [];
}

public sealed record ZabbixTriggerInfo
{
    public string TriggerId { get; init; } = "";

    public string Description { get; init; } = "";

    public string Status { get; init; } = "";

    public string Priority { get; init; } = "";

    public string Value { get; init; } = "";

    public string Expression { get; init; } = "";

    public string RecoveryExpression { get; init; } = "";

    public IReadOnlyList<ZabbixServiceTag> Tags { get; init; } = [];

    public IReadOnlyList<ZabbixHostInfo> Hosts { get; init; } = [];

    public IReadOnlyList<ZabbixTriggerDependencyInfo> Dependencies { get; init; } = [];
}

public sealed record ZabbixTriggerDependencyInfo
{
    public string TriggerId { get; init; } = "";

    public string Description { get; init; } = "";
}

public sealed record ZabbixSuppressionAggregateDefinition
{
    public string Layer { get; init; } = "suppression";

    public string TargetManagedKey { get; init; } = "";

    public string TargetClass { get; init; } = "";

    public string TargetCardId { get; init; } = "";

    public string TargetName { get; init; } = "";

    public string AggregationType { get; init; } = "";

    public string HostGroupName { get; init; } = "";

    public string HostName { get; init; } = "";

    public string HostVisibleName { get; init; } = "";

    public string ItemKey { get; init; } = "";

    public string ItemName { get; init; } = "";

    public string CalculationFormula { get; init; } = "";

    public string TriggerName { get; init; } = "";

    public string TriggerExpression { get; init; } = "";

    public int TriggerPriority { get; init; }
}

public sealed record ZabbixSuppressionAggregateApplyResult
{
    public string HostId { get; init; } = "";

    public string ItemId { get; init; } = "";

    public string TriggerId { get; init; } = "";

    public string HostAction { get; init; } = "";

    public string ItemAction { get; init; } = "";

    public string TriggerAction { get; init; } = "";
}

public sealed record ZabbixSuppressionAggregateItemInfo
{
    public string ItemId { get; init; } = "";

    public string Name { get; init; } = "";

    public string Key { get; init; } = "";

    public string Status { get; init; } = "";

    public string State { get; init; } = "";

    public string Error { get; init; } = "";

    public string LastValue { get; init; } = "";

    public string LastClock { get; init; } = "";
}

public sealed record ZabbixManagedServiceApplyResult
{
    public bool Success { get; init; }

    public string Action { get; init; } = "";

    public string ServiceId { get; init; } = "";

    public int RelationsApplied { get; init; }

    public int RelationsDeferred { get; init; }

    public int SourceLeafServicesApplied { get; init; }

    public int ProblemTagsApplied { get; init; }

    public int HostTagsApplied { get; init; }

    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public static class ZabbixServiceAlgorithms
{
    public const int AlwaysOk = 0;

    public const int MostCriticalIfAllChildrenHaveProblems = 1;

    public const int MostCriticalOfChildren = 2;
}

public static class ZabbixProblemTagOperators
{
    public const int Equal = 0;

    public const int Contains = 2;
}

public static class ZabbixSlaPeriods
{
    public const int Daily = 0;

    public const int Weekly = 1;

    public const int Monthly = 2;

    public const int Quarterly = 3;

    public const int Annually = 4;
}

public static class ZabbixSlaStatuses
{
    public const int Enabled = 0;

    public const int Disabled = 1;
}

public static class ZabbixSlaServiceTagOperators
{
    public const int Equal = 0;

    public const int Like = 2;
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

    public const string SourceKeyAttribute = "cmdb2monitoring:source_key_attribute";

    public const string SourceKeyValue = "cmdb2monitoring:source_key_value";

    public const string SourceZabbixHostId = "cmdb2monitoring:source_hostid";

    public const string SourceLeaf = "cmdb2monitoring:source_leaf";

    public const string Role = "cmdb2monitoring:role";

    public const string Visibility = "cmdb2monitoring:visibility";

    public const string Aggregate = "cmdb2monitoring:aggregate";

    public const string AggregateKind = "cmdb2monitoring:aggregate_kind";

    public const string AggregationType = "cmdb2monitoring:aggregation_type";

    public const string IsCritical = "cmdb2monitoring:is_critical";

    public const string Threshold = "cmdb2monitoring:threshold";

    public const string N = "cmdb2monitoring:n";

    public const string SlaPolicy = "cmdb2monitoring:sla_policy";

    public const string SlaTarget = "cmdb2monitoring:sla_target";
}

public static class ZabbixManagedServiceRoles
{
    public const string RootService = "root_service";

    public const string ServiceGroup = "service_group";

    public const string Aggregate = "aggregate";

    public const string SourceLeaf = "source_leaf";

    public const string Internal = "internal";
}

public static class ZabbixManagedServiceVisibility
{
    public const string Root = "root";

    public const string Child = "child";

    public const string Internal = "internal";
}

public static class ZabbixManagedServiceMapper
{
    public static ZabbixManagedServiceDefinition FromAggregationCommand(
        AggregationCommand command,
        string layer,
        IReadOnlyList<string>? childManagedKeys = null)
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
        var role = ServiceRole(command, layer);
        var visibility = ServiceVisibility(command.Target.Attributes, role);
        var tags = BuildTags(command, layer, managedKey, role, visibility);

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
            SourceKeyAttribute = command.Source.KeyAttribute,
            SourceKeyValue = command.Source.KeyValue,
            SourceZabbixHostId = command.Source.ZabbixHostId,
            Name = Trim(name, 255),
            Description = Trim(description, 2048),
            Algorithm = ServiceAlgorithm(command.Target.Attributes),
            SortOrder = ClampInt(FirstAttribute(command.Target.Attributes, "sortorder", "sort_order"), 0, 0, 999),
            Weight = ClampInt(FirstAttribute(command.Target.Attributes, "weight"), 0, 0, 1_000_000),
            Role = role,
            Visibility = visibility,
            Relations = command.Target.Relations
                .Select(relation => new ZabbixManagedServiceRelation
                {
                    DomainCode = relation.DomainCode,
                    TargetClassCode = relation.TargetClassCode,
                    TargetLookup = relation.TargetLookup
                })
                .ToArray(),
            ParentManagedKeys = command.Target.ParentManagedKeys
                .Where(key => !string.IsNullOrWhiteSpace(key))
                .Distinct(StringComparer.Ordinal)
                .ToArray(),
            ChildManagedKeys = (childManagedKeys ?? [])
                .Where(key => !string.IsNullOrWhiteSpace(key))
                .Distinct(StringComparer.Ordinal)
                .ToArray(),
            Tags = tags
        };
    }

    public static ZabbixManagedServiceDefinition FromSourceBinding(AggregationCommand command, string layer)
    {
        var managedKey = SourceLeafManagedKey(layer, command.Source);
        var name = SourceLeafDisplayName(command.Source);
        var tags = HostProblemBindingTags(command.Source)
            .Where(item => !string.IsNullOrWhiteSpace(item.Value))
            .Concat(new[]
            {
                new KeyValuePair<string, string>(ZabbixManagedServiceTags.Managed, "true"),
                new KeyValuePair<string, string>(ZabbixManagedServiceTags.Layer, layer),
                new KeyValuePair<string, string>(ZabbixManagedServiceTags.Class, command.Source.ClassCode),
                new KeyValuePair<string, string>(ZabbixManagedServiceTags.Key, managedKey),
                new KeyValuePair<string, string>(ZabbixManagedServiceTags.SourceLeaf, "true"),
                new KeyValuePair<string, string>(ZabbixManagedServiceTags.Role, ZabbixManagedServiceRoles.SourceLeaf),
                new KeyValuePair<string, string>(ZabbixManagedServiceTags.Visibility, ZabbixManagedServiceVisibility.Internal)
            })
            .GroupBy(item => item.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => Trim(group.Last().Value, 255), StringComparer.Ordinal);

        AddTag(tags, ZabbixManagedServiceTags.CardId, command.Source.CardId);
        AddTag(tags, ZabbixManagedServiceTags.RuleId, command.RuleId);
        AddTag(tags, ZabbixManagedServiceTags.RuleName, command.RuleName);

        return new ZabbixManagedServiceDefinition
        {
            Layer = layer,
            ManagedKey = managedKey,
            ClassCode = command.Source.ClassCode,
            CardId = command.Source.CardId,
            RuleId = command.RuleId,
            RuleName = command.RuleName,
            SourceClass = command.Source.ClassCode,
            SourceCardId = command.Source.CardId,
            SourceKeyAttribute = command.Source.KeyAttribute,
            SourceKeyValue = command.Source.KeyValue,
            SourceZabbixHostId = command.Source.ZabbixHostId,
            Name = Trim(name, 255),
            Description = Trim($"CMDBuild source object for Zabbix problem binding: {name}", 2048),
            Algorithm = ZabbixServiceAlgorithms.MostCriticalOfChildren,
            Role = ZabbixManagedServiceRoles.SourceLeaf,
            Visibility = ZabbixManagedServiceVisibility.Internal,
            Tags = tags,
            ProblemTags = ProblemTagsForSource(command.Source)
        };
    }

    public static string SourceLeafManagedKey(string layer, AggregationSourceObject source)
    {
        return string.Join(
            ":",
            new[]
            {
                "source",
                string.IsNullOrWhiteSpace(layer) ? "unknown" : layer.Trim(),
                string.IsNullOrWhiteSpace(source.ClassCode) ? "unknown" : source.ClassCode.Trim(),
                string.IsNullOrWhiteSpace(source.CardId) ? "unknown" : source.CardId.Trim()
            });
    }

    public static IReadOnlyDictionary<string, string> HostTagsForSource(AggregationSourceObject source)
    {
        return HostProblemBindingTags(source)
            .Where(item => !string.IsNullOrWhiteSpace(item.Value))
            .GroupBy(item => item.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => Trim(group.Last().Value, 255), StringComparer.Ordinal);
    }

    public static IReadOnlyList<ZabbixProblemTag> ProblemTagsForSource(AggregationSourceObject source)
    {
        if (string.IsNullOrWhiteSpace(source.ZabbixHostId))
        {
            return [];
        }

        return
        [
            new ZabbixProblemTag(
                ZabbixManagedServiceTags.SourceZabbixHostId,
                Trim(source.ZabbixHostId, 255),
                ZabbixProblemTagOperators.Equal)
        ];
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

    public static string ServiceRole(AggregationCommand command, string layer)
    {
        var explicitRole = FirstAttribute(
            command.Target.Attributes,
            "cmdb2monitoring_role",
            "zabbix_service_role",
            "service_role");
        if (!string.IsNullOrWhiteSpace(explicitRole))
        {
            return NormalizeRole(explicitRole);
        }

        var hasSource = !string.IsNullOrWhiteSpace(command.Source.ClassCode)
            || !string.IsNullOrWhiteSpace(command.Source.CardId);
        return ServiceRoleForTarget(layer, command.Target.ClassCode, hasSource);
    }

    public static string ServiceRoleForTarget(string layer, string classCode, bool hasSource)
    {
        if (string.Equals(layer, "service", StringComparison.OrdinalIgnoreCase)
            && RemoveKnownManagedPrefix(classCode).Equals("ServicePlatformService", StringComparison.OrdinalIgnoreCase))
        {
            return ZabbixManagedServiceRoles.RootService;
        }

        return hasSource
            ? ZabbixManagedServiceRoles.Aggregate
            : ZabbixManagedServiceRoles.ServiceGroup;
    }

    public static string ServiceVisibility(IReadOnlyDictionary<string, object?> attributes, string role)
    {
        var explicitVisibility = FirstAttribute(
            attributes,
            "cmdb2monitoring_visibility",
            "zabbix_service_visibility",
            "service_visibility");
        if (!string.IsNullOrWhiteSpace(explicitVisibility))
        {
            return NormalizeVisibility(explicitVisibility, role);
        }

        return role switch
        {
            ZabbixManagedServiceRoles.RootService => ZabbixManagedServiceVisibility.Root,
            ZabbixManagedServiceRoles.SourceLeaf or ZabbixManagedServiceRoles.Internal => ZabbixManagedServiceVisibility.Internal,
            _ => ZabbixManagedServiceVisibility.Child
        };
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
        string managedKey,
        string role,
        string visibility)
    {
        var tags = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [ZabbixManagedServiceTags.Managed] = "true",
            [ZabbixManagedServiceTags.Layer] = layer,
            [ZabbixManagedServiceTags.Class] = command.Target.ClassCode,
            [ZabbixManagedServiceTags.Key] = managedKey,
            [ZabbixManagedServiceTags.Role] = role,
            [ZabbixManagedServiceTags.Visibility] = visibility
        };

        AddTag(tags, ZabbixManagedServiceTags.CardId, command.Target.CardId);
        AddTag(tags, ZabbixManagedServiceTags.RuleId, command.RuleId);
        AddTag(tags, ZabbixManagedServiceTags.RuleName, command.RuleName);
        AddTag(tags, ZabbixManagedServiceTags.SourceClass, command.Source.ClassCode);
        AddTag(tags, ZabbixManagedServiceTags.SourceCardId, command.Source.CardId);
        AddTag(tags, ZabbixManagedServiceTags.SourceKeyAttribute, command.Source.KeyAttribute);
        AddTag(tags, ZabbixManagedServiceTags.SourceKeyValue, command.Source.KeyValue);
        AddTag(tags, ZabbixManagedServiceTags.SourceZabbixHostId, command.Source.ZabbixHostId);
        AddTag(tags, ZabbixManagedServiceTags.AggregationType, FirstAttribute(command.Target.Attributes, "aggregation_type"));
        AddTag(tags, ZabbixManagedServiceTags.IsCritical, FirstAttribute(command.Target.Attributes, "is_critical"));
        AddTag(tags, ZabbixManagedServiceTags.Threshold, FirstAttribute(command.Target.Attributes, "threshold"));
        AddTag(tags, ZabbixManagedServiceTags.N, FirstAttribute(command.Target.Attributes, "n"));
        AddTag(tags, ZabbixManagedServiceTags.SlaPolicy, FirstAttribute(command.Target.Attributes, "sla_policy_key", "sla_policy", "SlaPolicy"));
        AddTag(tags, ZabbixManagedServiceTags.SlaTarget, FirstAttribute(command.Target.Attributes, "sla_target"));
        return tags;
    }

    private static IEnumerable<KeyValuePair<string, string>> HostProblemBindingTags(AggregationSourceObject source)
    {
        yield return new KeyValuePair<string, string>(ZabbixManagedServiceTags.SourceClass, source.ClassCode);
        yield return new KeyValuePair<string, string>(ZabbixManagedServiceTags.SourceCardId, source.CardId);
        yield return new KeyValuePair<string, string>(ZabbixManagedServiceTags.SourceKeyAttribute, source.KeyAttribute);
        yield return new KeyValuePair<string, string>(ZabbixManagedServiceTags.SourceKeyValue, source.KeyValue);
        yield return new KeyValuePair<string, string>(ZabbixManagedServiceTags.SourceZabbixHostId, source.ZabbixHostId);
    }

    private static string SourceLeafDisplayName(AggregationSourceObject source)
    {
        var key = FirstSourceAttribute(
                source.Attributes,
                "zabbix_service_name",
                "monitoring_name",
                "Code",
                "code",
                "Name",
                "name",
                "Description",
                "description",
                "hostName",
                "HostName",
                "hostname",
                "host_name")
            ?? source.CardId;
        if (string.IsNullOrWhiteSpace(key))
        {
            key = "unknown";
        }

        return $"{source.ClassCode} / {key}";
    }

    private static string? FirstSourceAttribute(IReadOnlyDictionary<string, string> attributes, params string[] names)
    {
        foreach (var name in names)
        {
            if (attributes.TryGetValue(name, out var exactValue) && !string.IsNullOrWhiteSpace(exactValue))
            {
                return exactValue;
            }
        }

        foreach (var pair in attributes)
        {
            if (!string.IsNullOrWhiteSpace(pair.Value)
                && names.Any(name => string.Equals(name, pair.Key, StringComparison.OrdinalIgnoreCase)))
            {
                return pair.Value;
            }
        }

        return null;
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

    private static string NormalizeRole(string value)
    {
        var normalized = value.Trim().ToLowerInvariant()
            .Replace("-", "_", StringComparison.Ordinal)
            .Replace(" ", "_", StringComparison.Ordinal);
        return normalized switch
        {
            "root" or "business_service" or "root_service" => ZabbixManagedServiceRoles.RootService,
            "group" or "visible_group" or "service_group" => ZabbixManagedServiceRoles.ServiceGroup,
            "aggregate" or "aggregate_group" => ZabbixManagedServiceRoles.Aggregate,
            "source_leaf" or "leaf" => ZabbixManagedServiceRoles.SourceLeaf,
            "internal" or "technical" => ZabbixManagedServiceRoles.Internal,
            _ => normalized
        };
    }

    private static string NormalizeVisibility(string value, string role)
    {
        var normalized = value.Trim().ToLowerInvariant()
            .Replace("-", "_", StringComparison.Ordinal)
            .Replace(" ", "_", StringComparison.Ordinal);
        return normalized switch
        {
            "root" => ZabbixManagedServiceVisibility.Root,
            "internal" or "hidden" or "technical" => ZabbixManagedServiceVisibility.Internal,
            "child" or "visible" => ZabbixManagedServiceVisibility.Child,
            _ => ServiceVisibility(new Dictionary<string, object?>(StringComparer.Ordinal), role)
        };
    }

    private static string RemoveKnownManagedPrefix(string code)
    {
        var normalized = code.Trim();
        return normalized.StartsWith("C2M_", StringComparison.OrdinalIgnoreCase)
            ? normalized["C2M_".Length..]
            : normalized;
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
