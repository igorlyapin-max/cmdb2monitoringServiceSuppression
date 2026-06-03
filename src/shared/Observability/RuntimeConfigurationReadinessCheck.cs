using Cmdb2MonitoringServiceSuppression.Shared.Configuration;
using Microsoft.Extensions.Options;

namespace Cmdb2MonitoringServiceSuppression.Shared.Observability;

public sealed class RuntimeConfigurationReadinessCheck(
    IOptionsMonitor<KafkaOptions> kafkaOptions,
    IOptionsMonitor<KafkaLoggingOptions> kafkaLoggingOptions,
    IOptionsMonitor<ElkLoggingOptions> elkLoggingOptions)
    : IServiceReadinessCheck
{
    public string Name => "runtime-configuration";

    public Task<ServiceReadinessCheckResult> CheckAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var kafka = kafkaOptions.CurrentValue;
        var kafkaLogging = kafkaLoggingOptions.CurrentValue;
        var elkLogging = elkLoggingOptions.CurrentValue;
        var failures = new List<string>();

        if (kafka.Enabled && string.IsNullOrWhiteSpace(kafka.BootstrapServers))
        {
            failures.Add("Kafka:BootstrapServers is required when Kafka is enabled");
        }

        if (kafkaLogging.Enabled)
        {
            if (!kafka.Enabled)
            {
                failures.Add("KafkaLogging requires Kafka:Enabled=true");
            }

            if (string.IsNullOrWhiteSpace(kafkaLogging.Topic))
            {
                failures.Add("KafkaLogging:Topic is required when Kafka logging is enabled");
            }
        }

        if (elkLogging.Enabled && string.IsNullOrWhiteSpace(elkLogging.Endpoint))
        {
            failures.Add("ElkLogging:Endpoint is required when ELK logging is enabled");
        }

        return Task.FromResult(failures.Count == 0
            ? ServiceReadinessCheckResult.Ok(Name, "runtime configuration is valid")
            : ServiceReadinessCheckResult.NotReady(Name, string.Join("; ", failures)));
    }
}
