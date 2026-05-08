using System.Text.Json;
using Cmdb2MonitoringServiceSuppression.Shared.Aggregation;
using Cmdb2MonitoringServiceSuppression.Shared.Configuration;
using Cmdb2MonitoringServiceSuppression.Shared.ConversionRules;
using Cmdb2MonitoringServiceSuppression.Shared.Integrations;
using Cmdb2MonitoringServiceSuppression.Shared.Logging;
using Cmdb2MonitoringServiceSuppression.Shared.Messaging;
using Cmdb2MonitoringServiceSuppression.Shared.Secrets;
using Confluent.Kafka;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);
await builder.Configuration.ResolveSecretReferencesAsync("cmdbconfigbuilder");
builder.AddServiceDefaults();

builder.Services.AddOptions<ApplyOptions>()
    .Bind(builder.Configuration.GetSection(ApplyOptions.SectionName))
    .Validate(options => options.HasValidMode(), "Apply mode must be manual, auto, or dry-run.")
    .ValidateOnStart();
builder.Services.AddOptions<CmdbuildOptions>()
    .Bind(builder.Configuration.GetSection(CmdbuildOptions.SectionName))
    .Validate(options => options.HasValidAuthMode(), "CMDBuild auth mode is invalid.")
    .Validate(options => options.RequestTimeoutMs > 0, "CMDBuild request timeout must be greater than zero.")
    .ValidateOnStart();
builder.Services.AddOptions<ZabbixOptions>()
    .Bind(builder.Configuration.GetSection(ZabbixOptions.SectionName))
    .Validate(options => options.HasValidAuthMode(), "Zabbix auth mode is invalid.")
    .Validate(options => options.RequestTimeoutMs > 0, "Zabbix request timeout must be greater than zero.")
    .ValidateOnStart();
builder.Services.AddOptions<ConversionRulesOptions>()
    .Bind(builder.Configuration.GetSection(ConversionRulesOptions.SectionName))
    .Validate(options => options.HasValidFilePath(), "ConversionRules:FilePath is required.")
    .ValidateOnStart();
builder.Services.AddOptions<KafkaTopicsOptions>()
    .Bind(builder.Configuration.GetSection(KafkaTopicsOptions.SectionName))
    .Validate(options => !string.IsNullOrWhiteSpace(options.CmdbWebhookEvents), "CMDB raw event topic is required.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.EffectiveAggregationCommands()), "Aggregation command topic is required.")
    .ValidateOnStart();

builder.Services.AddSingleton<ConversionRulesValidator>();
builder.Services.AddSingleton<ConversionRulesFileLoader>();
builder.Services.AddSingleton<AggregationRuleEngine>();
builder.Services.AddSingleton<KafkaJsonProducer>();
builder.Services.AddSingleton<KafkaTopicExplorer>();
builder.Services.AddHostedService<RuleEngineWorker>();
builder.Services.AddHttpClient<CmdbuildClient>();
builder.Services.AddHttpClient<ZabbixClient>();

var app = builder.Build();
app.MapServiceHealth();

app.MapPost("/rules/validate", (
    ConversionRulesDocument document,
    ConversionRulesValidator validator) =>
{
    var result = validator.Validate(document);
    return result.IsValid ? Results.Ok(result) : Results.BadRequest(result);
});

app.MapGet("/rules/status", async (
    ConversionRulesFileLoader loader,
    ConversionRulesValidator validator,
    CancellationToken cancellationToken) =>
{
    try
    {
        return Results.Ok(await loader.StatusAsync(validator, cancellationToken));
    }
    catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException)
    {
        return Results.Problem(
            title: "Conversion rules status is unavailable.",
            detail: ex.Message,
            statusCode: StatusCodes.Status500InternalServerError);
    }
});

app.MapPost("/build/dry-run", () => Results.Accepted(value: new
{
    status = "accepted",
    mode = "dry-run"
}));

app.MapPost("/events/convert/dry-run", async (
    CmdbRawEvent rawEvent,
    ConversionRulesFileLoader loader,
    AggregationRuleEngine engine,
    ConversionRulesValidator validator,
    CancellationToken cancellationToken) =>
{
    var rules = await loader.LoadAsync(cancellationToken);
    var validation = validator.Validate(rules);
    if (!validation.IsValid)
    {
        return Results.BadRequest(validation);
    }

    var commands = engine.BuildCommands(rawEvent, rules);
    return Results.Ok(new
    {
        raw_event = rawEvent.EventId,
        command_count = commands.Count,
        commands
    });
});

app.MapGet("/integrations/check", async (
    CmdbuildClient cmdbuild,
    ZabbixClient zabbix,
    CancellationToken cancellationToken) =>
{
    var cmdbuildResult = await cmdbuild.CheckConnectionAsync(cancellationToken);
    var zabbixResult = await zabbix.CheckConnectionAsync(cancellationToken);
    var success = cmdbuildResult.Success && zabbixResult.Success;

    return success
        ? Results.Ok(new { success, systems = new[] { cmdbuildResult, zabbixResult } })
        : Results.Problem(
            detail: "One or more integrations are unavailable.",
            extensions: new Dictionary<string, object?> { ["systems"] = new[] { cmdbuildResult, zabbixResult } },
            statusCode: StatusCodes.Status502BadGateway);
});

app.MapGet("/kafka/topics", (
    KafkaTopicExplorer explorer,
    CancellationToken cancellationToken) =>
{
    return explorer.ListTopicsAsync(cancellationToken);
});

app.MapGet("/kafka/topics/{topic}/events", async (
    string topic,
    int? limit,
    KafkaTopicExplorer explorer,
    CancellationToken cancellationToken) =>
{
    try
    {
        return Results.Ok(await explorer.ReadRecentEventsAsync(topic, limit ?? 5, cancellationToken));
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
    catch (Exception ex) when (ex is KafkaException or InvalidOperationException or TimeoutException)
    {
        return Results.Problem(ex.Message, statusCode: StatusCodes.Status502BadGateway);
    }
});

app.Run();

public sealed class RuleEngineWorker(
    IOptions<KafkaOptions> kafkaOptions,
    IOptions<KafkaTopicsOptions> topicOptions,
    IOptions<DebugOptions> debugOptions,
    ConversionRulesFileLoader loader,
    ConversionRulesValidator validator,
    AggregationRuleEngine engine,
    KafkaJsonProducer producer,
    ILogger<RuleEngineWorker> logger)
    : KafkaJsonConsumerWorker<CmdbRawEvent>(kafkaOptions, logger)
{
    protected override string Topic => topicOptions.Value.CmdbWebhookEvents;

    protected override string ConsumerGroupId => "";

    protected override async Task HandleMessageAsync(
        CmdbRawEvent message,
        string key,
        CancellationToken cancellationToken)
    {
        var rules = await loader.LoadAsync(cancellationToken);
        var validation = validator.Validate(rules);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException($"Conversion rules are invalid: {string.Join("; ", validation.Errors)}");
        }

        var commands = engine.BuildCommands(message, rules);
        logger.LogDebugBasic(
            debugOptions,
            "rule engine processed event={EventId}, class={ClassCode}, card={CardId}, commands={CommandCount}",
            message.EventId,
            message.ClassCode,
            message.CardId,
            commands.Count);

        foreach (var command in commands)
        {
            await producer.PublishAsync(
                topicOptions.Value.EffectiveAggregationCommands(),
                command.Target.CardId.Length > 0 ? command.Target.CardId : command.Target.IdempotencyKey,
                command,
                cancellationToken);
        }
    }
}

public sealed class KafkaTopicExplorer(
    IOptions<KafkaOptions> kafkaOptions,
    IOptions<KafkaTopicsOptions> topicOptions,
    ILogger<KafkaTopicExplorer> logger)
{
    private static readonly TimeSpan MetadataTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ConsumeTimeout = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan ReadDeadline = TimeSpan.FromSeconds(8);

    public Task<KafkaTopicsResponse> ListTopicsAsync(CancellationToken cancellationToken)
    {
        var kafka = kafkaOptions.Value;
        var managedTopics = ManagedTopics();
        if (!kafka.Enabled)
        {
            return Task.FromResult(new KafkaTopicsResponse(
                Enabled: false,
                ManagedIdentifier: topicOptions.Value.ManagedIdentifier,
                ManagedPrefix: topicOptions.Value.ManagedPrefix,
                CheckedAtUtc: DateTimeOffset.UtcNow,
                Topics: managedTopics
                    .Select(topic => ToTopicInfo(topic, null, exists: null))
                    .ToArray(),
                Error: "Kafka is disabled."));
        }

        cancellationToken.ThrowIfCancellationRequested();
        using var admin = new AdminClientBuilder(KafkaConfigFactory.AdminClientConfig(kafka)).Build();
        var metadata = admin.GetMetadata(MetadataTimeout);
        var metadataByTopic = metadata.Topics.ToDictionary(item => item.Topic, StringComparer.Ordinal);
        var topics = managedTopics
            .Select(topic =>
            {
                metadataByTopic.TryGetValue(topic.Name, out var topicMetadata);
                var exists = topicMetadata is not null && topicMetadata.Error.Code == ErrorCode.NoError;
                return ToTopicInfo(topic, topicMetadata, exists);
            })
            .ToArray();

        return Task.FromResult(new KafkaTopicsResponse(
            Enabled: true,
            ManagedIdentifier: topicOptions.Value.ManagedIdentifier,
            ManagedPrefix: topicOptions.Value.ManagedPrefix,
            CheckedAtUtc: DateTimeOffset.UtcNow,
            Topics: topics,
            Error: ""));
    }

    public async Task<KafkaTopicEventsResponse> ReadRecentEventsAsync(
        string topic,
        int limit,
        CancellationToken cancellationToken)
    {
        var normalizedTopic = Uri.UnescapeDataString(topic ?? "").Trim();
        var normalizedLimit = Math.Clamp(limit, 1, 100);
        var managedTopics = ManagedTopics();
        var managedTopic = managedTopics.FirstOrDefault(item => item.Name.Equals(normalizedTopic, StringComparison.Ordinal));
        if (managedTopic is null)
        {
            throw new ArgumentException($"Kafka topic '{normalizedTopic}' is not configured as a managed cmdb2monitoring topic.");
        }

        var kafka = kafkaOptions.Value;
        if (!kafka.Enabled)
        {
            return new KafkaTopicEventsResponse(
                Enabled: false,
                Topic: normalizedTopic,
                Role: managedTopic.Role,
                Limit: normalizedLimit,
                CheckedAtUtc: DateTimeOffset.UtcNow,
                Events: Array.Empty<KafkaEventInfo>(),
                Error: "Kafka is disabled.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        using var admin = new AdminClientBuilder(KafkaConfigFactory.AdminClientConfig(kafka)).Build();
        var metadata = admin.GetMetadata(normalizedTopic, MetadataTimeout);
        var topicMetadata = metadata.Topics.FirstOrDefault(item => item.Topic.Equals(normalizedTopic, StringComparison.Ordinal));
        if (topicMetadata is null || topicMetadata.Error.Code != ErrorCode.NoError)
        {
            return new KafkaTopicEventsResponse(
                Enabled: true,
                Topic: normalizedTopic,
                Role: managedTopic.Role,
                Limit: normalizedLimit,
                CheckedAtUtc: DateTimeOffset.UtcNow,
                Events: Array.Empty<KafkaEventInfo>(),
                Error: topicMetadata?.Error.Reason ?? "Kafka topic was not found.");
        }

        var consumerConfig = KafkaConfigFactory.ConsumerConfig(kafka, $"monitoring-ui-kafka-browser-{Guid.NewGuid():N}");
        consumerConfig.EnablePartitionEof = true;
        using var consumer = new ConsumerBuilder<string, string>(consumerConfig).Build();
        var assignments = new List<TopicPartitionOffset>();
        var highOffsets = new Dictionary<TopicPartition, long>();
        foreach (var partition in topicMetadata.Partitions.Where(item => item.Error.Code == ErrorCode.NoError))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var topicPartition = new TopicPartition(normalizedTopic, new Partition(partition.PartitionId));
            var watermark = consumer.QueryWatermarkOffsets(topicPartition, MetadataTimeout);
            if (watermark.High.Value <= watermark.Low.Value)
            {
                continue;
            }

            var startOffset = Math.Max(watermark.Low.Value, watermark.High.Value - normalizedLimit);
            assignments.Add(new TopicPartitionOffset(topicPartition, new Offset(startOffset)));
            highOffsets[topicPartition] = watermark.High.Value;
        }

        if (assignments.Count == 0)
        {
            return new KafkaTopicEventsResponse(
                Enabled: true,
                Topic: normalizedTopic,
                Role: managedTopic.Role,
                Limit: normalizedLimit,
                CheckedAtUtc: DateTimeOffset.UtcNow,
                Events: Array.Empty<KafkaEventInfo>(),
                Error: "");
        }

        consumer.Assign(assignments);
        var events = new List<KafkaEventInfo>();
        var completedPartitions = new HashSet<TopicPartition>();
        var deadline = DateTimeOffset.UtcNow.Add(ReadDeadline);
        while (completedPartitions.Count < assignments.Count && DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = consumer.Consume(ConsumeTimeout);
            if (result is null)
            {
                continue;
            }

            if (result.IsPartitionEOF)
            {
                completedPartitions.Add(result.TopicPartition);
                continue;
            }

            if (result.Message is null)
            {
                continue;
            }

            events.Add(ToEventInfo(result));
            if (highOffsets.TryGetValue(result.TopicPartition, out var highOffset)
                && result.Offset.Value + 1 >= highOffset)
            {
                completedPartitions.Add(result.TopicPartition);
            }
        }

        consumer.Close();
        var orderedEvents = events
            .OrderByDescending(item => item.TimestampUtc ?? DateTimeOffset.MinValue)
            .ThenByDescending(item => item.Partition)
            .ThenByDescending(item => item.Offset)
            .Take(normalizedLimit)
            .ToArray();

        logger.LogInformation(
            "Read {EventCount} recent Kafka events from managed topic {Topic}.",
            orderedEvents.Length,
            normalizedTopic);

        return new KafkaTopicEventsResponse(
            Enabled: true,
            Topic: normalizedTopic,
            Role: managedTopic.Role,
            Limit: normalizedLimit,
            CheckedAtUtc: DateTimeOffset.UtcNow,
            Events: orderedEvents,
            Error: "");
    }

    private IReadOnlyList<ManagedKafkaTopic> ManagedTopics()
    {
        var options = topicOptions.Value;
        var candidates = new[]
        {
            new ManagedKafkaTopic(options.CmdbWebhookEvents, "cmdb_webhook_events", "Raw CMDBuild webhook events"),
            new ManagedKafkaTopic(options.EffectiveAggregationCommands(), "aggregation_commands", "Canonical aggregation commands"),
            new ManagedKafkaTopic(options.ConfigBuildRequests, "config_build_requests", "Configuration build requests"),
            new ManagedKafkaTopic(options.ZabbixApplyPlans, "zabbix_apply_plans", "Zabbix apply plans"),
            new ManagedKafkaTopic(options.DebugLogs, "debug_logs", "Service debug and operational logs")
        };
        var result = new List<ManagedKafkaTopic>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate.Name) || !IsManagedTopic(candidate.Name) || !seen.Add(candidate.Name))
            {
                continue;
            }

            result.Add(candidate);
        }

        return result;
    }

    private bool IsManagedTopic(string topic)
    {
        var managedPrefix = topicOptions.Value.ManagedPrefix;
        return string.IsNullOrWhiteSpace(managedPrefix)
            || topic.StartsWith(managedPrefix, StringComparison.OrdinalIgnoreCase);
    }

    private static KafkaTopicInfo ToTopicInfo(ManagedKafkaTopic topic, TopicMetadata? metadata, bool? exists)
    {
        return new KafkaTopicInfo(
            Name: topic.Name,
            Role: topic.Role,
            Description: topic.Description,
            Exists: exists,
            PartitionCount: metadata?.Partitions.Count,
            Error: metadata is null || metadata.Error.Code == ErrorCode.NoError ? "" : metadata.Error.Reason);
    }

    private static KafkaEventInfo ToEventInfo(ConsumeResult<string, string> result)
    {
        return new KafkaEventInfo(
            Key: result.Message.Key ?? "",
            Value: result.Message.Value ?? "",
            Json: TryParseJson(result.Message.Value),
            Partition: result.Partition.Value,
            Offset: result.Offset.Value,
            TimestampUtc: result.Message.Timestamp.Type == TimestampType.NotAvailable
                ? null
                : result.Message.Timestamp.UtcDateTime,
            Headers: result.Message.Headers
                .Select(header => new KafkaHeaderInfo(
                    header.Key,
                    header.GetValueBytes() is { } bytes ? Convert.ToBase64String(bytes) : ""))
                .ToArray());
    }

    private static JsonElement? TryParseJson(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(value);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

public sealed record ManagedKafkaTopic(string Name, string Role, string Description);

public sealed record KafkaTopicsResponse(
    bool Enabled,
    string ManagedIdentifier,
    string ManagedPrefix,
    DateTimeOffset CheckedAtUtc,
    IReadOnlyList<KafkaTopicInfo> Topics,
    string Error);

public sealed record KafkaTopicInfo(
    string Name,
    string Role,
    string Description,
    bool? Exists,
    int? PartitionCount,
    string Error);

public sealed record KafkaTopicEventsResponse(
    bool Enabled,
    string Topic,
    string Role,
    int Limit,
    DateTimeOffset CheckedAtUtc,
    IReadOnlyList<KafkaEventInfo> Events,
    string Error);

public sealed record KafkaEventInfo(
    string Key,
    string Value,
    JsonElement? Json,
    int Partition,
    long Offset,
    DateTimeOffset? TimestampUtc,
    IReadOnlyList<KafkaHeaderInfo> Headers);

public sealed record KafkaHeaderInfo(string Key, string ValueBase64);
