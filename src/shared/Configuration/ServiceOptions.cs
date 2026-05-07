namespace Cmdb2MonitoringServiceSuppression.Shared.Configuration;

public sealed class ServiceOptions
{
    public const string SectionName = "Service";

    public string Name { get; init; } = "";

    public string HealthRoute { get; init; } = "/health";
}
