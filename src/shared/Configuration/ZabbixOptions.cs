namespace Cmdb2MonitoringServiceSuppression.Shared.Configuration;

public sealed class ZabbixOptions
{
    public const string SectionName = "Zabbix";

    public string ApiEndpoint { get; init; } = "";

    public string AuthMode { get; init; } = "Login";

    public string ApiToken { get; init; } = "";

    public string User { get; init; } = "";

    public string Password { get; init; } = "";

    public int RequestTimeoutMs { get; init; } = 30000;

    public bool HasValidAuthMode()
    {
        return string.Equals(AuthMode, "None", StringComparison.OrdinalIgnoreCase)
            || string.Equals(AuthMode, "Token", StringComparison.OrdinalIgnoreCase)
            || string.Equals(AuthMode, "Login", StringComparison.OrdinalIgnoreCase)
            || string.Equals(AuthMode, "LoginOrToken", StringComparison.OrdinalIgnoreCase);
    }
}
