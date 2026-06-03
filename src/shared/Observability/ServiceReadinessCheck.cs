namespace Cmdb2MonitoringServiceSuppression.Shared.Observability;

public interface IServiceReadinessCheck
{
    string Name { get; }

    Task<ServiceReadinessCheckResult> CheckAsync(CancellationToken cancellationToken);
}

public sealed record ServiceReadinessCheckResult(
    string Name,
    bool Ready,
    string Message,
    bool Required = true)
{
    public static ServiceReadinessCheckResult Ok(string name, string message = "ready", bool required = true)
    {
        return new ServiceReadinessCheckResult(name, true, message, required);
    }

    public static ServiceReadinessCheckResult NotReady(string name, string message, bool required = true)
    {
        return new ServiceReadinessCheckResult(name, false, message, required);
    }
}
