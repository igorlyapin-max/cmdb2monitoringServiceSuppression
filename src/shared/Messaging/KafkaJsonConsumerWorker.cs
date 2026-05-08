using System.Text.Json;
using Cmdb2MonitoringServiceSuppression.Shared.Configuration;
using Confluent.Kafka;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cmdb2MonitoringServiceSuppression.Shared.Messaging;

public abstract class KafkaJsonConsumerWorker<TMessage>(
    IOptions<KafkaOptions> options,
    ILogger logger)
    : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    protected abstract string Topic { get; }

    protected abstract string ConsumerGroupId { get; }

    protected abstract Task HandleMessageAsync(
        TMessage message,
        string key,
        CancellationToken cancellationToken);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var kafkaOptions = options.Value;
        if (!kafkaOptions.Enabled)
        {
            logger.LogInformation("Kafka is disabled; consumer {Consumer} is not started.", GetType().Name);
            return;
        }

        if (string.IsNullOrWhiteSpace(Topic))
        {
            throw new InvalidOperationException($"{GetType().Name}: topic is not configured.");
        }

        var groupId = string.IsNullOrWhiteSpace(ConsumerGroupId)
            ? kafkaOptions.ConsumerGroupId
            : ConsumerGroupId;
        if (string.IsNullOrWhiteSpace(groupId))
        {
            throw new InvalidOperationException($"{GetType().Name}: consumer group id is not configured.");
        }

        using var consumer = new ConsumerBuilder<string, string>(
                KafkaConfigFactory.ConsumerConfig(kafkaOptions, groupId))
            .Build();
        consumer.Subscribe(Topic);
        logger.LogInformation("Kafka consumer {Consumer} subscribed to {Topic} with group {GroupId}.", GetType().Name, Topic, groupId);

        while (!stoppingToken.IsCancellationRequested)
        {
            ConsumeResult<string, string>? result;
            try
            {
                result = consumer.Consume(TimeSpan.FromMilliseconds(Math.Max(100, kafkaOptions.ConsumeTimeoutMs)));
            }
            catch (ConsumeException ex)
            {
                logger.LogError(ex, "Kafka consume failed for topic {Topic}.", Topic);
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                continue;
            }

            if (result is null)
            {
                continue;
            }

            try
            {
                var message = JsonSerializer.Deserialize<TMessage>(result.Message.Value, JsonOptions)
                    ?? throw new JsonException("Kafka message body is empty.");
                await HandleMessageAsync(message, result.Message.Key, stoppingToken);
                consumer.Commit(result);
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or HttpRequestException)
            {
                logger.LogError(ex, "Kafka message processing failed for topic {Topic}, partition {Partition}, offset {Offset}.",
                    Topic,
                    result.Partition.Value,
                    result.Offset.Value);
            }
        }
    }
}
