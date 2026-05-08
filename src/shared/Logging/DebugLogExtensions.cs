using Cmdb2MonitoringServiceSuppression.Shared.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cmdb2MonitoringServiceSuppression.Shared.Logging;

public static class DebugLogExtensions
{
    public static void LogDebugBasic(
        this ILogger logger,
        IOptions<DebugOptions> options,
        string message,
        params object?[] args)
    {
        if (options.Value.IsBasicEnabled())
        {
            logger.LogInformation($"Debug {options.Value.NormalizedLevel()}: {message}", args);
        }
    }

    public static void LogDebugVerbose(
        this ILogger logger,
        IOptions<DebugOptions> options,
        string message,
        params object?[] args)
    {
        if (options.Value.IsVerboseEnabled())
        {
            logger.LogInformation($"Debug Verbose: {message}", args);
        }
    }
}
