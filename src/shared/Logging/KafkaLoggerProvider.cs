using System.Text.Json;
using System.Threading.Channels;
using Cmdb2MonitoringServiceSuppression.Shared.Configuration;
using Cmdb2MonitoringServiceSuppression.Shared.Messaging;
using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cmdb2MonitoringServiceSuppression.Shared.Logging;

public sealed class KafkaLoggerProvider : ILoggerProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly KafkaLoggingOptions loggingOptions;
    private readonly IProducer<string, string>? producer;
    private readonly Channel<LogEvent>? channel;
    private readonly CancellationTokenSource cancellation = new();
    private readonly Task? worker;

    public KafkaLoggerProvider(IOptions<KafkaOptions> kafkaOptions, IOptions<KafkaLoggingOptions> loggingOptions)
    {
        this.loggingOptions = loggingOptions.Value;
        if (!kafkaOptions.Value.Enabled || !this.loggingOptions.Enabled)
        {
            return;
        }

        producer = new ProducerBuilder<string, string>(KafkaConfigFactory.ProducerConfig(kafkaOptions.Value)).Build();
        channel = Channel.CreateBounded<LogEvent>(new BoundedChannelOptions(Math.Max(1, this.loggingOptions.QueueCapacity))
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false
        });
        worker = Task.Run(ProcessQueueAsync);
    }

    public ILogger CreateLogger(string categoryName)
    {
        return channel is null || producer is null || !loggingOptions.Enabled
            ? NullLogger.Instance
            : new StructuredLogger(categoryName, loggingOptions, channel.Writer);
    }

    public void Dispose()
    {
        if (channel is not null)
        {
            channel.Writer.TryComplete();
        }

        using var flush = new CancellationTokenSource(TimeSpan.FromMilliseconds(Math.Max(1, loggingOptions.FlushTimeoutMs)));
        try
        {
            worker?.Wait(flush.Token);
            producer?.Flush(TimeSpan.FromMilliseconds(Math.Max(1, loggingOptions.FlushTimeoutMs)));
        }
        catch (OperationCanceledException)
        {
            cancellation.Cancel();
        }
        finally
        {
            producer?.Dispose();
            cancellation.Dispose();
        }
    }

    private async Task ProcessQueueAsync()
    {
        if (channel is null || producer is null)
        {
            return;
        }

        await foreach (var item in channel.Reader.ReadAllAsync(cancellation.Token))
        {
            try
            {
                await producer.ProduceAsync(loggingOptions.Topic, new Message<string, string>
                {
                    Key = item.Service,
                    Value = JsonSerializer.Serialize(item, JsonOptions)
                }, cancellation.Token);
            }
            catch
            {
                // Logging failures must not break the main flow.
            }
        }
    }

    private sealed class StructuredLogger(
        string categoryName,
        KafkaLoggingOptions options,
        ChannelWriter<LogEvent> writer) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return logLevel != LogLevel.None && logLevel >= options.GetMinimumLevel();
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            writer.TryWrite(LogEvent.From(categoryName, options.ServiceName, options.Environment, logLevel, eventId, formatter(state, exception), exception));
        }
    }
}
