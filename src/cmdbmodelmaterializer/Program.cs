using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Cmdb2MonitoringServiceSuppression.Shared.Aggregation;
using Cmdb2MonitoringServiceSuppression.Shared.Configuration;
using Cmdb2MonitoringServiceSuppression.Shared.Messaging;
using Cmdb2MonitoringServiceSuppression.Shared.Secrets;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);
await builder.Configuration.ResolveSecretReferencesAsync("cmdbmodelmaterializer");
builder.AddServiceDefaults();

builder.Services.AddOptions<KafkaTopicsOptions>()
    .Bind(builder.Configuration.GetSection(KafkaTopicsOptions.SectionName))
    .Validate(options => !string.IsNullOrWhiteSpace(options.CmdbModelMissingDimensions), "CMDB model missing-dimensions topic is required.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.DeadLetterTopic), "KafkaTopics:DeadLetterTopic is required.")
    .ValidateOnStart();
builder.Services.AddOptions<ConversionConfigStoreClientOptions>()
    .Bind(builder.Configuration.GetSection(ConversionConfigStoreClientOptions.SectionName))
    .Validate(options => Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out _), "ConversionConfigStore:BaseUrl must be an absolute URL.")
    .Validate(options => options.CurrentPath.StartsWith('/'), "ConversionConfigStore:CurrentPath must start with '/'.")
    .Validate(options => options.DeployPath.StartsWith('/'), "ConversionConfigStore:DeployPath must start with '/'.")
    .Validate(options => options.TimeoutMs > 0, "ConversionConfigStore:TimeoutMs must be greater than zero.")
    .ValidateOnStart();
builder.Services.AddOptions<MaterializerOptions>()
    .Bind(builder.Configuration.GetSection(MaterializerOptions.SectionName))
    .Validate(options => options.DuplicateTtlSeconds > 0, "Materializer:DuplicateTtlSeconds must be greater than zero.")
    .Validate(options => options.MaxWriteAttempts > 0, "Materializer:MaxWriteAttempts must be greater than zero.")
    .Validate(options => options.ReloadTargets.All(target => !target.Enabled || Uri.TryCreate(target.Url, UriKind.Absolute, out _)), "Materializer reload target URLs must be absolute.")
    .ValidateOnStart();
builder.Services.AddOptions<ReplayOptions>()
    .Bind(builder.Configuration.GetSection(ReplayOptions.SectionName))
    .Validate(options => !options.Enabled || Uri.TryCreate(options.ReprocessUrl, UriKind.Absolute, out _), "Replay:ReprocessUrl must be an absolute URL when Replay:Enabled=true.")
    .Validate(options => options.TimeoutMs > 0, "Replay:TimeoutMs must be greater than zero.")
    .Validate(options => options.MaxBackfillCards > 0, "Replay:MaxBackfillCards must be greater than zero.")
    .ValidateOnStart();
builder.Services.AddOptions<GraphOverlayOptions>()
    .Bind(builder.Configuration.GetSection(GraphOverlayOptions.SectionName))
    .Validate(options => !options.Enabled || Uri.TryCreate(options.ApplyCurrentUrl, UriKind.Absolute, out _), "GraphOverlay:ApplyCurrentUrl must be an absolute URL when GraphOverlay:Enabled=true.")
    .Validate(options => !options.Enabled || !options.UsesDirectTarget() || Uri.TryCreate(options.ZabbixCommandApplyUrl, UriKind.Absolute, out _), "GraphOverlay:ZabbixCommandApplyUrl must be an absolute URL when zabbix-direct is used.")
    .Validate(options => options.Targets.All(GraphOverlayOptions.IsValidTarget), "GraphOverlay:Targets may contain only zabbix or zabbix-direct.")
    .Validate(options => string.Equals(options.PublishMode, "changes", StringComparison.OrdinalIgnoreCase) || string.Equals(options.PublishMode, "full", StringComparison.OrdinalIgnoreCase), "GraphOverlay:PublishMode must be changes or full.")
    .Validate(options => GraphOverlayOptions.IsValidTopologyReadMode(options.TopologyReadMode), "GraphOverlay:TopologyReadMode must be auto, rules, or full.")
    .Validate(options => options.ScopeDepth >= 0, "GraphOverlay:ScopeDepth must be zero or greater.")
    .Validate(options => options.TimeoutMs > 0, "GraphOverlay:TimeoutMs must be greater than zero.")
    .ValidateOnStart();

builder.Services.AddHttpClient<ConversionConfigStoreClient>((provider, client) =>
{
    var options = provider.GetRequiredService<IOptions<ConversionConfigStoreClientOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl);
    client.Timeout = TimeSpan.FromMilliseconds(options.TimeoutMs);
});
builder.Services.AddSingleton<MaterializationCoordinator>();
builder.Services.AddSingleton<KafkaJsonProducer>();

var initialMaterializerOptions = builder.Configuration
    .GetSection(MaterializerOptions.SectionName)
    .Get<MaterializerOptions>() ?? new MaterializerOptions();
if (initialMaterializerOptions.Enabled)
{
    builder.Services.AddHostedService<CmdbModelMissingDimensionWorker>();
}

var app = builder.Build();
app.UseServiceDefaults();
if (!initialMaterializerOptions.Enabled)
{
    app.Logger.LogInformation("CMDB model materializer Kafka consumer is not started because Materializer:Enabled is false.");
}

app.MapServiceHealth();
app.MapConfigurationReload(builder.Configuration);

app.MapGet("/materializer/status", (
    MaterializationCoordinator coordinator,
    IOptionsMonitor<MaterializerOptions> options,
    IOptionsMonitor<GraphOverlayOptions> graphOverlayOptions) =>
{
    var current = options.CurrentValue;
    var graphOverlay = graphOverlayOptions.CurrentValue;
    return Results.Ok(new
    {
        enabled = current.Enabled,
        duplicateTtlSeconds = current.DuplicateTtlSeconds,
        maxWriteAttempts = current.MaxWriteAttempts,
        reloadAppliersOnSave = current.ReloadAppliersOnSave,
        graphOverlay = new
        {
            enabled = graphOverlay.Enabled,
            applyCurrentUrl = graphOverlay.ApplyCurrentUrl,
            targets = graphOverlay.EffectiveTargets(),
            publishMode = graphOverlay.PublishMode,
            scopeDepth = graphOverlay.ScopeDepth,
            requireScopeMatch = graphOverlay.RequireScopeMatch,
            dryRun = graphOverlay.DryRun
        },
        recentJobs = coordinator.RecentJobs()
    });
});

app.MapPost("/materializer/process", async (
    CmdbModelMissingDimensionRequest request,
    MaterializationCoordinator coordinator,
    CancellationToken cancellationToken) =>
{
    var result = await coordinator.MaterializeAsync(request, request.IdempotencyKey, cancellationToken);
    return Results.Ok(result);
});

app.Run();

public sealed class ConversionConfigStoreClientOptions
{
    public const string SectionName = "ConversionConfigStore";

    public string BaseUrl { get; init; } = "http://127.0.0.1:8091";

    public string CurrentPath { get; init; } = "/api/conversion-config-store/current";

    public string DeployPath { get; init; } = "/api/conversion-config-store/deploy";

    public int TimeoutMs { get; init; } = 10000;
}

public sealed class MaterializerOptions
{
    public const string SectionName = "Materializer";

    public bool Enabled { get; init; } = true;

    public int DuplicateTtlSeconds { get; init; } = 3600;

    public int MaxWriteAttempts { get; init; } = 3;

    public bool ReloadAppliersOnSave { get; init; } = true;

    public IReadOnlyList<ReloadTargetOptions> ReloadTargets { get; init; } = [];
}

public sealed class ReloadTargetOptions
{
    public string Name { get; init; } = "";

    public string Url { get; init; } = "";

    public string BearerToken { get; init; } = "";

    public string BearerTokenSecret { get; init; } = "";

    public bool Enabled { get; init; } = true;
}

public sealed class ReplayOptions
{
    public const string SectionName = "Replay";

    public bool Enabled { get; init; } = true;

    public string ReprocessUrl { get; init; } = "http://127.0.0.1:5182/rules/reprocess-card";

    public bool BackfillDimensionOnSave { get; init; }

    public int MaxBackfillCards { get; init; } = 1000;

    public int TimeoutMs { get; init; } = 30000;
}

public sealed class GraphOverlayOptions
{
    public const string SectionName = "GraphOverlay";

    public bool Enabled { get; init; }

    public string ApplyCurrentUrl { get; init; } = "http://127.0.0.1:5182/rules/apply-current";

    public IReadOnlyList<string> Targets { get; init; } = ["zabbix-direct"];

    public string CmdbuildPrefix { get; init; } = "C2M_";

    public string ServiceModelRoot { get; init; } = "";

    public string SuppressionModelRoot { get; init; } = "";

    public string ZabbixCommandApplyUrl { get; init; } = "http://127.0.0.1:5183/commands/apply-graph";

    public string PublishMode { get; init; } = "changes";

    public string TopologyReadMode { get; init; } = "rules";

    public int ScopeDepth { get; init; }

    public bool RequireScopeMatch { get; init; }

    public bool DryRun { get; init; }

    public int TimeoutMs { get; init; } = 60000;

    public bool UsesDirectTarget()
    {
        var normalized = Targets
            .Select(NormalizeTarget)
            .Where(target => !string.IsNullOrWhiteSpace(target))
            .ToArray();
        return normalized.Length == 0
            ? !string.IsNullOrWhiteSpace(ZabbixCommandApplyUrl)
            : normalized.Any(IsDirectTarget);
    }

    public IReadOnlyList<string> EffectiveTargets()
    {
        var normalized = Targets
            .Select(NormalizeTarget)
            .Where(target => !string.IsNullOrWhiteSpace(target))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (normalized.Length > 0)
        {
            return normalized;
        }

        return string.IsNullOrWhiteSpace(ZabbixCommandApplyUrl)
            ? ["zabbix"]
            : ["zabbix-direct"];
    }

    public static bool IsValidTarget(string? value)
    {
        var normalized = NormalizeTarget(value);
        return string.IsNullOrWhiteSpace(normalized)
            || string.Equals(normalized, "zabbix", StringComparison.OrdinalIgnoreCase)
            || IsDirectTarget(normalized);
    }

    public static bool IsValidTopologyReadMode(string? value)
    {
        var normalized = (value ?? "").Trim().Replace('_', '-').ToLowerInvariant();
        return string.IsNullOrWhiteSpace(normalized)
            || normalized is "auto" or "rules" or "rule" or "scoped" or "scope" or "runtime-rules" or "full" or "cmdbuild" or "cmdbuild-full" or "legacy-full";
    }

    private static bool IsDirectTarget(string? value)
    {
        return string.Equals(value, "zabbix-direct", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeTarget(string? value)
    {
        return (value ?? "").Trim().Replace('_', '-').ToLowerInvariant();
    }
}

public sealed class CmdbModelMissingDimensionWorker(
    IOptions<KafkaTopicsOptions> topicOptions,
    IOptions<KafkaOptions> kafkaOptions,
    MaterializationCoordinator coordinator,
    IServiceProvider services,
    ILogger<CmdbModelMissingDimensionWorker> logger)
    : KafkaJsonConsumerWorker<CmdbModelMissingDimensionRequest>(kafkaOptions, services, logger)
{
    protected override string Topic => topicOptions.Value.CmdbModelMissingDimensions;

    protected override string ConsumerGroupId => "";

    protected override Task HandleMessageAsync(
        CmdbModelMissingDimensionRequest message,
        string key,
        CancellationToken cancellationToken)
    {
        return coordinator.MaterializeAsync(message, key, cancellationToken);
    }
}

public sealed class ConversionConfigStoreClient(
    HttpClient httpClient,
    IOptionsMonitor<ConversionConfigStoreClientOptions> options)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    public async Task<ConversionConfigSnapshot> ReadCurrentAsync(CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(options.CurrentValue.CurrentPath, cancellationToken);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var node = await JsonNode.ParseAsync(stream, cancellationToken: cancellationToken) as JsonObject
            ?? throw new InvalidOperationException("conversion-config-store returned an empty response.");
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"conversion-config-store current failed with HTTP {(int)response.StatusCode}: {JsonText.StringValue(node["error"])}");
        }

        return new ConversionConfigSnapshot(
            node,
            JsonText.LongValue(node["version"]),
            JsonText.StringValue(node["etag"]));
    }

    public async Task<ConversionConfigWriteResult> DeployAsync(JsonObject payload, CancellationToken cancellationToken)
    {
        using var content = JsonContent.Create(payload, options: JsonOptions);
        using var response = await httpClient.PostAsync(options.CurrentValue.DeployPath, content, cancellationToken);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var node = await JsonNode.ParseAsync(stream, cancellationToken: cancellationToken) as JsonObject ?? new JsonObject();
        var success = response.IsSuccessStatusCode && JsonText.BoolValue(node["success"]);
        return new ConversionConfigWriteResult(
            success,
            (int)response.StatusCode,
            JsonText.StringValue(node["error"]),
            JsonText.StringValue(node["message"]),
            JsonText.LongValue(node["version"]),
            JsonText.StringValue(node["etag"]),
            node);
    }
}

public sealed class MaterializationCoordinator(
    ConversionConfigStoreClient storeClient,
    IHttpClientFactory httpClientFactory,
    IOptionsMonitor<MaterializerOptions> options,
    IOptionsMonitor<ReplayOptions> replayOptions,
    IOptionsMonitor<GraphOverlayOptions> graphOverlayOptions,
    ILogger<MaterializationCoordinator> logger)
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> locks = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, DateTimeOffset> completed = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<MaterializationJobSnapshot> recentJobs = new();

    public IReadOnlyList<MaterializationJobSnapshot> RecentJobs()
    {
        return recentJobs.ToArray().Reverse().Take(50).ToArray();
    }

    public async Task<MaterializationJobSnapshot> MaterializeAsync(
        CmdbModelMissingDimensionRequest request,
        string kafkaKey,
        CancellationToken cancellationToken)
    {
        var key = MaterializationKey(request, kafkaKey);
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException("Missing dimension request does not contain layer/templateId/dimensionKey.");
        }

        CleanupCompleted(options.CurrentValue);
        if (completed.TryGetValue(key, out var completedAt)
            && DateTimeOffset.UtcNow - completedAt < TimeSpan.FromSeconds(options.CurrentValue.DuplicateTtlSeconds))
        {
            return RecordJob(new MaterializationJobSnapshot(
                key,
                "duplicate",
                request,
                JsonText.NormalizeLayer(request.Layer),
                request.TemplateId,
                request.DimensionKey,
                "",
                0,
                0,
                0,
                [],
                [],
                [],
                [],
                completedAt,
                DateTimeOffset.UtcNow));
        }

        var gate = locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (completed.TryGetValue(key, out completedAt)
                && DateTimeOffset.UtcNow - completedAt < TimeSpan.FromSeconds(options.CurrentValue.DuplicateTtlSeconds))
            {
                return RecordJob(new MaterializationJobSnapshot(
                    key,
                    "duplicate",
                    request,
                    JsonText.NormalizeLayer(request.Layer),
                    request.TemplateId,
                    request.DimensionKey,
                    "",
                    0,
                    0,
                    0,
                    [],
                    [],
                    [],
                    [],
                    completedAt,
                    DateTimeOffset.UtcNow));
            }

            var startedAt = DateTimeOffset.UtcNow;
            for (var attempt = 1; attempt <= options.CurrentValue.MaxWriteAttempts; attempt++)
            {
                var snapshot = await storeClient.ReadCurrentAsync(cancellationToken);
                var edit = ConversionConfigMaterializer.Materialize(snapshot.Root, request);
                if (!edit.Changed)
                {
                    var replayExisting = ShouldReplayAfterMaterialization(edit);
                    var replays = replayExisting
                        ? await ReplaySourceCardAsync(request, cancellationToken)
                        : [];
                    var overlays = replayExisting
                        ? await RunGraphOverlayAsync(request, edit, cancellationToken)
                        : [];
                    completed[key] = DateTimeOffset.UtcNow;
                    return RecordJob(new MaterializationJobSnapshot(
                        key,
                        edit.Status,
                        request,
                        edit.Layer,
                        request.TemplateId,
                        request.DimensionKey,
                        edit.RuleId,
                        edit.CreatedRules,
                        edit.UpdatedRules,
                        edit.UpdatedRelations,
                        edit.Warnings,
                        replays,
                        overlays,
                        [],
                        startedAt,
                        DateTimeOffset.UtcNow));
                }

                var payload = edit.Payload ?? throw new InvalidOperationException("Materializer edit did not return a payload.");
                payload["baseVersion"] = snapshot.Version;
                payload["baseEtag"] = snapshot.Etag;
                payload["actor"] = "cmdbmodelmaterializer";
                payload["changeType"] = "missing_dimension_materialization";
                payload["reason"] = $"{key}: {request.Reason}".TrimEnd(' ', ':');

                var write = await storeClient.DeployAsync(payload, cancellationToken);
                if (write.Success)
                {
                    var reloads = await ReloadAppliersAsync(cancellationToken);
                    var replays = await ReplaySourceCardAsync(request, cancellationToken);
                    var overlays = await RunGraphOverlayAsync(request, edit, cancellationToken);
                    completed[key] = DateTimeOffset.UtcNow;
                    logger.LogInformation(
                        "Materialized missing CMDB model dimension {Key}: created {CreatedRules}, updated {UpdatedRules}, relation updates {UpdatedRelations}.",
                        key,
                        edit.CreatedRules,
                        edit.UpdatedRules,
                        edit.UpdatedRelations);
                    return RecordJob(new MaterializationJobSnapshot(
                        key,
                        "saved",
                        request,
                        edit.Layer,
                        request.TemplateId,
                        request.DimensionKey,
                        edit.RuleId,
                        edit.CreatedRules,
                        edit.UpdatedRules,
                        edit.UpdatedRelations,
                        edit.Warnings,
                        replays,
                        overlays,
                        reloads,
                        startedAt,
                        DateTimeOffset.UtcNow));
                }

                if (write.StatusCode == StatusCodes.Status409Conflict && attempt < options.CurrentValue.MaxWriteAttempts)
                {
                    logger.LogInformation(
                        "conversion-config-store conflict while materializing {Key}; retrying attempt {Attempt}/{MaxAttempts}.",
                        key,
                        attempt + 1,
                        options.CurrentValue.MaxWriteAttempts);
                    continue;
                }

                throw new InvalidOperationException(
                    $"conversion-config-store deploy failed with HTTP {write.StatusCode}: {FirstNonEmpty(write.Message, write.Error, "unknown error")}");
            }

            throw new InvalidOperationException($"conversion-config-store deploy conflicted for {key} after {options.CurrentValue.MaxWriteAttempts} attempts.");
        }
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException or TaskCanceledException)
        {
            logger.LogError(ex, "CMDB model materialization failed for {Key}.", key);
            RecordJob(new MaterializationJobSnapshot(
                key,
                "failed",
                request,
                JsonText.NormalizeLayer(request.Layer),
                request.TemplateId,
                request.DimensionKey,
                "",
                0,
                0,
                0,
                [ex.Message],
                [],
                [],
                [],
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow));
            throw;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<IReadOnlyList<ReloadResult>> ReloadAppliersAsync(CancellationToken cancellationToken)
    {
        var current = options.CurrentValue;
        if (!current.ReloadAppliersOnSave)
        {
            return [];
        }

        var results = new List<ReloadResult>();
        foreach (var target in current.ReloadTargets.Where(item => item.Enabled && !string.IsNullOrWhiteSpace(item.Url)))
        {
            try
            {
                var client = httpClientFactory.CreateClient();
                using var request = new HttpRequestMessage(HttpMethod.Post, target.Url);
                var bearer = FirstNonEmpty(target.BearerToken, target.BearerTokenSecret);
                if (!string.IsNullOrWhiteSpace(bearer))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
                }

                using var response = await client.SendAsync(request, cancellationToken);
                results.Add(new ReloadResult(target.Name, target.Url, response.IsSuccessStatusCode, (int)response.StatusCode, ""));
                if (!response.IsSuccessStatusCode)
                {
                    logger.LogWarning(
                        "Configuration reload for {Target} returned HTTP {StatusCode}.",
                        target.Name,
                        (int)response.StatusCode);
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
            {
                logger.LogWarning(ex, "Configuration reload for {Target} failed.", target.Name);
                results.Add(new ReloadResult(target.Name, target.Url, false, 0, ex.Message));
            }
        }

        return results;
    }

    private async Task<IReadOnlyList<ReplayResult>> ReplaySourceCardAsync(
        CmdbModelMissingDimensionRequest request,
        CancellationToken cancellationToken)
    {
        var current = replayOptions.CurrentValue;
        if (!current.Enabled)
        {
            return [];
        }

        if (string.IsNullOrWhiteSpace(current.ReprocessUrl))
        {
            throw new InvalidOperationException("Replay:ReprocessUrl is required when replay is enabled.");
        }

        if (string.IsNullOrWhiteSpace(request.SourceClass))
        {
            return
            [
                new ReplayResult(
                    "cmdbconfigbuilder",
                    current.ReprocessUrl,
                    false,
                    0,
                    request.SourceClass,
                    request.SourceCardId,
                    request.DimensionKey,
                    0,
                    0,
                    0,
                    0,
                    "source_class is empty; replay skipped")
            ];
        }

        var client = httpClientFactory.CreateClient();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(current.TimeoutMs));
        var payload = new
        {
            source_class = request.SourceClass,
            source_card_id = current.BackfillDimensionOnSave ? "" : request.SourceCardId,
            event_type = string.IsNullOrWhiteSpace(request.EventType) ? "UPDATE" : request.EventType,
            layer = request.Layer,
            template_id = request.TemplateId,
            dimension_key = request.DimensionKey,
            field = request.Field,
            field_value = request.FieldValue,
            reason = $"cmdbmodelmaterializer replay after materialization: {request.IdempotencyKey}",
            backfill_dimension = current.BackfillDimensionOnSave,
            max_cards = current.MaxBackfillCards
        };

        using var response = await client.PostAsJsonAsync(current.ReprocessUrl, payload, timeout.Token);
        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
        var node = await JsonNode.ParseAsync(stream, cancellationToken: timeout.Token) as JsonObject ?? new JsonObject();
        var success = response.IsSuccessStatusCode && JsonText.BoolValue(FirstNode(node, "success", "Success"), response.IsSuccessStatusCode);
        var result = new ReplayResult(
            "cmdbconfigbuilder",
            current.ReprocessUrl,
            success,
            (int)response.StatusCode,
            request.SourceClass,
            request.SourceCardId,
            request.DimensionKey,
            JsonText.IntValue(FirstNode(node, "cardsProcessed", "CardsProcessed"), 0),
            JsonText.IntValue(FirstNode(node, "commandsBuilt", "CommandsBuilt"), 0),
            JsonText.IntValue(FirstNode(node, "commandsPublished", "CommandsPublished"), 0),
            JsonText.IntValue(FirstNode(node, "commandsSkippedAsDuplicates", "CommandsSkippedAsDuplicates"), 0),
            JsonText.StringValue(FirstNode(node, "error", "Error", "detail", "Detail")));
        if (!success)
        {
            throw new InvalidOperationException(
                $"cmdbconfigbuilder reprocess failed with HTTP {(int)response.StatusCode}: {FirstNonEmpty(result.Error, "unknown error")}");
        }

        logger.LogInformation(
            "Replayed CMDB source card after materialization: {SourceClass}/{SourceCardId}, dimension {DimensionKey}, commands built {CommandsBuilt}, published {CommandsPublished}.",
            request.SourceClass,
            request.SourceCardId,
            request.DimensionKey,
            result.CommandsBuilt,
            result.CommandsPublished);
        return [result];
    }

    private async Task<IReadOnlyList<GraphOverlayResult>> RunGraphOverlayAsync(
        CmdbModelMissingDimensionRequest request,
        MaterializationEdit edit,
        CancellationToken cancellationToken)
    {
        var current = graphOverlayOptions.CurrentValue;
        if (!current.Enabled)
        {
            return [];
        }

        if (string.IsNullOrWhiteSpace(current.ApplyCurrentUrl))
        {
            throw new InvalidOperationException("GraphOverlay:ApplyCurrentUrl is required when graph overlay is enabled.");
        }

        var layer = JsonText.NormalizeLayer(request.Layer);
        var scopeKeys = GraphOverlayScopeKeys(request, edit);
        if (scopeKeys.Count == 0)
        {
            return
            [
                new GraphOverlayResult(
                    "cmdbconfigbuilder",
                    current.ApplyCurrentUrl,
                    false,
                    0,
                    "",
                    "graph-overlay",
                    layer,
                    current.DryRun,
                    current.EffectiveTargets(),
                    [],
                    0,
                    0,
                    0,
                    0,
                    "",
                    "graph overlay scope keys are empty; overlay skipped")
            ];
        }

        var operationId = $"materializer-graph-overlay-{SanitizeOperationId(MaterializationKey(request, ""))}-{Guid.NewGuid():N}";
        var payload = new
        {
            operationId,
            layers = string.IsNullOrWhiteSpace(layer) ? Array.Empty<string>() : new[] { layer },
            targets = current.EffectiveTargets(),
            cmdbuildPrefix = current.CmdbuildPrefix,
            serviceModelRoot = current.ServiceModelRoot,
            suppressionModelRoot = current.SuppressionModelRoot,
            zabbixCommandApplyUrl = current.ZabbixCommandApplyUrl,
            zabbixPublishMode = current.PublishMode,
            buildMode = "graph-overlay",
            topologyReadMode = current.TopologyReadMode,
            zabbixScopeKeys = scopeKeys,
            zabbixScopeDepth = current.ScopeDepth,
            requireZabbixScopeMatch = current.RequireScopeMatch,
            dryRun = current.DryRun,
            sourceClasses = Array.Empty<string>(),
            maxCardsPerClass = 0,
            eventType = "UPDATE"
        };

        var client = httpClientFactory.CreateClient();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(current.TimeoutMs));
        using var response = await client.PostAsJsonAsync(current.ApplyCurrentUrl, payload, timeout.Token);
        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
        var node = await JsonNode.ParseAsync(stream, cancellationToken: timeout.Token) as JsonObject ?? new JsonObject();
        var error = FirstJsonError(node);
        var success = response.IsSuccessStatusCode && string.IsNullOrWhiteSpace(error);
        var result = new GraphOverlayResult(
            "cmdbconfigbuilder",
            current.ApplyCurrentUrl,
            success,
            (int)response.StatusCode,
            JsonText.StringValue(FirstNode(node, "operationId", "OperationId")),
            JsonText.StringValue(FirstNode(node, "buildMode", "BuildMode")),
            layer,
            current.DryRun,
            current.EffectiveTargets(),
            scopeKeys,
            JsonText.IntValue(FirstNode(node, "ruleCount", "RuleCount"), 0),
            JsonText.IntValue(FirstNode(node, "cardsScanned", "CardsScanned"), 0),
            JsonText.IntValue(FirstNode(node, "commandsBuilt", "CommandsBuilt"), 0),
            JsonText.IntValue(FirstNode(node, "commandsPublished", "CommandsPublished"), 0),
            JsonText.StringValue(FirstNode(node, "zabbixDeliveryMode", "ZabbixDeliveryMode")),
            error);
        if (!success)
        {
            throw new InvalidOperationException(
                $"cmdbconfigbuilder graph-overlay failed with HTTP {(int)response.StatusCode}: {FirstNonEmpty(result.Error, "unknown error")}");
        }

        logger.LogInformation(
            "Ran scoped Zabbix graph overlay for {Key}: scope {ScopeCount}, rules {RuleCount}, commands built {CommandsBuilt}, published {CommandsPublished}.",
            MaterializationKey(request, ""),
            scopeKeys.Count,
            result.RuleCount,
            result.CommandsBuilt,
            result.CommandsPublished);
        return [result];
    }

    private static bool ShouldReplayAfterMaterialization(MaterializationEdit edit)
    {
        return edit.Status.Equals("already-materialized", StringComparison.OrdinalIgnoreCase);
    }

    private MaterializationJobSnapshot RecordJob(MaterializationJobSnapshot snapshot)
    {
        recentJobs.Enqueue(snapshot);
        while (recentJobs.Count > 50 && recentJobs.TryDequeue(out _))
        {
        }

        return snapshot;
    }

    private void CleanupCompleted(MaterializerOptions current)
    {
        var cutoff = DateTimeOffset.UtcNow - TimeSpan.FromSeconds(current.DuplicateTtlSeconds);
        foreach (var item in completed)
        {
            if (item.Value < cutoff)
            {
                completed.TryRemove(item.Key, out _);
            }
        }
    }

    private static string MaterializationKey(CmdbModelMissingDimensionRequest request, string kafkaKey)
    {
        return FirstNonEmpty(
            request.IdempotencyKey,
            kafkaKey,
            $"{JsonText.NormalizeLayer(request.Layer)}/{request.TemplateId}/{request.DimensionKey}");
    }

    private static IReadOnlyList<string> GraphOverlayScopeKeys(
        CmdbModelMissingDimensionRequest request,
        MaterializationEdit edit)
    {
        var keys = new List<string>();
        AddGraphOverlayScopeKey(keys, edit.RuleId);
        AddGraphOverlayScopeKey(keys, request.TargetKey);
        AddGraphOverlayScopeKey(keys, request.DimensionKey);
        AddGraphOverlayScopeKey(keys, request.DimensionValue);
        AddGraphOverlayScopeKey(keys, request.FieldValue);
        if (!string.IsNullOrWhiteSpace(request.TemplateId) && !string.IsNullOrWhiteSpace(request.DimensionKey))
        {
            AddGraphOverlayScopeKey(keys, $"{request.TemplateId}:{request.DimensionKey}");
        }

        return keys
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void AddGraphOverlayScopeKey(ICollection<string> keys, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            keys.Add(value.Trim());
        }
    }

    private static string SanitizeOperationId(string value)
    {
        var normalized = Regex.Replace(value, "[^A-Za-z0-9_.-]+", "-").Trim('-');
        return string.IsNullOrWhiteSpace(normalized)
            ? "missing-dimension"
            : normalized;
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        return values.Select(value => value?.Trim() ?? "").FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "";
    }

    private static JsonNode? FirstNode(JsonObject node, params string[] names)
    {
        foreach (var name in names)
        {
            if (node.TryGetPropertyValue(name, out var value) && value is not null)
            {
                return value;
            }
        }

        return null;
    }

    private static string FirstJsonError(JsonObject node)
    {
        var direct = FirstNonEmpty(JsonText.StringValue(FirstNode(node, "error", "Error", "detail", "Detail", "message", "Message")));
        if (!string.IsNullOrWhiteSpace(direct))
        {
            return direct;
        }

        foreach (var name in new[] { "errors", "Errors" })
        {
            if (node[name] is not JsonArray errors)
            {
                continue;
            }

            foreach (var item in errors)
            {
                var value = JsonText.StringValue(item);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }
        }

        return "";
    }
}

public sealed record ConversionConfigSnapshot(JsonObject Root, long Version, string Etag);

public sealed record ConversionConfigWriteResult(
    bool Success,
    int StatusCode,
    string Error,
    string Message,
    long Version,
    string Etag,
    JsonObject Body);

public sealed record MaterializationJobSnapshot(
    string IdempotencyKey,
    string Status,
    CmdbModelMissingDimensionRequest Request,
    string Layer,
    string TemplateId,
    string DimensionKey,
    string RuleId,
    int CreatedRules,
    int UpdatedRules,
    int UpdatedRelations,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<ReplayResult> Replays,
    IReadOnlyList<GraphOverlayResult> GraphOverlays,
    IReadOnlyList<ReloadResult> Reloads,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt);

public sealed record ReloadResult(string Name, string Url, bool Success, int StatusCode, string Error);

public sealed record ReplayResult(
    string Name,
    string Url,
    bool Success,
    int StatusCode,
    string SourceClass,
    string SourceCardId,
    string DimensionKey,
    int CardsProcessed,
    int CommandsBuilt,
    int CommandsPublished,
    int CommandsSkippedAsDuplicates,
    string Error);

public sealed record GraphOverlayResult(
    string Name,
    string Url,
    bool Success,
    int StatusCode,
    string OperationId,
    string BuildMode,
    string Layer,
    bool DryRun,
    IReadOnlyList<string> Targets,
    IReadOnlyList<string> ScopeKeys,
    int RuleCount,
    int CardsScanned,
    int CommandsBuilt,
    int CommandsPublished,
    string ZabbixDeliveryMode,
    string Error);

public sealed record MaterializationEdit(
    bool Changed,
    string Status,
    string Layer,
    string RuleId,
    int CreatedRules,
    int UpdatedRules,
    int UpdatedRelations,
    IReadOnlyList<string> Warnings,
    JsonObject? Payload);

public static class ConversionConfigMaterializer
{
    private const string PopulationSourceKeyAttribute = "population_source_key";

    private static readonly string[] UserResponsibilityAttributes =
    [
        "is_critical",
        "aggregation_type",
        "threshold",
        "n"
    ];

    public static MaterializationEdit Materialize(JsonObject current, CmdbModelMissingDimensionRequest request)
    {
        var layer = JsonText.NormalizeLayer(request.Layer);
        var warnings = new List<string>();
        if (string.IsNullOrWhiteSpace(layer)
            || string.IsNullOrWhiteSpace(request.TemplateId)
            || string.IsNullOrWhiteSpace(request.DimensionKey)
            || string.IsNullOrWhiteSpace(request.SourceClass))
        {
            return new MaterializationEdit(false, "invalid-request", layer, "", 0, 0, 0, ["layer, template_id, source_class and dimension_key are required."], null);
        }

        var payload = BuildWritablePayload(current);
        var ruleDocument = EnsureRuleDocument(payload, layer);
        var rules = EnsureArray(ruleDocument, "rules");
        var template = FindTemplate(payload, layer, request.TemplateId);
        if (template is null)
        {
            return new MaterializationEdit(false, "template-not-found", layer, "", 0, 0, 0, [$"Template {request.TemplateId} was not found in {layer} template documents."], null);
        }

        if (!JsonText.BoolValue(template["enabled"], true))
        {
            return new MaterializationEdit(false, "template-disabled", layer, "", 0, 0, 0, [$"Template {request.TemplateId} is disabled."], null);
        }

        var existing = FindGeneratedRule(rules, layer, request.TemplateId, request.SourceClass, request.DimensionKey);
        var changed = false;
        var createdRules = 0;
        var updatedRules = 0;
        string ruleId;
        if (existing is null)
        {
            var generated = BuildGeneratedRule(template, request, layer, rules);
            rules.Add(generated);
            ruleId = JsonText.StringValue(generated["rule_id"]);
            createdRules = 1;
            changed = true;
        }
        else if (IsDetached(existing))
        {
            return new MaterializationEdit(
                false,
                "detached-rule-exists",
                layer,
                JsonText.StringValue(existing["rule_id"]),
                0,
                0,
                0,
                [$"Detached generated rule {JsonText.StringValue(existing["rule_id"])} already owns this dimension; automatic materializer will not reattach it."],
                null);
        }
        else
        {
            ruleId = JsonText.StringValue(existing["rule_id"]);
        }

        var relationUpdates = ReconcileGeneratedRuleRelations(payload, layer, warnings);
        if (relationUpdates > 0)
        {
            changed = true;
            updatedRules += relationUpdates;
        }

        RefreshGeneratedRuleFingerprints(rules);
        return new MaterializationEdit(
            changed,
            changed ? "changed" : "already-materialized",
            layer,
            ruleId,
            createdRules,
            updatedRules,
            relationUpdates,
            warnings,
            changed ? payload : null);
    }

    private static JsonObject BuildWritablePayload(JsonObject current)
    {
        return new JsonObject
        {
            ["prefix"] = current["prefix"]?.DeepClone() ?? "",
            ["ruleDocuments"] = new JsonObject
            {
                ["service"] = current["ruleDocuments"]?["service"]?.DeepClone(),
                ["suppression"] = current["ruleDocuments"]?["suppression"]?.DeepClone()
            },
            ["templateDocuments"] = new JsonObject
            {
                ["service"] = current["templateDocuments"]?["service"]?.DeepClone(),
                ["suppression"] = current["templateDocuments"]?["suppression"]?.DeepClone(),
                ["shared"] = current["templateDocuments"]?["shared"]?.DeepClone()
            }
        };
    }

    private static JsonObject EnsureRuleDocument(JsonObject payload, string layer)
    {
        var ruleDocuments = EnsureObject(payload, "ruleDocuments");
        if (ruleDocuments[layer] is not JsonObject document)
        {
            document = new JsonObject
            {
                ["version"] = "1",
                ["rules"] = new JsonArray()
            };
            ruleDocuments[layer] = document;
        }

        EnsureArray(document, "rules");
        return document;
    }

    private static JsonObject? FindTemplate(JsonObject payload, string layer, string templateId)
    {
        foreach (var document in TemplateDocuments(payload, layer))
        {
            foreach (var item in JsonText.Array(document["templates"]).OfType<JsonObject>())
            {
                if (JsonText.Same(JsonText.StringValue(item["template_id"]), templateId))
                {
                    return item;
                }
            }
        }

        return null;
    }

    private static IReadOnlyList<JsonObject> TemplateDocuments(JsonObject payload, string layer)
    {
        var templateDocuments = payload["templateDocuments"] as JsonObject;
        return new[]
            {
                templateDocuments?[layer] as JsonObject,
                templateDocuments?["shared"] as JsonObject
            }
            .Where(document => document is not null)
            .Cast<JsonObject>()
            .ToArray();
    }

    private static Dictionary<string, JsonObject> TemplatesById(JsonObject payload, string layer)
    {
        var result = new Dictionary<string, JsonObject>(StringComparer.OrdinalIgnoreCase);
        foreach (var document in TemplateDocuments(payload, layer))
        {
            foreach (var template in JsonText.Array(document["templates"]).OfType<JsonObject>())
            {
                var id = JsonText.StringValue(template["template_id"]);
                if (!string.IsNullOrWhiteSpace(id))
                {
                    result[id] = template;
                }
            }
        }

        return result;
    }

    private static JsonObject? FindGeneratedRule(JsonArray rules, string layer, string templateId, string sourceClass, string dimensionKey)
    {
        foreach (var rule in rules.OfType<JsonObject>())
        {
            if (!JsonText.Same(JsonText.NormalizeLayer(JsonText.StringValue(rule["layer"])), layer)
                || !JsonText.Same(RuleTemplateId(rule), templateId)
                || !JsonText.Same(RuleDimensionKey(rule), dimensionKey))
            {
                continue;
            }

            if (JsonText.Same(RuleSourceClass(rule), sourceClass)
                || string.IsNullOrWhiteSpace(RuleSourceClass(rule)))
            {
                return rule;
            }
        }

        return null;
    }

    private static JsonObject BuildGeneratedRule(
        JsonObject template,
        CmdbModelMissingDimensionRequest request,
        string layer,
        JsonArray existingRules)
    {
        var targetClass = JsonText.StringValue(template["target"]?["class_code"]);
        var templateId = JsonText.StringValue(template["template_id"]);
        var templateName = FirstNonEmpty(JsonText.StringValue(template["name"]), request.TemplateName, templateId);
        var targetKey = FirstNonEmpty(
            request.TargetKey,
            RenderTemplateString(JsonText.StringValue(template["target"]?["population_source_key_template"]), template, request),
            RenderTemplateString(JsonText.StringValue(template["population_dimension"]?["key_template"]), template, request),
            $"{templateId}:{request.DimensionKey}");
        var ruleId = NormalizeRuleId($"{layer}-{templateId}-{request.SourceClass}-{request.DimensionKey}");
        var managedKey = GeneratedRuleManagedKey(layer, templateId, request.SourceClass, targetClass, request.DimensionKey);
        var generatedAt = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        var templateVersion = template["version"]?.DeepClone() ?? 1;
        var sourceField = FirstNonEmpty(
            request.Field,
            JsonText.StringValue(template["population_dimension"]?["condition_field"]),
            JsonText.StringValue(template["population_dimension"]?["source_field"]),
            "id");
        var name = RenderTemplateString(
            FirstNonEmpty(JsonText.StringValue(template["target"]?["name_template"]), "${dimension.name}"),
            template,
            request);
        var description = RenderTemplateString(JsonText.StringValue(template["target"]?["description_template"]), template, request);
        var dimensionName = FirstNonEmpty(request.DimensionName, request.DimensionValue, request.DimensionKey);
        var ruleName = $"{templateName} / {dimensionName}";
        var dimensionStrategy = ExistingTemplateMetadata(existingRules, templateId, "dimension_read_strategy");
        var dimensionSourceClass = ExistingTemplateMetadata(existingRules, templateId, "dimension_value_source_class");

        var rule = new JsonObject
        {
            ["rule_id"] = ruleId,
            ["name"] = ruleName,
            ["layer"] = layer,
            ["priority"] = JsonText.IntValue(template["priority"], 100),
            ["generated_from_template"] = templateId,
            ["template_version"] = templateVersion.DeepClone(),
            ["template_generation"] = new JsonObject
            {
                ["status"] = "managed",
                ["template_id"] = templateId,
                ["template_name"] = templateName,
                ["template_version"] = templateVersion.DeepClone(),
                ["managed_key"] = managedKey,
                ["artifact_kind"] = "rule",
                ["template_source_regex"] = JsonText.StringValue(template["source_class_regex"]),
                ["template_fingerprint"] = TemplateFingerprint(template),
                ["variables_fingerprint"] = FirstNonEmpty(JsonText.StringValue(template["lifecycle"]?["variables_fingerprint"]), StableHash(template["variables"])),
                ["variables"] = VariablesObject(request),
                ["relation_fingerprint"] = FirstNonEmpty(JsonText.StringValue(template["lifecycle"]?["relation_fingerprint"]), StableHash(template["managed_relations"])),
                ["generated_at"] = generatedAt,
                ["candidate_class_code"] = request.SourceClass,
                ["dimension_key"] = request.DimensionKey,
                ["dimension_name"] = dimensionName,
                ["dimension_value"] = FirstNonEmpty(request.DimensionValue, request.FieldValue, request.DimensionKey),
                ["dimension_stable_value"] = request.DimensionKey,
                ["dimension_display_value"] = FirstNonEmpty(request.DimensionName, request.DimensionValue, request.FieldValue, request.DimensionKey),
                ["dimension_read_strategy"] = FirstNonEmpty(dimensionStrategy, JsonText.StringValue(template["population_dimension"]?["type"])),
                ["dimension_value_source_class"] = FirstNonEmpty(dimensionSourceClass, request.SourceClass),
                ["target_class_code"] = targetClass
            },
            ["source"] = new JsonObject
            {
                ["class_code"] = request.SourceClass,
                ["key_attribute"] = sourceField
            },
            ["when"] = new JsonObject
            {
                ["allRegex"] = RuleMatchers(template, request, sourceField),
                ["fieldExists"] = sourceField
            },
            ["target"] = new JsonObject
            {
                ["class_code"] = targetClass,
                ["create_instance"] = true,
                ["idempotency_key"] = targetKey,
                ["attribute_mappings"] = new JsonObject
                {
                    ["name"] = name,
                    [PopulationSourceKeyAttribute] = targetKey
                },
                ["initial_user_values"] = InitialUserValues(template, request, description),
                ["user_responsibility_attributes"] = new JsonArray(UserResponsibilityAttributes
                    .Select(value => (JsonNode?)JsonValue.Create(value))
                    .ToArray()),
                ["created_by_template"] = new JsonObject
                {
                    ["template_id"] = templateId,
                    ["template_name"] = templateName,
                    ["template_version"] = templateVersion.DeepClone(),
                    ["managed_key"] = managedKey,
                    ["template_fingerprint"] = TemplateFingerprint(template),
                    ["generated_at"] = generatedAt,
                    ["candidate_class_code"] = request.SourceClass,
                    ["dimension_key"] = request.DimensionKey,
                    ["reconcile_policy"] = "managed_key_fingerprint"
                },
                ["card_id"] = ""
            },
            ["managed_relations"] = new JsonArray(),
            ["relations"] = new JsonArray()
        };

        RefreshGeneratedRuleFingerprint(rule);
        return rule;
    }

    private static JsonArray RuleMatchers(JsonObject template, CmdbModelMissingDimensionRequest request, string sourceField)
    {
        var result = new JsonArray
        {
            new JsonObject
            {
                ["field"] = "className",
                ["pattern"] = $"(?i)^{EscapeRegex(request.SourceClass)}$"
            }
        };

        foreach (var filter in JsonText.Array(template["filter"]?["include"]).OfType<JsonObject>())
        {
            var field = JsonText.StringValue(filter["field"]);
            var regex = JsonText.StringValue(filter["regex"]);
            if (!string.IsNullOrWhiteSpace(field) && !string.IsNullOrWhiteSpace(regex))
            {
                result.Add(new JsonObject
                {
                    ["field"] = field,
                    ["pattern"] = regex
                });
            }
        }

        if (!string.IsNullOrWhiteSpace(sourceField))
        {
            result.Add(new JsonObject
            {
                ["field"] = sourceField,
                ["pattern"] = $"(?i)^{EscapeRegex(FirstNonEmpty(request.FieldValue, request.DimensionValue, request.DimensionKey))}$"
            });
        }

        return result;
    }

    private static JsonObject InitialUserValues(JsonObject template, CmdbModelMissingDimensionRequest request, string description)
    {
        var result = new JsonObject();
        if (!string.IsNullOrWhiteSpace(description))
        {
            result["description"] = description;
        }

        if (template["target"]?["initial_user_values"] is JsonObject values)
        {
            foreach (var item in values)
            {
                var rendered = item.Value is null
                    ? ""
                    : item.Value is JsonValue
                        ? RenderTemplateString(JsonText.StringValue(item.Value), template, request)
                        : item.Value.ToJsonString();
                result[item.Key] = CoerceJsonValue(rendered);
            }
        }

        return result;
    }

    private static int ReconcileGeneratedRuleRelations(JsonObject payload, string layer, List<string> warnings)
    {
        var ruleDocument = EnsureRuleDocument(payload, layer);
        var rules = EnsureArray(ruleDocument, "rules");
        var templatesById = TemplatesById(payload, layer);
        var rulesById = rules.OfType<JsonObject>()
            .Where(rule => !string.IsNullOrWhiteSpace(JsonText.StringValue(rule["rule_id"])))
            .ToDictionary(rule => JsonText.StringValue(rule["rule_id"]), rule => rule, StringComparer.Ordinal);
        var generatedRules = rules.OfType<JsonObject>()
            .Where(rule => !string.IsNullOrWhiteSpace(JsonText.StringValue(rule["generated_from_template"]))
                && !IsDetached(rule))
            .ToArray();
        var generatedByTemplate = generatedRules
            .GroupBy(RuleTemplateId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);

        var updates = 0;
        foreach (var sourceRule in generatedRules)
        {
            if (!templatesById.TryGetValue(RuleTemplateId(sourceRule), out var sourceTemplate))
            {
                continue;
            }

            foreach (var templateRelation in JsonText.Array(sourceTemplate["managed_relations"]).OfType<JsonObject>())
            {
                foreach (var targetRule in TargetRules(templateRelation, generatedByTemplate, rulesById))
                {
                    if (JsonText.Same(JsonText.StringValue(sourceRule["rule_id"]), JsonText.StringValue(targetRule["rule_id"]))
                        || !TemplateRelationMatchesRulePair(sourceRule, targetRule, templateRelation))
                    {
                        continue;
                    }

                    if (AppendManagedRuleRelation(layer, sourceRule, targetRule, templateRelation, generatedRules))
                    {
                        updates++;
                    }

                    var runtimeRelation = BuildRuntimeRelation(sourceRule, targetRule, templateRelation, generatedRules, warnings);
                    if (runtimeRelation is not null && AppendRuntimeRelation(sourceRule, runtimeRelation))
                    {
                        updates++;
                    }
                }
            }
        }

        return updates;
    }

    private static IEnumerable<JsonObject> TargetRules(
        JsonObject templateRelation,
        IReadOnlyDictionary<string, JsonObject[]> generatedByTemplate,
        IReadOnlyDictionary<string, JsonObject> rulesById)
    {
        var kind = JsonText.StringValue(templateRelation["kind"]);
        var targetTemplateId = JsonText.StringValue(templateRelation["target_template_id"]);
        var targetRuleId = JsonText.StringValue(templateRelation["target_rule_id"]);

        if ((JsonText.Same(kind, "template") || JsonText.Same(kind, "rule_template"))
            && !string.IsNullOrWhiteSpace(targetTemplateId)
            && generatedByTemplate.TryGetValue(targetTemplateId, out var rules))
        {
            return rules;
        }

        if (JsonText.Same(kind, "rule")
            && !string.IsNullOrWhiteSpace(targetRuleId)
            && rulesById.TryGetValue(targetRuleId, out var rule))
        {
            return [rule];
        }

        return [];
    }

    private static bool AppendManagedRuleRelation(
        string layer,
        JsonObject sourceRule,
        JsonObject targetRule,
        JsonObject templateRelation,
        IReadOnlyList<JsonObject> generatedRules)
    {
        var managedRelations = EnsureArray(sourceRule, "managed_relations");
        var relationRole = JsonText.StringValue(templateRelation["relation_role"]);
        var targetRuleId = JsonText.StringValue(targetRule["rule_id"]);
        var key = RuleManagedRelationKey(layer, JsonText.StringValue(sourceRule["rule_id"]), relationRole, targetRuleId);
        if (string.IsNullOrWhiteSpace(key)
            || managedRelations.OfType<JsonObject>().Any(item => JsonText.Same(JsonText.StringValue(item["managed_key"]), key)))
        {
            return false;
        }

        var attributes = new JsonObject
        {
            ["inherited_from_template_relation"] = JsonText.StringValue(templateRelation["managed_key"]),
            ["target_lookup"] = TargetLookup(targetRule)
        };
        if (templateRelation["attributes"]?["match"] is JsonNode match)
        {
            attributes["match"] = match.DeepClone();
        }

        var sample = FindRuntimeRelationSample(sourceRule, templateRelation, generatedRules);
        if (sample is not null)
        {
            attributes["domain_code"] = JsonText.StringValue(sample["domain_code"]);
        }

        managedRelations.Add(new JsonObject
        {
            ["kind"] = "rule",
            ["relation_role"] = relationRole,
            ["target_template_id"] = "",
            ["target_rule_id"] = targetRuleId,
            ["managed_key"] = key,
            ["artifact_fingerprint"] = StableHash(new JsonObject
            {
                ["relation_role"] = relationRole,
                ["target_rule_id"] = targetRuleId,
                ["template_relation"] = JsonText.StringValue(templateRelation["managed_key"])
            }),
            ["attributes"] = attributes
        });
        return true;
    }

    private static JsonObject? BuildRuntimeRelation(
        JsonObject sourceRule,
        JsonObject targetRule,
        JsonObject templateRelation,
        IReadOnlyList<JsonObject> generatedRules,
        List<string> warnings)
    {
        var sample = FindRuntimeRelationSample(sourceRule, templateRelation, generatedRules);
        var domainCode = FirstNonEmpty(JsonText.StringValue(sample?["domain_code"]), JsonText.StringValue(templateRelation["attributes"]?["domain_code"]));
        if (string.IsNullOrWhiteSpace(domainCode))
        {
            AddWarning(warnings, $"Runtime relation for {JsonText.StringValue(sourceRule["rule_id"])} -> {JsonText.StringValue(targetRule["rule_id"])} was not created because domain_code is not known yet.");
            return null;
        }

        var targetClass = FirstNonEmpty(RuleTargetClass(targetRule), JsonText.StringValue(sample?["target_class_code"]));
        var targetLookup = TargetLookup(targetRule);
        if (string.IsNullOrWhiteSpace(targetClass) || string.IsNullOrWhiteSpace(targetLookup))
        {
            AddWarning(warnings, $"Runtime relation for {JsonText.StringValue(sourceRule["rule_id"])} -> {JsonText.StringValue(targetRule["rule_id"])} was not created because target lookup is empty.");
            return null;
        }

        return new JsonObject
        {
            ["domain_code"] = domainCode,
            ["target_class_code"] = targetClass,
            ["target_lookup"] = targetLookup,
            ["managed_relation_key"] = JsonText.StringValue(templateRelation["managed_key"]),
            ["attribute_mappings"] = sample?["attribute_mappings"]?.DeepClone() ?? new JsonObject
            {
                ["is_active"] = "true"
            }
        };
    }

    private static JsonObject? FindRuntimeRelationSample(
        JsonObject sourceRule,
        JsonObject templateRelation,
        IReadOnlyList<JsonObject> generatedRules)
    {
        var relationKey = JsonText.StringValue(templateRelation["managed_key"]);
        if (string.IsNullOrWhiteSpace(relationKey))
        {
            return null;
        }

        foreach (var candidate in new[] { sourceRule }.Concat(generatedRules))
        {
            var sample = JsonText.Array(candidate["relations"]).OfType<JsonObject>()
                .FirstOrDefault(relation => JsonText.Same(JsonText.StringValue(relation["managed_relation_key"]), relationKey));
            if (sample is not null)
            {
                return sample;
            }

            sample = JsonText.Array(candidate["managed_relations"]).OfType<JsonObject>()
                .Where(relation => JsonText.Same(JsonText.StringValue(relation["attributes"]?["inherited_from_template_relation"]), relationKey))
                .Select(relation => new JsonObject
                {
                    ["domain_code"] = JsonText.StringValue(relation["attributes"]?["domain_code"]),
                    ["target_lookup"] = JsonText.StringValue(relation["attributes"]?["target_lookup"]),
                    ["attribute_mappings"] = new JsonObject
                    {
                        ["is_active"] = "true"
                    }
                })
                .FirstOrDefault(relation => !string.IsNullOrWhiteSpace(JsonText.StringValue(relation["domain_code"])));
            if (sample is not null)
            {
                return sample;
            }
        }

        return null;
    }

    private static bool AppendRuntimeRelation(JsonObject sourceRule, JsonObject relation)
    {
        var relations = EnsureArray(sourceRule, "relations");
        var key = RuntimeRelationKey(relation);
        if (string.IsNullOrWhiteSpace(key)
            || relations.OfType<JsonObject>().Any(item => JsonText.Same(RuntimeRelationKey(item), key)))
        {
            return false;
        }

        relations.Add(relation);
        return true;
    }

    private static bool TemplateRelationMatchesRulePair(JsonObject sourceRule, JsonObject targetRule, JsonObject relation)
    {
        var match = relation["attributes"]?["match"] as JsonObject;
        if (match is null)
        {
            return true;
        }

        var mode = JsonText.StringValue(match["mode"]);
        if (JsonText.Same(mode, "exact"))
        {
            var sourceValue = RuleVariable(sourceRule, JsonText.StringValue(match["source_variable"]));
            var targetValue = RuleVariable(targetRule, JsonText.StringValue(match["target_variable"]));
            if (!string.IsNullOrWhiteSpace(JsonText.StringValue(match["source_pattern"]))
                && !RegexMatches(sourceValue, JsonText.StringValue(match["source_pattern"])))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(JsonText.StringValue(match["target_pattern"]))
                && !RegexMatches(targetValue, JsonText.StringValue(match["target_pattern"])))
            {
                return false;
            }

            return JsonText.Same(sourceValue, targetValue);
        }

        foreach (var filter in JsonText.Array(match["filters"]).OfType<JsonObject>())
        {
            var variable = JsonText.StringValue(filter["variable"]);
            var regex = JsonText.StringValue(filter["regex"]);
            var value = JsonText.Same(mode, "target_filters")
                ? RuleVariable(targetRule, variable)
                : RuleVariable(sourceRule, variable);
            var matched = RegexMatches(value, regex);
            if (JsonText.Same(JsonText.StringValue(filter["mode"]), "exclude") && matched)
            {
                return false;
            }

            if (!JsonText.Same(JsonText.StringValue(filter["mode"]), "exclude") && !matched)
            {
                return false;
            }
        }

        return true;
    }

    private static void RefreshGeneratedRuleFingerprints(JsonArray rules)
    {
        foreach (var rule in rules.OfType<JsonObject>())
        {
            if (!string.IsNullOrWhiteSpace(JsonText.StringValue(rule["generated_from_template"])))
            {
                RefreshGeneratedRuleFingerprint(rule);
            }
        }
    }

    private static void RefreshGeneratedRuleFingerprint(JsonObject rule)
    {
        var fingerprint = StableHash(new JsonObject
        {
            ["artifact_kind"] = "rule",
            ["rule_id"] = JsonText.StringValue(rule["rule_id"]),
            ["name"] = JsonText.StringValue(rule["name"]),
            ["layer"] = JsonText.StringValue(rule["layer"]),
            ["priority"] = rule["priority"]?.DeepClone() ?? 100,
            ["source"] = rule["source"]?.DeepClone(),
            ["when"] = rule["when"]?.DeepClone(),
            ["target"] = TargetFingerprintObject(rule),
            ["relations"] = rule["relations"]?.DeepClone() ?? new JsonArray()
        });
        if (rule["template_generation"] is JsonObject generation)
        {
            generation["artifact_fingerprint"] = fingerprint;
        }

        if (rule["target"]?["created_by_template"] is JsonObject created)
        {
            created["artifact_fingerprint"] = fingerprint;
        }
    }

    private static JsonObject TargetFingerprintObject(JsonObject rule)
    {
        var target = rule["target"]?.DeepClone() as JsonObject ?? new JsonObject();
        target.Remove("created_by_template");
        return target;
    }

    private static string RenderTemplateString(string templateText, JsonObject template, CmdbModelMissingDimensionRequest request)
    {
        if (string.IsNullOrWhiteSpace(templateText))
        {
            return "";
        }

        var replacements = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["template.id"] = JsonText.StringValue(template["template_id"]),
            ["template.name"] = JsonText.StringValue(template["name"]),
            ["class.code"] = request.SourceClass,
            ["class.description"] = request.SourceClass,
            ["dimension.key"] = request.DimensionKey,
            ["dimension.value"] = FirstNonEmpty(request.DimensionValue, request.FieldValue, request.DimensionKey),
            ["dimension.name"] = FirstNonEmpty(request.DimensionName, request.DimensionValue, request.FieldValue, request.DimensionKey),
            ["dimension.stableValue"] = request.DimensionKey,
            ["dimension.displayValue"] = FirstNonEmpty(request.DimensionName, request.DimensionValue, request.FieldValue, request.DimensionKey)
        };
        if (!string.IsNullOrWhiteSpace(request.Field))
        {
            replacements[$"source.{request.Field}"] = FirstNonEmpty(request.FieldValue, request.DimensionValue, request.DimensionKey);
        }

        foreach (var item in request.Variables)
        {
            replacements[item.Key] = item.Value;
            replacements[$"vars.{item.Key}"] = item.Value;
        }

        return Regex.Replace(templateText, "\\$\\{([^}]+)\\}", match =>
        {
            var key = match.Groups[1].Value.Trim();
            return replacements.TryGetValue(key, out var value) ? value : match.Value;
        });
    }

    private static JsonObject VariablesObject(CmdbModelMissingDimensionRequest request)
    {
        var result = new JsonObject();
        foreach (var item in request.Variables.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            result[item.Key] = item.Value;
        }

        return result;
    }

    private static JsonNode CoerceJsonValue(string value)
    {
        var text = value.Trim();
        if (bool.TryParse(text, out var boolean))
        {
            return JsonValue.Create(boolean);
        }

        if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer))
        {
            return JsonValue.Create(integer);
        }

        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            return JsonValue.Create(number);
        }

        return JsonValue.Create(value);
    }

    private static JsonObject EnsureObject(JsonObject parent, string propertyName)
    {
        if (parent[propertyName] is not JsonObject value)
        {
            value = new JsonObject();
            parent[propertyName] = value;
        }

        return value;
    }

    private static JsonArray EnsureArray(JsonObject parent, string propertyName)
    {
        if (parent[propertyName] is not JsonArray value)
        {
            value = new JsonArray();
            parent[propertyName] = value;
        }

        return value;
    }

    private static string RuleTemplateId(JsonObject rule)
    {
        return FirstNonEmpty(JsonText.StringValue(rule["template_generation"]?["template_id"]), JsonText.StringValue(rule["generated_from_template"]));
    }

    private static string RuleDimensionKey(JsonObject rule)
    {
        return JsonText.StringValue(rule["template_generation"]?["dimension_key"]);
    }

    private static string RuleSourceClass(JsonObject rule)
    {
        return FirstNonEmpty(JsonText.StringValue(rule["source"]?["class_code"]), JsonText.StringValue(rule["template_generation"]?["candidate_class_code"]));
    }

    private static string RuleTargetClass(JsonObject rule)
    {
        return FirstNonEmpty(JsonText.StringValue(rule["target"]?["class_code"]), JsonText.StringValue(rule["template_generation"]?["target_class_code"]));
    }

    private static string RuleVariable(JsonObject rule, string variableName)
    {
        if (string.IsNullOrWhiteSpace(variableName))
        {
            return "";
        }

        return JsonText.StringValue(rule["template_generation"]?["variables"]?[variableName]);
    }

    private static bool IsDetached(JsonObject rule)
    {
        return JsonText.Same(JsonText.StringValue(rule["template_generation"]?["status"]), "detached");
    }

    private static string TargetLookup(JsonObject targetRule)
    {
        return FirstNonEmpty(
            JsonText.StringValue(targetRule["target"]?["card_id"]),
            JsonText.StringValue(targetRule["target"]?["idempotency_key"]),
            JsonText.StringValue(targetRule["target"]?["attribute_mappings"]?["Code"]),
            JsonText.StringValue(targetRule["target"]?["attribute_mappings"]?["code"]));
    }

    private static string RuntimeRelationKey(JsonObject relation)
    {
        var domain = JsonText.StringValue(relation["domain_code"]);
        var targetClass = JsonText.StringValue(relation["target_class_code"]);
        var targetLookup = JsonText.StringValue(relation["target_lookup"]);
        return string.IsNullOrWhiteSpace(domain) || string.IsNullOrWhiteSpace(targetClass) || string.IsNullOrWhiteSpace(targetLookup)
            ? ""
            : $"{domain}::{targetClass}::{targetLookup}";
    }

    private static string TemplateFingerprint(JsonObject template)
    {
        return FirstNonEmpty(JsonText.StringValue(template["lifecycle"]?["full_fingerprint"]), StableHash(template));
    }

    private static string ExistingTemplateMetadata(JsonArray rules, string templateId, string metadataField)
    {
        return rules.OfType<JsonObject>()
            .Where(rule => JsonText.Same(RuleTemplateId(rule), templateId))
            .Select(rule => JsonText.StringValue(rule["template_generation"]?[metadataField]))
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "";
    }

    private static string GeneratedRuleManagedKey(string layer, string templateId, string sourceClassCode, string targetClassCode, string dimensionKey)
    {
        return NormalizeRuleId(string.Join('-', new[]
        {
            layer,
            "template",
            templateId,
            "rule",
            sourceClassCode,
            string.IsNullOrWhiteSpace(dimensionKey) ? "" : $"dimension-{dimensionKey}",
            targetClassCode
        }.Where(value => !string.IsNullOrWhiteSpace(value))));
    }

    private static string RuleManagedRelationKey(string layer, string ruleId, string relationRole, string targetId)
    {
        return string.IsNullOrWhiteSpace(ruleId) || string.IsNullOrWhiteSpace(targetId)
            ? ""
            : NormalizeRuleId($"{layer}-rule-{ruleId}-relation-rule-{relationRole}-{targetId}");
    }

    private static string NormalizeRuleId(string value)
    {
        var normalized = Regex.Replace(value.Trim().ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
        return string.IsNullOrWhiteSpace(normalized) ? "rule" : normalized;
    }

    private static string EscapeRegex(string value)
    {
        return Regex.Escape(value);
    }

    private static bool RegexMatches(string value, string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return true;
        }

        try
        {
            return Regex.IsMatch(value, pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static void AddWarning(List<string> warnings, string warning)
    {
        if (warnings.Count < 20 && !warnings.Contains(warning, StringComparer.Ordinal))
        {
            warnings.Add(warning);
        }
    }

    private static string StableHash(JsonNode? value)
    {
        var text = StableStringify(value);
        uint hash = 5381;
        foreach (var character in text)
        {
            hash = ((hash << 5) + hash) ^ character;
        }

        return $"{hash:x}:{text.Length}";
    }

    private static string StableStringify(JsonNode? value)
    {
        return value switch
        {
            null => "null",
            JsonArray array => $"[{string.Join(',', array.Select(StableStringify))}]",
            JsonObject obj => "{" + string.Join(',', obj.OrderBy(item => item.Key, StringComparer.Ordinal)
                .Select(item => $"{JsonSerializer.Serialize(item.Key)}:{StableStringify(item.Value)}")) + "}",
            _ => value.ToJsonString()
        };
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        return values.Select(value => value?.Trim() ?? "").FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "";
    }
}

public static class JsonText
{
    public static string StringValue(JsonNode? node)
    {
        if (node is null)
        {
            return "";
        }

        if (node is JsonValue value)
        {
            if (value.TryGetValue<string>(out var text))
            {
                return text;
            }

            if (value.TryGetValue<bool>(out var boolean))
            {
                return boolean ? "true" : "false";
            }

            if (value.TryGetValue<long>(out var integer))
            {
                return integer.ToString(CultureInfo.InvariantCulture);
            }

            if (value.TryGetValue<double>(out var number))
            {
                return number.ToString(CultureInfo.InvariantCulture);
            }
        }

        return node.ToJsonString();
    }

    public static long LongValue(JsonNode? node)
    {
        var text = StringValue(node);
        return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0;
    }

    public static int IntValue(JsonNode? node, int fallback)
    {
        var text = StringValue(node);
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : fallback;
    }

    public static bool BoolValue(JsonNode? node, bool fallback = false)
    {
        var text = StringValue(node);
        return bool.TryParse(text, out var value) ? value : fallback;
    }

    public static JsonArray Array(JsonNode? node)
    {
        return node as JsonArray ?? new JsonArray();
    }

    public static string NormalizeLayer(string layer)
    {
        var value = layer.Trim().ToLowerInvariant();
        return value is "service" or "suppression" ? value : "";
    }

    public static bool Same(string left, string right)
    {
        return string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
