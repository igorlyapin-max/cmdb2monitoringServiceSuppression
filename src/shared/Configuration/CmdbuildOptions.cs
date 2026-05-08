namespace Cmdb2MonitoringServiceSuppression.Shared.Configuration;

public sealed class CmdbuildOptions
{
    public const string SectionName = "Cmdbuild";

    public string BaseUrl { get; set; } = "";

    public string AuthMode { get; set; } = "Login";

    public string Username { get; set; } = "";

    public string Password { get; set; } = "";

    public string ApiToken { get; set; } = "";

    public int RequestTimeoutMs { get; set; } = 10000;

    public bool HasValidAuthMode()
    {
        return string.Equals(AuthMode, "None", StringComparison.OrdinalIgnoreCase)
            || string.Equals(AuthMode, "Login", StringComparison.OrdinalIgnoreCase)
            || string.Equals(AuthMode, "Token", StringComparison.OrdinalIgnoreCase)
            || string.Equals(AuthMode, "IndeedPam", StringComparison.OrdinalIgnoreCase);
    }
}
