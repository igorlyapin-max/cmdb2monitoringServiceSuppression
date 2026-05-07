namespace Cmdb2MonitoringServiceSuppression.Shared.CmdbuildSchema;

public sealed record CmdbuildSchemaDefinition
{
    public required string Prefix { get; init; }

    public required SchemaLanguage Language { get; init; }

    public required string BuilderVersion { get; init; }

    public required IReadOnlyList<CmdbuildLookupDefinition> Lookups { get; init; }

    public required IReadOnlyList<CmdbuildModelRootDefinition> ModelRoots { get; init; }

    public required IReadOnlyList<CmdbuildClassDefinition> Classes { get; init; }

    public required IReadOnlyList<CmdbuildDomainDefinition> Domains { get; init; }

    public required IReadOnlyList<CmdbuildDomainDefinition> SuggestedDomains { get; init; }
}

public sealed record CmdbuildClassDefinition
{
    public required string Code { get; init; }

    public required string DisplayName { get; init; }

    public required BuilderLayer Layer { get; init; }

    public required string Purpose { get; init; }

    public required string Help { get; init; }

    public bool IsSuperclass { get; init; }

    public string ParentClassCode { get; init; } = "";

    public string Origin { get; init; } = "planned";

    public string SchemaStatus { get; init; } = "";

    public string SchemaStatusLabel { get; init; } = "";

    public bool ExistingInModelRoot { get; init; }

    public string ModelRoot { get; init; } = "";

    public bool ManagedByBuilder { get; init; }

    public bool AutoPopulationEnabled { get; init; }

    public required IReadOnlyList<CmdbuildAttributeDefinition> Attributes { get; init; }
}

public sealed record CmdbuildAttributeDefinition
{
    public required string Code { get; init; }

    public required string DisplayName { get; init; }

    public required string Type { get; init; }

    public string LookupTypeCode { get; init; } = "";

    public required bool Required { get; init; }

    public required string Help { get; init; }

    public string ValidationRules { get; init; } = "";
}

public sealed record CmdbuildLookupDefinition
{
    public required string Code { get; init; }

    public required string DisplayName { get; init; }

    public required IReadOnlyList<CmdbuildLookupValueDefinition> Values { get; init; }
}

public sealed record CmdbuildLookupValueDefinition
{
    public required string Code { get; init; }

    public required string DisplayName { get; init; }

    public required string Help { get; init; }
}

public sealed record CmdbuildModelRootDefinition
{
    public required BuilderLayer Layer { get; init; }

    public required string RootPath { get; init; }

    public required string Help { get; init; }
}

public sealed record CmdbuildDomainDefinition
{
    public required string Code { get; init; }

    public required string DisplayName { get; init; }

    public required BuilderLayer Layer { get; init; }

    public required string SourceClassCode { get; init; }

    public required string TargetClassCode { get; init; }

    public required string RelationType { get; init; }

    public required bool DeleteRelationOnCardDelete { get; init; }

    public required string Help { get; init; }

    public required IReadOnlyList<CmdbuildAttributeDefinition> Attributes { get; init; }

    public bool Suggested { get; init; }

    public string Reason { get; init; } = "";

    public bool IsSourceLink { get; init; }
}

public sealed record CmdbuildSchemaApplyResult
{
    public required IReadOnlyList<CmdbuildSchemaApplyItemResult> Items { get; init; }

    public int Created { get; init; }

    public int Updated { get; init; }

    public int Skipped { get; init; }

    public int Failed { get; init; }

    public bool Success => Failed == 0;
}

public sealed record CmdbuildSchemaApplyItemResult
{
    public required string Kind { get; init; }

    public required string Code { get; init; }

    public required string Action { get; init; }

    public required bool Success { get; init; }

    public string Message { get; init; } = "";
}
