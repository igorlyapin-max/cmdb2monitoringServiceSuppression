using Cmdb2MonitoringServiceSuppression.Shared.Configuration;
using Confluent.Kafka;

namespace Cmdb2MonitoringServiceSuppression.Shared.Messaging;

public static class KafkaConfigFactory
{
    public static ProducerConfig ProducerConfig(KafkaOptions options)
    {
        var config = new ProducerConfig
        {
            BootstrapServers = options.BootstrapServers,
            ClientId = string.IsNullOrWhiteSpace(options.ClientId) ? null : options.ClientId,
            MessageMaxBytes = options.MessageMaxBytes
        };
        ApplySecurity(config, options);
        return config;
    }

    public static ConsumerConfig ConsumerConfig(KafkaOptions options, string groupId)
    {
        var config = new ConsumerConfig
        {
            BootstrapServers = options.BootstrapServers,
            GroupId = groupId,
            ClientId = string.IsNullOrWhiteSpace(options.ClientId) ? null : options.ClientId,
            EnableAutoCommit = false,
            AutoOffsetReset = options.AutoOffsetReset.Equals("Latest", StringComparison.OrdinalIgnoreCase)
                ? AutoOffsetReset.Latest
                : AutoOffsetReset.Earliest
        };
        ApplySecurity(config, options);
        return config;
    }

    public static AdminClientConfig AdminClientConfig(KafkaOptions options)
    {
        var config = new AdminClientConfig
        {
            BootstrapServers = options.BootstrapServers,
            ClientId = string.IsNullOrWhiteSpace(options.ClientId) ? null : options.ClientId
        };
        ApplySecurity(config, options);
        return config;
    }

    private static void ApplySecurity(ClientConfig config, KafkaOptions options)
    {
        config.SecurityProtocol = options.SecurityProtocol.ToLowerInvariant() switch
        {
            "ssl" => SecurityProtocol.Ssl,
            "saslplaintext" => SecurityProtocol.SaslPlaintext,
            "saslssl" => SecurityProtocol.SaslSsl,
            _ => SecurityProtocol.Plaintext
        };

        if (config.SecurityProtocol is SecurityProtocol.SaslPlaintext or SecurityProtocol.SaslSsl)
        {
            config.SaslMechanism = options.SaslMechanism.ToLowerInvariant() switch
            {
                "scramsha256" or "scram-sha-256" => SaslMechanism.ScramSha256,
                "scramsha512" or "scram-sha-512" => SaslMechanism.ScramSha512,
                _ => SaslMechanism.Plain
            };
            config.SaslUsername = options.Username;
            config.SaslPassword = options.Password;
        }
    }
}
