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
builder.Services.AddOptions<KafkaTopicsOptions>()
    .Bind(builder.Configuration.GetSection(KafkaTopicsOptions.SectionName))
    .Validate(options => !string.IsNullOrWhiteSpace(options.CmdbWebhookEvents), "CMDB raw event topic is required.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.EffectiveAggregationCommands()), "Aggregation command topic is required.")
    .ValidateOnStart();

builder.Services.AddSingleton<ConversionRulesValidator>();
builder.Services.AddSingleton<ConversionRulesFileLoader>();
builder.Services.AddSingleton<AggregationRuleEngine>();
builder.Services.AddSingleton<SemanticCommandDeduplicator>();
builder.Services.AddSingleton<SourceEventEnricher>();
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
    SemanticCommandDeduplicator deduplicator,
    KafkaJsonProducer producer,
    CmdbuildClient cmdbuild,
    IOptions<KafkaTopicsOptions> topicOptions,
    CancellationToken cancellationToken) =>
{
    try
    {
        var rules = await loader.LoadAsync(cancellationToken);
        var validation = validator.Validate(rules);
        if (!validation.IsValid)
        {
            return Results.BadRequest(validation);
        }

        var selectedRules = SelectRulesForCurrentApply(rules, request);
        var selectedDocument = rules with { Rules = selectedRules };
        var sourceClasses = SourceClassesForCurrentApply(selectedRules, request);
        var result = new ApplyCurrentRulesResult
        {
            DryRun = request.DryRun,
            Topic = topicOptions.Value.EffectiveAggregationCommands(),
            SourceClassCount = sourceClasses.Count,
            RuleCount = selectedRules.Count
        };

        foreach (var sourceClass in sourceClasses)
        {
            var classResult = new ApplyCurrentRulesClassResult
            {
                SourceClass = sourceClass
            };

            try
            {
                var catalog = await cmdbuild.ListClassCardsCatalogAsync(sourceClass, "source", cancellationToken);
                var cards = request.MaxCardsPerClass > 0
                    ? catalog.Cards.Take(request.MaxCardsPerClass).ToArray()
                    : catalog.Cards;
                classResult.Cards = cards.Count;

                foreach (var card in cards)
                {
                    var rawEvent = BuildApplyCurrentRawEvent(sourceClass, card, request.EventType);
                    var enrichedEvent = await enricher.EnrichAsync(rawEvent, selectedDocument, cancellationToken);
                    var plans = engine.BuildCommandPlans(enrichedEvent, selectedDocument);
                    classResult.CommandsBuilt += plans.Count;
                    result.CommandsBuilt += plans.Count;

                    foreach (var plan in plans)
                    {
                        Increment(result.CommandsByLayer, plan.Command.Layer);
                        if (result.SampleCommands.Count < 20)
                        {
                            result.SampleCommands.Add(new ApplyCurrentRulesCommandSample
                            {
                                RuleId = plan.Command.RuleId,
                                Layer = plan.Command.Layer,
                                SourceClass = plan.Command.Source.ClassCode,
                                SourceCardId = plan.Command.Source.CardId,
                                TargetClass = plan.Command.Target.ClassCode,
                                TargetKey = plan.Command.Target.IdempotencyKey
                            });
                        }

                        if (request.DryRun)
                        {
                            continue;
                        }

                        if (deduplicator.IsDuplicate(plan, out _))
                        {
                            classResult.CommandsSkippedAsDuplicates++;
                            result.CommandsSkippedAsDuplicates++;
                            continue;
                        }

                        await producer.PublishAsync(
                            topicOptions.Value.EffectiveAggregationCommands(),
                            plan.Command.Target.CardId.Length > 0
                                ? plan.Command.Target.CardId
                                : plan.Command.Target.IdempotencyKey,
                            plan.Command,
                            cancellationToken);
                        deduplicator.MarkPublished(plan);
                        classResult.CommandsPublished++;
                        result.CommandsPublished++;
                    }
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
            {
                classResult.Error = ex.Message;
                result.Errors.Add($"{sourceClass}: {ex.Message}");
            }

            result.Classes.Add(classResult);
            result.CardsScanned += classResult.Cards;
        }

        return Results.Ok(result);
    }
    catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException)
    {
        return Results.Problem(ex.Message, statusCode: StatusCodes.Status502BadGateway);
    }
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

app.Run();

public sealed record ApplyCurrentRulesRequest
{
    public IReadOnlyList<string> Layers { get; init; } = [];

    public IReadOnlyList<string> SourceClasses { get; init; } = [];

    public int MaxCardsPerClass { get; init; }

    public bool DryRun { get; init; }

    public string EventType { get; init; } = "UPDATE";
}

public sealed class ApplyCurrentRulesResult
{
    public bool DryRun { get; init; }

    public string Topic { get; init; } = "";

    public int SourceClassCount { get; init; }

    public int RuleCount { get; init; }

    public int CardsScanned { get; set; }

    public int CommandsBuilt { get; set; }

    public int CommandsPublished { get; set; }

    public int CommandsSkippedAsDuplicates { get; set; }

    public Dictionary<string, int> CommandsByLayer { get; } = new(StringComparer.OrdinalIgnoreCase);

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

    public string TargetClass { get; init; } = "";

    public string TargetKey { get; init; } = "";
}

public sealed class SourceEventEnricher(CmdbuildClient cmdbuild, ILogger<SourceEventEnricher> logger)
{
    public async Task<CmdbRawEvent> EnrichAsync(
        CmdbRawEvent message,
        ConversionRulesDocument rules,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(message.ClassCode)
            || string.IsNullOrWhiteSpace(message.CardId)
            || rules.Source.Fields.Count == 0)
        {
            return message;
        }

        var referencedFields = ReferencedFieldsForClass(rules, message.ClassCode);
        if (referencedFields.Count == 0)
        {
            return message;
        }

        var attributes = new Dictionary<string, string>(message.Attributes, StringComparer.OrdinalIgnoreCase);
        var resolvedCount = 0;
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

            await producer.PublishAsync(
                topicOptions.Value.EffectiveAggregationCommands(),
                plan.Command.Target.CardId.Length > 0 ? plan.Command.Target.CardId : plan.Command.Target.IdempotencyKey,
                plan.Command,
                cancellationToken);
            deduplicator.MarkPublished(plan);
        }
    }

    private async Task<CmdbRawEvent> EnrichSourceFieldsAsync(
        CmdbRawEvent message,
        ConversionRulesDocument rules,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(message.ClassCode)
            || string.IsNullOrWhiteSpace(message.CardId)
            || rules.Source.Fields.Count == 0)
        {
            return message;
        }

        var referencedFields = ReferencedFieldsForClass(rules, message.ClassCode);
        if (referencedFields.Count == 0)
        {
            return message;
        }

        var attributes = new Dictionary<string, string>(message.Attributes, StringComparer.OrdinalIgnoreCase);
        var resolvedCount = 0;
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
