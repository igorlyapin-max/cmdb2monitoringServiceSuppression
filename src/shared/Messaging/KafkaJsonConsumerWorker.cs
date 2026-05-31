using System.Text.Json;
using Cmdb2MonitoringServiceSuppression.Shared.Configuration;
using Cmdb2MonitoringServiceSuppression.Shared.Observability;
using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cmdb2MonitoringServiceSuppression.Shared.Messaging;

public abstract class KafkaJsonConsumerWorker<TMessage>(
    IOptions<KafkaOptions> options,
    IServiceProvider services,
    ILogger logger)
    : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IOptions<KafkaTopicsOptions> topicOptions = services.GetRequiredService<IOptions<KafkaTopicsOptions>>();
    private readonly KafkaJsonProducer producer = services.GetRequiredService<KafkaJsonProducer>();
    private readonly AppMetrics metrics = services.GetRequiredService<AppMetrics>();
    private readonly IOptionsMonitor<CorrelationOptions> correlationOptions = services.GetRequiredService<IOptionsMonitor<CorrelationOptions>>();

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

            var correlationId = ReadCorrelationId(result.Message.Headers, correlationOptions.CurrentValue);
            using var correlationScope = CorrelationContext.Begin(correlationId);
            using var loggerScope = logger.BeginScope(new Dictionary<string, object>
            {
                ["CorrelationId"] = CorrelationContext.CurrentId ?? ""
            });
            metrics.Increment(
                "kafka_messages_consumed_total",
                ("topic", result.Topic),
                ("consumer", GetType().Name));

            TMessage message;
            try
            {
                message = JsonSerializer.Deserialize<TMessage>(result.Message.Value, JsonOptions)
                    ?? throw new JsonException("Kafka message body is empty.");
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException)
            {
                logger.LogError(ex, "Kafka message deserialization failed for topic {Topic}, partition {Partition}, offset {Offset}.",
                    Topic,
                    result.Partition.Value,
                    result.Offset.Value);
                await PublishDeadLetterAsync(result, ex, attempt: 1, stoppingToken);
                consumer.Commit(result);
                continue;
            }

            var handled = false;
            var maxAttempts = Math.Max(1, kafkaOptions.MaxProcessingAttempts);
            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
                var started = DateTimeOffset.UtcNow;
                try
                {
                    await HandleMessageAsync(message, result.Message.Key, stoppingToken);
                    consumer.Commit(result);
                    metrics.ObserveSeconds(
                        "kafka_message_processing_duration_seconds",
                        DateTimeOffset.UtcNow - started,
                        ("topic", result.Topic),
                        ("consumer", GetType().Name),
                        ("status", "ok"));
                    handled = true;
                    break;
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    handled = true;
                    break;
                }
                catch (Exception ex) when (ex is JsonException or InvalidOperationException or HttpRequestException or TaskCanceledException)
                {
                    logger.LogError(ex, "Kafka message processing failed for topic {Topic}, partition {Partition}, offset {Offset}; attempt {Attempt}/{MaxAttempts}.",
                        Topic,
                        result.Partition.Value,
                        result.Offset.Value,
                        attempt,
                        maxAttempts);
                    metrics.Increment(
                        "kafka_message_processing_failures_total",
                        ("topic", result.Topic),
                        ("consumer", GetType().Name),
                        ("exception", ex.GetType().Name));

                    if (attempt >= maxAttempts)
                    {
                        await PublishDeadLetterAsync(result, ex, attempt, stoppingToken);
                        consumer.Commit(result);
                        handled = true;
                        break;
                    }

                    await Task.Delay(TimeSpan.FromMilliseconds(kafkaOptions.ProcessingRetryDelayMs), stoppingToken);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    logger.LogError(ex, "Kafka message processing failed unexpectedly for topic {Topic}, partition {Partition}, offset {Offset}; attempt {Attempt}/{MaxAttempts}.",
                        Topic,
                        result.Partition.Value,
                        result.Offset.Value,
                        attempt,
                        maxAttempts);
                    metrics.Increment(
                        "kafka_message_processing_failures_total",
                        ("topic", result.Topic),
                        ("consumer", GetType().Name),
                        ("exception", ex.GetType().Name));

                    if (attempt >= maxAttempts)
                    {
                        await PublishDeadLetterAsync(result, ex, attempt, stoppingToken);
                        consumer.Commit(result);
                        handled = true;
                        break;
                    }

                    await Task.Delay(TimeSpan.FromMilliseconds(kafkaOptions.ProcessingRetryDelayMs), stoppingToken);
                }
            }

            if (!handled && stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task PublishDeadLetterAsync(
        ConsumeResult<string, string> result,
        Exception exception,
        int attempt,
        CancellationToken cancellationToken)
    {
        var kafkaOptions = options.Value;
        var deadLetterTopic = topicOptions.Value.DeadLetterTopic;
        if (!kafkaOptions.DeadLetterEnabled || string.IsNullOrWhiteSpace(deadLetterTopic))
        {
            logger.LogWarning(
                "Kafka dead letter publishing is disabled; committing failed message from topic {Topic}, partition {Partition}, offset {Offset}.",
                result.Topic,
                result.Partition.Value,
                result.Offset.Value);
            return;
        }

        var envelope = new KafkaDeadLetterEnvelope(
            DateTimeOffset.UtcNow,
            GetType().Name,
            result.Topic,
            result.Partition.Value,
            result.Offset.Value,
            result.Message.Key,
            result.Message.Value,
            exception.GetType().FullName ?? exception.GetType().Name,
            exception.Message,
            attempt,
            CorrelationContext.CurrentId);

        await producer.PublishAsync(
            deadLetterTopic,
            result.Message.Key,
            envelope,
            cancellationToken);
        metrics.Increment(
            "kafka_messages_dead_lettered_total",
            ("topic", result.Topic),
            ("dead_letter_topic", deadLetterTopic),
            ("consumer", GetType().Name));
    }

    private static string? ReadCorrelationId(Headers? headers, CorrelationOptions options)
    {
        if (!options.Enabled || headers is null)
        {
            return null;
        }

        var headerName = CorrelationContext.HeaderName(options.HeaderName);
        var header = headers.FirstOrDefault(header =>
            string.Equals(header.Key, headerName, StringComparison.OrdinalIgnoreCase));
        if (header is null)
        {
            return null;
        }

        var bytes = header.GetValueBytes();
        return bytes is null || bytes.Length == 0
            ? null
            : System.Text.Encoding.UTF8.GetString(bytes);
    }

    private sealed record KafkaDeadLetterEnvelope(
        DateTimeOffset FailedAtUtc,
        string Consumer,
        string Topic,
        int Partition,
        long Offset,
        string Key,
        string Payload,
        string ExceptionType,
        string Error,
        int Attempt,
        string? CorrelationId);
}
