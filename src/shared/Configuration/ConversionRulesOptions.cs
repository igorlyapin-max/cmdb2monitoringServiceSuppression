namespace Cmdb2MonitoringServiceSuppression.Shared.Configuration;

public sealed class ConversionRulesOptions
{
    public const string SectionName = "ConversionRules";

    public string FilePath { get; init; } = "rules/conversion-rules.sample.json";

    public string ServiceTemplatesFilePath { get; init; } = "state/conversion-config/service-templates.json";

    public string SuppressionTemplatesFilePath { get; init; } = "state/conversion-config/suppression-templates.json";

    public bool ReloadOnEachEvent { get; init; } = true;

    public bool HasValidFilePath()
    {
        return !string.IsNullOrWhiteSpace(FilePath);
    }
}
