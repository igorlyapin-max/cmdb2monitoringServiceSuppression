using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
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
builder.Services.AddHttpContextAccessor();

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
builder.Services.AddOptions<RuntimeRedisOptions>()
    .Bind(builder.Configuration.GetSection(RuntimeRedisOptions.SectionName))
    .Validate(options => !options.Enabled || !string.IsNullOrWhiteSpace(options.ConnectionString), "Redis:ConnectionString is required when Redis:Enabled=true.")
    .Validate(options => options.OperationTtlSeconds > 0, "Redis:OperationTtlSeconds must be greater than zero.")
    .Validate(options => options.HasValidFailureMode(), "Redis:FailureMode must be fallback or fail.")
    .ValidateOnStart();
builder.Services.AddOptions<ReadinessOptions>()
    .Bind(builder.Configuration.GetSection(ReadinessOptions.SectionName))
    .Validate(options => options.HasValidZabbixHostIdAttribute(), "Readiness:ZabbixHostIdAttribute is required.")
    .ValidateOnStart();
builder.Services.AddOptions<ZabbixDirtyScopeOptions>()
    .Bind(builder.Configuration.GetSection(ZabbixDirtyScopeOptions.SectionName))
    .Validate(options => !options.Enabled || options.HasValidEndpoint(), "ZabbixDirtyScopes:Endpoint is required when ZabbixDirtyScopes:Enabled=true.")
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
builder.Services.AddHttpClient<ZabbixDirtyScopeClient>();

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

app.MapGet("/redis/check", (
    IOptionsMonitor<RuntimeRedisOptions> redisOptions,
    IOptionsMonitor<SemanticDeduplicationOptions> dedupOptions) =>
{
    var redis = redisOptions.CurrentValue;
    var dedup = dedupOptions.CurrentValue;
    if (!redis.Enabled)
    {
        return Results.Ok(new
        {
            configured = false,
            success = true,
            backend = "in-memory",
            redisRequested = false,
            redisAvailable = false,
            fallbackActive = false,
            blockingOnRedisUnavailable = false,
            keyPrefix = redis.KeyPrefix,
            semanticDeduplicationEnabled = dedup.Enabled,
            semanticDeduplicationWindowSeconds = dedup.WindowSeconds,
            message = "Redis is disabled; semantic deduplication uses process memory."
        });
    }

    try
    {
        using var client = RedisRespClient.Connect(redis);
        client.Ping();
        return Results.Ok(new
        {
            configured = true,
            success = true,
            backend = "redis",
            redisRequested = true,
            redisAvailable = true,
            fallbackActive = false,
            blockingOnRedisUnavailable = false,
            keyPrefix = redis.KeyPrefix,
            semanticDeduplicationEnabled = dedup.Enabled,
            semanticDeduplicationWindowSeconds = dedup.WindowSeconds,
            message = "Redis semantic deduplication backend is available."
        });
    }
    catch (Exception ex) when (ex is SocketException or IOException or InvalidOperationException or TimeoutException)
    {
        var failMode = string.Equals(redis.FailureMode, "fail", StringComparison.OrdinalIgnoreCase);
        return Results.Ok(new
        {
            configured = true,
            success = !failMode,
            backend = failMode ? "redis" : "in-memory-fallback",
            redisRequested = true,
            redisAvailable = false,
            fallbackActive = !failMode,
            blockingOnRedisUnavailable = failMode,
            keyPrefix = redis.KeyPrefix,
            semanticDeduplicationEnabled = dedup.Enabled,
            semanticDeduplicationWindowSeconds = dedup.WindowSeconds,
            message = failMode
                ? $"Redis is unavailable and FailureMode=fail. Last Redis error: {ex.Message}"
                : $"Redis is unavailable; semantic deduplication falls back to process memory. Last Redis error: {ex.Message}"
        });
    }
});

app.MapPost("/redis/semantic-dedup/check", (
    SemanticCommandDeduplicator deduplicator,
    IOptionsMonitor<RuntimeRedisOptions> redisOptions,
    IOptionsMonitor<SemanticDeduplicationOptions> dedupOptions) =>
{
    var redis = redisOptions.CurrentValue;
    var dedup = dedupOptions.CurrentValue;
    var redisAvailable = false;
    if (redis.Enabled)
    {
        try
        {
            using var client = RedisRespClient.Connect(redis);
            client.Ping();
            redisAvailable = true;
        }
        catch (Exception ex) when (ex is SocketException or IOException or InvalidOperationException or TimeoutException)
        {
            if (string.Equals(redis.FailureMode, "fail", StringComparison.OrdinalIgnoreCase))
            {
                return Results.Ok(new
                {
                    configured = true,
                    success = false,
                    backend = "redis",
                    redisRequested = true,
                    redisAvailable = false,
                    fallbackActive = false,
                    blockingOnRedisUnavailable = true,
                    keyPrefix = redis.KeyPrefix,
                    semanticDeduplicationEnabled = dedup.Enabled,
                    semanticDeduplicationWindowSeconds = dedup.WindowSeconds,
                    message = $"Redis is unavailable and FailureMode=fail. Semantic dedup self-check is blocked. Last Redis error: {ex.Message}"
                });
            }
        }
    }

    var plan = BuildSemanticDedupSelfCheckPlan();
    try
    {
        var firstDuplicate = deduplicator.IsDuplicate(plan, out var firstDuplicateAge);
        deduplicator.MarkPublished(plan);
        var secondDuplicate = deduplicator.IsDuplicate(plan, out var secondDuplicateAge);
        var success = dedup.Enabled ? !firstDuplicate && secondDuplicate : !firstDuplicate && !secondDuplicate;
        var backend = redis.Enabled
            ? redisAvailable ? "redis" : "in-memory-fallback"
            : "in-memory";

        return Results.Ok(new
        {
            configured = redis.Enabled,
            success,
            backend,
            redisRequested = redis.Enabled,
            redisAvailable,
            fallbackActive = redis.Enabled && !redisAvailable,
            blockingOnRedisUnavailable = false,
            keyPrefix = redis.KeyPrefix,
            semanticDeduplicationEnabled = dedup.Enabled,
            semanticDeduplicationWindowSeconds = dedup.WindowSeconds,
            semanticKeyHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(plan.SemanticKey))).ToLowerInvariant(),
            firstDuplicate,
            firstDuplicateAgeSeconds = firstDuplicateAge?.TotalSeconds,
            secondDuplicate,
            secondDuplicateAgeSeconds = secondDuplicateAge?.TotalSeconds,
            message = dedup.Enabled
                ? "Semantic dedup self-check completed: first check is new, second check is duplicate."
                : "Semantic deduplication is disabled; self-check verified that no duplicate is reported."
        });
    }
    catch (Exception ex) when (ex is SocketException or IOException or InvalidOperationException or TimeoutException)
    {
        return Results.Ok(new
        {
            configured = redis.Enabled,
            success = false,
            backend = redis.Enabled ? "redis" : "in-memory",
            redisRequested = redis.Enabled,
            redisAvailable = false,
            fallbackActive = false,
            blockingOnRedisUnavailable = redis.Enabled && string.Equals(redis.FailureMode, "fail", StringComparison.OrdinalIgnoreCase),
            keyPrefix = redis.KeyPrefix,
            semanticDeduplicationEnabled = dedup.Enabled,
            semanticDeduplicationWindowSeconds = dedup.WindowSeconds,
            message = $"Semantic dedup self-check failed: {ex.Message}"
        });
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

app.MapPost("/rules/apply-current/scope-preview", async (
    ApplyCurrentRulesRequest request,
    ConversionRulesFileLoader loader,
    ConversionRulesValidator validator,
    CmdbuildClient cmdbuild,
    IOptions<ConversionRulesOptions> conversionRulesOptions,
    IHostEnvironment environment,
    CancellationToken cancellationToken) =>
{
    var rules = await loader.LoadAsync(cancellationToken);
    var validation = validator.Validate(rules);
    if (!validation.IsValid)
    {
        return Results.BadRequest(validation);
    }

    var baseSelectedRules = SelectRulesForCurrentApply(rules, request);
    var serviceObjectScopeHints = await ResolveServiceObjectScopeHintsAsync(
        cmdbuild,
        request,
        baseSelectedRules,
        conversionRulesOptions.Value,
        environment,
        cancellationToken);
    var scopePrefilter = SelectScopedRulesForCurrentApply(
        baseSelectedRules,
        request,
        serviceObjectScopeHints.ScopeKeys);
    var selectedRules = scopePrefilter.Applied
        ? scopePrefilter.Rules
        : baseSelectedRules;
    var sourceClasses = SourceClassesForCurrentApply(selectedRules, request);
    var scopeMatchError = CurrentApplyScopeMatchError(request, scopePrefilter, serviceObjectScopeHints);
    if (!string.IsNullOrWhiteSpace(scopeMatchError))
    {
        return Results.BadRequest(new
        {
            error = scopeMatchError,
            zabbixScopePrefilter = scopePrefilter.ToSummary(sourceClasses, serviceObjectScopeHints)
        });
    }

    return Results.Ok(new ApplyCurrentRulesScopePreviewResult
    {
        Layer = ScopedLayerForCurrentApply(request, baseSelectedRules),
        RuleCount = selectedRules.Count,
        SourceClassCount = sourceClasses.Count,
        SourceClasses = sourceClasses,
        ZabbixScopePrefilter = scopePrefilter.ToSummary(sourceClasses, serviceObjectScopeHints),
        Rules = selectedRules
            .Take(50)
            .Select(rule => new ApplyCurrentRulesScopePreviewRule
            {
                RuleId = rule.RuleId,
                Name = rule.Name,
                Layer = rule.Layer,
                SourceClass = rule.Source.ClassCode,
                TargetClass = rule.Target.ClassCode,
                GeneratedFromTemplate = rule.GeneratedFromTemplate,
                TemplateId = rule.TemplateGeneration.TemplateId
            })
            .ToArray()
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
    IHttpClientFactory httpClientFactory,
    ZabbixDirtyScopeClient dirtyScopeClient,
    IOptions<KafkaTopicsOptions> topicOptions,
    IOptions<ConversionRulesOptions> conversionRulesOptions,
    IHostEnvironment environment,
    ApplyCurrentRulesProgressStore progress,
    CancellationToken cancellationToken) =>
{
    var operationId = progress.Start(request.OperationId, request.DryRun);
    using var operationCancellation = progress.LinkCancellation(operationId, cancellationToken);
    var applyCancellationToken = operationCancellation.Token;
    var operationWatch = Stopwatch.StartNew();
    var performance = new ApplyCurrentRulesPerformance();
    try
    {
        progress.Stage(operationId, "loading_rules", "Загрузка правил конвертации.");
        var loadRulesWatch = Stopwatch.StartNew();
        var rules = await loader.LoadAsync(applyCancellationToken);
        loadRulesWatch.Stop();
        performance.LoadRulesMs += loadRulesWatch.ElapsedMilliseconds;
        progress.AddPerformance(operationId, item => item.LoadRulesMs += loadRulesWatch.ElapsedMilliseconds);
        var validationWatch = Stopwatch.StartNew();
        var validation = validator.Validate(rules);
        validationWatch.Stop();
        performance.ValidateRulesMs += validationWatch.ElapsedMilliseconds;
        progress.AddPerformance(operationId, item => item.ValidateRulesMs += validationWatch.ElapsedMilliseconds);
        if (!validation.IsValid)
        {
            operationWatch.Stop();
            performance.TotalMs = operationWatch.ElapsedMilliseconds;
            progress.AddPerformance(operationId, item => item.TotalMs = performance.TotalMs);
            progress.Fail(operationId, "validation_failed", "Правила конвертации не прошли проверку.");
            return Results.BadRequest(validation);
        }

        var baseSelectedRules = SelectRulesForCurrentApply(rules, request);
        var serviceObjectScopeHints = await ResolveServiceObjectScopeHintsAsync(
            cmdbuild,
            request,
            baseSelectedRules,
            conversionRulesOptions.Value,
            environment,
            applyCancellationToken);
        var scopePrefilter = SelectScopedRulesForCurrentApply(
            baseSelectedRules,
            request,
            serviceObjectScopeHints.ScopeKeys);
        var selectedRules = scopePrefilter.Applied
            ? scopePrefilter.Rules
            : baseSelectedRules;
        var selectedDocument = rules with { Rules = selectedRules };
        var sourceClasses = SourceClassesForCurrentApply(selectedRules, request);
        var scopeMatchError = CurrentApplyScopeMatchError(request, scopePrefilter, serviceObjectScopeHints);
        if (!string.IsNullOrWhiteSpace(scopeMatchError))
        {
            operationWatch.Stop();
            performance.TotalMs = operationWatch.ElapsedMilliseconds;
            progress.AddPerformance(operationId, item => item.TotalMs = performance.TotalMs);
            progress.Fail(operationId, "scope_not_matched", scopeMatchError);
            return Results.BadRequest(new
            {
                operationId,
                error = scopeMatchError,
                zabbixScopePrefilter = scopePrefilter.ToSummary(sourceClasses, serviceObjectScopeHints)
            });
        }

        var publishTargets = ResolvePublishTargets(request.Targets);
        var publishTopics = PublishTopicsForRequest(topicOptions.Value, publishTargets, selectedRules.Select(rule => rule.Layer));
        progress.Configure(operationId, sourceClasses, selectedRules.Count, publishTopics);
        var result = new ApplyCurrentRulesResult
        {
            OperationId = operationId,
            DryRun = request.DryRun,
            Topic = string.Join(", ", publishTopics),
            Topics = publishTopics,
            ZabbixDeliveryMode = publishTargets.ZabbixDirect ? "direct" : publishTargets.Zabbix ? "topic" : "",
            ZabbixPublishMode = NormalizeZabbixPublishMode(request.ZabbixPublishMode),
            ZabbixScopePrefilter = scopePrefilter.ToSummary(sourceClasses, serviceObjectScopeHints),
            SourceClassCount = sourceClasses.Count,
            RuleCount = selectedRules.Count,
            Performance = performance
        };
        var operationDeduplicationKeys = new HashSet<string>(StringComparer.Ordinal);
        var pendingPlans = new List<PendingApplyCurrentPlan>();

        foreach (var sourceClass in sourceClasses)
        {
            applyCancellationToken.ThrowIfCancellationRequested();
            progress.BeginClass(operationId, sourceClass);
            var classResult = new ApplyCurrentRulesClassResult
            {
                SourceClass = sourceClass
            };

            try
            {
                progress.Stage(operationId, "loading_cards", $"Загрузка карточек класса {sourceClass}.");
                var loadCardsWatch = Stopwatch.StartNew();
                var catalog = await cmdbuild.ListClassCardsCatalogAsync(sourceClass, "source", applyCancellationToken);
                loadCardsWatch.Stop();
                performance.LoadCardsMs += loadCardsWatch.ElapsedMilliseconds;
                progress.AddPerformance(operationId, item => item.LoadCardsMs += loadCardsWatch.ElapsedMilliseconds);
                var cards = request.MaxCardsPerClass > 0
                    ? catalog.Cards.Take(request.MaxCardsPerClass).ToArray()
                    : catalog.Cards;
                classResult.Cards = cards.Count;
                progress.SetCurrentClassCards(operationId, sourceClass, cards.Count);

                foreach (var card in cards)
                {
                    applyCancellationToken.ThrowIfCancellationRequested();
                    progress.Stage(operationId, "processing_cards", $"Обработка {sourceClass}/{card.Id}.");
                    var rawEvent = BuildApplyCurrentRawEvent(sourceClass, card, request.EventType);
                    var enrichWatch = Stopwatch.StartNew();
                    var enrichedEvent = await enricher.EnrichAsync(rawEvent, selectedDocument, applyCancellationToken);
                    enrichWatch.Stop();
                    performance.EnrichMs += enrichWatch.ElapsedMilliseconds;
                    progress.AddPerformance(operationId, item => item.EnrichMs += enrichWatch.ElapsedMilliseconds);
                    var buildCommandsWatch = Stopwatch.StartNew();
                    var plans = engine.BuildCommandPlans(enrichedEvent, selectedDocument);
                    buildCommandsWatch.Stop();
                    performance.BuildCommandsMs += buildCommandsWatch.ElapsedMilliseconds;
                    progress.AddPerformance(operationId, item => item.BuildCommandsMs += buildCommandsWatch.ElapsedMilliseconds);
                    classResult.CommandsBuilt += plans.Count;
                    result.CommandsBuilt += plans.Count;
                    progress.AddCommandsBuilt(operationId, plans.Count);

                    foreach (var plan in plans)
                    {
                        TrackPendingZabbixPlan(
                            pendingPlans,
                            plan,
                            classResult,
                            result,
                            progress,
                            operationId,
                            serviceTopology: false);
                    }

                    progress.CardProcessed(operationId, sourceClass, card.Id);
                }
            }
            catch (Exception ex) when (!applyCancellationToken.IsCancellationRequested
                && ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
            {
                classResult.Error = ex.Message;
                result.Errors.Add($"{sourceClass}: {ex.Message}");
                progress.AddError(operationId, $"{sourceClass}: {ex.Message}");
            }

            result.Classes.Add(classResult);
            result.CardsScanned += classResult.Cards;
            progress.CompleteClass(operationId, classResult);
        }

        if (ShouldPublishServiceTopology(request, publishTargets))
        {
            progress.Stage(operationId, "service_topology", "Публикация ручных сервисных объектов и их связей.");
            var serviceTopologyResult = await ApplyServiceTopologyAsync(
                request,
                cmdbuild,
                producer,
                httpClientFactory.CreateClient(),
                topicOptions.Value,
                conversionRulesOptions.Value,
                environment,
                publishTargets,
                operationId,
                progress,
                result,
                selectedDocument,
                pendingPlans,
                operationDeduplicationKeys,
                applyCancellationToken);
            result.Classes.Add(serviceTopologyResult);
            result.CardsScanned += serviceTopologyResult.Cards;
            result.ServiceObjectsScanned = serviceTopologyResult.Cards;
        }

        progress.Stage(operationId, "zabbix_graph_validation", "Проверка полного desired graph перед публикацией в Zabbix.");
        var graphValidationErrors = AddZabbixTopologyDiagnostics(result, pendingPlans, progress, operationId);
        if (request.DryRun && publishTargets.ZabbixDirect)
        {
            progress.Stage(
                operationId,
                "zabbix_graph_diff",
                "Расчет diff desired graph в zabbixconfig2api без публикации в Zabbix.");
            await PublishPendingZabbixPlansAsync(
                request,
                producer,
                httpClientFactory.CreateClient(),
                topicOptions.Value,
                publishTargets,
                dirtyScopeClient,
                pendingPlans,
                operationDeduplicationKeys,
                result,
                progress,
                operationId,
                forceDryRun: true,
                applyCancellationToken);
        }

        if (!request.DryRun)
        {
            var blockingErrors = result.Errors
                .Concat(graphValidationErrors)
                .Where(error => !string.IsNullOrWhiteSpace(error))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (blockingErrors.Length > 0)
            {
                var message = "Публикация в Zabbix заблокирована: desired graph содержит ошибки. Выполните dry-run и исправьте связи/шаблоны перед записью.";
                result.Errors.Add(message);
                progress.Fail(operationId, "zabbix_graph_validation_failed", message);
                return Results.Conflict(result);
            }

            progress.Stage(
                operationId,
                "zabbix_graph_publish",
                publishTargets.ZabbixDirect
                    ? "Публикация проверенного целевого графа в Zabbix: batch выполняется целиком; дерево в Zabbix может быть промежуточным до завершения."
                    : "Публикация проверенного целевого графа в Zabbix.");
            await PublishPendingZabbixPlansAsync(
                request,
                producer,
                httpClientFactory.CreateClient(),
                topicOptions.Value,
                publishTargets,
                dirtyScopeClient,
                pendingPlans,
                operationDeduplicationKeys,
                result,
                progress,
                operationId,
                forceDryRun: false,
                applyCancellationToken);
        }
        operationWatch.Stop();
        performance.TotalMs = operationWatch.ElapsedMilliseconds;
        progress.AddPerformance(operationId, item => item.TotalMs = performance.TotalMs);
        progress.Complete(operationId);
        return Results.Ok(result);
    }
    catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException)
    {
        operationWatch.Stop();
        performance.TotalMs = operationWatch.ElapsedMilliseconds;
        progress.AddPerformance(operationId, item => item.TotalMs = performance.TotalMs);
        progress.Fail(operationId, "failed", ex.Message);
        return Results.Problem(ex.Message, statusCode: StatusCodes.Status502BadGateway);
    }
    catch (TaskCanceledException ex) when (!applyCancellationToken.IsCancellationRequested)
    {
        operationWatch.Stop();
        performance.TotalMs = operationWatch.ElapsedMilliseconds;
        progress.AddPerformance(operationId, item => item.TotalMs = performance.TotalMs);
        var message = $"Операция применения прервана по timeout внешнего вызова: {ex.Message}";
        progress.Fail(operationId, "timeout", message);
        return Results.Problem(message, statusCode: StatusCodes.Status504GatewayTimeout);
    }
    catch (OperationCanceledException)
    {
        operationWatch.Stop();
        performance.TotalMs = operationWatch.ElapsedMilliseconds;
        progress.AddPerformance(operationId, item => item.TotalMs = performance.TotalMs);
        progress.Canceled(operationId, "Операция применения отменена.");
        return Results.StatusCode(StatusCodes.Status499ClientClosedRequest);
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

app.MapPost("/rules/apply-current/cancel/{operationId}", (
    string operationId,
    ApplyCurrentRulesProgressStore progress) =>
{
    return progress.RequestCancel(operationId)
        ? Results.Ok(new { operationId, status = "cancel_requested" })
        : Results.NotFound(new { error = "not_found", operationId });
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

static ApplyCurrentRulesScopeSelection SelectScopedRulesForCurrentApply(
    IReadOnlyList<ConversionRule> rules,
    ApplyCurrentRulesRequest request,
    IReadOnlyList<string>? extraScopeKeys = null)
{
    var requestedKeys = ScopeKeysForCurrentApply(request)
        .Concat(extraScopeKeys ?? [])
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();
    var hasExtraScopeKeys = (extraScopeKeys?.Count ?? 0) > 0;
    var originalSourceClasses = rules
        .Select(rule => rule.Source.ClassCode.Trim())
        .Where(item => !string.IsNullOrWhiteSpace(item))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Count();
    if (requestedKeys.Length == 0 || rules.Count == 0)
    {
        return ApplyCurrentRulesScopeSelection.Disabled(rules, originalSourceClasses);
    }

    var candidatesByRule = rules
        .Select((rule, index) => new
        {
            Rule = rule,
            Index = index,
            Candidates = RuleScopeCandidates(rule)
        })
        .ToArray();
    var seedIndexes = candidatesByRule
        .Where(item => requestedKeys.Any(requested => item.Candidates.Contains(requested)))
        .Select(item => item.Index)
        .ToHashSet();
    var missingKeys = requestedKeys
        .Where(requested => !candidatesByRule.Any(item => item.Candidates.Contains(requested)))
        .ToArray();
    if (seedIndexes.Count == 0)
    {
        if (hasExtraScopeKeys)
        {
            return new ApplyCurrentRulesScopeSelection(
                Enabled: true,
                Applied: true,
                Rules: [],
                RequestedKeys: requestedKeys,
                MissingKeys: missingKeys,
                Layer: ScopedLayerForCurrentApply(request, rules),
                Depth: request.ZabbixScopeDepth <= 0 ? 0 : Math.Min(request.ZabbixScopeDepth, 50),
                MatchedSeedCount: 0,
                OriginalRuleCount: rules.Count,
                SelectedRuleCount: 0,
                OriginalSourceClassCount: originalSourceClasses,
                SelectedSourceClassCount: 0);
        }

        return ApplyCurrentRulesScopeSelection.NotMatched(
            rules,
            requestedKeys,
            missingKeys,
            originalSourceClasses);
    }

    var layer = ScopedLayerForCurrentApply(request, rules);
    var maxDepth = request.ZabbixScopeDepth <= 0
        ? int.MaxValue
        : Math.Min(request.ZabbixScopeDepth, 50);
    var edges = BuildRuleScopeEdges(rules);
    var selectedIndexes = string.Equals(layer, "service", StringComparison.OrdinalIgnoreCase)
        ? ResolveServiceRuleScope(seedIndexes, edges, maxDepth)
        : ResolveConnectedRuleScope(seedIndexes, edges, maxDepth);
    var selectedRules = selectedIndexes
        .Order()
        .Select(index => rules[index])
        .ToArray();
    var selectedSourceClasses = selectedRules
        .Select(rule => rule.Source.ClassCode.Trim())
        .Where(item => !string.IsNullOrWhiteSpace(item))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .Count();

    return new ApplyCurrentRulesScopeSelection(
        Enabled: true,
        Applied: selectedRules.Length < rules.Count,
        Rules: selectedRules,
        RequestedKeys: requestedKeys,
        MissingKeys: missingKeys,
        Layer: layer,
        Depth: request.ZabbixScopeDepth <= 0 ? 0 : maxDepth,
        MatchedSeedCount: seedIndexes.Count,
        OriginalRuleCount: rules.Count,
        SelectedRuleCount: selectedRules.Length,
        OriginalSourceClassCount: originalSourceClasses,
        SelectedSourceClassCount: selectedSourceClasses);
}

static IReadOnlyList<string> ScopeKeysForCurrentApply(ApplyCurrentRulesRequest request)
{
    return (request.ZabbixScopeKeys ?? [])
        .SelectMany(value => value.Split(
            [',', '\n', '\r', '\t', ';'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        .Where(item => !string.IsNullOrWhiteSpace(item))
        .ToArray();
}

static string? CurrentApplyScopeMatchError(
    ApplyCurrentRulesRequest request,
    ApplyCurrentRulesScopeSelection scopePrefilter,
    ApplyCurrentRulesServiceObjectScopeHints serviceObjectScopeHints)
{
    var requestedKeys = ScopeKeysForCurrentApply(request);
    if (!request.RequireZabbixScopeMatch || requestedKeys.Count == 0)
    {
        return null;
    }

    if (scopePrefilter.MatchedSeedCount > 0
        || serviceObjectScopeHints.MatchedServiceObjectCount > 0)
    {
        return null;
    }

    return $"Scope публикации задан ({requestedKeys.Count}), но не сопоставлен ни с rule id/name/managed key, ни с ручным сервисным объектом. Подготовка полного scan остановлена; исправьте scope или отключите строгую проверку.";
}

static string ScopedLayerForCurrentApply(
    ApplyCurrentRulesRequest request,
    IReadOnlyList<ConversionRule> rules)
{
    var requestedLayers = (request.Layers ?? [])
        .Select(item => item.Trim())
        .Where(item => !string.IsNullOrWhiteSpace(item))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();
    if (requestedLayers.Length == 1)
    {
        return requestedLayers[0];
    }

    var ruleLayers = rules
        .Select(rule => rule.Layer.Trim())
        .Where(item => !string.IsNullOrWhiteSpace(item))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();
    return ruleLayers.Length == 1
        ? ruleLayers[0]
        : "service";
}

static HashSet<string> RuleScopeCandidates(ConversionRule rule)
{
    var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    AddScopeCandidate(result, rule.RuleId);
    AddScopeCandidate(result, rule.Name);
    AddScopeCandidate(result, rule.Layer);
    AddScopeCandidate(result, rule.Source.ClassCode);
    AddScopeCandidate(result, rule.Target.ClassCode);
    AddScopeCandidate(result, rule.Target.CardId);
    AddScopeCandidate(result, rule.Target.CardDescription);
    AddScopeCandidate(result, RuleTargetManagedKey(rule.Target));
    AddScopeCandidate(result, rule.GeneratedFromTemplate);
    AddScopeCandidate(result, rule.TemplateGeneration.TemplateId);
    foreach (var value in rule.Target.AttributeMappings.Values.Concat(rule.Target.InitialUserValues.Values))
    {
        AddScopeCandidate(result, value);
    }

    return result;
}

static void AddScopeCandidate(ISet<string> values, string? value)
{
    if (!string.IsNullOrWhiteSpace(value))
    {
        values.Add(value.Trim());
    }
}

static AggregationCommandPlan BuildSemanticDedupSelfCheckPlan()
{
    var suffix = Guid.NewGuid().ToString("N");
    return new AggregationCommandPlan
    {
        SemanticKey = $"redis:self-check:{suffix}",
        SemanticFingerprint = $"fingerprint:{suffix}",
        Command = new AggregationCommand
        {
            CommandId = $"redis-self-check-{suffix}",
            CorrelationId = $"redis-self-check-{suffix}",
            SourceEventId = $"redis-self-check-{suffix}",
            CommandType = AggregationCommandTypes.EnsureMembership,
            Layer = "diagnostic",
            RuleId = "redis-semantic-dedup-self-check",
            RuleName = "Redis semantic dedup self-check",
            EventType = "diagnostic",
            CreatedAt = DateTimeOffset.UtcNow,
            Source = new AggregationSourceObject
            {
                ClassCode = "RedisSelfCheckSource",
                CardId = suffix,
                KeyAttribute = "Code",
                KeyValue = suffix
            },
            Target = new AggregationTargetObject
            {
                ClassCode = "RedisSelfCheckTarget",
                CardDescription = "Redis semantic dedup self-check",
                CreateInstance = false,
                IdempotencyKey = suffix
            }
        }
    };
}

static RuleScopeEdges BuildRuleScopeEdges(IReadOnlyList<ConversionRule> rules)
{
    var targetKeyToIndexes = new Dictionary<string, List<int>>(StringComparer.Ordinal);
    for (var index = 0; index < rules.Count; index++)
    {
        var key = RuleTargetManagedKey(rules[index].Target);
        if (string.IsNullOrWhiteSpace(key))
        {
            continue;
        }

        if (!targetKeyToIndexes.TryGetValue(key, out var indexes))
        {
            indexes = [];
            targetKeyToIndexes[key] = indexes;
        }

        indexes.Add(index);
    }

    var childrenByParent = new Dictionary<int, HashSet<int>>();
    var parentsByChild = new Dictionary<int, HashSet<int>>();
    var undirected = new Dictionary<int, HashSet<int>>();
    for (var sourceIndex = 0; sourceIndex < rules.Count; sourceIndex++)
    {
        foreach (var relation in rules[sourceIndex].Relations)
        {
            var targetIndexes = ZabbixManagedServiceMapper
                .LookupCandidates(relation.TargetClassCode, relation.TargetLookup)
                .Where(targetKeyToIndexes.ContainsKey)
                .SelectMany(key => targetKeyToIndexes[key])
                .Where(targetIndex => targetIndex != sourceIndex)
                .Distinct()
                .ToArray();
            foreach (var targetIndex in targetIndexes)
            {
                AddRuleScopeEdge(childrenByParent, sourceIndex, targetIndex);
                AddRuleScopeEdge(parentsByChild, targetIndex, sourceIndex);
                AddRuleScopeEdge(undirected, sourceIndex, targetIndex);
                AddRuleScopeEdge(undirected, targetIndex, sourceIndex);
            }
        }
    }

    return new RuleScopeEdges(childrenByParent, parentsByChild, undirected);
}

static HashSet<int> ResolveServiceRuleScope(
    HashSet<int> seeds,
    RuleScopeEdges edges,
    int maxDepth)
{
    var result = new HashSet<int>(seeds);
    foreach (var seed in seeds)
    {
        TraverseRuleScope(seed, edges.ParentsByChild, int.MaxValue, result);
        TraverseRuleScope(seed, edges.ChildrenByParent, maxDepth, result);
    }

    return result;
}

static HashSet<int> ResolveConnectedRuleScope(
    HashSet<int> seeds,
    RuleScopeEdges edges,
    int maxDepth)
{
    var result = new HashSet<int>(seeds);
    foreach (var seed in seeds)
    {
        TraverseRuleScope(seed, edges.Undirected, maxDepth, result);
    }

    return result;
}

static void TraverseRuleScope(
    int start,
    IReadOnlyDictionary<int, HashSet<int>> adjacency,
    int maxDepth,
    HashSet<int> result)
{
    var queue = new Queue<(int Index, int Depth)>();
    queue.Enqueue((start, 0));
    while (queue.Count > 0)
    {
        var (index, depth) = queue.Dequeue();
        if (depth >= maxDepth || !adjacency.TryGetValue(index, out var next))
        {
            continue;
        }

        foreach (var nextIndex in next)
        {
            if (!result.Add(nextIndex))
            {
                continue;
            }

            queue.Enqueue((nextIndex, depth + 1));
        }
    }
}

static void AddRuleScopeEdge(
    IDictionary<int, HashSet<int>> edges,
    int from,
    int to)
{
    if (from == to)
    {
        return;
    }

    if (!edges.TryGetValue(from, out var next))
    {
        next = [];
        edges[from] = next;
    }

    next.Add(to);
}

static async Task<ApplyCurrentRulesServiceObjectScopeHints> ResolveServiceObjectScopeHintsAsync(
    CmdbuildClient cmdbuild,
    ApplyCurrentRulesRequest request,
    IReadOnlyList<ConversionRule> rules,
    ConversionRulesOptions conversionRulesOptions,
    IHostEnvironment environment,
    CancellationToken cancellationToken)
{
    var requestedKeys = ScopeKeysForCurrentApply(request);
    if (requestedKeys.Count == 0
        || !CurrentApplyIncludesServiceLayer(request)
        || rules.Count == 0)
    {
        return ApplyCurrentRulesServiceObjectScopeHints.Empty;
    }

    var catalog = await cmdbuild.ListManagedLayerClassInstancesAsync(
        request.CmdbuildPrefix,
        "Service",
        request.ServiceModelRoot,
        item => IsManualServiceObjectClass(request.CmdbuildPrefix, item.Code),
        cancellationToken);
    var serviceObjects = catalog.Classes
        .SelectMany(item => item.Cards.Select(card => new ServiceTopologyCard(item.ClassCode, card)))
        .ToArray();
    if (serviceObjects.Length == 0)
    {
        return ApplyCurrentRulesServiceObjectScopeHints.Empty;
    }

    var serviceObjectKeys = serviceObjects
        .Select(item => CardRefKey(item.ClassCode, item.Card.Id))
        .ToHashSet(StringComparer.Ordinal);
    var matchedServiceObjectKeys = serviceObjects
        .Where(item => requestedKeys.Any(requested => ServiceObjectScopeCandidates(item).Contains(requested)))
        .Select(item => CardRefKey(item.ClassCode, item.Card.Id))
        .ToHashSet(StringComparer.Ordinal);
    if (matchedServiceObjectKeys.Count == 0)
    {
        return ApplyCurrentRulesServiceObjectScopeHints.Empty;
    }

    var domains = await cmdbuild.ListDomainsAsync(request.CmdbuildPrefix, cancellationToken);
    var domainByCode = domains
        .GroupBy(domain => domain.Code, StringComparer.Ordinal)
        .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
    var relationsCatalog = await cmdbuild.ListDomainRelationsAsync(
        request.CmdbuildPrefix,
        domain => ServiceTopologyDirection(request.CmdbuildPrefix, domain.Code) != ServiceTopologyRelationDirection.Skip,
        cancellationToken);
    var relationsByParent = BuildServiceTopologyRelations(
        request.CmdbuildPrefix,
        relationsCatalog.Relations,
        domainByCode,
        serviceObjectKeys);
    var templateRelations = await LoadServiceObjectTemplateRelationsAsync(
        conversionRulesOptions,
        environment,
        cancellationToken);
    AddServiceObjectTemplateRelations(
        relationsByParent,
        templateRelations,
        new ConversionRulesDocument
        {
            Version = "scope-prefilter",
            Rules = rules
        },
        serviceObjectKeys);
    NormalizeRelations(relationsByParent);

    var maxDepth = request.ZabbixScopeDepth <= 0 ? int.MaxValue : Math.Min(request.ZabbixScopeDepth, 50);
    var visitedServiceObjects = ResolveServiceObjectScopeTargets(
        matchedServiceObjectKeys,
        relationsByParent,
        serviceObjectKeys,
        maxDepth);
    var targetKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var serviceObjectKey in visitedServiceObjects)
    {
        AddScopeCandidate(targetKeys, serviceObjectKey);
        var parts = serviceObjectKey.Split('\u001f');
        if (parts.Length == 2)
        {
            AddScopeCandidate(targetKeys, $"cmdbuild:{parts[0]}:{parts[1]}");
            AddScopeCandidate(targetKeys, parts[1]);
        }

        if (!relationsByParent.TryGetValue(serviceObjectKey, out var relations))
        {
            continue;
        }

        foreach (var relation in relations)
        {
            AddScopeCandidate(targetKeys, relation.TargetLookup);
            AddScopeCandidate(targetKeys, $"{relation.TargetClassCode}:{relation.TargetLookup}");
            foreach (var candidate in ZabbixManagedServiceMapper.LookupCandidates(
                relation.TargetClassCode,
                relation.TargetLookup))
            {
                AddScopeCandidate(targetKeys, candidate);
            }
        }
    }

    return new ApplyCurrentRulesServiceObjectScopeHints(
        Enabled: true,
        MatchedServiceObjectCount: matchedServiceObjectKeys.Count,
        TraversedServiceObjectCount: visitedServiceObjects.Count,
        ScopeKeys: targetKeys
            .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
            .ToArray());
}

static bool CurrentApplyIncludesServiceLayer(ApplyCurrentRulesRequest request)
{
    var layers = new HashSet<string>(
        (request.Layers ?? [])
            .Select(item => item.Trim())
            .Where(item => !string.IsNullOrWhiteSpace(item)),
        StringComparer.OrdinalIgnoreCase);
    return layers.Count == 0 || layers.Contains("service");
}

static HashSet<string> ServiceObjectScopeCandidates(ServiceTopologyCard item)
{
    var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    AddScopeCandidate(result, CardRefKey(item.ClassCode, item.Card.Id));
    AddScopeCandidate(result, $"cmdbuild:{item.ClassCode}:{item.Card.Id}");
    AddScopeCandidate(result, item.Card.Id);
    AddScopeCandidate(result, item.ClassCode);
    AddScopeCandidate(result, ServiceCardDisplayName(item.Card));
    foreach (var attribute in ServiceCardAttributes(item.Card))
    {
        AddScopeCandidate(result, Convert.ToString(attribute.Value, CultureInfo.InvariantCulture));
    }

    return result;
}

static HashSet<string> ResolveServiceObjectScopeTargets(
    HashSet<string> seeds,
    IReadOnlyDictionary<string, List<AggregationTargetRelation>> relationsByParent,
    IReadOnlySet<string> serviceObjectKeys,
    int maxDepth)
{
    var result = new HashSet<string>(seeds, StringComparer.Ordinal);
    var parentsByChild = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
    foreach (var (parent, relations) in relationsByParent)
    {
        foreach (var relation in relations)
        {
            var childKey = CardRefKey(relation.TargetClassCode, relation.TargetLookup);
            if (!serviceObjectKeys.Contains(childKey))
            {
                continue;
            }

            if (!parentsByChild.TryGetValue(childKey, out var parents))
            {
                parents = [];
                parentsByChild[childKey] = parents;
            }

            parents.Add(parent);
        }
    }

    foreach (var seed in seeds)
    {
        TraverseServiceObjectScope(seed, parentsByChild, int.MaxValue, result);
        TraverseServiceObjectScope(
            seed,
            relationsByParent.ToDictionary(
                pair => pair.Key,
                pair => pair.Value
                    .Select(relation => CardRefKey(relation.TargetClassCode, relation.TargetLookup))
                    .Where(serviceObjectKeys.Contains)
                    .ToHashSet(StringComparer.Ordinal),
                StringComparer.Ordinal),
            maxDepth,
            result);
    }

    return result;
}

static void TraverseServiceObjectScope(
    string start,
    IReadOnlyDictionary<string, HashSet<string>> adjacency,
    int maxDepth,
    HashSet<string> result)
{
    var queue = new Queue<(string Key, int Depth)>();
    queue.Enqueue((start, 0));
    while (queue.Count > 0)
    {
        var (key, depth) = queue.Dequeue();
        if (depth >= maxDepth || !adjacency.TryGetValue(key, out var next))
        {
            continue;
        }

        foreach (var nextKey in next)
        {
            if (!result.Add(nextKey))
            {
                continue;
            }

            queue.Enqueue((nextKey, depth + 1));
        }
    }
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

static bool ShouldPublishServiceTopology(ApplyCurrentRulesRequest request, PublishTargets targets)
{
    if (!targets.Zabbix && !targets.ZabbixDirect)
    {
        return false;
    }

    var layers = new HashSet<string>(
        (request.Layers ?? [])
            .Select(item => item.Trim())
            .Where(item => !string.IsNullOrWhiteSpace(item)),
        StringComparer.OrdinalIgnoreCase);
    return layers.Count == 0 || layers.Contains("service");
}

static void AddPublishPerformance(
    ApplyCurrentRulesPerformance performance,
    ApplyCurrentRulesProgressStore progress,
    string operationId,
    bool zabbixDirect,
    long elapsedMs,
    bool serviceTopology)
{
    performance.PublishMs += elapsedMs;
    if (serviceTopology)
    {
        performance.ServiceTopologyPublishMs += elapsedMs;
    }

    if (zabbixDirect)
    {
        performance.DirectZabbixApplyMs += elapsedMs;
        performance.DirectZabbixApplyCalls++;
    }
    else
    {
        performance.KafkaPublishMs += elapsedMs;
        performance.KafkaPublishCalls++;
    }

    progress.AddPerformance(operationId, item =>
    {
        item.PublishMs += elapsedMs;
        if (serviceTopology)
        {
            item.ServiceTopologyPublishMs += elapsedMs;
        }

        if (zabbixDirect)
        {
            item.DirectZabbixApplyMs += elapsedMs;
            item.DirectZabbixApplyCalls++;
        }
        else
        {
            item.KafkaPublishMs += elapsedMs;
            item.KafkaPublishCalls++;
        }
    });
}

static IReadOnlyList<string> AddZabbixTopologyDiagnostics(
    ApplyCurrentRulesResult result,
    IReadOnlyList<PendingApplyCurrentPlan> pendingPlans,
    ApplyCurrentRulesProgressStore progress,
    string operationId)
{
    if (!result.CommandsByLayer.ContainsKey("service"))
    {
        return [];
    }

    var messages = new List<string>();
    var orphanVisibleNodes = result.ZabbixPlan.OrphanVisibleNodes();
    if (orphanVisibleNodes.Count > 0)
    {
        messages.Add(
            $"Zabbix service topology: {orphanVisibleNodes.Count} видимых managed-узлов не имеют parent в desired graph и попадут в корень Zabbix Services. "
            + string.Join("; ", orphanVisibleNodes
                .Take(10)
                .Select(item => $"{item.Name} ({item.ClassCode}, role={item.Role}, key={item.ManagedKey})")));
    }

    foreach (var cycle in FindZabbixServiceGraphCycles(pendingPlans))
    {
        messages.Add($"Zabbix service topology: обнаружен цикл desired graph: {cycle}.");
    }

    foreach (var duplicate in FindConflictingZabbixManagedKeys(pendingPlans))
    {
        messages.Add($"Zabbix service topology: managed key используется для разных объектов: {duplicate}.");
    }

    foreach (var message in messages.Distinct(StringComparer.Ordinal))
    {
        result.Errors.Add(message);
        progress.AddError(operationId, message);
    }

    return messages;
}

static void TrackPendingZabbixPlan(
    ICollection<PendingApplyCurrentPlan> pendingPlans,
    AggregationCommandPlan plan,
    ApplyCurrentRulesClassResult classResult,
    ApplyCurrentRulesResult result,
    ApplyCurrentRulesProgressStore progress,
    string operationId,
    bool serviceTopology)
{
    result.ZabbixPlan.Add(plan.Command);
    progress.AddPlannedCommand(operationId, plan.Command);
    pendingPlans.Add(new PendingApplyCurrentPlan(plan, classResult, serviceTopology));
    Increment(result.CommandsByLayer, plan.Command.Layer);
    if (result.SampleCommands.Count >= 20)
    {
        return;
    }

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
        TargetKey = !string.IsNullOrWhiteSpace(plan.Command.Target.IdempotencyKey)
            ? plan.Command.Target.IdempotencyKey
            : plan.Command.Target.CardId
    });
}

static async Task PublishPendingZabbixPlansAsync(
    ApplyCurrentRulesRequest request,
    KafkaJsonProducer producer,
    HttpClient httpClient,
    KafkaTopicsOptions topicOptions,
    PublishTargets publishTargets,
    ZabbixDirtyScopeClient dirtyScopeClient,
    IReadOnlyList<PendingApplyCurrentPlan> pendingPlans,
    ISet<string> operationDeduplicationKeys,
    ApplyCurrentRulesResult result,
    ApplyCurrentRulesProgressStore progress,
    string operationId,
    bool forceDryRun,
    CancellationToken cancellationToken)
{
    var orderedPlans = PrepareZabbixGraphPublishPlans(pendingPlans);
    if (publishTargets.ZabbixDirect)
    {
        var graphPlans = new List<PendingApplyCurrentPlan>();
        foreach (var pending in orderedPlans)
        {
            if (ShouldSkipOperationDuplicate(pending.Plan, publishTargets, operationDeduplicationKeys))
            {
                pending.ClassResult.CommandsSkippedAsDuplicates++;
                result.CommandsSkippedAsDuplicates++;
                progress.AddDuplicate(operationId);
                continue;
            }

            graphPlans.Add(pending);
        }

        foreach (var layerGroup in graphPlans.GroupBy(item => item.Plan.Command.Layer, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var publishWatch = Stopwatch.StartNew();
            var publishedTopics = await ApplyZabbixGraphDirectAsync(
                httpClient,
                request.ZabbixCommandApplyUrl,
                layerGroup.Key,
                layerGroup.Select(item => item.Plan.Command).ToArray(),
                forceDryRun,
                request.ZabbixPublishMode,
                request.ZabbixScopeKeys,
                request.ZabbixScopeDepth,
                cancellationToken);
            publishWatch.Stop();
            if (publishedTopics.Body.ValueKind == JsonValueKind.Object)
            {
                result.ZabbixDirectGraphResults.Add(publishedTopics.Body);
            }

            var serviceTopology = layerGroup.Any(item => item.ServiceTopology);
            AddPublishPerformance(
                result.Performance,
                progress,
                operationId,
                zabbixDirect: true,
                publishWatch.ElapsedMilliseconds,
                serviceTopology);
            if (forceDryRun)
            {
                continue;
            }

            var appliedCommandIds = ReadGraphResultCommandIds(publishedTopics.Body);
            var selectedCommandCount = ReadJsonInt(publishedTopics.Body, "commandsSelectedForPublish");
            var publishedPlans = appliedCommandIds.Count > 0
                ? layerGroup
                    .Where(item => appliedCommandIds.Contains(item.Plan.Command.CommandId))
                    .ToArray()
                : layerGroup
                    .Take(Math.Max(0, selectedCommandCount ?? layerGroup.Count()))
                    .ToArray();
            foreach (var pending in publishedPlans)
            {
                pending.ClassResult.CommandsPublished++;
                result.CommandsPublished++;
                result.CommandsAppliedDirect++;
                progress.AddPublished(operationId, publishedTopics.Topics);
                foreach (var topic in publishedTopics.Topics)
                {
                    Increment(result.CommandsPublishedByTopic, topic);
                }
            }
        }

        return;
    }

    foreach (var pending in orderedPlans)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var plan = pending.Plan;
        if (ShouldSkipOperationDuplicate(plan, publishTargets, operationDeduplicationKeys))
        {
            pending.ClassResult.CommandsSkippedAsDuplicates++;
            result.CommandsSkippedAsDuplicates++;
            progress.AddDuplicate(operationId);
            continue;
        }

        var publishWatch = Stopwatch.StartNew();
        var publishedTopics = await PublishAggregationPlanAsync(
            producer,
            topicOptions,
            plan,
            publishTargets,
            cancellationToken);
        publishWatch.Stop();
        AddPublishPerformance(
            result.Performance,
            progress,
            operationId,
            publishTargets.ZabbixDirect,
            publishWatch.ElapsedMilliseconds,
            pending.ServiceTopology);
        pending.ClassResult.CommandsPublished++;
        result.CommandsPublished++;
        if (publishTargets.ZabbixDirect)
        {
            result.CommandsAppliedDirect++;
        }

        progress.AddPublished(operationId, publishedTopics);
        foreach (var topic in publishedTopics)
        {
            Increment(result.CommandsPublishedByTopic, topic);
        }
        await dirtyScopeClient.MarkPendingIfZabbixPublishedAsync(
            plan.Command,
            publishedTopics,
            "apply-current zabbix topic publish",
            cancellationToken);
    }
}

static IReadOnlyList<PendingApplyCurrentPlan> PrepareZabbixGraphPublishPlans(
    IReadOnlyList<PendingApplyCurrentPlan> pendingPlans)
{
    var servicePlans = pendingPlans
        .Where(item => string.Equals(item.Plan.Command.Layer, "service", StringComparison.OrdinalIgnoreCase))
        .ToArray();
    var parentKeysByManagedKey = BuildParentKeysByManagedKey(servicePlans);
    var enriched = pendingPlans
        .Select(item => AttachParentManagedKeys(item, parentKeysByManagedKey))
        .ToArray();
    var depths = CalculateZabbixServiceGraphDepths(enriched, parentKeysByManagedKey);

    return enriched
        .Select((item, index) => new { item, index })
        .OrderBy(pair => string.Equals(pair.item.Plan.Command.Layer, "service", StringComparison.OrdinalIgnoreCase)
            ? depths.GetValueOrDefault(ZabbixManagedServiceMapper.ManagedKey(pair.item.Plan.Command.Target), 0)
            : 0)
        .ThenBy(pair => pair.index)
        .Select(pair => pair.item)
        .ToArray();
}

static PendingApplyCurrentPlan AttachParentManagedKeys(
    PendingApplyCurrentPlan pending,
    IReadOnlyDictionary<string, HashSet<string>> parentKeysByManagedKey)
{
    if (!string.Equals(pending.Plan.Command.Layer, "service", StringComparison.OrdinalIgnoreCase))
    {
        return pending;
    }

    var managedKey = ZabbixManagedServiceMapper.ManagedKey(pending.Plan.Command.Target);
    if (string.IsNullOrWhiteSpace(managedKey)
        || !parentKeysByManagedKey.TryGetValue(managedKey, out var parentKeys)
        || parentKeys.Count == 0)
    {
        return pending;
    }

    var target = pending.Plan.Command.Target with
    {
        ParentManagedKeys = pending.Plan.Command.Target.ParentManagedKeys
            .Concat(parentKeys)
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToArray()
    };
    var command = pending.Plan.Command with { Target = target };
    return pending with { Plan = pending.Plan with { Command = command } };
}

static Dictionary<string, HashSet<string>> BuildParentKeysByManagedKey(
    IReadOnlyList<PendingApplyCurrentPlan> servicePlans)
{
    var knownKeys = servicePlans
        .Select(item => ZabbixManagedServiceMapper.ManagedKey(item.Plan.Command.Target))
        .Where(key => !string.IsNullOrWhiteSpace(key))
        .ToHashSet(StringComparer.Ordinal);
    var result = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
    foreach (var plan in servicePlans)
    {
        var parentKey = ZabbixManagedServiceMapper.ManagedKey(plan.Plan.Command.Target);
        if (string.IsNullOrWhiteSpace(parentKey))
        {
            continue;
        }

        foreach (var relation in plan.Plan.Command.Target.Relations)
        {
            var childKey = ZabbixManagedServiceMapper
                .LookupCandidates(relation.TargetClassCode, relation.TargetLookup)
                .FirstOrDefault(knownKeys.Contains);
            if (string.IsNullOrWhiteSpace(childKey) || string.Equals(childKey, parentKey, StringComparison.Ordinal))
            {
                continue;
            }

            if (!result.TryGetValue(childKey, out var parents))
            {
                parents = new HashSet<string>(StringComparer.Ordinal);
                result[childKey] = parents;
            }

            parents.Add(parentKey);
        }
    }

    return result;
}

static Dictionary<string, int> CalculateZabbixServiceGraphDepths(
    IReadOnlyList<PendingApplyCurrentPlan> plans,
    IReadOnlyDictionary<string, HashSet<string>> parentKeysByManagedKey)
{
    var keys = plans
        .Where(item => string.Equals(item.Plan.Command.Layer, "service", StringComparison.OrdinalIgnoreCase))
        .Select(item => ZabbixManagedServiceMapper.ManagedKey(item.Plan.Command.Target))
        .Where(key => !string.IsNullOrWhiteSpace(key))
        .Distinct(StringComparer.Ordinal)
        .ToArray();
    var depths = new Dictionary<string, int>(StringComparer.Ordinal);
    var visiting = new HashSet<string>(StringComparer.Ordinal);

    int Depth(string key)
    {
        if (depths.TryGetValue(key, out var existing))
        {
            return existing;
        }

        if (!visiting.Add(key))
        {
            return 0;
        }

        var depth = 0;
        if (parentKeysByManagedKey.TryGetValue(key, out var parents))
        {
            depth = parents
                .Where(parent => !string.Equals(parent, key, StringComparison.Ordinal))
                .Select(parent => Depth(parent) + 1)
                .DefaultIfEmpty(0)
                .Max();
        }

        visiting.Remove(key);
        depths[key] = depth;
        return depth;
    }

    foreach (var key in keys)
    {
        Depth(key);
    }

    return depths;
}

static IReadOnlyList<string> FindZabbixServiceGraphCycles(
    IReadOnlyList<PendingApplyCurrentPlan> pendingPlans)
{
    var servicePlans = pendingPlans
        .Where(item => string.Equals(item.Plan.Command.Layer, "service", StringComparison.OrdinalIgnoreCase))
        .ToArray();
    var parentKeysByManagedKey = BuildParentKeysByManagedKey(servicePlans);
    if (parentKeysByManagedKey.Count == 0)
    {
        return [];
    }

    var childrenByParent = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
    foreach (var (child, parents) in parentKeysByManagedKey)
    {
        foreach (var parent in parents)
        {
            if (!childrenByParent.TryGetValue(parent, out var children))
            {
                children = new HashSet<string>(StringComparer.Ordinal);
                childrenByParent[parent] = children;
            }

            children.Add(child);
        }
    }

    var cycles = new List<string>();
    var visited = new HashSet<string>(StringComparer.Ordinal);
    var stack = new List<string>();
    var inStack = new HashSet<string>(StringComparer.Ordinal);

    void Visit(string key)
    {
        if (cycles.Count >= 10)
        {
            return;
        }

        if (inStack.Contains(key))
        {
            var start = stack.IndexOf(key);
            if (start >= 0)
            {
                cycles.Add(string.Join(" -> ", stack.Skip(start).Concat([key])));
            }
            return;
        }

        if (!visited.Add(key))
        {
            return;
        }

        stack.Add(key);
        inStack.Add(key);
        if (childrenByParent.TryGetValue(key, out var children))
        {
            foreach (var child in children)
            {
                Visit(child);
            }
        }

        inStack.Remove(key);
        stack.RemoveAt(stack.Count - 1);
    }

    foreach (var key in childrenByParent.Keys.OrderBy(key => key, StringComparer.Ordinal))
    {
        Visit(key);
    }

    return cycles
        .Distinct(StringComparer.Ordinal)
        .ToArray();
}

static IReadOnlyList<string> FindConflictingZabbixManagedKeys(
    IReadOnlyList<PendingApplyCurrentPlan> pendingPlans)
{
    return pendingPlans
        .Where(item => string.Equals(item.Plan.Command.Layer, "service", StringComparison.OrdinalIgnoreCase))
        .GroupBy(item => ZabbixManagedServiceMapper.ManagedKey(item.Plan.Command.Target), StringComparer.Ordinal)
        .Where(group => !string.IsNullOrWhiteSpace(group.Key))
        .Select(group => new
        {
            ManagedKey = group.Key,
            Targets = group
                .Select(item => item.Plan.Command.Target.ClassCode)
                .Distinct(StringComparer.Ordinal)
                .Take(5)
                .ToArray()
        })
        .Where(group => group.Targets.Length > 1)
        .Select(group => $"{group.ManagedKey}: {string.Join("; ", group.Targets)}")
        .Take(10)
        .ToArray();
}

static async Task<ApplyCurrentRulesClassResult> ApplyServiceTopologyAsync(
    ApplyCurrentRulesRequest request,
    CmdbuildClient cmdbuild,
    KafkaJsonProducer producer,
    HttpClient httpClient,
    KafkaTopicsOptions topicOptions,
    ConversionRulesOptions conversionRulesOptions,
    IHostEnvironment environment,
    PublishTargets publishTargets,
    string operationId,
    ApplyCurrentRulesProgressStore progress,
    ApplyCurrentRulesResult result,
    ConversionRulesDocument rules,
    ICollection<PendingApplyCurrentPlan> pendingPlans,
    ISet<string> operationDeduplicationKeys,
    CancellationToken cancellationToken)
{
    var classResult = new ApplyCurrentRulesClassResult
    {
        SourceClass = "service_objects"
    };

    try
    {
        var buildTopologyWatch = Stopwatch.StartNew();
        var plans = await BuildServiceTopologyCommandPlansAsync(
            cmdbuild,
            request,
            rules,
            conversionRulesOptions,
            environment,
            cancellationToken);
        buildTopologyWatch.Stop();
        result.Performance.ServiceTopologyBuildMs += buildTopologyWatch.ElapsedMilliseconds;
        progress.AddPerformance(operationId, item => item.ServiceTopologyBuildMs += buildTopologyWatch.ElapsedMilliseconds);
        classResult.Cards = plans.Count;
        classResult.CommandsBuilt = plans.Count;
        result.CommandsBuilt += plans.Count;
        progress.AddCommandsBuilt(operationId, plans.Count);

        foreach (var plan in plans)
        {
            TrackPendingZabbixPlan(
                pendingPlans,
                plan,
                classResult,
                result,
                progress,
                operationId,
                serviceTopology: true);
        }
    }
    catch (Exception ex) when (!cancellationToken.IsCancellationRequested
        && ex is HttpRequestException or TaskCanceledException or InvalidOperationException or JsonException)
    {
        classResult.Error = ex.Message;
        result.Errors.Add($"service_objects: {ex.Message}");
        progress.AddError(operationId, $"service_objects: {ex.Message}");
    }

    return classResult;
}

static async Task<IReadOnlyList<AggregationCommandPlan>> BuildServiceTopologyCommandPlansAsync(
    CmdbuildClient cmdbuild,
    ApplyCurrentRulesRequest request,
    ConversionRulesDocument rules,
    ConversionRulesOptions conversionRulesOptions,
    IHostEnvironment environment,
    CancellationToken cancellationToken)
{
    var catalog = await cmdbuild.ListManagedLayerClassInstancesAsync(
        request.CmdbuildPrefix,
        "Service",
        request.ServiceModelRoot,
        item => IsManualServiceObjectClass(request.CmdbuildPrefix, item.Code),
        cancellationToken);
    var serviceObjects = catalog.Classes
        .SelectMany(item => item.Cards.Select(card => new ServiceTopologyCard(item.ClassCode, card)))
        .ToArray();
    if (serviceObjects.Length == 0)
    {
        return [];
    }

    var serviceObjectKeys = serviceObjects
        .Select(item => CardRefKey(item.ClassCode, item.Card.Id))
        .ToHashSet(StringComparer.Ordinal);

    var domains = await cmdbuild.ListDomainsAsync(request.CmdbuildPrefix, cancellationToken);
    var domainByCode = domains
        .GroupBy(domain => domain.Code, StringComparer.Ordinal)
        .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
    var relationsCatalog = await cmdbuild.ListDomainRelationsAsync(
        request.CmdbuildPrefix,
        domain => ServiceTopologyDirection(request.CmdbuildPrefix, domain.Code) != ServiceTopologyRelationDirection.Skip,
        cancellationToken);
    var relationsByParent = BuildServiceTopologyRelations(
        request.CmdbuildPrefix,
        relationsCatalog.Relations,
        domainByCode,
        serviceObjectKeys);
    var templateRelations = await LoadServiceObjectTemplateRelationsAsync(
        conversionRulesOptions,
        environment,
        cancellationToken);
    AddServiceObjectTemplateRelations(
        relationsByParent,
        templateRelations,
        rules,
        serviceObjectKeys);
    NormalizeRelations(relationsByParent);

    var plans = serviceObjects
        .Select(item => BuildServiceTopologyCommandPlan(
            item,
            relationsByParent.TryGetValue(CardRefKey(item.ClassCode, item.Card.Id), out var relations)
                ? relations
                : []))
        .ToArray();
    return SortServiceTopologyPlans(plans);
}

static Dictionary<string, List<AggregationTargetRelation>> BuildServiceTopologyRelations(
    string prefix,
    IReadOnlyList<CmdbuildDomainRelationCatalogItem> relations,
    IReadOnlyDictionary<string, CmdbuildDomainCatalogItem> domainByCode,
    IReadOnlySet<string> serviceObjectKeys)
{
    var byParent = new Dictionary<string, List<AggregationTargetRelation>>(StringComparer.Ordinal);
    foreach (var relation in relations)
    {
        if (!domainByCode.TryGetValue(relation.DomainCode, out var domain))
        {
            continue;
        }

        var sourceKey = CardRefKey(relation.SourceType, relation.SourceId);
        var destinationKey = CardRefKey(relation.DestinationType, relation.DestinationId);
        var direction = ServiceTopologyDirection(prefix, domain.Code);
        if (direction == ServiceTopologyRelationDirection.Skip)
        {
            continue;
        }

        var parentClass = direction == ServiceTopologyRelationDirection.SourceParentDestinationChild
            ? relation.SourceType
            : relation.DestinationType;
        var parentId = direction == ServiceTopologyRelationDirection.SourceParentDestinationChild
            ? relation.SourceId
            : relation.DestinationId;
        var childClass = direction == ServiceTopologyRelationDirection.SourceParentDestinationChild
            ? relation.DestinationType
            : relation.SourceType;
        var childId = direction == ServiceTopologyRelationDirection.SourceParentDestinationChild
            ? relation.DestinationId
            : relation.SourceId;
        if (string.Equals(parentClass, childClass, StringComparison.Ordinal)
            && string.Equals(parentId, childId, StringComparison.Ordinal))
        {
            continue;
        }

        var parentKey = CardRefKey(parentClass, parentId);
        if (!serviceObjectKeys.Contains(parentKey))
        {
            continue;
        }

        if (!byParent.TryGetValue(parentKey, out var parentRelations))
        {
            parentRelations = [];
            byParent[parentKey] = parentRelations;
        }

        parentRelations.Add(new AggregationTargetRelation
        {
            DomainCode = domain.Code,
            TargetClassCode = childClass,
            TargetLookup = childId
        });
    }

    foreach (var pair in byParent)
    {
        byParent[pair.Key] = pair.Value
            .DistinctBy(item => $"{item.DomainCode}\u001f{item.TargetClassCode}\u001f{item.TargetLookup}", StringComparer.Ordinal)
            .OrderBy(item => item.TargetClassCode, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.TargetLookup, StringComparer.Ordinal)
            .ToList();
    }

    return byParent;
}

static async Task<IReadOnlyList<ServiceObjectTemplateRelationIntent>> LoadServiceObjectTemplateRelationsAsync(
    ConversionRulesOptions options,
    IHostEnvironment environment,
    CancellationToken cancellationToken)
{
    var path = ResolveServiceTemplatesPath(options, environment);
    if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
    {
        return [];
    }

    await using var stream = File.OpenRead(path);
    var document = await JsonSerializer.DeserializeAsync<ServiceTemplateRelationsDocument>(
        stream,
        new JsonSerializerOptions(JsonSerializerDefaults.Web),
        cancellationToken);
    return document?.ServiceObjectTemplateRelations ?? [];
}

static string ResolveServiceTemplatesPath(ConversionRulesOptions options, IHostEnvironment environment)
{
    foreach (var candidate in ServiceTemplatePathCandidates(options, environment))
    {
        var fullPath = Path.GetFullPath(candidate);
        if (File.Exists(fullPath))
        {
            return fullPath;
        }
    }

    return "";
}

static IEnumerable<string> ServiceTemplatePathCandidates(ConversionRulesOptions options, IHostEnvironment environment)
{
    if (!string.IsNullOrWhiteSpace(options.ServiceTemplatesFilePath))
    {
        yield return options.ServiceTemplatesFilePath;
    }

    var rulePath = options.FilePath ?? "";
    if (!string.IsNullOrWhiteSpace(rulePath))
    {
        var ruleDirectory = Path.GetDirectoryName(rulePath);
        if (!string.IsNullOrWhiteSpace(ruleDirectory))
        {
            yield return Path.Combine(ruleDirectory, "service-templates.json");
        }
    }

    foreach (var basePath in CandidateBasePaths(environment))
    {
        if (!string.IsNullOrWhiteSpace(options.ServiceTemplatesFilePath)
            && !Path.IsPathRooted(options.ServiceTemplatesFilePath))
        {
            yield return Path.Combine(basePath, options.ServiceTemplatesFilePath);
        }

        yield return Path.Combine(basePath, "state/conversion-config/service-templates.json");
        yield return Path.Combine(basePath, "rules/service-templates.json");
    }
}

static IEnumerable<string> CandidateBasePaths(IHostEnvironment environment)
{
    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var start in new[] { environment.ContentRootPath, Environment.CurrentDirectory })
    {
        var directory = new DirectoryInfo(Path.GetFullPath(start));
        while (directory is not null)
        {
            if (seen.Add(directory.FullName))
            {
                yield return directory.FullName;
            }

            directory = directory.Parent;
        }
    }
}

static void AddServiceObjectTemplateRelations(
    Dictionary<string, List<AggregationTargetRelation>> relationsByParent,
    IReadOnlyList<ServiceObjectTemplateRelationIntent> templateRelations,
    ConversionRulesDocument rules,
    IReadOnlySet<string> serviceObjectKeys)
{
    if (templateRelations.Count == 0)
    {
        return;
    }

    var generatedRules = rules.Rules
        .Where(rule => rule.Enabled
            && string.Equals(rule.Layer, "service", StringComparison.OrdinalIgnoreCase))
        .Select(rule => new
        {
            Rule = rule,
            TemplateId = FirstNonEmpty(rule.TemplateGeneration.TemplateId, rule.GeneratedFromTemplate),
            ManagedKey = RuleTargetManagedKey(rule.Target)
        })
        .Where(item => !string.IsNullOrWhiteSpace(item.TemplateId)
            && !string.IsNullOrWhiteSpace(item.Rule.Target.ClassCode)
            && !string.IsNullOrWhiteSpace(item.ManagedKey))
        .ToArray();

    foreach (var relation in templateRelations)
    {
        if (!relation.TargetType.Equals("service_template", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(relation.SourceClassCode)
            || string.IsNullOrWhiteSpace(relation.SourceCardId)
            || string.IsNullOrWhiteSpace(relation.TargetTemplateId))
        {
            continue;
        }

        var parentKey = CardRefKey(relation.SourceClassCode, relation.SourceCardId);
        if (!serviceObjectKeys.Contains(parentKey))
        {
            continue;
        }

        if (!relationsByParent.TryGetValue(parentKey, out var parentRelations))
        {
            parentRelations = [];
            relationsByParent[parentKey] = parentRelations;
        }

        foreach (var target in generatedRules.Where(item =>
            item.TemplateId.Equals(relation.TargetTemplateId, StringComparison.Ordinal)
            && (string.IsNullOrWhiteSpace(relation.TargetClassCode)
                || item.Rule.Target.ClassCode.Equals(relation.TargetClassCode, StringComparison.Ordinal))))
        {
            parentRelations.Add(new AggregationTargetRelation
            {
                DomainCode = FirstNonEmpty(relation.RelationType, relation.RelationKind, relation.RelationId),
                TargetClassCode = target.Rule.Target.ClassCode,
                TargetLookup = target.ManagedKey
            });
        }
    }
}

static void NormalizeRelations(Dictionary<string, List<AggregationTargetRelation>> relationsByParent)
{
    foreach (var pair in relationsByParent.ToArray())
    {
        relationsByParent[pair.Key] = pair.Value
            .Where(item => !string.IsNullOrWhiteSpace(item.TargetClassCode)
                && !string.IsNullOrWhiteSpace(item.TargetLookup))
            .DistinctBy(item => $"{item.DomainCode}\u001f{item.TargetClassCode}\u001f{item.TargetLookup}", StringComparer.Ordinal)
            .OrderBy(item => item.TargetClassCode, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.TargetLookup, StringComparer.Ordinal)
            .ToList();
    }
}

static string RuleTargetManagedKey(TargetObject target)
{
    if (!string.IsNullOrWhiteSpace(target.IdempotencyKey))
    {
        return target.IdempotencyKey;
    }

    if (!string.IsNullOrWhiteSpace(target.CardId) && !string.IsNullOrWhiteSpace(target.ClassCode))
    {
        return $"cmdbuild:{target.ClassCode}:{target.CardId}";
    }

    return string.IsNullOrWhiteSpace(target.ClassCode)
        ? target.CardId
        : $"{target.ClassCode}:{target.CardId}";
}

static string FirstNonEmpty(params string?[] values)
{
    foreach (var value in values)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value.Trim();
        }
    }

    return "";
}

static AggregationCommandPlan BuildServiceTopologyCommandPlan(
    ServiceTopologyCard item,
    IReadOnlyList<AggregationTargetRelation> relations)
{
    var attributes = ServiceCardAttributes(item.Card);
    var displayName = ServiceCardDisplayName(item.Card);
    var command = new AggregationCommand
    {
        CommandId = $"service-topology-{item.ClassCode}-{item.Card.Id}-{Guid.NewGuid():N}",
        CorrelationId = $"service-topology-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}",
        SourceEventId = $"service-topology-{item.ClassCode}-{item.Card.Id}",
        CommandType = AggregationCommandTypes.EnsureMembership,
        Layer = "service",
        RuleId = $"service-object-{item.ClassCode}-{item.Card.Id}",
        RuleName = string.IsNullOrWhiteSpace(displayName)
            ? $"Сервисный объект {item.ClassCode}/{item.Card.Id}"
            : displayName,
        EventType = "UPDATE",
        Source = new AggregationSourceObject(),
        Target = new AggregationTargetObject
        {
            ClassCode = item.ClassCode,
            CardId = item.Card.Id,
            CardDescription = displayName,
            CreateInstance = false,
            Attributes = attributes,
            Relations = relations
        }
    };
    var semanticKey = $"service-topology:{item.ClassCode}:{item.Card.Id}";
    var semanticFingerprint = string.Join(
        "\n",
        semanticKey,
        string.Join("|", relations.Select(relation => $"{relation.DomainCode}:{relation.TargetClassCode}:{relation.TargetLookup}")));
    return new AggregationCommandPlan
    {
        Command = command,
        SemanticKey = semanticKey,
        SemanticFingerprint = semanticFingerprint
    };
}

static IReadOnlyList<AggregationCommandPlan> SortServiceTopologyPlans(IReadOnlyList<AggregationCommandPlan> plans)
{
    var byKey = plans
        .GroupBy(plan => CardRefKey(plan.Command.Target.ClassCode, plan.Command.Target.CardId), StringComparer.Ordinal)
        .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
    var depthCache = new Dictionary<string, int>(StringComparer.Ordinal);
    return plans
        .OrderBy(plan => ServiceTopologyDepth(plan, byKey, depthCache, []))
        .ThenBy(plan => plan.Command.Target.CardDescription, StringComparer.OrdinalIgnoreCase)
        .ThenBy(plan => plan.Command.Target.CardId, StringComparer.Ordinal)
        .ToArray();
}

static int ServiceTopologyDepth(
    AggregationCommandPlan plan,
    IReadOnlyDictionary<string, AggregationCommandPlan> byKey,
    Dictionary<string, int> depthCache,
    HashSet<string> visiting)
{
    var key = CardRefKey(plan.Command.Target.ClassCode, plan.Command.Target.CardId);
    if (depthCache.TryGetValue(key, out var cached))
    {
        return cached;
    }

    if (!visiting.Add(key))
    {
        return 0;
    }

    var childDepth = 0;
    foreach (var relation in plan.Command.Target.Relations)
    {
        var childKey = CardRefKey(relation.TargetClassCode, relation.TargetLookup);
        if (byKey.TryGetValue(childKey, out var childPlan))
        {
            childDepth = Math.Max(childDepth, ServiceTopologyDepth(childPlan, byKey, depthCache, visiting) + 1);
        }
    }

    visiting.Remove(key);
    depthCache[key] = childDepth;
    return childDepth;
}

static Dictionary<string, object?> ServiceCardAttributes(CmdbuildClassCardCatalogItem card)
{
    var attributes = card.Attributes
        .Where(attribute => !string.IsNullOrWhiteSpace(attribute.Code))
        .GroupBy(attribute => attribute.Code, StringComparer.Ordinal)
        .ToDictionary(
            group => group.Key,
            group => (object?)group.Last().Value,
            StringComparer.Ordinal);
    if (!string.IsNullOrWhiteSpace(card.Description))
    {
        attributes.TryAdd("Description", card.Description);
    }

    return attributes;
}

static string ServiceCardDisplayName(CmdbuildClassCardCatalogItem card)
{
    foreach (var name in new[] { "zabbix_service_name", "zabbix_name", "monitoring_name", "name", "Name", "Description", "description", "Code", "code" })
    {
        var value = card.Attributes.FirstOrDefault(attribute =>
            attribute.Code.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value;
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value.Trim();
        }
    }

    return string.IsNullOrWhiteSpace(card.Description) ? card.Id : card.Description.Trim();
}

static ServiceTopologyRelationDirection ServiceTopologyDirection(string prefix, string domainCode)
{
    var baseCode = RemoveManagedPrefix(prefix, domainCode);
    if (baseCode.Contains("HasSla", StringComparison.OrdinalIgnoreCase)
        || baseCode.Contains("SlaPolicy", StringComparison.OrdinalIgnoreCase)
        || baseCode.Contains("SlaCalendar", StringComparison.OrdinalIgnoreCase)
        || baseCode.Contains("RegularDowntime", StringComparison.OrdinalIgnoreCase)
        || baseCode.Contains("PopulatedFrom", StringComparison.OrdinalIgnoreCase))
    {
        return ServiceTopologyRelationDirection.Skip;
    }

    if (baseCode.Contains("AggregatesTo", StringComparison.OrdinalIgnoreCase)
        || baseCode.Contains("MemberOf", StringComparison.OrdinalIgnoreCase))
    {
        return ServiceTopologyRelationDirection.SourceChildDestinationParent;
    }

    if (baseCode.Contains("DependsOn", StringComparison.OrdinalIgnoreCase)
        || baseCode.Contains("Uses", StringComparison.OrdinalIgnoreCase))
    {
        return ServiceTopologyRelationDirection.SourceParentDestinationChild;
    }

    return ServiceTopologyRelationDirection.Skip;
}

static bool IsManualServiceObjectClass(string prefix, string classCode)
{
    return RemoveManagedPrefix(prefix, classCode)
        .Equals("ServicePlatformService", StringComparison.OrdinalIgnoreCase);
}

static string RemoveManagedPrefix(string prefix, string code)
{
    if (!string.IsNullOrWhiteSpace(prefix)
        && code.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
    {
        return code[prefix.Length..];
    }

    return code;
}

static string CardRefKey(string classCode, string cardId)
{
    return $"{classCode}\u001f{cardId}";
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
        Zabbix: normalized.Contains("zabbix"),
        ZabbixDirect: normalized.Contains("zabbix-direct") || normalized.Contains("zabbix_direct"));
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

    if (targets.ZabbixDirect)
    {
        result.Add("zabbix-direct");
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

    if (targets.ZabbixDirect)
    {
        result.Add("zabbix-direct");
    }

    return result
        .Where(topic => !string.IsNullOrWhiteSpace(topic))
        .Distinct(StringComparer.Ordinal)
        .ToArray();
}

static async Task<ZabbixGraphDirectApplyResponse> ApplyZabbixGraphDirectAsync(
    HttpClient client,
    string? applyUrl,
    string layer,
    IReadOnlyList<AggregationCommand> commands,
    bool dryRun,
    string publishMode,
    IReadOnlyList<string> scopeKeys,
    int scopeDepth,
    CancellationToken cancellationToken)
{
    if (string.IsNullOrWhiteSpace(applyUrl))
    {
        throw new InvalidOperationException(
            "Zabbix direct apply URL is not configured; set backend.zabbixCommandApplyUrl in monitoring-ui-api.");
    }

    client.Timeout = Timeout.InfiniteTimeSpan;
    var graphApplyUrl = ResolveZabbixGraphApplyUrl(applyUrl);
    using var request = new HttpRequestMessage(HttpMethod.Post, graphApplyUrl)
    {
        Content = new StringContent(
            JsonSerializer.Serialize(new
            {
                layer,
                commands,
                dryRun,
                publishMode = NormalizeZabbixPublishMode(publishMode),
                scopeKeys,
                scopeDepth
            }),
            Encoding.UTF8,
            "application/json")
    };
    using var response = await client.SendAsync(request, cancellationToken);
    var text = await response.Content.ReadAsStringAsync(cancellationToken);
    if (!response.IsSuccessStatusCode)
    {
        throw new InvalidOperationException($"Zabbix direct graph apply failed: HTTP {(int)response.StatusCode}: {Trim(text)}");
    }

    JsonElement body = default;
    if (!string.IsNullOrWhiteSpace(text))
    {
        using var document = JsonDocument.Parse(text);
        body = document.RootElement.Clone();
        var status = ReadJsonString(document.RootElement, "status");
        var error = ReadJsonString(document.RootElement, "error");
        var firstError = ReadFirstJsonString(document.RootElement, "errors");
        if (string.Equals(status, "error", StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrWhiteSpace(error)
            || !string.IsNullOrWhiteSpace(firstError))
        {
            var message = firstError
                ?? error
                ?? ReadJsonString(document.RootElement, "message")
                ?? "Zabbix direct graph apply returned error status.";
            throw new InvalidOperationException(message);
        }
    }

    return new ZabbixGraphDirectApplyResponse
    {
        Topics = ["zabbix-direct-graph"],
        Body = body
    };
}

static string ResolveZabbixGraphApplyUrl(string applyUrl)
{
    var value = applyUrl.Trim();
    if (value.EndsWith("/commands/apply-graph", StringComparison.OrdinalIgnoreCase))
    {
        return value;
    }

    const string singleEndpoint = "/commands/apply";
    if (value.EndsWith(singleEndpoint, StringComparison.OrdinalIgnoreCase))
    {
        return value[..^singleEndpoint.Length] + "/commands/apply-graph";
    }

    return value.TrimEnd('/') + "/commands/apply-graph";
}

static string NormalizeZabbixPublishMode(string? value)
{
    return string.Equals(value, "full", StringComparison.OrdinalIgnoreCase)
        ? "full"
        : "changes";
}

static IReadOnlySet<string> ReadGraphResultCommandIds(JsonElement element)
{
    var result = new HashSet<string>(StringComparer.Ordinal);
    if (element.ValueKind != JsonValueKind.Object
        || !element.TryGetProperty("commandResults", out var commandResults)
        || commandResults.ValueKind != JsonValueKind.Array)
    {
        return result;
    }

    foreach (var commandResult in commandResults.EnumerateArray())
    {
        var commandId = ReadJsonString(commandResult, "commandId");
        if (!string.IsNullOrWhiteSpace(commandId))
        {
            result.Add(commandId);
        }
    }

    return result;
}

static int? ReadJsonInt(JsonElement element, string propertyName)
{
    if (element.ValueKind != JsonValueKind.Object
        || !element.TryGetProperty(propertyName, out var value))
    {
        return null;
    }

    return value.ValueKind switch
    {
        JsonValueKind.Number when value.TryGetInt32(out var number) => number,
        JsonValueKind.String when int.TryParse(value.GetString(), out var number) => number,
        _ => null
    };
}

static string? ReadJsonString(JsonElement element, string propertyName)
{
    if (element.ValueKind != JsonValueKind.Object
        || !element.TryGetProperty(propertyName, out var value))
    {
        return null;
    }

    return value.ValueKind == JsonValueKind.String
        ? value.GetString()
        : value.GetRawText();
}

static string? ReadFirstJsonString(JsonElement element, string propertyName)
{
    if (element.ValueKind != JsonValueKind.Object
        || !element.TryGetProperty(propertyName, out var value)
        || value.ValueKind != JsonValueKind.Array)
    {
        return null;
    }

    foreach (var item in value.EnumerateArray())
    {
        return item.ValueKind == JsonValueKind.String
            ? item.GetString()
            : item.GetRawText();
    }

    return null;
}

static string Trim(string value, int maxLength = 500)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        return "";
    }

    var normalized = value.Trim();
    return normalized.Length <= maxLength
        ? normalized
        : normalized[..maxLength];
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
    if ((!targets.Zabbix && !targets.ZabbixDirect) || targets.Aggregation)
    {
        return false;
    }

    return !operationDeduplicationKeys.Add(OperationDeduplicationKey(plan));
}

app.Run();

public sealed record PublishTargets(bool Aggregation, bool Zabbix, bool ZabbixDirect = false)
{
    public static PublishTargets All { get; } = new(Aggregation: true, Zabbix: true);

    public static PublishTargets AggregationOnly { get; } = new(Aggregation: true, Zabbix: false);
}

public enum ServiceTopologyRelationDirection
{
    Skip,
    SourceParentDestinationChild,
    SourceChildDestinationParent
}

public sealed record ServiceTopologyCard(string ClassCode, CmdbuildClassCardCatalogItem Card);

public sealed record ServiceTemplateRelationsDocument
{
    [JsonPropertyName("serviceObjectTemplateRelations")]
    public IReadOnlyList<ServiceObjectTemplateRelationIntent> ServiceObjectTemplateRelations { get; init; } = [];
}

public sealed record ServiceObjectTemplateRelationIntent
{
    [JsonPropertyName("relation_id")]
    public string RelationId { get; init; } = "";

    [JsonPropertyName("relation_kind")]
    public string RelationKind { get; init; } = "";

    [JsonPropertyName("relation_type")]
    public string RelationType { get; init; } = "";

    [JsonPropertyName("source_class_code")]
    public string SourceClassCode { get; init; } = "";

    [JsonPropertyName("source_card_id")]
    public string SourceCardId { get; init; } = "";

    [JsonPropertyName("target_type")]
    public string TargetType { get; init; } = "";

    [JsonPropertyName("target_template_id")]
    public string TargetTemplateId { get; init; } = "";

    [JsonPropertyName("target_class_code")]
    public string TargetClassCode { get; init; } = "";
}

public sealed record ApplyCurrentRulesRequest
{
    public string OperationId { get; init; } = "";

    public IReadOnlyList<string> Layers { get; init; } = [];

    public IReadOnlyList<string> SourceClasses { get; init; } = [];

    public IReadOnlyList<string> Targets { get; init; } = [];

    public string CmdbuildPrefix { get; init; } = "";

    public string ServiceModelRoot { get; init; } = "";

    public string SuppressionModelRoot { get; init; } = "";

    public string ZabbixCommandApplyUrl { get; init; } = "";

    public string ZabbixPublishMode { get; init; } = "changes";

    public IReadOnlyList<string> ZabbixScopeKeys { get; init; } = [];

    public int ZabbixScopeDepth { get; init; }

    public bool RequireZabbixScopeMatch { get; init; }

    public int MaxCardsPerClass { get; init; }

    public bool DryRun { get; init; }

    public string EventType { get; init; } = "UPDATE";
}

public sealed record PendingApplyCurrentPlan(
    AggregationCommandPlan Plan,
    ApplyCurrentRulesClassResult ClassResult,
    bool ServiceTopology);

public sealed record RuleScopeEdges(
    IReadOnlyDictionary<int, HashSet<int>> ChildrenByParent,
    IReadOnlyDictionary<int, HashSet<int>> ParentsByChild,
    IReadOnlyDictionary<int, HashSet<int>> Undirected);

public sealed record ApplyCurrentRulesScopeSelection(
    bool Enabled,
    bool Applied,
    IReadOnlyList<ConversionRule> Rules,
    IReadOnlyList<string> RequestedKeys,
    IReadOnlyList<string> MissingKeys,
    string Layer,
    int Depth,
    int MatchedSeedCount,
    int OriginalRuleCount,
    int SelectedRuleCount,
    int OriginalSourceClassCount,
    int SelectedSourceClassCount)
{
    public static ApplyCurrentRulesScopeSelection Disabled(
        IReadOnlyList<ConversionRule> rules,
        int sourceClassCount) =>
        new(
            Enabled: false,
            Applied: false,
            Rules: rules,
            RequestedKeys: [],
            MissingKeys: [],
            Layer: "",
            Depth: 0,
            MatchedSeedCount: 0,
            OriginalRuleCount: rules.Count,
            SelectedRuleCount: rules.Count,
            OriginalSourceClassCount: sourceClassCount,
            SelectedSourceClassCount: sourceClassCount);

    public static ApplyCurrentRulesScopeSelection NotMatched(
        IReadOnlyList<ConversionRule> rules,
        IReadOnlyList<string> requestedKeys,
        IReadOnlyList<string> missingKeys,
        int sourceClassCount) =>
        new(
            Enabled: true,
            Applied: false,
            Rules: rules,
            RequestedKeys: requestedKeys,
            MissingKeys: missingKeys,
            Layer: "",
            Depth: 0,
            MatchedSeedCount: 0,
            OriginalRuleCount: rules.Count,
            SelectedRuleCount: rules.Count,
            OriginalSourceClassCount: sourceClassCount,
            SelectedSourceClassCount: sourceClassCount);

    public ApplyCurrentRulesScopePrefilterSummary ToSummary(
        IReadOnlyList<string> selectedSourceClasses,
        ApplyCurrentRulesServiceObjectScopeHints serviceObjectScopeHints) =>
        new()
        {
            Enabled = Enabled,
            Applied = Applied,
            RequestedKeyCount = RequestedKeys.Count,
            MissingKeys = MissingKeys.Take(30).ToArray(),
            ServiceObjectMatchedCount = serviceObjectScopeHints.MatchedServiceObjectCount,
            ServiceObjectTraversedCount = serviceObjectScopeHints.TraversedServiceObjectCount,
            ServiceObjectScopeKeyCount = serviceObjectScopeHints.ScopeKeys.Count,
            Layer = Layer,
            Depth = Depth,
            MatchedSeedCount = MatchedSeedCount,
            OriginalRuleCount = OriginalRuleCount,
            SelectedRuleCount = SelectedRuleCount,
            OriginalSourceClassCount = OriginalSourceClassCount,
            SelectedSourceClassCount = selectedSourceClasses.Count,
            Message = ScopePrefilterMessage(selectedSourceClasses.Count, serviceObjectScopeHints)
        };

    private string ScopePrefilterMessage(
        int selectedSourceClassCount,
        ApplyCurrentRulesServiceObjectScopeHints serviceObjectScopeHints)
    {
        if (!Enabled)
        {
            return "Scope публикации не задан; правила и карточки не сужались.";
        }

        if (MatchedSeedCount == 0)
        {
            return serviceObjectScopeHints.MatchedServiceObjectCount > 0
                ? $"Scope сопоставлен с сервисными объектами ({serviceObjectScopeHints.MatchedServiceObjectCount}), но связанные правила не найдены; source-карточки правил не читаются."
                : "Scope задан, но статически не сопоставлен с rule id/name/managed key; без строгой проверки подготовка выполняется полным набором.";
        }

        if (!Applied)
        {
            return "Scope сопоставлен, но после раскрытия связей выбраны все правила; чтение CMDBuild не сократилось.";
        }

        return $"Scope сократил подготовку: правил {SelectedRuleCount}/{OriginalRuleCount}, source-классов {selectedSourceClassCount}/{OriginalSourceClassCount}.";
    }
}

public sealed record ApplyCurrentRulesServiceObjectScopeHints(
    bool Enabled,
    int MatchedServiceObjectCount,
    int TraversedServiceObjectCount,
    IReadOnlyList<string> ScopeKeys)
{
    public static ApplyCurrentRulesServiceObjectScopeHints Empty { get; } = new(
        Enabled: false,
        MatchedServiceObjectCount: 0,
        TraversedServiceObjectCount: 0,
        ScopeKeys: []);
}

public sealed class ApplyCurrentRulesScopePrefilterSummary
{
    public bool Enabled { get; init; }

    public bool Applied { get; init; }

    public int RequestedKeyCount { get; init; }

    public IReadOnlyList<string> MissingKeys { get; init; } = [];

    public int ServiceObjectMatchedCount { get; init; }

    public int ServiceObjectTraversedCount { get; init; }

    public int ServiceObjectScopeKeyCount { get; init; }

    public string Layer { get; init; } = "";

    public int Depth { get; init; }

    public int MatchedSeedCount { get; init; }

    public int OriginalRuleCount { get; init; }

    public int SelectedRuleCount { get; init; }

    public int OriginalSourceClassCount { get; init; }

    public int SelectedSourceClassCount { get; init; }

    public string Message { get; init; } = "";
}

public sealed class ApplyCurrentRulesScopePreviewResult
{
    public string Layer { get; init; } = "";

    public int RuleCount { get; init; }

    public int SourceClassCount { get; init; }

    public IReadOnlyList<string> SourceClasses { get; init; } = [];

    public ApplyCurrentRulesScopePrefilterSummary ZabbixScopePrefilter { get; init; } = new();

    public IReadOnlyList<ApplyCurrentRulesScopePreviewRule> Rules { get; init; } = [];
}

public sealed class ApplyCurrentRulesScopePreviewRule
{
    public string RuleId { get; init; } = "";

    public string Name { get; init; } = "";

    public string Layer { get; init; } = "";

    public string SourceClass { get; init; } = "";

    public string TargetClass { get; init; } = "";

    public string GeneratedFromTemplate { get; init; } = "";

    public string TemplateId { get; init; } = "";
}

public sealed class ApplyCurrentRulesResult
{
    public string OperationId { get; init; } = "";

    public bool DryRun { get; init; }

    public string Topic { get; init; } = "";

    public IReadOnlyList<string> Topics { get; init; } = [];

    public string ZabbixDeliveryMode { get; init; } = "";

    public string ZabbixPublishMode { get; init; } = "changes";

    public ApplyCurrentRulesScopePrefilterSummary ZabbixScopePrefilter { get; init; } = new();

    public int SourceClassCount { get; init; }

    public int RuleCount { get; init; }

    public int CardsScanned { get; set; }

    public int ServiceObjectsScanned { get; set; }

    public int CommandsBuilt { get; set; }

    public int CommandsPublished { get; set; }

    public int CommandsAppliedDirect { get; set; }

    public int CommandsSkippedAsDuplicates { get; set; }

    public Dictionary<string, int> CommandsByLayer { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, int> CommandsPublishedByTopic { get; } = new(StringComparer.Ordinal);

    public ApplyCurrentRulesPerformance Performance { get; init; } = new();

    public ApplyCurrentRulesZabbixPlanSummary ZabbixPlan { get; } = new();

    public List<ApplyCurrentRulesClassResult> Classes { get; } = [];

    public List<ApplyCurrentRulesCommandSample> SampleCommands { get; } = [];

    public List<JsonElement> ZabbixDirectGraphResults { get; } = [];

    public List<string> Errors { get; } = [];
}

public sealed class ZabbixGraphDirectApplyResponse
{
    public IReadOnlyList<string> Topics { get; init; } = [];

    public JsonElement Body { get; init; }
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

public sealed class ApplyCurrentRulesPerformance
{
    public long TotalMs { get; set; }

    public long LoadRulesMs { get; set; }

    public long ValidateRulesMs { get; set; }

    public long LoadCardsMs { get; set; }

    public long EnrichMs { get; set; }

    public long BuildCommandsMs { get; set; }

    public long PublishMs { get; set; }

    public long DirectZabbixApplyMs { get; set; }

    public int DirectZabbixApplyCalls { get; set; }

    public long KafkaPublishMs { get; set; }

    public int KafkaPublishCalls { get; set; }

    public long ServiceTopologyBuildMs { get; set; }

    public long ServiceTopologyPublishMs { get; set; }

    public ApplyCurrentRulesPerformance Clone()
    {
        return new ApplyCurrentRulesPerformance
        {
            TotalMs = TotalMs,
            LoadRulesMs = LoadRulesMs,
            ValidateRulesMs = ValidateRulesMs,
            LoadCardsMs = LoadCardsMs,
            EnrichMs = EnrichMs,
            BuildCommandsMs = BuildCommandsMs,
            PublishMs = PublishMs,
            DirectZabbixApplyMs = DirectZabbixApplyMs,
            DirectZabbixApplyCalls = DirectZabbixApplyCalls,
            KafkaPublishMs = KafkaPublishMs,
            KafkaPublishCalls = KafkaPublishCalls,
            ServiceTopologyBuildMs = ServiceTopologyBuildMs,
            ServiceTopologyPublishMs = ServiceTopologyPublishMs
        };
    }
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
    private readonly ConcurrentDictionary<string, CancellationTokenSource> cancellations = new(StringComparer.OrdinalIgnoreCase);

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
        if (cancellations.TryRemove(operationId, out var previousCancellation))
        {
            previousCancellation.Dispose();
        }

        cancellations[operationId] = new CancellationTokenSource();
        TrimOldOperations();
        return operationId;
    }

    public CancellationTokenSource LinkCancellation(string operationId, CancellationToken requestCancellationToken)
    {
        return cancellations.TryGetValue(operationId, out var operationCancellation)
            ? CancellationTokenSource.CreateLinkedTokenSource(requestCancellationToken, operationCancellation.Token)
            : CancellationTokenSource.CreateLinkedTokenSource(requestCancellationToken);
    }

    public bool RequestCancel(string operationId)
    {
        if (!cancellations.TryGetValue(operationId, out var cancellation))
        {
            return false;
        }

        cancellation.Cancel();
        Update(operationId, progress =>
        {
            progress.Status = "canceling";
            progress.Stage = "cancel_requested";
            progress.Message = "Запрошена отмена операции. Backend остановится на ближайшей безопасной точке.";
        });
        return true;
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

    public void AddPerformance(string operationId, Action<ApplyCurrentRulesPerformance> updatePerformance)
    {
        Update(operationId, progress => updatePerformance(progress.Performance));
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
        DisposeCancellation(operationId);
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
        DisposeCancellation(operationId);
    }

    public void Canceled(string operationId, string message)
    {
        Update(operationId, progress =>
        {
            progress.Status = "canceled";
            progress.Stage = "canceled";
            progress.Message = message;
            progress.FinishedAtUtc = DateTimeOffset.UtcNow;
            progress.Errors.Insert(0, message);
        });
        DisposeCancellation(operationId);
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
            DisposeCancellation(item.Key);
        }
    }

    private void DisposeCancellation(string operationId)
    {
        if (cancellations.TryRemove(operationId, out var cancellation))
        {
            cancellation.Dispose();
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

    public ApplyCurrentRulesPerformance Performance { get; } = new();

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
            Performance = Performance.Clone(),
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

    public ApplyCurrentRulesPerformance Performance { get; init; } = new();

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
    private readonly HashSet<string> incomingManagedKeys = new(StringComparer.Ordinal);
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
        foreach (var relation in command.Target.Relations)
        {
            AddIncomingManagedKey(relation);
        }

        if (!objects.TryGetValue(objectKey, out var plannedObject))
        {
            if (objects.Count >= MaxObjectSamples)
            {
                return;
            }

            var role = ZabbixManagedServiceMapper.ServiceRole(command, command.Layer);
            plannedObject = new ApplyCurrentRulesZabbixObjectPlan
            {
                Action = command.CommandType,
                ActionLabel = ActionLabel(command),
                Layer = command.Layer,
                ManagedKey = ZabbixManagedServiceMapper.ManagedKey(command.Target),
                Role = role,
                Visibility = ZabbixManagedServiceMapper.ServiceVisibility(command.Target.Attributes, role),
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
        var hasSourceObject = !string.IsNullOrWhiteSpace(command.Source.ClassCode)
            || !string.IsNullOrWhiteSpace(command.Source.CardId);
        if (hasSourceObject)
        {
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
        }

        AddLimited(plannedObject.RuleIds, command.RuleId, MaxValuesPerObject);
        AddLimited(plannedObject.RuleNames, command.RuleName, MaxValuesPerObject);
        if (hasSourceObject)
        {
            AddLimited(plannedObject.SourceObjects, SourceObjectLabel(command.Source), MaxValuesPerObject);
            AddSourceBinding(plannedObject, command.Source);
        }
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
        var rootServices = RootServices();
        var orphanVisibleNodes = OrphanVisibleNodes();
        return new ApplyCurrentRulesZabbixPlanSnapshot
        {
            ObjectCount = ObjectCount,
            RelationCount = RelationCount,
            ObjectSamplesLimit = ObjectSamplesLimit,
            HasMoreObjects = HasMoreObjects,
            Objects = Objects,
            RootServiceCount = rootServices.Count,
            RootServices = rootServices,
            OrphanVisibleNodeCount = orphanVisibleNodes.Count,
            OrphanVisibleNodes = orphanVisibleNodes
        };
    }

    public IReadOnlyList<ApplyCurrentRulesZabbixTopologyIssue> OrphanVisibleNodes()
    {
        var incoming = IncomingManagedKeys();
        return objects.Values
            .Where(item => IsVisibleNonRoot(item)
                && !incoming.Contains(item.ManagedKey))
            .OrderBy(item => item.TargetName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.ManagedKey, StringComparer.Ordinal)
            .Select(item => new ApplyCurrentRulesZabbixTopologyIssue
            {
                ManagedKey = item.ManagedKey,
                Name = item.TargetName,
                ClassCode = item.TargetClass,
                Role = item.Role,
                Visibility = item.Visibility,
                Message = "Видимый расчетный узел не имеет parent в desired graph и попадет в корень Zabbix Services."
            })
            .ToArray();
    }

    private IReadOnlyList<ApplyCurrentRulesZabbixTopologyIssue> RootServices()
    {
        return objects.Values
            .Where(item => item.Role.Equals(ZabbixManagedServiceRoles.RootService, StringComparison.OrdinalIgnoreCase)
                || item.Visibility.Equals(ZabbixManagedServiceVisibility.Root, StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.TargetName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.ManagedKey, StringComparer.Ordinal)
            .Select(item => new ApplyCurrentRulesZabbixTopologyIssue
            {
                ManagedKey = item.ManagedKey,
                Name = item.TargetName,
                ClassCode = item.TargetClass,
                Role = item.Role,
                Visibility = item.Visibility,
                Message = "Root service desired graph."
            })
            .ToArray();
    }

    private HashSet<string> IncomingManagedKeys()
    {
        return new HashSet<string>(incomingManagedKeys, StringComparer.Ordinal);
    }

    private void AddIncomingManagedKey(AggregationTargetRelation relation)
    {
        if (!string.IsNullOrWhiteSpace(relation.TargetLookup))
        {
            incomingManagedKeys.Add(relation.TargetLookup);
        }

        if (!string.IsNullOrWhiteSpace(relation.TargetClassCode)
            && !string.IsNullOrWhiteSpace(relation.TargetLookup))
        {
            incomingManagedKeys.Add($"cmdbuild:{relation.TargetClassCode}:{relation.TargetLookup}");
        }
    }

    private static bool IsVisibleNonRoot(ApplyCurrentRulesZabbixObjectPlan item)
    {
        if (item.Visibility.Equals(ZabbixManagedServiceVisibility.Internal, StringComparison.OrdinalIgnoreCase)
            || item.Role.Equals(ZabbixManagedServiceRoles.Internal, StringComparison.OrdinalIgnoreCase)
            || item.Role.Equals(ZabbixManagedServiceRoles.SourceLeaf, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !item.Role.Equals(ZabbixManagedServiceRoles.RootService, StringComparison.OrdinalIgnoreCase)
            && !item.Visibility.Equals(ZabbixManagedServiceVisibility.Root, StringComparison.OrdinalIgnoreCase);
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

    public int RootServiceCount { get; init; }

    public IReadOnlyList<ApplyCurrentRulesZabbixTopologyIssue> RootServices { get; init; } = [];

    public int OrphanVisibleNodeCount { get; init; }

    public IReadOnlyList<ApplyCurrentRulesZabbixTopologyIssue> OrphanVisibleNodes { get; init; } = [];

    public IReadOnlyList<ApplyCurrentRulesZabbixObjectPlan> Objects { get; init; } = [];
}

public sealed class ApplyCurrentRulesZabbixTopologyIssue
{
    public string ManagedKey { get; init; } = "";

    public string Name { get; init; } = "";

    public string ClassCode { get; init; } = "";

    public string Role { get; init; } = "";

    public string Visibility { get; init; } = "";

    public string Message { get; init; } = "";
}

public sealed class ApplyCurrentRulesZabbixObjectPlan
{
    public string Action { get; init; } = "";

    public string ActionLabel { get; init; } = "";

    public string Layer { get; init; } = "";

    public string ManagedKey { get; init; } = "";

    public string Role { get; init; } = "";

    public string Visibility { get; init; } = "";

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
    IOptions<ConversionRulesOptions> conversionRulesOptions,
    IHostEnvironment environment,
    ConversionRulesValidator validator,
    AggregationRuleEngine engine,
    SemanticCommandDeduplicator deduplicator,
    KafkaJsonProducer producer,
    CmdbuildClient cmdbuild,
    ZabbixDirtyScopeClient dirtyScopeClient,
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
        var plans = await AttachServiceParentManagedKeysAsync(
            engine.BuildCommandPlans(enrichedMessage, rules),
            rules,
            cancellationToken);
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

            var publishedTopics = await PublishAggregationPlanAsync(
                producer,
                topicOptions.Value,
                plan,
                PublishTargets.All,
                cancellationToken);
            await dirtyScopeClient.MarkPendingIfZabbixPublishedAsync(
                plan.Command,
                publishedTopics,
                "streaming webhook zabbix topic publish",
                cancellationToken);
            deduplicator.MarkPublished(plan);
        }

        await MarkDirtyScopesForIntermediateWebhookAsync(message, rules, cancellationToken);
    }

    private async Task MarkDirtyScopesForIntermediateWebhookAsync(
        CmdbRawEvent message,
        ConversionRulesDocument rules,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(message.ClassCode) || rules.Source.Fields.Count == 0)
        {
            return;
        }

        var impactedFields = rules.Source.Fields
            .Where(item => CmdbPathContainsIntermediateClass(item.Value.CmdbPath, message.ClassCode))
            .Select(item => item.Key)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (impactedFields.Length == 0)
        {
            return;
        }

        var impactedFieldSet = impactedFields.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var group in rules.Rules
            .Where(rule => rule.Enabled
                && !rule.Source.ClassCode.Equals(message.ClassCode, StringComparison.OrdinalIgnoreCase)
                && ReferencedFieldsForRule(rule).Overlaps(impactedFieldSet))
            .SelectMany(rule => DirtyScopeEntriesForRule(rule)
                .Select(scopeKey => new
                {
                    Layer = NormalizeDirtyScopeLayer(rule.Layer),
                    ScopeKey = scopeKey,
                    RuleId = rule.RuleId
                }))
            .Where(item => !string.IsNullOrWhiteSpace(item.Layer) && !string.IsNullOrWhiteSpace(item.ScopeKey))
            .GroupBy(item => item.Layer, StringComparer.Ordinal)
            .ToArray())
        {
            var reason = $"intermediate cmdbPath webhook: {message.ClassCode}/{message.CardId} can affect {string.Join(", ", impactedFields)}";
            await dirtyScopeClient.MarkPendingAsync(
                group.Key,
                group.Select(item => item.ScopeKey).Distinct(StringComparer.Ordinal).ToArray(),
                reason,
                cancellationToken);
        }
    }

    private async Task<IReadOnlyList<AggregationCommandPlan>> AttachServiceParentManagedKeysAsync(
        IReadOnlyList<AggregationCommandPlan> plans,
        ConversionRulesDocument rules,
        CancellationToken cancellationToken)
    {
        if (!plans.Any(plan => string.Equals(plan.Command.Layer, "service", StringComparison.OrdinalIgnoreCase)))
        {
            return plans;
        }

        var templateRelations = await LoadServiceObjectTemplateRelationsForStreamingAsync(cancellationToken);
        if (templateRelations.Count == 0)
        {
            return plans;
        }

        var templateIdByRuleId = rules.Rules
            .Where(rule => !string.IsNullOrWhiteSpace(rule.RuleId))
            .Select(rule => new
            {
                RuleId = rule.RuleId,
                TemplateId = FirstNonEmpty(rule.TemplateGeneration.TemplateId, rule.GeneratedFromTemplate)
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.TemplateId))
            .ToDictionary(item => item.RuleId, item => item.TemplateId, StringComparer.Ordinal);

        return plans
            .Select(plan => AttachServiceParentManagedKeys(plan, templateIdByRuleId, templateRelations))
            .ToArray();
    }

    private async Task<IReadOnlyList<ServiceObjectTemplateRelationIntent>> LoadServiceObjectTemplateRelationsForStreamingAsync(
        CancellationToken cancellationToken)
    {
        foreach (var candidate in ServiceTemplatePathCandidatesForStreaming())
        {
            var fullPath = Path.GetFullPath(candidate);
            if (!File.Exists(fullPath))
            {
                continue;
            }

            await using var stream = File.OpenRead(fullPath);
            var document = await JsonSerializer.DeserializeAsync<ServiceTemplateRelationsDocument>(
                stream,
                new JsonSerializerOptions(JsonSerializerDefaults.Web),
                cancellationToken);
            return document?.ServiceObjectTemplateRelations ?? [];
        }

        return [];
    }

    private IEnumerable<string> ServiceTemplatePathCandidatesForStreaming()
    {
        var options = conversionRulesOptions.Value;
        if (!string.IsNullOrWhiteSpace(options.ServiceTemplatesFilePath))
        {
            yield return options.ServiceTemplatesFilePath;
        }

        var rulePath = options.FilePath ?? "";
        if (!string.IsNullOrWhiteSpace(rulePath))
        {
            var ruleDirectory = Path.GetDirectoryName(rulePath);
            if (!string.IsNullOrWhiteSpace(ruleDirectory))
            {
                yield return Path.Combine(ruleDirectory, "service-templates.json");
            }
        }

        foreach (var basePath in CandidateBasePathsForStreaming())
        {
            if (!string.IsNullOrWhiteSpace(options.ServiceTemplatesFilePath)
                && !Path.IsPathRooted(options.ServiceTemplatesFilePath))
            {
                yield return Path.Combine(basePath, options.ServiceTemplatesFilePath);
            }

            yield return Path.Combine(basePath, "state/conversion-config/service-templates.json");
            yield return Path.Combine(basePath, "rules/service-templates.json");
        }
    }

    private IEnumerable<string> CandidateBasePathsForStreaming()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var start in new[] { environment.ContentRootPath, Environment.CurrentDirectory })
        {
            var directory = new DirectoryInfo(Path.GetFullPath(start));
            while (directory is not null)
            {
                if (seen.Add(directory.FullName))
                {
                    yield return directory.FullName;
                }

                directory = directory.Parent;
            }
        }
    }

    private static AggregationCommandPlan AttachServiceParentManagedKeys(
        AggregationCommandPlan plan,
        IReadOnlyDictionary<string, string> templateIdByRuleId,
        IReadOnlyList<ServiceObjectTemplateRelationIntent> templateRelations)
    {
        if (!string.Equals(plan.Command.Layer, "service", StringComparison.OrdinalIgnoreCase)
            || !templateIdByRuleId.TryGetValue(plan.Command.RuleId, out var templateId)
            || string.IsNullOrWhiteSpace(templateId))
        {
            return plan;
        }

        var parentKeys = templateRelations
            .Where(relation => relation.TargetType.Equals("service_template", StringComparison.OrdinalIgnoreCase)
                && relation.TargetTemplateId.Equals(templateId, StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(relation.SourceClassCode)
                && !string.IsNullOrWhiteSpace(relation.SourceCardId)
                && (string.IsNullOrWhiteSpace(relation.TargetClassCode)
                    || relation.TargetClassCode.Equals(plan.Command.Target.ClassCode, StringComparison.Ordinal)))
            .Select(relation => CardRefKeyForStreaming(relation.SourceClassCode, relation.SourceCardId))
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (parentKeys.Length == 0)
        {
            return plan;
        }

        var target = plan.Command.Target with
        {
            ParentManagedKeys = plan.Command.Target.ParentManagedKeys
                .Concat(parentKeys)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(key => key, StringComparer.Ordinal)
                .ToArray()
        };
        return plan with { Command = plan.Command with { Target = target } };
    }

    private static string CardRefKeyForStreaming(string classCode, string cardId)
    {
        return string.IsNullOrWhiteSpace(classCode) || string.IsNullOrWhiteSpace(cardId)
            ? ""
            : $"cmdbuild:{classCode}:{cardId}";
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return "";
    }

    private static async Task<IReadOnlyList<string>> PublishAggregationPlanAsync(
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

        return topics;
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

    private static HashSet<string> ReferencedFieldsForRule(ConversionRule rule)
    {
        var fields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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

        return fields;
    }

    private static bool CmdbPathContainsIntermediateClass(string? cmdbPath, string classCode)
    {
        if (string.IsNullOrWhiteSpace(cmdbPath) || string.IsNullOrWhiteSpace(classCode))
        {
            return false;
        }

        var parts = cmdbPath.Split('.', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return parts.Skip(1).Any(part => part.Equals(classCode, StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<string> DirtyScopeEntriesForRule(ConversionRule rule)
    {
        var keys = new List<string>();
        AddStaticScopeKey(keys, rule.Target.CardId);
        AddStaticScopeKey(keys, rule.Target.IdempotencyKey);
        if (!string.IsNullOrWhiteSpace(rule.Target.CardId) && !string.IsNullOrWhiteSpace(rule.Target.ClassCode))
        {
            AddStaticScopeKey(keys, $"cmdbuild:{rule.Target.ClassCode.Trim()}:{rule.Target.CardId.Trim()}");
        }

        if (rule.Target.AttributeMappings.TryGetValue("population_source_key", out var mappedPopulationKey))
        {
            AddStaticScopeKey(keys, mappedPopulationKey);
        }

        if (rule.Target.InitialUserValues.TryGetValue("population_source_key", out var initialPopulationKey))
        {
            AddStaticScopeKey(keys, initialPopulationKey);
        }

        AddStaticScopeKey(keys, rule.RuleId);
        AddStaticScopeKey(keys, rule.Name);
        return keys
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static void AddStaticScopeKey(ICollection<string> keys, string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Contains("${source.", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        keys.Add(value.Trim());
    }

    private static string NormalizeDirtyScopeLayer(string layer)
    {
        if (layer.Equals("service", StringComparison.OrdinalIgnoreCase))
        {
            return "service";
        }

        if (layer.Equals("suppression", StringComparison.OrdinalIgnoreCase))
        {
            return "suppression";
        }

        return "";
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

public sealed class SemanticCommandDeduplicator(
    IOptionsMonitor<SemanticDeduplicationOptions> options,
    IOptionsMonitor<RuntimeRedisOptions> redisOptions,
    ILogger<SemanticCommandDeduplicator> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
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

        if (redisOptions.CurrentValue.Enabled)
        {
            try
            {
                return IsRedisDuplicate(plan, currentOptions, redisOptions.CurrentValue, out duplicateAge);
            }
            catch (Exception ex) when (ex is SocketException or IOException or InvalidOperationException or TimeoutException)
            {
                if (string.Equals(redisOptions.CurrentValue.FailureMode, "fail", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"Redis semantic deduplication is unavailable: {ex.Message}", ex);
                }

                logger.LogWarning(ex, "Redis semantic deduplication failed; falling back to in-memory dedup for key {SemanticKey}.", plan.SemanticKey);
            }
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

        if (redisOptions.CurrentValue.Enabled)
        {
            try
            {
                MarkRedisPublished(plan, currentOptions, redisOptions.CurrentValue);
                return;
            }
            catch (Exception ex) when (ex is SocketException or IOException or InvalidOperationException or TimeoutException)
            {
                if (string.Equals(redisOptions.CurrentValue.FailureMode, "fail", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"Redis semantic deduplication is unavailable: {ex.Message}", ex);
                }

                logger.LogWarning(ex, "Redis semantic deduplication publish mark failed; falling back to in-memory dedup for key {SemanticKey}.", plan.SemanticKey);
            }
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

    private bool IsRedisDuplicate(
        AggregationCommandPlan plan,
        SemanticDeduplicationOptions currentOptions,
        RuntimeRedisOptions redis,
        out TimeSpan? duplicateAge)
    {
        duplicateAge = null;
        using var client = RedisRespClient.Connect(redis);
        var now = DateTimeOffset.UtcNow;
        var window = TimeSpan.FromSeconds(currentOptions.WindowSeconds);
        var key = SemanticDeduplicationRedisKey(redis, plan.SemanticKey);
        var json = client.ExecuteBulkString("GET", key);
        if (TryReadSemanticDeduplicationEntry(json, out var existing)
            && existing.Fingerprint.Equals(plan.SemanticFingerprint, StringComparison.Ordinal)
            && now - existing.LastSeenAtUtc <= window)
        {
            duplicateAge = now - existing.LastPublishedAtUtc;
            var next = existing with { LastSeenAtUtc = now };
            client.ExecuteBulkString("SET", key, JsonSerializer.Serialize(next, JsonOptions), "EX", currentOptions.WindowSeconds.ToString(CultureInfo.InvariantCulture));
            return true;
        }

        if (!string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        var reservationTtlSeconds = Math.Clamp(currentOptions.WindowSeconds, 5, 60);
        var reservation = new SemanticDeduplicationEntry(plan.SemanticFingerprint, now)
        {
            LastSeenAtUtc = now,
            Pending = true
        };
        var reserved = client.ExecuteBulkString(
            "SET",
            key,
            JsonSerializer.Serialize(reservation, JsonOptions),
            "EX",
            reservationTtlSeconds.ToString(CultureInfo.InvariantCulture),
            "NX");
        if (string.Equals(reserved, "OK", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var racedJson = client.ExecuteBulkString("GET", key);
        if (TryReadSemanticDeduplicationEntry(racedJson, out var raced)
            && raced.Fingerprint.Equals(plan.SemanticFingerprint, StringComparison.Ordinal)
            && now - raced.LastSeenAtUtc <= window)
        {
            duplicateAge = now - raced.LastPublishedAtUtc;
            return true;
        }

        return false;
    }

    private static void MarkRedisPublished(
        AggregationCommandPlan plan,
        SemanticDeduplicationOptions currentOptions,
        RuntimeRedisOptions redis)
    {
        using var client = RedisRespClient.Connect(redis);
        var now = DateTimeOffset.UtcNow;
        var key = SemanticDeduplicationRedisKey(redis, plan.SemanticKey);
        var entry = new SemanticDeduplicationEntry(plan.SemanticFingerprint, now);
        client.ExecuteBulkString("SET", key, JsonSerializer.Serialize(entry, JsonOptions), "EX", currentOptions.WindowSeconds.ToString(CultureInfo.InvariantCulture));
    }

    private static string SemanticDeduplicationRedisKey(RuntimeRedisOptions redis, string semanticKey)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(semanticKey))).ToLowerInvariant();
        return $"{NormalizeRedisPrefix(redis.KeyPrefix)}:semantic-dedup:{hash}";
    }

    private static string NormalizeRedisPrefix(string prefix)
    {
        return string.IsNullOrWhiteSpace(prefix) ? "cmdb2m:test" : prefix.Trim().TrimEnd(':');
    }

    private static bool TryReadSemanticDeduplicationEntry(
        string? json,
        out SemanticDeduplicationEntry entry)
    {
        entry = default!;
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<SemanticDeduplicationEntry>(json, JsonOptions);
            if (parsed is null)
            {
                return false;
            }

            entry = parsed;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}

public sealed record SemanticDeduplicationEntry(
    string Fingerprint,
    DateTimeOffset LastPublishedAtUtc)
{
    public DateTimeOffset LastSeenAtUtc { get; init; } = LastPublishedAtUtc;

    public bool Pending { get; init; }
}

public sealed class RuntimeRedisOptions
{
    public const string SectionName = "Redis";

    public bool Enabled { get; init; }

    public string ConnectionString { get; init; } = "";

    public string KeyPrefix { get; init; } = "cmdb2m:test";

    public int OperationTtlSeconds { get; init; } = 86400;

    public string FailureMode { get; init; } = "fallback";

    public bool HasValidFailureMode()
    {
        return string.Equals(FailureMode, "fallback", StringComparison.OrdinalIgnoreCase)
            || string.Equals(FailureMode, "fail", StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class ZabbixDirtyScopeOptions
{
    public const string SectionName = "ZabbixDirtyScopes";

    public bool Enabled { get; init; } = true;

    public string Endpoint { get; init; } = "";

    public int TimeoutMs { get; init; } = 3000;

    public bool HasValidEndpoint()
    {
        return Uri.TryCreate(Endpoint, UriKind.Absolute, out _);
    }
}

public sealed class ZabbixDirtyScopeClient(
    HttpClient httpClient,
    IOptionsMonitor<ZabbixDirtyScopeOptions> options,
    ILogger<ZabbixDirtyScopeClient> logger)
{
    public async Task MarkPendingIfZabbixPublishedAsync(
        AggregationCommand command,
        IReadOnlyList<string> publishedTopics,
        string reason,
        CancellationToken cancellationToken)
    {
        if (!publishedTopics.Any(IsZabbixTopic))
        {
            return;
        }

        var current = options.CurrentValue;
        if (!current.Enabled || string.IsNullOrWhiteSpace(current.Endpoint))
        {
            return;
        }

        var layer = NormalizeLayer(command.Layer);
        var scopeKey = DirtyScopeKey(command);
        if (string.IsNullOrWhiteSpace(layer) || string.IsNullOrWhiteSpace(scopeKey))
        {
            return;
        }

        await MarkPendingAsync(layer, [scopeKey], reason, cancellationToken);
    }

    public async Task MarkPendingAsync(
        string layer,
        IEnumerable<string> scopeKeys,
        string reason,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var current = options.CurrentValue;
        if (!current.Enabled || string.IsNullOrWhiteSpace(current.Endpoint))
        {
            return;
        }

        var normalizedLayer = NormalizeLayer(layer);
        var keys = scopeKeys
            .Select(item => (item ?? "").Trim())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (string.IsNullOrWhiteSpace(normalizedLayer) || keys.Length == 0)
        {
            return;
        }

        timeout.CancelAfter(TimeSpan.FromMilliseconds(Math.Max(100, current.TimeoutMs)));
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, current.Endpoint)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(new
                    {
                        layer = normalizedLayer,
                        reason,
                        entries = keys.Select(scopeKey => new
                        {
                            scopeType = "target",
                            scopeKey,
                            reason,
                            status = "pending"
                        }).ToArray()
                    }),
                    Encoding.UTF8,
                    "application/json")
            };
            request.Headers.Accept.ParseAdd("application/json");
            using var response = await httpClient.SendAsync(request, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(timeout.Token);
                logger.LogWarning(
                    "Failed to mark Zabbix dirty scopes {Layer}/{Count}: HTTP {StatusCode}: {Body}",
                    normalizedLayer,
                    keys.Length,
                    (int)response.StatusCode,
                    TrimForLog(body));
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            logger.LogWarning(ex, "Failed to mark Zabbix dirty scopes {Layer}/{Count}.", normalizedLayer, keys.Length);
        }
    }

    private static bool IsZabbixTopic(string topic)
    {
        return topic.Contains(".zabbix.", StringComparison.OrdinalIgnoreCase)
            || topic.Equals("zabbix-direct", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeLayer(string layer)
    {
        if (layer.Equals("service", StringComparison.OrdinalIgnoreCase))
        {
            return "service";
        }

        if (layer.Equals("suppression", StringComparison.OrdinalIgnoreCase))
        {
            return "suppression";
        }

        return "";
    }

    private static string DirtyScopeKey(AggregationCommand command)
    {
        return string.IsNullOrWhiteSpace(command.Target.CardId)
            ? command.Target.IdempotencyKey.Trim()
            : command.Target.CardId.Trim();
    }

    private static string TrimForLog(string value, int maxLength = 500)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        return value.Length <= maxLength ? value : value[..maxLength] + "...";
    }
}

public sealed class RedisRespClient : IDisposable
{
    private readonly TcpClient tcpClient;
    private readonly NetworkStream stream;

    private RedisRespClient(TcpClient tcpClient)
    {
        this.tcpClient = tcpClient;
        stream = tcpClient.GetStream();
    }

    public static RedisRespClient Connect(RuntimeRedisOptions options)
    {
        var endpoint = RedisEndpoint.Parse(options.ConnectionString);
        var client = new TcpClient
        {
            ReceiveTimeout = 3000,
            SendTimeout = 3000
        };
        client.Connect(endpoint.Host, endpoint.Port);
        var redis = new RedisRespClient(client);
        if (!string.IsNullOrWhiteSpace(endpoint.Password))
        {
            if (!string.IsNullOrWhiteSpace(endpoint.UserName))
            {
                redis.ExecuteBulkString("AUTH", endpoint.UserName, endpoint.Password);
            }
            else
            {
                redis.ExecuteBulkString("AUTH", endpoint.Password);
            }
        }

        if (endpoint.Database > 0)
        {
            redis.ExecuteBulkString("SELECT", endpoint.Database.ToString(CultureInfo.InvariantCulture));
        }

        return redis;
    }

    public void Ping()
    {
        var response = ExecuteBulkString("PING");
        if (!string.Equals(response, "PONG", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Redis PING returned an unexpected response.");
        }
    }

    public string? ExecuteBulkString(params string[] args)
    {
        WriteCommand(args);
        return ReadValue() switch
        {
            null => null,
            string text => text,
            long number => number.ToString(CultureInfo.InvariantCulture),
            string[] values => string.Join("\n", values),
            var other => other.ToString()
        };
    }

    private void WriteCommand(IReadOnlyList<string> args)
    {
        var builder = new StringBuilder();
        builder.Append('*').Append(args.Count).Append("\r\n");
        foreach (var arg in args)
        {
            var text = arg ?? "";
            var bytes = Encoding.UTF8.GetBytes(text);
            builder.Append('$').Append(bytes.Length).Append("\r\n");
            builder.Append(text).Append("\r\n");
        }

        var payload = Encoding.UTF8.GetBytes(builder.ToString());
        stream.Write(payload, 0, payload.Length);
        stream.Flush();
    }

    private object? ReadValue()
    {
        var prefix = stream.ReadByte();
        if (prefix < 0)
        {
            throw new IOException("Redis closed the connection.");
        }

        return (char)prefix switch
        {
            '+' => ReadLine(),
            '-' => throw new InvalidOperationException(ReadLine()),
            ':' => long.Parse(ReadLine(), CultureInfo.InvariantCulture),
            '$' => ReadBulkString(),
            '*' => ReadArray(),
            _ => throw new InvalidOperationException($"Unsupported Redis response prefix: {(char)prefix}")
        };
    }

    private string? ReadBulkString()
    {
        var length = int.Parse(ReadLine(), CultureInfo.InvariantCulture);
        if (length < 0)
        {
            return null;
        }

        var buffer = new byte[length];
        ReadExact(buffer);
        ExpectCrLf();
        return Encoding.UTF8.GetString(buffer);
    }

    private string[] ReadArray()
    {
        var length = int.Parse(ReadLine(), CultureInfo.InvariantCulture);
        if (length < 0)
        {
            return [];
        }

        var values = new List<string>();
        for (var index = 0; index < length; index++)
        {
            var value = ReadValue();
            values.Add(value switch
            {
                null => "",
                string text => text,
                long number => number.ToString(CultureInfo.InvariantCulture),
                _ => value.ToString() ?? ""
            });
        }

        return values.ToArray();
    }

    private string ReadLine()
    {
        var bytes = new List<byte>();
        while (true)
        {
            var value = stream.ReadByte();
            if (value < 0)
            {
                throw new IOException("Redis closed the connection.");
            }

            if (value == '\r')
            {
                if (stream.ReadByte() != '\n')
                {
                    throw new IOException("Invalid Redis line terminator.");
                }

                return Encoding.UTF8.GetString(bytes.ToArray());
            }

            bytes.Add((byte)value);
        }
    }

    private void ReadExact(byte[] buffer)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = stream.Read(buffer, offset, buffer.Length - offset);
            if (read <= 0)
            {
                throw new IOException("Redis closed the connection.");
            }

            offset += read;
        }
    }

    private void ExpectCrLf()
    {
        if (stream.ReadByte() != '\r' || stream.ReadByte() != '\n')
        {
            throw new IOException("Invalid Redis bulk string terminator.");
        }
    }

    public void Dispose()
    {
        stream.Dispose();
        tcpClient.Dispose();
    }
}

public sealed record RedisEndpoint(string Host, int Port, string UserName, string Password, int Database)
{
    public static RedisEndpoint Parse(string connectionString)
    {
        var text = connectionString.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException("Redis connection string is empty.");
        }

        if (Uri.TryCreate(text, UriKind.Absolute, out var uri) && uri.Scheme.StartsWith("redis", StringComparison.OrdinalIgnoreCase))
        {
            var userInfo = Uri.UnescapeDataString(uri.UserInfo ?? "");
            var userParts = userInfo.Split(':', 2);
            var dbText = uri.AbsolutePath.Trim('/');
            return new RedisEndpoint(
                uri.Host,
                uri.Port > 0 ? uri.Port : 6379,
                userParts.Length == 2 ? userParts[0] : "",
                userParts.Length == 2 ? userParts[1] : (userParts.Length == 1 ? userParts[0] : ""),
                int.TryParse(dbText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var uriDatabase) ? uriDatabase : 0);
        }

        var parts = text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var hostPart = parts.FirstOrDefault(part => !part.Contains('=', StringComparison.Ordinal)) ?? "127.0.0.1:6379";
        var hostPieces = hostPart.Split(':', 2);
        var values = parts
            .Where(part => part.Contains('=', StringComparison.Ordinal))
            .Select(part => part.Split('=', 2))
            .ToDictionary(part => part[0].Trim(), part => part[1].Trim(), StringComparer.OrdinalIgnoreCase);
        return new RedisEndpoint(
            hostPieces[0],
            hostPieces.Length == 2 && int.TryParse(hostPieces[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var port) ? port : 6379,
            values.GetValueOrDefault("user") ?? values.GetValueOrDefault("username") ?? "",
            values.GetValueOrDefault("password") ?? "",
            int.TryParse(values.GetValueOrDefault("defaultDatabase") ?? values.GetValueOrDefault("database"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var configuredDatabase) ? configuredDatabase : 0);
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
