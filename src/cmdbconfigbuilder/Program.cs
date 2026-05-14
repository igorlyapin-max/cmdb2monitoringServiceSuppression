using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
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
builder.Services.AddOptions<SemanticDeduplicationOptions>()
    .Bind(builder.Configuration.GetSection(SemanticDeduplicationOptions.SectionName))
    .Validate(options => options.HasValidWindow(), "SemanticDeduplication:WindowSeconds must be greater than zero.")
    .Validate(options => options.HasValidMaxEntries(), "SemanticDeduplication:MaxEntries must be greater than zero.")
    .ValidateOnStart();
builder.Services.AddOptions<ReadinessOptions>()
    .Bind(builder.Configuration.GetSection(ReadinessOptions.SectionName))
    .Validate(options => options.HasValidZabbixHostIdAttribute(), "Readiness:ZabbixHostIdAttribute is required.")
    .ValidateOnStart();
builder.Services.AddOptions<KafkaTopicsOptions>()
    .Bind(builder.Configuration.GetSection(KafkaTopicsOptions.SectionName))
    .Validate(options => !string.IsNullOrWhiteSpace(options.CmdbWebhookEvents), "CMDB raw event topic is required.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.EffectiveAggregationCommands()), "Aggregation command topic is required.")
    .ValidateOnStart();

builder.Services.AddSingleton<ConversionRulesValidator>();
builder.Services.AddSingleton<ConversionRulesFileLoader>();
builder.Services.AddSingleton(provider =>
{
    var options = provider.GetRequiredService<IOptions<ReadinessOptions>>().Value;
    return new AggregationRuleEngine(options.ZabbixHostIdAttribute);
});
builder.Services.AddSingleton<SemanticCommandDeduplicator>();
builder.Services.AddSingleton<SourceEventEnricher>();
builder.Services.AddSingleton<KafkaJsonProducer>();
builder.Services.AddSingleton<KafkaTopicExplorer>();
builder.Services.AddSingleton<ApplyCurrentRulesProgressStore>();
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
    SourceEventEnricher enricher,
    CancellationToken cancellationToken) =>
{
    var rules = await loader.LoadAsync(cancellationToken);
    var validation = validator.Validate(rules);
    if (!validation.IsValid)
    {
        return Results.BadRequest(validation);
    }

    var enrichedEvent = await enricher.EnrichAsync(rawEvent, rules, cancellationToken);
    var commands = engine.BuildCommands(enrichedEvent, rules);
    return Results.Ok(new
    {
        raw_event = enrichedEvent.EventId,
        command_count = commands.Count,
        commands
    });
});

app.MapPost("/rules/apply-current", async (
    ApplyCurrentRulesRequest request,
    ConversionRulesFileLoader loader,
    ConversionRulesValidator validator,
    AggregationRuleEngine engine,
    SourceEventEnricher enricher,
    KafkaJsonProducer producer,
    CmdbuildClient cmdbuild,
    IOptions<KafkaTopicsOptions> topicOptions,
    ApplyCurrentRulesProgressStore progress,
    CancellationToken cancellationToken) =>
{
    var operationId = progress.Start(request.OperationId, request.DryRun);
    try
    {
        progress.Stage(operationId, "loading_rules", "Загрузка правил конвертации.");
        var rules = await loader.LoadAsync(cancellationToken);
        var validation = validator.Validate(rules);
        if (!validation.IsValid)
        {
            progress.Fail(operationId, "validation_failed", "Правила конвертации не прошли проверку.");
            return Results.BadRequest(validation);
        }

        var selectedRules = SelectRulesForCurrentApply(rules, request);
        var selectedDocument = rules with { Rules = selectedRules };
        var sourceClasses = SourceClassesForCurrentApply(selectedRules, request);
        var publishTargets = ResolvePublishTargets(request.Targets);
        var publishTopics = PublishTopicsForRequest(topicOptions.Value, publishTargets, selectedRules.Select(rule => rule.Layer));
        progress.Configure(operationId, sourceClasses, selectedRules.Count, publishTopics);
        var result = new ApplyCurrentRulesResult
        {
            OperationId = operationId,
            DryRun = request.DryRun,
            Topic = string.Join(", ", publishTopics),
            Topics = publishTopics,
            SourceClassCount = sourceClasses.Count,
            RuleCount = selectedRules.Count
        };
        var operationDeduplicationKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var sourceClass in sourceClasses)
        {
            progress.BeginClass(operationId, sourceClass);
            var classResult = new ApplyCurrentRulesClassResult
            {
                SourceClass = sourceClass
            };

            try
            {
                progress.Stage(operationId, "loading_cards", $"Загрузка карточек класса {sourceClass}.");
                var catalog = await cmdbuild.ListClassCardsCatalogAsync(sourceClass, "source", cancellationToken);
                var cards = request.MaxCardsPerClass > 0
                    ? catalog.Cards.Take(request.MaxCardsPerClass).ToArray()
                    : catalog.Cards;
                classResult.Cards = cards.Count;
                progress.SetCurrentClassCards(operationId, sourceClass, cards.Count);

                foreach (var card in cards)
                {
                    progress.Stage(operationId, "processing_cards", $"Обработка {sourceClass}/{card.Id}.");
                    var rawEvent = BuildApplyCurrentRawEvent(sourceClass, card, request.EventType);
                    var enrichedEvent = await enricher.EnrichAsync(rawEvent, selectedDocument, cancellationToken);
                    var plans = engine.BuildCommandPlans(enrichedEvent, selectedDocument);
                    classResult.CommandsBuilt += plans.Count;
                    result.CommandsBuilt += plans.Count;
                    progress.AddCommandsBuilt(operationId, plans.Count);

                    foreach (var plan in plans)
                    {
                        result.ZabbixPlan.Add(plan.Command);
                        progress.AddPlannedCommand(operationId, plan.Command);
                        Increment(result.CommandsByLayer, plan.Command.Layer);
                        if (result.SampleCommands.Count < 20)
                        {
                            result.SampleCommands.Add(new ApplyCurrentRulesCommandSample
                            {
                                RuleId = plan.Command.RuleId,
                                Layer = plan.Command.Layer,
                                SourceClass = plan.Command.Source.ClassCode,
                                SourceCardId = plan.Command.Source.CardId,
                                SourceKeyAttribute = plan.Command.Source.KeyAttribute,
                                SourceKeyValue = plan.Command.Source.KeyValue,
                                SourceZabbixHostId = plan.Command.Source.ZabbixHostId,
                                TargetClass = plan.Command.Target.ClassCode,
                                TargetKey = plan.Command.Target.IdempotencyKey
                            });
                        }

                        if (request.DryRun)
                        {
                            continue;
                        }

                        if (ShouldSkipOperationDuplicate(plan, publishTargets, operationDeduplicationKeys))
                        {
                            classResult.CommandsSkippedAsDuplicates++;
                            result.CommandsSkippedAsDuplicates++;
                            progress.AddDuplicate(operationId);
                            continue;
                        }

                        var publishedTopics = await PublishAggregationPlanAsync(
                            producer,
                            topicOptions.Value,
                            plan,
                            publishTargets,
                            cancellationToken);
                        classResult.CommandsPublished++;
                        result.CommandsPublished++;
                        progress.AddPublished(operationId, publishedTopics);
                        foreach (var topic in publishedTopics)
                        {
                            Increment(result.CommandsPublishedByTopic, topic);
                        }
                    }

                    progress.CardProcessed(operationId, sourceClass, card.Id);
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
            {
                classResult.Error = ex.Message;
                result.Errors.Add($"{sourceClass}: {ex.Message}");
                progress.AddError(operationId, $"{sourceClass}: {ex.Message}");
            }

            result.Classes.Add(classResult);
            result.CardsScanned += classResult.Cards;
            progress.CompleteClass(operationId, classResult);
        }

        progress.Complete(operationId);
        return Results.Ok(result);
    }
    catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException)
    {
        progress.Fail(operationId, "failed", ex.Message);
        return Results.Problem(ex.Message, statusCode: StatusCodes.Status502BadGateway);
    }
});

app.MapGet("/rules/apply-current/progress/{operationId}", (
    string operationId,
    ApplyCurrentRulesProgressStore progress) =>
{
    var snapshot = progress.Get(operationId);
    return snapshot is null
        ? Results.NotFound(new { error = "not_found" })
        : Results.Ok(snapshot);
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

static IReadOnlyList<ConversionRule> SelectRulesForCurrentApply(
    ConversionRulesDocument document,
    ApplyCurrentRulesRequest request)
{
    var layers = new HashSet<string>(
        (request.Layers ?? [])
            .Select(item => item.Trim())
            .Where(item => !string.IsNullOrWhiteSpace(item)),
        StringComparer.OrdinalIgnoreCase);

    var sourceClasses = new HashSet<string>(
        (request.SourceClasses ?? [])
            .Select(item => item.Trim())
            .Where(item => !string.IsNullOrWhiteSpace(item)),
        StringComparer.OrdinalIgnoreCase);

    return document.Rules
        .Where(rule => rule.Enabled)
        .Where(rule => layers.Count == 0 || layers.Contains(rule.Layer))
        .Where(rule => sourceClasses.Count == 0 || sourceClasses.Contains(rule.Source.ClassCode))
        .Where(rule => !string.IsNullOrWhiteSpace(rule.Source.ClassCode))
        .ToArray();
}

static IReadOnlyList<string> SourceClassesForCurrentApply(
    IReadOnlyList<ConversionRule> rules,
    ApplyCurrentRulesRequest request)
{
    var requested = new HashSet<string>(
        (request.SourceClasses ?? [])
            .Select(item => item.Trim())
            .Where(item => !string.IsNullOrWhiteSpace(item)),
        StringComparer.OrdinalIgnoreCase);

    var result = rules
        .Select(rule => rule.Source.ClassCode.Trim())
        .Where(item => !string.IsNullOrWhiteSpace(item))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    return requested.Count == 0
        ? result
        : result.Where(requested.Contains).ToArray();
}

static CmdbRawEvent BuildApplyCurrentRawEvent(
    string sourceClass,
    CmdbuildClassCardCatalogItem card,
    string? eventType)
{
    var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["className"] = sourceClass,
        ["class_code"] = sourceClass,
        ["card_id"] = card.Id,
        ["id"] = card.Id,
        ["_id"] = card.Id,
        ["Description"] = card.Description
    };

    foreach (var attribute in card.Attributes)
    {
        if (!string.IsNullOrWhiteSpace(attribute.Value))
        {
            attributes[attribute.Code] = attribute.Value;
        }
    }

    return new CmdbRawEvent
    {
        EventId = $"apply-current-{sourceClass}-{card.Id}-{Guid.NewGuid():N}",
        Source = "manual-apply-current",
        EventType = string.IsNullOrWhiteSpace(eventType) ? "UPDATE" : eventType.Trim().ToUpperInvariant(),
        ClassCode = sourceClass,
        CardId = card.Id,
        OccurredAt = DateTimeOffset.UtcNow,
        Attributes = attributes,
        RawPayload = JsonSerializer.SerializeToElement(new
        {
            source = "manual-apply-current",
            class_code = sourceClass,
            card_id = card.Id
        })
    };
}

static void Increment(IDictionary<string, int> values, string key)
{
    var normalizedKey = string.IsNullOrWhiteSpace(key) ? "unknown" : key.Trim();
    values[normalizedKey] = values.TryGetValue(normalizedKey, out var current)
        ? current + 1
        : 1;
}

static PublishTargets ResolvePublishTargets(IReadOnlyList<string> targets)
{
    if (targets.Count == 0)
    {
        return PublishTargets.AggregationOnly;
    }

    var normalized = targets
        .Select(target => target.Trim().ToLowerInvariant())
        .Where(target => !string.IsNullOrWhiteSpace(target))
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    if (normalized.Contains("all"))
    {
        return PublishTargets.All;
    }

    return new PublishTargets(
        Aggregation: normalized.Contains("aggregation") || normalized.Contains("cmdbuild"),
        Zabbix: normalized.Contains("zabbix"));
}

static IReadOnlyList<string> PublishTopicsForRequest(
    KafkaTopicsOptions options,
    PublishTargets targets,
    IEnumerable<string> layers)
{
    var result = new List<string>();
    if (targets.Aggregation)
    {
        result.Add(options.EffectiveAggregationCommands());
    }

    if (targets.Zabbix)
    {
        foreach (var layer in layers)
        {
            var topic = options.EffectiveZabbixApplyPlans(layer);
            if (!string.IsNullOrWhiteSpace(topic))
            {
                result.Add(topic);
            }
        }
    }

    return result
        .Where(topic => !string.IsNullOrWhiteSpace(topic))
        .Distinct(StringComparer.Ordinal)
        .ToArray();
}

static async Task<IReadOnlyList<string>> PublishAggregationPlanAsync(
    KafkaJsonProducer producer,
    KafkaTopicsOptions options,
    AggregationCommandPlan plan,
    PublishTargets targets,
    CancellationToken cancellationToken)
{
    var topics = PublishTopicsForCommand(options, plan.Command, targets);
    if (topics.Count > 0 && !producer.Enabled)
    {
        throw new InvalidOperationException(
            $"Kafka producer is disabled; command for {plan.Command.Target.ClassCode}:{plan.Command.Target.IdempotencyKey} was not published. Enable Kafka__Enabled=true for apply-current publishing.");
    }

    var key = CommandKafkaKey(plan.Command);
    foreach (var topic in topics)
    {
        await producer.PublishAsync(topic, key, plan.Command, cancellationToken);
    }

    return topics;
}

static IReadOnlyList<string> PublishTopicsForCommand(
    KafkaTopicsOptions options,
    AggregationCommand command,
    PublishTargets targets)
{
    var result = new List<string>();
    if (targets.Aggregation)
    {
        result.Add(options.EffectiveAggregationCommands());
    }

    if (targets.Zabbix)
    {
        result.Add(options.EffectiveZabbixApplyPlans(command.Layer));
    }

    return result
        .Where(topic => !string.IsNullOrWhiteSpace(topic))
        .Distinct(StringComparer.Ordinal)
        .ToArray();
}

static string CommandKafkaKey(AggregationCommand command)
{
    if (string.Equals(command.CommandType, AggregationCommandTypes.RemoveSourceMembership, StringComparison.OrdinalIgnoreCase))
    {
        return $"{command.Layer}:{command.Source.ClassCode}:{command.Source.CardId}";
    }

    return command.Target.CardId.Length > 0
        ? command.Target.CardId
        : command.Target.IdempotencyKey;
}

static string OperationDeduplicationKey(AggregationCommandPlan plan)
{
    var command = plan.Command;
    var targetKey = string.IsNullOrWhiteSpace(command.Target.IdempotencyKey)
        ? command.Target.CardId
        : command.Target.IdempotencyKey;
    var relationKey = string.Join(
        "|",
        command.Target.Relations
            .Select(relation => $"{relation.DomainCode}:{relation.TargetClassCode}:{relation.TargetLookup}")
            .OrderBy(value => value, StringComparer.Ordinal));
    return string.Join(
        "\n",
        command.Layer,
        command.CommandType,
        command.RuleId,
        command.Source.ClassCode,
        command.Source.CardId,
        command.Source.KeyValue,
        command.Target.ClassCode,
        targetKey,
        relationKey);
}

static bool ShouldSkipOperationDuplicate(
    AggregationCommandPlan plan,
    PublishTargets targets,
    ISet<string> operationDeduplicationKeys)
{
    if (!targets.Zabbix || targets.Aggregation)
    {
        return false;
    }

    return !operationDeduplicationKeys.Add(OperationDeduplicationKey(plan));
}

app.Run();

public sealed record PublishTargets(bool Aggregation, bool Zabbix)
{
    public static PublishTargets All { get; } = new(Aggregation: true, Zabbix: true);

    public static PublishTargets AggregationOnly { get; } = new(Aggregation: true, Zabbix: false);
}

public sealed record ApplyCurrentRulesRequest
{
    public string OperationId { get; init; } = "";

    public IReadOnlyList<string> Layers { get; init; } = [];

    public IReadOnlyList<string> SourceClasses { get; init; } = [];

    public IReadOnlyList<string> Targets { get; init; } = [];

    public int MaxCardsPerClass { get; init; }

    public bool DryRun { get; init; }

    public string EventType { get; init; } = "UPDATE";
}

public sealed class ApplyCurrentRulesResult
{
    public string OperationId { get; init; } = "";

    public bool DryRun { get; init; }

    public string Topic { get; init; } = "";

    public IReadOnlyList<string> Topics { get; init; } = [];

    public int SourceClassCount { get; init; }

    public int RuleCount { get; init; }

    public int CardsScanned { get; set; }

    public int CommandsBuilt { get; set; }

    public int CommandsPublished { get; set; }

    public int CommandsSkippedAsDuplicates { get; set; }

    public Dictionary<string, int> CommandsByLayer { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, int> CommandsPublishedByTopic { get; } = new(StringComparer.Ordinal);

    public ApplyCurrentRulesZabbixPlanSummary ZabbixPlan { get; } = new();

    public List<ApplyCurrentRulesClassResult> Classes { get; } = [];

    public List<ApplyCurrentRulesCommandSample> SampleCommands { get; } = [];

    public List<string> Errors { get; } = [];
}

public sealed class ApplyCurrentRulesClassResult
{
    public string SourceClass { get; init; } = "";

    public int Cards { get; set; }

    public int CommandsBuilt { get; set; }

    public int CommandsPublished { get; set; }

    public int CommandsSkippedAsDuplicates { get; set; }

    public string Error { get; set; } = "";
}

public sealed class ApplyCurrentRulesCommandSample
{
    public string RuleId { get; init; } = "";

    public string Layer { get; init; } = "";

    public string SourceClass { get; init; } = "";

    public string SourceCardId { get; init; } = "";

    public string SourceKeyAttribute { get; init; } = "";

    public string SourceKeyValue { get; init; } = "";

    public string SourceZabbixHostId { get; init; } = "";

    public string TargetClass { get; init; } = "";

    public string TargetKey { get; init; } = "";
}

public sealed class ApplyCurrentRulesProgressStore
{
    private const int MaxErrors = 30;
    private readonly ConcurrentDictionary<string, ApplyCurrentRulesProgress> operations = new(StringComparer.OrdinalIgnoreCase);

    public string Start(string requestedOperationId, bool dryRun)
    {
        var operationId = NormalizeOperationId(requestedOperationId);
        var now = DateTimeOffset.UtcNow;
        var progress = new ApplyCurrentRulesProgress
        {
            OperationId = operationId,
            Status = "running",
            Stage = "starting",
            Message = "Операция применения поставлена в работу.",
            DryRun = dryRun,
            StartedAtUtc = now,
            UpdatedAtUtc = now
        };
        operations[operationId] = progress;
        TrimOldOperations();
        return operationId;
    }

    public ApplyCurrentRulesProgressSnapshot? Get(string operationId)
    {
        if (!operations.TryGetValue(operationId, out var progress))
        {
            return null;
        }

        lock (progress)
        {
            return progress.ToSnapshot();
        }
    }

    public void Configure(
        string operationId,
        IReadOnlyList<string> sourceClasses,
        int ruleCount,
        IReadOnlyList<string> topics)
    {
        Update(operationId, progress =>
        {
            progress.SourceClassCount = sourceClasses.Count;
            progress.RuleCount = ruleCount;
            progress.SourceClasses = sourceClasses.ToArray();
            progress.Topics = topics.ToArray();
            progress.Stage = "configured";
            progress.Message = $"Подготовлено классов-источников: {sourceClasses.Count}; правил: {ruleCount}.";
        });
    }

    public void Stage(string operationId, string stage, string message)
    {
        Update(operationId, progress =>
        {
            progress.Stage = stage;
            progress.Message = message;
        });
    }

    public void BeginClass(string operationId, string sourceClass)
    {
        Update(operationId, progress =>
        {
            progress.CurrentSourceClass = sourceClass;
            progress.CurrentClassCardsTotal = 0;
            progress.CurrentClassCardsProcessed = 0;
            progress.Stage = "loading_cards";
            progress.Message = $"Загрузка карточек класса {sourceClass}.";
        });
    }

    public void SetCurrentClassCards(string operationId, string sourceClass, int cardCount)
    {
        Update(operationId, progress =>
        {
            progress.CurrentSourceClass = sourceClass;
            progress.CurrentClassCardsTotal = cardCount;
            progress.CurrentClassCardsProcessed = 0;
            progress.CardsDiscovered += cardCount;
            progress.Stage = "processing_cards";
            progress.Message = $"Класс {sourceClass}: загружено карточек {cardCount}.";
        });
    }

    public void AddCommandsBuilt(string operationId, int count)
    {
        if (count <= 0)
        {
            return;
        }

        Update(operationId, progress => progress.CommandsBuilt += count);
    }

    public void AddPlannedCommand(string operationId, AggregationCommand command)
    {
        Update(operationId, progress => progress.ZabbixPlan.Add(command));
    }

    public void AddPublished(string operationId, IReadOnlyList<string> topics)
    {
        Update(operationId, progress =>
        {
            progress.CommandsPublished++;
            foreach (var topic in topics)
            {
                IncrementValue(progress.CommandsPublishedByTopic, topic);
            }
        });
    }

    public void AddDuplicate(string operationId)
    {
        Update(operationId, progress => progress.CommandsSkippedAsDuplicates++);
    }

    public void CardProcessed(string operationId, string sourceClass, string cardId)
    {
        Update(operationId, progress =>
        {
            progress.CurrentSourceClass = sourceClass;
            progress.CurrentSourceCardId = cardId;
            progress.CurrentClassCardsProcessed++;
            progress.CardsScanned++;
            progress.Stage = "processing_cards";
            progress.Message = $"Класс {sourceClass}: обработано карточек {progress.CurrentClassCardsProcessed} из {progress.CurrentClassCardsTotal}.";
        });
    }

    public void AddError(string operationId, string error)
    {
        Update(operationId, progress =>
        {
            progress.Errors.Insert(0, error);
            if (progress.Errors.Count > MaxErrors)
            {
                progress.Errors.RemoveRange(MaxErrors, progress.Errors.Count - MaxErrors);
            }
        });
    }

    public void CompleteClass(string operationId, ApplyCurrentRulesClassResult classResult)
    {
        Update(operationId, progress =>
        {
            progress.SourceClassesCompleted++;
            progress.CompletedClasses.Add(new ApplyCurrentRulesClassProgress
            {
                SourceClass = classResult.SourceClass,
                Cards = classResult.Cards,
                CommandsBuilt = classResult.CommandsBuilt,
                CommandsPublished = classResult.CommandsPublished,
                CommandsSkippedAsDuplicates = classResult.CommandsSkippedAsDuplicates,
                Error = classResult.Error
            });
            progress.CurrentClassCardsProcessed = progress.CurrentClassCardsTotal;
            progress.Message = $"Класс {classResult.SourceClass} завершен: карточек {classResult.Cards}, команд {classResult.CommandsBuilt}.";
        });
    }

    public void Complete(string operationId)
    {
        Update(operationId, progress =>
        {
            progress.Status = "completed";
            progress.Stage = "completed";
            progress.CurrentSourceClass = "";
            progress.CurrentSourceCardId = "";
            progress.FinishedAtUtc = DateTimeOffset.UtcNow;
            progress.Message = "Операция применения завершена.";
        });
    }

    public void Fail(string operationId, string stage, string error)
    {
        Update(operationId, progress =>
        {
            progress.Status = "error";
            progress.Stage = stage;
            progress.Message = error;
            progress.FinishedAtUtc = DateTimeOffset.UtcNow;
            progress.Errors.Insert(0, error);
        });
    }

    private void Update(string operationId, Action<ApplyCurrentRulesProgress> update)
    {
        if (!operations.TryGetValue(operationId, out var progress))
        {
            return;
        }

        lock (progress)
        {
            update(progress);
            progress.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }
    }

    private void TrimOldOperations()
    {
        const int maxOperations = 50;
        if (operations.Count <= maxOperations)
        {
            return;
        }

        foreach (var item in operations
            .OrderBy(pair => pair.Value.UpdatedAtUtc)
            .Take(Math.Max(0, operations.Count - maxOperations)))
        {
            operations.TryRemove(item.Key, out _);
        }
    }

    private static string NormalizeOperationId(string operationId)
    {
        var value = (operationId ?? "").Trim();
        return !string.IsNullOrWhiteSpace(value) && value.Length <= 120
            ? value
            : Guid.NewGuid().ToString("N");
    }

    private static void IncrementValue(IDictionary<string, int> values, string key)
    {
        values[key] = values.TryGetValue(key, out var current)
            ? current + 1
            : 1;
    }
}

public sealed class ApplyCurrentRulesProgress
{
    public string OperationId { get; init; } = "";

    public string Status { get; set; } = "";

    public string Stage { get; set; } = "";

    public string Message { get; set; } = "";

    public bool DryRun { get; init; }

    public DateTimeOffset StartedAtUtc { get; init; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public DateTimeOffset? FinishedAtUtc { get; set; }

    public IReadOnlyList<string> Topics { get; set; } = [];

    public IReadOnlyList<string> SourceClasses { get; set; } = [];

    public int SourceClassCount { get; set; }

    public int SourceClassesCompleted { get; set; }

    public int RuleCount { get; set; }

    public int CardsDiscovered { get; set; }

    public int CardsScanned { get; set; }

    public int CommandsBuilt { get; set; }

    public int CommandsPublished { get; set; }

    public int CommandsSkippedAsDuplicates { get; set; }

    public string CurrentSourceClass { get; set; } = "";

    public string CurrentSourceCardId { get; set; } = "";

    public int CurrentClassCardsTotal { get; set; }

    public int CurrentClassCardsProcessed { get; set; }

    public Dictionary<string, int> CommandsPublishedByTopic { get; } = new(StringComparer.Ordinal);

    public ApplyCurrentRulesZabbixPlanSummary ZabbixPlan { get; } = new();

    public List<ApplyCurrentRulesClassProgress> CompletedClasses { get; } = [];

    public List<string> Errors { get; } = [];

    public ApplyCurrentRulesProgressSnapshot ToSnapshot()
    {
        var remainingClasses = Math.Max(0, SourceClassCount - SourceClassesCompleted);
        if (!string.IsNullOrWhiteSpace(CurrentSourceClass)
            && CurrentClassCardsProcessed < CurrentClassCardsTotal
            && remainingClasses > 0)
        {
            remainingClasses--;
        }

        return new ApplyCurrentRulesProgressSnapshot
        {
            OperationId = OperationId,
            Status = Status,
            Stage = Stage,
            Message = Message,
            DryRun = DryRun,
            StartedAtUtc = StartedAtUtc,
            UpdatedAtUtc = UpdatedAtUtc,
            FinishedAtUtc = FinishedAtUtc,
            Topics = Topics.ToArray(),
            SourceClasses = SourceClasses.ToArray(),
            SourceClassCount = SourceClassCount,
            SourceClassesCompleted = SourceClassesCompleted,
            SourceClassesRemaining = remainingClasses,
            RuleCount = RuleCount,
            CardsDiscovered = CardsDiscovered,
            CardsScanned = CardsScanned,
            CommandsBuilt = CommandsBuilt,
            CommandsPublished = CommandsPublished,
            CommandsSkippedAsDuplicates = CommandsSkippedAsDuplicates,
            CurrentSourceClass = CurrentSourceClass,
            CurrentSourceCardId = CurrentSourceCardId,
            CurrentClassCardsTotal = CurrentClassCardsTotal,
            CurrentClassCardsProcessed = CurrentClassCardsProcessed,
            CurrentClassCardsRemaining = Math.Max(0, CurrentClassCardsTotal - CurrentClassCardsProcessed),
            CommandsPublishedByTopic = new Dictionary<string, int>(CommandsPublishedByTopic, StringComparer.Ordinal),
            ZabbixPlan = ZabbixPlan.ToSnapshot(),
            CompletedClasses = CompletedClasses.ToArray(),
            Errors = Errors.ToArray()
        };
    }
}

public sealed class ApplyCurrentRulesProgressSnapshot
{
    public string OperationId { get; init; } = "";

    public string Status { get; init; } = "";

    public string Stage { get; init; } = "";

    public string Message { get; init; } = "";

    public bool DryRun { get; init; }

    public DateTimeOffset StartedAtUtc { get; init; }

    public DateTimeOffset UpdatedAtUtc { get; init; }

    public DateTimeOffset? FinishedAtUtc { get; init; }

    public IReadOnlyList<string> Topics { get; init; } = [];

    public IReadOnlyList<string> SourceClasses { get; init; } = [];

    public int SourceClassCount { get; init; }

    public int SourceClassesCompleted { get; init; }

    public int SourceClassesRemaining { get; init; }

    public int RuleCount { get; init; }

    public int CardsDiscovered { get; init; }

    public int CardsScanned { get; init; }

    public int CommandsBuilt { get; init; }

    public int CommandsPublished { get; init; }

    public int CommandsSkippedAsDuplicates { get; init; }

    public string CurrentSourceClass { get; init; } = "";

    public string CurrentSourceCardId { get; init; } = "";

    public int CurrentClassCardsTotal { get; init; }

    public int CurrentClassCardsProcessed { get; init; }

    public int CurrentClassCardsRemaining { get; init; }

    public Dictionary<string, int> CommandsPublishedByTopic { get; init; } = new(StringComparer.Ordinal);

    public ApplyCurrentRulesZabbixPlanSnapshot ZabbixPlan { get; init; } = new();

    public IReadOnlyList<ApplyCurrentRulesClassProgress> CompletedClasses { get; init; } = [];

    public IReadOnlyList<string> Errors { get; init; } = [];
}

public sealed class ApplyCurrentRulesClassProgress
{
    public string SourceClass { get; init; } = "";

    public int Cards { get; init; }

    public int CommandsBuilt { get; init; }

    public int CommandsPublished { get; init; }

    public int CommandsSkippedAsDuplicates { get; init; }

    public string Error { get; init; } = "";
}

public sealed class ApplyCurrentRulesZabbixPlanSummary
{
    private const int MaxObjectSamples = 1000;
    private const int MaxValuesPerObject = 8;
    private readonly HashSet<string> objectKeys = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ApplyCurrentRulesZabbixObjectPlan> objects = new(StringComparer.Ordinal);

    public int ObjectCount { get; private set; }

    public int RelationCount { get; private set; }

    public int ObjectSamplesLimit => MaxObjectSamples;

    public bool HasMoreObjects => ObjectCount > Objects.Count;

    public IReadOnlyList<ApplyCurrentRulesZabbixObjectPlan> Objects => objects.Values
        .OrderBy(item => item.TargetClass, StringComparer.OrdinalIgnoreCase)
        .ThenBy(item => item.TargetName, StringComparer.OrdinalIgnoreCase)
        .ThenBy(item => item.TargetKey, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public void Add(AggregationCommand command)
    {
        var targetKey = TargetObjectKey(command);
        var objectKey = $"{command.CommandType}:{command.Target.ClassCode}:{targetKey}";
        if (objectKeys.Add(objectKey))
        {
            ObjectCount++;
        }

        RelationCount += command.Target.Relations.Count;

        if (!objects.TryGetValue(objectKey, out var plannedObject))
        {
            if (objects.Count >= MaxObjectSamples)
            {
                return;
            }

            plannedObject = new ApplyCurrentRulesZabbixObjectPlan
            {
                Action = command.CommandType,
                ActionLabel = ActionLabel(command),
                Layer = command.Layer,
                TargetClass = command.Target.ClassCode,
                TargetKey = targetKey,
                TargetCardId = command.Target.CardId,
                TargetName = TargetObjectName(command),
                CreateInstance = command.Target.CreateInstance,
                Attributes = AttributeSamples(command.Target.Attributes)
            };
            objects[objectKey] = plannedObject;
        }

        plannedObject.CommandCount++;
        plannedObject.RelationCount += command.Target.Relations.Count;
        plannedObject.SourceCount++;
        if (string.IsNullOrWhiteSpace(command.Source.ZabbixHostId))
        {
            plannedObject.MissingHostBindingCount++;
        }
        else
        {
            plannedObject.HostBindingCount++;
            plannedObject.ProblemTagCount += 1;
        }

        AddLimited(plannedObject.RuleIds, command.RuleId, MaxValuesPerObject);
        AddLimited(plannedObject.RuleNames, command.RuleName, MaxValuesPerObject);
        AddLimited(plannedObject.SourceObjects, SourceObjectLabel(command.Source), MaxValuesPerObject);
        AddSourceBinding(plannedObject, command.Source);
        foreach (var relation in command.Target.Relations)
        {
            if (plannedObject.Relations.Count >= MaxValuesPerObject)
            {
                break;
            }

            plannedObject.Relations.Add(new ApplyCurrentRulesZabbixRelationPlan
            {
                DomainCode = relation.DomainCode,
                TargetClassCode = relation.TargetClassCode,
                TargetLookup = relation.TargetLookup
            });
        }
    }

    public ApplyCurrentRulesZabbixPlanSnapshot ToSnapshot()
    {
        return new ApplyCurrentRulesZabbixPlanSnapshot
        {
            ObjectCount = ObjectCount,
            RelationCount = RelationCount,
            ObjectSamplesLimit = ObjectSamplesLimit,
            HasMoreObjects = HasMoreObjects,
            Objects = Objects
        };
    }

    private static string TargetObjectKey(AggregationCommand command)
    {
        if (string.Equals(command.CommandType, AggregationCommandTypes.RemoveSourceMembership, StringComparison.OrdinalIgnoreCase))
        {
            return $"{command.Layer}:{command.Source.ClassCode}:{command.Source.CardId}";
        }

        if (!string.IsNullOrWhiteSpace(command.Target.CardId))
        {
            return command.Target.CardId;
        }

        return command.Target.IdempotencyKey;
    }

    private static string TargetObjectName(AggregationCommand command)
    {
        if (string.Equals(command.CommandType, AggregationCommandTypes.RemoveSourceMembership, StringComparison.OrdinalIgnoreCase))
        {
            return $"Очистить membership {command.Source.ClassCode}/{command.Source.CardId} ({command.Layer})";
        }

        if (!string.IsNullOrWhiteSpace(command.Target.CardDescription))
        {
            return command.Target.CardDescription;
        }

        foreach (var key in new[] { "Description", "description", "Name", "name", "Code", "code" })
        {
            if (command.Target.Attributes.TryGetValue(key, out var value) && value is not null)
            {
                var text = Convert.ToString(value, CultureInfo.InvariantCulture);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }
        }

        return TargetObjectKey(command);
    }

    private static string ActionLabel(AggregationCommand command)
    {
        if (string.Equals(command.CommandType, AggregationCommandTypes.RemoveSourceMembership, StringComparison.OrdinalIgnoreCase))
        {
            return "удалить source membership";
        }

        if (string.Equals(command.CommandType, AggregationCommandTypes.RemoveMembership, StringComparison.OrdinalIgnoreCase))
        {
            return "удалить связь";
        }

        return command.Target.CreateInstance
            ? "создать при отсутствии / обновить"
            : "обновить / связать существующий";
    }

    private static Dictionary<string, string> AttributeSamples(IReadOnlyDictionary<string, object?> attributes)
    {
        return attributes
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Take(MaxValuesPerObject)
            .ToDictionary(
                pair => pair.Key,
                pair => Convert.ToString(pair.Value, CultureInfo.InvariantCulture) ?? "",
                StringComparer.Ordinal);
    }

    private static string SourceObjectLabel(AggregationSourceObject source)
    {
        var identity = string.IsNullOrWhiteSpace(source.CardId)
            ? source.ClassCode
            : $"{source.ClassCode}/{source.CardId}";
        if (string.IsNullOrWhiteSpace(source.KeyValue)
            || source.KeyValue.Equals(source.CardId, StringComparison.OrdinalIgnoreCase))
        {
            return identity;
        }

        var keyAttribute = string.IsNullOrWhiteSpace(source.KeyAttribute)
            ? "key"
            : source.KeyAttribute;
        return $"{identity} {keyAttribute}={source.KeyValue}";
    }

    private static void AddSourceBinding(
        ApplyCurrentRulesZabbixObjectPlan plannedObject,
        AggregationSourceObject source)
    {
        if (plannedObject.SourceBindings.Count >= MaxValuesPerObject)
        {
            return;
        }

        var label = SourceObjectLabel(source);
        if (plannedObject.SourceBindings.Any(item => item.Label.Equals(label, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        plannedObject.SourceBindings.Add(new ApplyCurrentRulesZabbixSourceBindingPlan
        {
            Label = label,
            SourceClass = source.ClassCode,
            SourceCardId = source.CardId,
            SourceKeyAttribute = source.KeyAttribute,
            SourceKeyValue = source.KeyValue,
            ZabbixHostId = source.ZabbixHostId,
            SourceLeafManagedKey = ZabbixManagedServiceMapper.SourceLeafManagedKey(plannedObject.Layer, source),
            ProblemTags = ZabbixManagedServiceMapper.ProblemTagsForSource(source)
                .Select(tag => $"{tag.Tag}={tag.Value}")
                .ToArray()
        });
    }

    private static void AddLimited(List<string> values, string value, int limit)
    {
        if (string.IsNullOrWhiteSpace(value)
            || values.Contains(value, StringComparer.OrdinalIgnoreCase)
            || values.Count >= limit)
        {
            return;
        }

        values.Add(value);
    }
}

public sealed class ApplyCurrentRulesZabbixPlanSnapshot
{
    public int ObjectCount { get; init; }

    public int RelationCount { get; init; }

    public int ObjectSamplesLimit { get; init; }

    public bool HasMoreObjects { get; init; }

    public IReadOnlyList<ApplyCurrentRulesZabbixObjectPlan> Objects { get; init; } = [];
}

public sealed class ApplyCurrentRulesZabbixObjectPlan
{
    public string Action { get; init; } = "";

    public string ActionLabel { get; init; } = "";

    public string Layer { get; init; } = "";

    public string TargetClass { get; init; } = "";

    public string TargetKey { get; init; } = "";

    public string TargetCardId { get; init; } = "";

    public string TargetName { get; init; } = "";

    public bool CreateInstance { get; init; }

    public int CommandCount { get; set; }

    public int RelationCount { get; set; }

    public int SourceCount { get; set; }

    public int HostBindingCount { get; set; }

    public int MissingHostBindingCount { get; set; }

    public int ProblemTagCount { get; set; }

    public Dictionary<string, string> Attributes { get; init; } = new(StringComparer.Ordinal);

    public List<string> RuleIds { get; } = [];

    public List<string> RuleNames { get; } = [];

    public List<string> SourceObjects { get; } = [];

    public List<ApplyCurrentRulesZabbixSourceBindingPlan> SourceBindings { get; } = [];

    public List<ApplyCurrentRulesZabbixRelationPlan> Relations { get; } = [];
}

public sealed class ApplyCurrentRulesZabbixSourceBindingPlan
{
    public string Label { get; init; } = "";

    public string SourceClass { get; init; } = "";

    public string SourceCardId { get; init; } = "";

    public string SourceKeyAttribute { get; init; } = "";

    public string SourceKeyValue { get; init; } = "";

    public string ZabbixHostId { get; init; } = "";

    public string SourceLeafManagedKey { get; init; } = "";

    public IReadOnlyList<string> ProblemTags { get; init; } = [];
}

public sealed class ApplyCurrentRulesZabbixRelationPlan
{
    public string DomainCode { get; init; } = "";

    public string TargetClassCode { get; init; } = "";

    public string TargetLookup { get; init; } = "";
}

public sealed class SourceEventEnricher(CmdbuildClient cmdbuild, ILogger<SourceEventEnricher> logger)
{
    private readonly string zabbixHostIdAttribute = "zabbix_main_hostid";

    public SourceEventEnricher(
        CmdbuildClient cmdbuild,
        IOptions<ReadinessOptions> readinessOptions,
        ILogger<SourceEventEnricher> logger)
        : this(cmdbuild, logger)
    {
        zabbixHostIdAttribute = string.IsNullOrWhiteSpace(readinessOptions.Value.ZabbixHostIdAttribute)
            ? "zabbix_main_hostid"
            : readinessOptions.Value.ZabbixHostIdAttribute.Trim();
    }

    public async Task<CmdbRawEvent> EnrichAsync(
        CmdbRawEvent message,
        ConversionRulesDocument rules,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(message.ClassCode)
            || string.IsNullOrWhiteSpace(message.CardId))
        {
            return message;
        }

        var attributes = new Dictionary<string, string>(message.Attributes, StringComparer.OrdinalIgnoreCase);
        var resolvedCount = 0;
        if (await SourceHostIdEnrichment.TryResolveAsync(
                message,
                attributes,
                zabbixHostIdAttribute,
                cmdbuild,
                logger,
                cancellationToken))
        {
            resolvedCount++;
        }

        if (rules.Source.Fields.Count == 0)
        {
            return resolvedCount == 0
                ? message
                : message with { Attributes = attributes };
        }

        var referencedFields = ReferencedFieldsForClass(rules, message.ClassCode);
        if (referencedFields.Count == 0)
        {
            return resolvedCount == 0
                ? message
                : message with { Attributes = attributes };
        }

        foreach (var (fieldName, definition) in rules.Source.Fields)
        {
            if (!referencedFields.Contains(fieldName)
                || string.IsNullOrWhiteSpace(definition.CmdbPath)
                || (attributes.TryGetValue(fieldName, out var existing) && !string.IsNullOrWhiteSpace(existing)))
            {
                continue;
            }

            var mode = definition.Resolve.Mode;
            if (!string.IsNullOrWhiteSpace(mode)
                && !mode.Equals("cmdbPath", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                var value = await cmdbuild.ResolveCardPathValueAsync(
                    message.ClassCode,
                    message.CardId,
                    definition.CmdbPath,
                    cancellationToken);
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                attributes[fieldName] = value;
                resolvedCount++;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
            {
                logger.LogWarning(
                    ex,
                    "Failed to resolve CMDBuild source field {FieldName} for {ClassCode}/{CardId} by path {CmdbPath}",
                    fieldName,
                    message.ClassCode,
                    message.CardId,
                    definition.CmdbPath);
            }
        }

        return resolvedCount == 0
            ? message
            : message with { Attributes = attributes };
    }

    private static HashSet<string> ReferencedFieldsForClass(
        ConversionRulesDocument rules,
        string classCode)
    {
        var fields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in rules.Rules)
        {
            if (!rule.Enabled
                || (!string.IsNullOrWhiteSpace(rule.Source.ClassCode)
                    && !rule.Source.ClassCode.Equals(classCode, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            AddField(fields, rule.Source.KeyAttribute);
            AddField(fields, rule.When.FieldExists);
            foreach (var matcher in rule.When.AllRegex.Concat(rule.When.AnyRegex).Concat(rule.When.NoneRegex))
            {
                AddField(fields, matcher.Field);
            }

            foreach (var condition in rule.Source.Conditions.Concat(rule.Source.Filters))
            {
                AddField(fields, condition.Attribute);
            }

            AddSourceTemplateFields(fields, rule.Target.IdempotencyKey);
            AddSourceTemplateFields(fields, rule.Target.CardId);
            AddSourceTemplateFields(fields, rule.Target.CardDescription);
            foreach (var value in rule.Target.AttributeMappings.Values.Concat(rule.Target.InitialUserValues.Values))
            {
                AddSourceTemplateFields(fields, value);
            }

            foreach (var relation in rule.Relations)
            {
                AddSourceTemplateFields(fields, relation.TargetLookup);
                foreach (var value in relation.AttributeMappings.Values)
                {
                    AddSourceTemplateFields(fields, value);
                }
            }
        }

        return fields;
    }

    private static void AddField(ISet<string> fields, string? field)
    {
        if (!string.IsNullOrWhiteSpace(field))
        {
            fields.Add(field.Trim());
        }
    }

    private static void AddSourceTemplateFields(ISet<string> fields, string? template)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            return;
        }

        foreach (Match match in Regex.Matches(template, "\\$\\{\\s*source\\.([A-Za-z_][A-Za-z0-9_]*)\\s*\\}", RegexOptions.IgnoreCase))
        {
            fields.Add(match.Groups[1].Value);
        }
    }
}

public sealed class RuleEngineWorker(
    IOptions<KafkaOptions> kafkaOptions,
    IOptions<KafkaTopicsOptions> topicOptions,
    IOptions<ReadinessOptions> readinessOptions,
    IOptions<DebugOptions> debugOptions,
    ConversionRulesFileLoader loader,
    ConversionRulesValidator validator,
    AggregationRuleEngine engine,
    SemanticCommandDeduplicator deduplicator,
    KafkaJsonProducer producer,
    CmdbuildClient cmdbuild,
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

        var enrichedMessage = await EnrichSourceFieldsAsync(message, rules, cancellationToken);
        var plans = engine.BuildCommandPlans(enrichedMessage, rules);
        logger.LogDebugBasic(
            debugOptions,
            "rule engine processed event={EventId}, class={ClassCode}, card={CardId}, commands={CommandCount}",
            enrichedMessage.EventId,
            enrichedMessage.ClassCode,
            enrichedMessage.CardId,
            plans.Count);

        foreach (var plan in plans)
        {
            if (deduplicator.IsDuplicate(plan, out var duplicateAge))
            {
                logger.LogDebugBasic(
                    debugOptions,
                    "rule engine suppressed duplicate semantic command: event={EventId}, command={CommandId}, rule={RuleId}, ageSeconds={AgeSeconds}",
                    message.EventId,
                    plan.Command.CommandId,
                    plan.Command.RuleId,
                    duplicateAge.HasValue
                        ? duplicateAge.Value.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture)
                        : "");
                continue;
            }

            await PublishAggregationPlanAsync(
                producer,
                topicOptions.Value,
                plan,
                PublishTargets.All,
                cancellationToken);
            deduplicator.MarkPublished(plan);
        }
    }

    private static async Task PublishAggregationPlanAsync(
        KafkaJsonProducer producer,
        KafkaTopicsOptions options,
        AggregationCommandPlan plan,
        PublishTargets targets,
        CancellationToken cancellationToken)
    {
        var topics = PublishTopicsForCommand(options, plan.Command, targets);
        var key = string.Equals(plan.Command.CommandType, AggregationCommandTypes.RemoveSourceMembership, StringComparison.OrdinalIgnoreCase)
            ? $"{plan.Command.Layer}:{plan.Command.Source.ClassCode}:{plan.Command.Source.CardId}"
            : plan.Command.Target.CardId.Length > 0
            ? plan.Command.Target.CardId
            : plan.Command.Target.IdempotencyKey;
        foreach (var topic in topics)
        {
            await producer.PublishAsync(topic, key, plan.Command, cancellationToken);
        }
    }

    private static IReadOnlyList<string> PublishTopicsForCommand(
        KafkaTopicsOptions options,
        AggregationCommand command,
        PublishTargets targets)
    {
        var result = new List<string>();
        if (targets.Aggregation)
        {
            result.Add(options.EffectiveAggregationCommands());
        }

        if (targets.Zabbix)
        {
            result.Add(options.EffectiveZabbixApplyPlans(command.Layer));
        }

        return result
            .Where(topic => !string.IsNullOrWhiteSpace(topic))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private async Task<CmdbRawEvent> EnrichSourceFieldsAsync(
        CmdbRawEvent message,
        ConversionRulesDocument rules,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(message.ClassCode)
            || string.IsNullOrWhiteSpace(message.CardId))
        {
            return message;
        }

        var attributes = new Dictionary<string, string>(message.Attributes, StringComparer.OrdinalIgnoreCase);
        var resolvedCount = 0;
        if (await SourceHostIdEnrichment.TryResolveAsync(
                message,
                attributes,
                readinessOptions.Value.ZabbixHostIdAttribute,
                cmdbuild,
                logger,
                cancellationToken))
        {
            resolvedCount++;
        }

        if (rules.Source.Fields.Count == 0)
        {
            return resolvedCount == 0
                ? message
                : message with { Attributes = attributes };
        }

        var referencedFields = ReferencedFieldsForClass(rules, message.ClassCode);
        if (referencedFields.Count == 0)
        {
            return resolvedCount == 0
                ? message
                : message with { Attributes = attributes };
        }

        foreach (var (fieldName, definition) in rules.Source.Fields)
        {
            if (!referencedFields.Contains(fieldName)
                || string.IsNullOrWhiteSpace(definition.CmdbPath)
                || (attributes.TryGetValue(fieldName, out var existing) && !string.IsNullOrWhiteSpace(existing)))
            {
                continue;
            }

            var mode = definition.Resolve.Mode;
            if (!string.IsNullOrWhiteSpace(mode)
                && !mode.Equals("cmdbPath", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                var value = await cmdbuild.ResolveCardPathValueAsync(
                    message.ClassCode,
                    message.CardId,
                    definition.CmdbPath,
                    cancellationToken);
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                attributes[fieldName] = value;
                resolvedCount++;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
            {
                logger.LogWarning(
                    ex,
                    "Failed to resolve CMDBuild source field {FieldName} for {ClassCode}/{CardId} by path {CmdbPath}",
                    fieldName,
                    message.ClassCode,
                    message.CardId,
                    definition.CmdbPath);
            }
        }

        if (resolvedCount == 0)
        {
            return message;
        }

        logger.LogDebugBasic(
            debugOptions,
            "resolved {ResolvedCount} CMDBuild source fields for event={EventId}, class={ClassCode}, card={CardId}",
            resolvedCount,
            message.EventId,
            message.ClassCode,
            message.CardId);

        return message with { Attributes = attributes };
    }

    private static HashSet<string> ReferencedFieldsForClass(
        ConversionRulesDocument rules,
        string classCode)
    {
        var fields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in rules.Rules)
        {
            if (!rule.Enabled
                || (!string.IsNullOrWhiteSpace(rule.Source.ClassCode)
                    && !rule.Source.ClassCode.Equals(classCode, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            AddField(fields, rule.Source.KeyAttribute);
            AddField(fields, rule.When.FieldExists);
            foreach (var matcher in rule.When.AllRegex.Concat(rule.When.AnyRegex).Concat(rule.When.NoneRegex))
            {
                AddField(fields, matcher.Field);
            }

            foreach (var condition in rule.Source.Conditions.Concat(rule.Source.Filters))
            {
                AddField(fields, condition.Attribute);
            }

            AddSourceTemplateFields(fields, rule.Target.IdempotencyKey);
            AddSourceTemplateFields(fields, rule.Target.CardId);
            AddSourceTemplateFields(fields, rule.Target.CardDescription);
            foreach (var value in rule.Target.AttributeMappings.Values.Concat(rule.Target.InitialUserValues.Values))
            {
                AddSourceTemplateFields(fields, value);
            }

            foreach (var relation in rule.Relations)
            {
                AddSourceTemplateFields(fields, relation.TargetLookup);
                foreach (var value in relation.AttributeMappings.Values)
                {
                    AddSourceTemplateFields(fields, value);
                }
            }
        }

        return fields;
    }

    private static void AddField(ISet<string> fields, string? field)
    {
        if (!string.IsNullOrWhiteSpace(field))
        {
            fields.Add(field.Trim());
        }
    }

    private static void AddSourceTemplateFields(ISet<string> fields, string? template)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            return;
        }

        foreach (Match match in Regex.Matches(template, "\\$\\{\\s*source\\.([A-Za-z_][A-Za-z0-9_]*)\\s*\\}", RegexOptions.IgnoreCase))
        {
            fields.Add(match.Groups[1].Value);
        }
    }
}

public static class SourceHostIdEnrichment
{
    public static async Task<bool> TryResolveAsync(
        CmdbRawEvent message,
        IDictionary<string, string> attributes,
        string configuredAttribute,
        CmdbuildClient cmdbuild,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var attribute = string.IsNullOrWhiteSpace(configuredAttribute)
            ? "zabbix_main_hostid"
            : configuredAttribute.Trim();
        if (attributes.TryGetValue(attribute, out var existing) && !string.IsNullOrWhiteSpace(existing))
        {
            return false;
        }

        try
        {
            var value = await cmdbuild.ResolveCardPathValueAsync(
                message.ClassCode,
                message.CardId,
                $"{message.ClassCode}.{attribute}",
                cancellationToken);
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            attributes[attribute] = value;
            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            logger.LogWarning(
                ex,
                "Failed to resolve CMDBuild source host id {AttributeName} for {ClassCode}/{CardId}.",
                attribute,
                message.ClassCode,
                message.CardId);
            return false;
        }
    }
}

public sealed class SemanticCommandDeduplicator(IOptionsMonitor<SemanticDeduplicationOptions> options)
{
    private readonly ConcurrentDictionary<string, SemanticDeduplicationEntry> entries = new();
    private long checkCounter;

    public bool IsDuplicate(AggregationCommandPlan plan, out TimeSpan? duplicateAge)
    {
        duplicateAge = null;
        var currentOptions = options.CurrentValue;
        if (!currentOptions.Enabled)
        {
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        var window = TimeSpan.FromSeconds(currentOptions.WindowSeconds);
        var key = plan.SemanticKey;
        if (!entries.TryGetValue(key, out var existing))
        {
            PruneIfNeeded(now, window, currentOptions.MaxEntries);
            return false;
        }

        if (existing.Fingerprint.Equals(plan.SemanticFingerprint, StringComparison.Ordinal)
            && now - existing.LastSeenAtUtc <= window)
        {
            duplicateAge = now - existing.LastPublishedAtUtc;
            entries.TryUpdate(
                key,
                existing with { LastSeenAtUtc = now },
                existing);
            PruneIfNeeded(now, window, currentOptions.MaxEntries);
            return true;
        }

        PruneIfNeeded(now, window, currentOptions.MaxEntries);
        return false;
    }

    public void MarkPublished(AggregationCommandPlan plan)
    {
        var currentOptions = options.CurrentValue;
        if (!currentOptions.Enabled)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        entries.AddOrUpdate(
            plan.SemanticKey,
            new SemanticDeduplicationEntry(plan.SemanticFingerprint, now),
            (_, _) => new SemanticDeduplicationEntry(plan.SemanticFingerprint, now));
        PruneIfNeeded(now, TimeSpan.FromSeconds(currentOptions.WindowSeconds), currentOptions.MaxEntries);
    }

    private void PruneIfNeeded(DateTimeOffset now, TimeSpan window, int maxEntries)
    {
        var counter = Interlocked.Increment(ref checkCounter);
        if (counter % 128 != 0 && entries.Count <= maxEntries)
        {
            return;
        }

        foreach (var (key, value) in entries)
        {
            if (now - value.LastSeenAtUtc > window || entries.Count > maxEntries)
            {
                entries.TryRemove(key, out _);
            }
        }
    }
}

public sealed record SemanticDeduplicationEntry(
    string Fingerprint,
    DateTimeOffset LastPublishedAtUtc)
{
    public DateTimeOffset LastSeenAtUtc { get; init; } = LastPublishedAtUtc;
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
            new ManagedKafkaTopic(options.ZabbixServiceApplyPlans, "zabbix_service_apply_plans", "Zabbix service apply plans"),
            new ManagedKafkaTopic(options.ZabbixSuppressionApplyPlans, "zabbix_suppression_apply_plans", "Zabbix suppression apply plans"),
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
