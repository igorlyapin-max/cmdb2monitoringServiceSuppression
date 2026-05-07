namespace Cmdb2MonitoringServiceSuppression.Shared.Integrations;

public sealed record IntegrationCheckResult
{
    public required string System { get; init; }

    public required string Endpoint { get; init; }

    public required bool Success { get; init; }

    public string? Version { get; init; }

    public string? Summary { get; init; }

    public string? Error { get; init; }
}
