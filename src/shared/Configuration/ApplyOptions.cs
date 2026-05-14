namespace Cmdb2MonitoringServiceSuppression.Shared.Configuration;

public sealed class ApplyOptions
{
    public const string SectionName = "Apply";

    public bool AutoApplyEnabled { get; init; }

    public bool SafeApply { get; init; } = true;

    public string Mode { get; init; } = "manual";

    public bool CreateSuppressionServices { get; init; }

    public bool HasValidMode()
    {
        return string.Equals(Mode, "manual", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Mode, "auto", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Mode, "dry-run", StringComparison.OrdinalIgnoreCase);
    }

    public bool EffectiveAutoApplyEnabled()
    {
        return AutoApplyEnabled || string.Equals(Mode, "auto", StringComparison.OrdinalIgnoreCase);
    }
}
