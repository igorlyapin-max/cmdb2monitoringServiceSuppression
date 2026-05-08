namespace Cmdb2MonitoringServiceSuppression.Shared.Configuration;

public sealed class ZabbixOptions
{
    public const string SectionName = "Zabbix";

    public string ApiEndpoint { get; set; } = "";

    public string AuthMode { get; set; } = "Login";

    public string ApiToken { get; set; } = "";

    public string User { get; set; } = "";

    public string Password { get; set; } = "";

    public int RequestTimeoutMs { get; set; } = 30000;

    public bool HasValidAuthMode()
    {
        return string.Equals(AuthMode, "None", StringComparison.OrdinalIgnoreCase)
            || string.Equals(AuthMode, "Token", StringComparison.OrdinalIgnoreCase)
            || string.Equals(AuthMode, "Login", StringComparison.OrdinalIgnoreCase)
            || string.Equals(AuthMode, "LoginOrToken", StringComparison.OrdinalIgnoreCase)
            || string.Equals(AuthMode, "IndeedPam", StringComparison.OrdinalIgnoreCase);
    }
}
