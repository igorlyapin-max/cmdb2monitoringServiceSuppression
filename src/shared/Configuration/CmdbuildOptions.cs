namespace Cmdb2MonitoringServiceSuppression.Shared.Configuration;

public sealed class CmdbuildOptions
{
    public const string SectionName = "Cmdbuild";

    public string BaseUrl { get; init; } = "";

    public string Username { get; init; } = "";

    public string Password { get; init; } = "";

    public int RequestTimeoutMs { get; init; } = 10000;
}
