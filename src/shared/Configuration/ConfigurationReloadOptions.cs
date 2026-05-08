namespace Cmdb2MonitoringServiceSuppression.Shared.Configuration;

public sealed class ConfigurationReloadOptions
{
    public const string SectionName = "ConfigurationReload";

    public bool Enabled { get; init; } = false;

    public string Route { get; init; } = "/configuration/reload";

    public string BearerToken { get; init; } = "";

    public string BearerTokenSecret { get; init; } = "";

    public bool HasValidRoute()
    {
        return !string.IsNullOrWhiteSpace(Route) && Route.StartsWith('/');
    }
}
