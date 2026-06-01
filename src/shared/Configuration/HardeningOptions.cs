namespace Cmdb2MonitoringServiceSuppression.Shared.Configuration;

public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    public bool Enabled { get; init; } = true;

    public int PermitLimit { get; init; } = 600;

    public int WindowSeconds { get; init; } = 60;

    public string[] ExcludedPathPrefixes { get; init; } = ["/health", "/ready", "/metrics"];

    public bool TrustForwardedFor { get; init; } = true;

    public bool HasValidWindow()
    {
        return WindowSeconds > 0;
    }

    public bool HasValidPermitLimit()
    {
        return PermitLimit > 0;
    }
}

public sealed class HostValidationOptions
{
    public const string SectionName = "HostValidation";

    public bool Enabled { get; set; } = true;

    public string[] AllowedHosts { get; set; } = ["localhost", "127.0.0.1", "::1"];

    public bool HasValidAllowedHosts()
    {
        return !Enabled
            || AllowedHosts.Any(host => !string.IsNullOrWhiteSpace(host));
    }
}

public sealed class TrustedProxyOptions
{
    public const string SectionName = "TrustedProxies";

    public bool Enabled { get; init; } = true;

    public string[] Networks { get; init; } = ["127.0.0.1", "::1"];

    public bool HasValidNetworks()
    {
        return !Enabled
            || Networks.Any(network => !string.IsNullOrWhiteSpace(network));
    }
}

public sealed class SecurityHeadersOptions
{
    public const string SectionName = "SecurityHeaders";

    public bool Enabled { get; init; } = true;

    public bool HstsEnabled { get; init; }

    public int HstsMaxAgeSeconds { get; init; } = 31536000;

    public string ContentSecurityPolicy { get; init; } = "";

    public string FrameOptions { get; init; } = "DENY";

    public string ReferrerPolicy { get; init; } = "no-referrer";

    public string PermissionsPolicy { get; init; } = "geolocation=(), microphone=(), camera=()";

    public bool HasValidHstsMaxAge()
    {
        return HstsMaxAgeSeconds > 0;
    }
}

public sealed class MetricsOptions
{
    public const string SectionName = "Metrics";

    public bool Enabled { get; init; } = true;

    public string Route { get; init; } = "/metrics";

    public bool RequireBearerToken { get; init; }

    public string BearerToken { get; init; } = "";

    public string BearerTokenSecret { get; init; } = "";

    public string[] AllowedNetworks { get; init; } = [];

    public bool HasValidRoute()
    {
        return Route.StartsWith("/", StringComparison.Ordinal);
    }

    public bool HasValidAccessPolicy()
    {
        return !RequireBearerToken
            || !string.IsNullOrWhiteSpace(BearerToken);
    }
}

public sealed class CorrelationOptions
{
    public const string SectionName = "Correlation";

    public const string DefaultHeaderName = "X-Correlation-Id";

    public bool Enabled { get; init; } = true;

    public string HeaderName { get; init; } = DefaultHeaderName;

    public bool HasValidHeaderName()
    {
        return !string.IsNullOrWhiteSpace(HeaderName)
            && !HeaderName.Contains('\r')
            && !HeaderName.Contains('\n');
    }
}

public sealed class ResilienceOptions
{
    public const string SectionName = "Resilience";

    public bool Enabled { get; init; } = true;

    public int MaxAttempts { get; init; } = 3;

    public int BaseDelayMs { get; init; } = 200;

    public int MaxDelayMs { get; init; } = 2000;

    public int CircuitBreakerFailures { get; init; } = 5;

    public int CircuitBreakerBreakSeconds { get; init; } = 30;

    public bool HasValidRetryPolicy()
    {
        return MaxAttempts > 0
            && BaseDelayMs >= 0
            && MaxDelayMs >= BaseDelayMs;
    }

    public bool HasValidCircuitBreaker()
    {
        return CircuitBreakerFailures > 0
            && CircuitBreakerBreakSeconds > 0;
    }
}
