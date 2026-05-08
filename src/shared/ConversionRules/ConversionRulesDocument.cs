using System.Text.Json.Serialization;

namespace Cmdb2MonitoringServiceSuppression.Shared.ConversionRules;

public sealed record ConversionRulesDocument
{
    [JsonPropertyName("version")]
    public string Version { get; init; } = "1";

    [JsonPropertyName("rules")]
    public IReadOnlyList<ConversionRule> Rules { get; init; } = [];

    [JsonPropertyName("source")]
    public ConversionRulesSourceCatalog Source { get; init; } = new();
}

public sealed record ConversionRulesSourceCatalog
{
    [JsonPropertyName("entityClasses")]
    public IReadOnlyList<string> EntityClasses { get; init; } = [];

    [JsonPropertyName("fields")]
    public IReadOnlyDictionary<string, SourceFieldDefinition> Fields { get; init; } =
        new Dictionary<string, SourceFieldDefinition>(StringComparer.Ordinal);
}

public sealed record SourceFieldDefinition
{
    [JsonPropertyName("classCode")]
    public string ClassCode { get; init; } = "";

    [JsonPropertyName("cmdbAttribute")]
    public string CmdbAttribute { get; init; } = "";
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

    [JsonPropertyName("when")]
    public RuleWhen When { get; init; } = new();

    [JsonPropertyName("target")]
    public required TargetObject Target { get; init; }

    [JsonPropertyName("relations")]
    public IReadOnlyList<TargetRelation> Relations { get; init; } = [];

    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; } = true;

    [JsonPropertyName("priority")]
    public int Priority { get; init; } = 100;
}

public sealed record SourceSelector
{
    [JsonPropertyName("class_code")]
    public required string ClassCode { get; init; }

    [JsonPropertyName("key_attribute")]
    public string? KeyAttribute { get; init; }

    [JsonPropertyName("conditions")]
    public IReadOnlyList<SourceCondition> Conditions { get; init; } = [];

    [JsonPropertyName("filters")]
    public IReadOnlyList<SourceCondition> Filters { get; init; } = [];
}

public sealed record RuleWhen
{
    [JsonPropertyName("allRegex")]
    public IReadOnlyList<RegexMatcher> AllRegex { get; init; } = [];

    [JsonPropertyName("anyRegex")]
    public IReadOnlyList<RegexMatcher> AnyRegex { get; init; } = [];

    [JsonPropertyName("noneRegex")]
    public IReadOnlyList<RegexMatcher> NoneRegex { get; init; } = [];

    [JsonPropertyName("fieldExists")]
    public string? FieldExists { get; init; }
}

public sealed record RegexMatcher
{
    [JsonPropertyName("field")]
    public required string Field { get; init; }

    [JsonPropertyName("pattern")]
    public required string Pattern { get; init; }
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
    public string IdempotencyKey { get; init; } = "";

    [JsonPropertyName("create_instance")]
    public bool CreateInstance { get; init; } = true;

    [JsonPropertyName("card_id")]
    public string CardId { get; init; } = "";

    [JsonPropertyName("card_description")]
    public string CardDescription { get; init; } = "";

    [JsonPropertyName("attribute_mappings")]
    public IReadOnlyDictionary<string, string> AttributeMappings { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    [JsonPropertyName("initial_user_values")]
    public IReadOnlyDictionary<string, string> InitialUserValues { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    [JsonPropertyName("user_responsibility_attributes")]
    public IReadOnlyList<string> UserResponsibilityAttributes { get; init; } = [];
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
