using System.Text.Json;
using System.Text.Json.Serialization;
using Cmdb2MonitoringServiceSuppression.Shared.Configuration;
using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cmdb2MonitoringServiceSuppression.Shared.Messaging;

public sealed class KafkaJsonProducer : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly KafkaOptions options;
    private readonly ILogger<KafkaJsonProducer> logger;
    private readonly IProducer<string, string>? producer;

    public KafkaJsonProducer(IOptions<KafkaOptions> options, ILogger<KafkaJsonProducer> logger)
    {
        this.options = options.Value;
        this.logger = logger;
        if (!this.options.Enabled)
        {
            return;
        }

        producer = new ProducerBuilder<string, string>(KafkaConfigFactory.ProducerConfig(this.options))
            .Build();
    }

    public bool Enabled => options.Enabled;

    public async Task PublishAsync<T>(
        string topic,
        string key,
        T value,
        CancellationToken cancellationToken)
    {
        if (!options.Enabled)
        {
            logger.LogInformation("Kafka is disabled; message for topic {Topic} was not published.", topic);
            return;
        }

        if (producer is null)
        {
            throw new InvalidOperationException("Kafka producer is not initialized.");
        }

        if (string.IsNullOrWhiteSpace(topic))
        {
            throw new InvalidOperationException("Kafka topic is not configured.");
        }

        var payload = JsonSerializer.Serialize(value, JsonOptions);
        await producer.ProduceAsync(topic, new Message<string, string>
        {
            Key = key,
            Value = payload
        }, cancellationToken);
    }

    public void Dispose()
    {
        producer?.Flush(TimeSpan.FromSeconds(5));
        producer?.Dispose();
    }
}
