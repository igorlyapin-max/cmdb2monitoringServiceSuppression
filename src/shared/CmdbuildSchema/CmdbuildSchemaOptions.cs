namespace Cmdb2MonitoringServiceSuppression.Shared.CmdbuildSchema;

public sealed record CmdbuildSchemaOptions
{
    public string Prefix { get; init; } = "";

    public SchemaLanguage Language { get; init; } = SchemaLanguage.Ru;

    public string BuilderVersion { get; init; } = "0.1";

    public string ServiceModelRoot { get; init; } = "";

    public string SuppressionModelRoot { get; init; } = "";

    public IReadOnlyList<CmdbuildCustomEntityOptions> CustomEntities { get; init; } = [];

    public IReadOnlyList<CmdbuildSourceLinkOptions> SourceLinks { get; init; } = [];

    public IReadOnlyList<CmdbuildExistingModelClassOptions> ExistingModelClasses { get; init; } = [];
}

public sealed record CmdbuildCustomEntityOptions
{
    public required string Code { get; init; }

    public BuilderLayer Layer { get; init; }

    public string DisplayName { get; init; } = "";

    public string Purpose { get; init; } = "";

    public bool SuggestDomains { get; init; } = true;
}

public sealed record CmdbuildSourceLinkOptions
{
    public required string ManagedClassCode { get; init; }

    public required string CustomerClassCode { get; init; }
}

public sealed record CmdbuildExistingModelClassOptions
{
    public required string Code { get; init; }

    public BuilderLayer Layer { get; init; }

    public string DisplayName { get; init; } = "";

    public string ModelRoot { get; init; } = "";

    public string ParentClassCode { get; init; } = "";

    public bool ManagedByBuilder { get; init; }

    public bool AutoPopulationEnabled { get; init; }
}

public sealed record CmdbuildSchemaApplyRequest
{
    public CmdbuildSchemaOptions Options { get; init; } = new();

    public CmdbuildSchemaSelection Selection { get; init; } = new();
}

public sealed record CmdbuildSchemaSelection
{
    public IReadOnlyList<string> Classes { get; init; } = [];

    public IReadOnlyList<string> Domains { get; init; } = [];

    public IReadOnlyList<string> Lookups { get; init; } = [];

    public bool IncludeDependencies { get; init; } = true;
}
