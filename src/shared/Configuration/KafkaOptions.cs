namespace Cmdb2MonitoringServiceSuppression.Shared.Configuration;

public sealed class KafkaOptions
{
    public const string SectionName = "Kafka";

    public bool Enabled { get; init; }

    public string BootstrapServers { get; init; } = "";

    public string ClientId { get; init; } = "";

    public string ConsumerGroupId { get; init; } = "";

    public string SecurityProtocol { get; init; } = "Plaintext";

    public string SaslMechanism { get; init; } = "Plain";

    public string Username { get; init; } = "";

    public string Password { get; init; } = "";

    public string AutoOffsetReset { get; init; } = "Earliest";

    public int ConsumeTimeoutMs { get; init; } = 1000;

    public int MessageMaxBytes { get; init; } = 1048576;

    public bool HasValidBootstrapServers()
    {
        return !Enabled || !string.IsNullOrWhiteSpace(BootstrapServers);
    }

    public bool HasValidSecurityProtocol()
    {
        return SecurityProtocol.Equals("Plaintext", StringComparison.OrdinalIgnoreCase)
            || SecurityProtocol.Equals("Ssl", StringComparison.OrdinalIgnoreCase)
            || SecurityProtocol.Equals("SaslPlaintext", StringComparison.OrdinalIgnoreCase)
            || SecurityProtocol.Equals("SaslSsl", StringComparison.OrdinalIgnoreCase);
    }

    public bool HasValidAutoOffsetReset()
    {
        return AutoOffsetReset.Equals("Earliest", StringComparison.OrdinalIgnoreCase)
            || AutoOffsetReset.Equals("Latest", StringComparison.OrdinalIgnoreCase);
    }
}
