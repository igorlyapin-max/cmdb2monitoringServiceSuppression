using System.Collections.Concurrent;
using Cmdb2MonitoringServiceSuppression.Shared.Aggregation;
using Cmdb2MonitoringServiceSuppression.Shared.Configuration;
using Cmdb2MonitoringServiceSuppression.Shared.Integrations;
using Cmdb2MonitoringServiceSuppression.Shared.Logging;
using Cmdb2MonitoringServiceSuppression.Shared.Messaging;
using Cmdb2MonitoringServiceSuppression.Shared.Secrets;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);
await builder.Configuration.ResolveSecretReferencesAsync("zabbixconfig2api");
builder.AddServiceDefaults();

builder.Services.AddOptions<ApplyOptions>()
    .Bind(builder.Configuration.GetSection(ApplyOptions.SectionName))
    .Validate(options => options.HasValidMode(), "Apply mode must be manual, auto, or dry-run.")
    .ValidateOnStart();
builder.Services.AddOptions<ZabbixOptions>()
    .Bind(builder.Configuration.GetSection(ZabbixOptions.SectionName))
    .Validate(options => options.HasValidAuthMode(), "Zabbix auth mode is invalid.")
    .Validate(options => options.RequestTimeoutMs > 0, "Zabbix request timeout must be greater than zero.")
    .ValidateOnStart();
builder.Services.AddOptions<KafkaTopicsOptions>()
    .Bind(builder.Configuration.GetSection(KafkaTopicsOptions.SectionName))
    .Validate(options => !string.IsNullOrWhiteSpace(options.EffectiveZabbixApplyPlans("service")), "Zabbix service apply topic is required.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.EffectiveZabbixApplyPlans("suppression")), "Zabbix suppression apply topic is required.")
    .ValidateOnStart();
builder.Services.AddHttpClient<ZabbixClient>();
builder.Services.AddSingleton<ZabbixApplyStateStore>();
builder.Services.AddHostedService<ZabbixServiceAggregationCommandWorker>();
builder.Services.AddHostedService<ZabbixSuppressionAggregationCommandWorker>();

var app = builder.Build();
app.MapServiceHealth();
app.MapConfigurationReload(builder.Configuration);

app.MapPost("/apply/manual", (IOptionsMonitor<ApplyOptions> options) =>
{
    return Results.Accepted(value: new
    {
        status = "accepted",
        mode = "manual",
        safe_apply = options.CurrentValue.SafeApply
    });
});

app.MapPost("/apply/auto", (IOptionsMonitor<ApplyOptions> options) =>
{
    if (!options.CurrentValue.AutoApplyEnabled)
    {
        return Results.Problem(
            title: "Automatic apply is disabled.",
            detail: "Enable Apply:AutoApplyEnabled only for approved development or automation scenarios.",
            statusCode: StatusCodes.Status409Conflict);
    }

    return Results.Accepted(value: new
    {
        status = "accepted",
        mode = "auto",
        safe_apply = options.CurrentValue.SafeApply
    });
});

app.MapGet("/zabbix/check", async (
    ZabbixClient client,
    CancellationToken cancellationToken) =>
{
    var result = await client.CheckConnectionAsync(cancellationToken);
    return result.Success ? Results.Ok(result) : Results.Problem(result.Error, statusCode: StatusCodes.Status502BadGateway);
});

app.MapGet("/apply/status", (
    ZabbixApplyStateStore state,
    IOptions<KafkaTopicsOptions> topicOptions,
    IOptionsMonitor<ApplyOptions> applyOptions) =>
{
    return Results.Ok(state.Snapshot(topicOptions.Value, applyOptions.CurrentValue));
});

app.MapGet("/apply/status/{layer}", (
    string layer,
    ZabbixApplyStateStore state,
    IOptions<KafkaTopicsOptions> topicOptions,
    IOptionsMonitor<ApplyOptions> applyOptions) =>
{
    var normalizedLayer = ZabbixApplyPlanner.NormalizeLayer(layer);
    if (string.IsNullOrWhiteSpace(normalizedLayer))
    {
        return Results.BadRequest(new { error = "layer must be service or suppression" });
    }

    return Results.Ok(state.LayerSnapshot(normalizedLayer, topicOptions.Value, applyOptions.CurrentValue));
});

app.MapPost("/commands/apply/dry-run", (
    AggregationCommand command,
    IOptionsMonitor<ApplyOptions> options,
    ZabbixApplyStateStore state) =>
{
    var layer = ZabbixApplyPlanner.NormalizeLayer(command.Layer);
    if (string.IsNullOrWhiteSpace(layer))
    {
        return Results.BadRequest(new { error = "command.layer must be service or suppression" });
    }

    var result = ZabbixApplyPlanner.Plan(command, layer, topic: "", options.CurrentValue, forceDryRun: true);
    state.Record(result);
    return Results.Accepted(value: new
    {
        status = "accepted",
        target = "zabbix",
        mode = "dry-run",
        safe_apply = options.CurrentValue.SafeApply,
        result
    });
});

app.Run();

public sealed class ZabbixServiceAggregationCommandWorker(
    IOptions<KafkaOptions> kafkaOptions,
    IOptions<KafkaTopicsOptions> topicOptions,
    IOptionsMonitor<ApplyOptions> applyOptions,
    IOptions<DebugOptions> debugOptions,
    ZabbixApplyStateStore state,
    ILogger<ZabbixServiceAggregationCommandWorker> logger)
    : ZabbixLayerAggregationCommandWorker("service", kafkaOptions, topicOptions, applyOptions, debugOptions, state, logger);

public sealed class ZabbixSuppressionAggregationCommandWorker(
    IOptions<KafkaOptions> kafkaOptions,
    IOptions<KafkaTopicsOptions> topicOptions,
    IOptionsMonitor<ApplyOptions> applyOptions,
    IOptions<DebugOptions> debugOptions,
    ZabbixApplyStateStore state,
    ILogger<ZabbixSuppressionAggregationCommandWorker> logger)
    : ZabbixLayerAggregationCommandWorker("suppression", kafkaOptions, topicOptions, applyOptions, debugOptions, state, logger);

public abstract class ZabbixLayerAggregationCommandWorker(
    string layer,
    IOptions<KafkaOptions> kafkaOptions,
    IOptions<KafkaTopicsOptions> topicOptions,
    IOptionsMonitor<ApplyOptions> applyOptions,
    IOptions<DebugOptions> debugOptions,
    ZabbixApplyStateStore state,
    ILogger logger)
    : KafkaJsonConsumerWorker<AggregationCommand>(kafkaOptions, logger)
{
    protected override string Topic => topicOptions.Value.EffectiveZabbixApplyPlans(layer);

    protected override string ConsumerGroupId => $"{kafkaOptions.Value.ConsumerGroupId}-{layer}";

    protected override Task HandleMessageAsync(
        AggregationCommand message,
        string key,
        CancellationToken cancellationToken)
    {
        var commandLayer = ZabbixApplyPlanner.NormalizeLayer(message.Layer);
        if (!string.Equals(commandLayer, layer, StringComparison.OrdinalIgnoreCase))
        {
            var mismatch = ZabbixApplyPlanner.LayerMismatch(message, layer, Topic);
            state.Record(mismatch);
            logger.LogWarning(
                "Zabbix {Layer} applier skipped command={CommandId}: command layer={CommandLayer}, topic={Topic}",
                layer,
                message.CommandId,
                message.Layer,
                Topic);
            return Task.CompletedTask;
        }

        var result = ZabbixApplyPlanner.Plan(message, layer, Topic, applyOptions.CurrentValue, forceDryRun: false);
        state.Record(result);

        logger.LogDebugBasic(
            debugOptions,
            "zabbix {Layer} applier received command={CommandId}, type={CommandType}, status={Status}, target={TargetClass}/{TargetCard}",
            layer,
            message.CommandId,
            message.CommandType,
            result.Status,
            message.Target.ClassCode,
            message.Target.CardId);

        logger.LogInformation(
            "Zabbix {Layer} apply plan {Status} in mode={Mode}, auto={AutoApplyEnabled}, safeApply={SafeApply}: command={CommandId}, type={CommandType}, rule={RuleId}, topic={Topic}",
            layer,
            result.Status,
            applyOptions.CurrentValue.Mode,
            applyOptions.CurrentValue.AutoApplyEnabled,
            applyOptions.CurrentValue.SafeApply,
            message.CommandId,
            message.CommandType,
            message.RuleId,
            Topic);

        return Task.CompletedTask;
    }
}

public static class ZabbixApplyPlanner
{
    public static string NormalizeLayer(string layer)
    {
        if (string.Equals(layer, "service", StringComparison.OrdinalIgnoreCase))
        {
            return "service";
        }

        if (string.Equals(layer, "suppression", StringComparison.OrdinalIgnoreCase))
        {
            return "suppression";
        }

        return "";
    }

    public static ZabbixCommandApplyResult Plan(
        AggregationCommand command,
        string layer,
        string topic,
        ApplyOptions options,
        bool forceDryRun)
    {
        var dryRun = forceDryRun || string.Equals(options.Mode, "dry-run", StringComparison.OrdinalIgnoreCase);
        var status = dryRun
            ? "dry-run"
            : options.EffectiveAutoApplyEnabled()
                ? "accepted"
                : "pending_manual";
        return new ZabbixCommandApplyResult
        {
            Layer = layer,
            Topic = topic,
            Status = status,
            Mode = dryRun ? "dry-run" : options.Mode,
            SafeApply = options.SafeApply,
            CommandId = command.CommandId,
            RuleId = command.RuleId,
            RuleName = command.RuleName,
            CommandType = command.CommandType,
            TargetClass = command.Target.ClassCode,
            TargetKey = string.IsNullOrWhiteSpace(command.Target.CardId)
                ? command.Target.IdempotencyKey
                : command.Target.CardId,
            SourceClass = command.Source.ClassCode,
            SourceCardId = command.Source.CardId,
            Reconcile = ReconcileCounters(command),
            Message = status switch
            {
                "dry-run" => "Команда проверена без публикации изменений в Zabbix.",
                "accepted" => "Команда принята контуром Zabbix для применения.",
                _ => "Команда ожидает ручного применения: автоматическое применение выключено."
            },
            AppliedAtUtc = DateTimeOffset.UtcNow
        };
    }

    public static ZabbixCommandApplyResult LayerMismatch(AggregationCommand command, string expectedLayer, string topic)
    {
        return new ZabbixCommandApplyResult
        {
            Layer = expectedLayer,
            Topic = topic,
            Status = "error",
            Mode = "skipped",
            CommandId = command.CommandId,
            RuleId = command.RuleId,
            RuleName = command.RuleName,
            CommandType = command.CommandType,
            TargetClass = command.Target.ClassCode,
            TargetKey = string.IsNullOrWhiteSpace(command.Target.CardId)
                ? command.Target.IdempotencyKey
                : command.Target.CardId,
            SourceClass = command.Source.ClassCode,
            SourceCardId = command.Source.CardId,
            Error = $"Команда слоя '{command.Layer}' попала в топик слоя '{expectedLayer}'.",
            AppliedAtUtc = DateTimeOffset.UtcNow
        };
    }

    private static ZabbixReconcileCounters ReconcileCounters(AggregationCommand command)
    {
        var relationCount = command.Target.Relations.Count;
        if (string.Equals(command.CommandType, AggregationCommandTypes.RemoveMembership, StringComparison.OrdinalIgnoreCase))
        {
            return new ZabbixReconcileCounters
            {
                RemoveObjects = string.IsNullOrWhiteSpace(command.Target.ClassCode) ? 0 : 1,
                RemoveRelations = Math.Max(1, relationCount)
            };
        }

        return new ZabbixReconcileCounters
        {
            EnsureObjects = string.IsNullOrWhiteSpace(command.Target.ClassCode) ? 0 : 1,
            EnsureRelations = relationCount
        };
    }
}

public sealed class ZabbixApplyStateStore
{
    private readonly ConcurrentDictionary<string, ZabbixLayerApplyStatus> layers = new(StringComparer.OrdinalIgnoreCase);

    public ZabbixApplyStateStore()
    {
        layers.TryAdd("service", new ZabbixLayerApplyStatus { Layer = "service" });
        layers.TryAdd("suppression", new ZabbixLayerApplyStatus { Layer = "suppression" });
    }

    public void Record(ZabbixCommandApplyResult result)
    {
        var status = layers.GetOrAdd(result.Layer, layer => new ZabbixLayerApplyStatus { Layer = layer });
        lock (status)
        {
            status.LastUpdatedAtUtc = result.AppliedAtUtc;
            status.LastTopic = result.Topic;
            status.LastStatus = result.Status;
            status.LastMode = result.Mode;
            status.LastCommandId = result.CommandId;
            status.LastRuleId = result.RuleId;
            status.LastRuleName = result.RuleName;
            status.LastTargetClass = result.TargetClass;
            status.LastTargetKey = result.TargetKey;
            status.CommandsReceived++;
            status.Reconcile.Add(result.Reconcile);
            if (string.Equals(result.Status, "dry-run", StringComparison.OrdinalIgnoreCase))
            {
                status.DryRunCommands++;
            }
            else if (string.Equals(result.Status, "accepted", StringComparison.OrdinalIgnoreCase))
            {
                status.AcceptedCommands++;
            }
            else if (string.Equals(result.Status, "pending_manual", StringComparison.OrdinalIgnoreCase))
            {
                status.PendingManualCommands++;
            }
            else if (string.Equals(result.Status, "error", StringComparison.OrdinalIgnoreCase))
            {
                status.ErrorCommands++;
            }

            if (!string.IsNullOrWhiteSpace(result.Error))
            {
                status.Errors.Insert(0, $"{result.AppliedAtUtc:O}: {result.Error}");
                if (status.Errors.Count > 20)
                {
                    status.Errors.RemoveRange(20, status.Errors.Count - 20);
                }
            }
        }
    }

    public object Snapshot(KafkaTopicsOptions topics, ApplyOptions options)
    {
        return new
        {
            mode = options.Mode,
            autoApplyEnabled = options.AutoApplyEnabled,
            effectiveAutoApplyEnabled = options.EffectiveAutoApplyEnabled(),
            safeApply = options.SafeApply,
            topics = new
            {
                service = topics.EffectiveZabbixApplyPlans("service"),
                suppression = topics.EffectiveZabbixApplyPlans("suppression")
            },
            layers = new[]
            {
                LayerSnapshot("service", topics, options),
                LayerSnapshot("suppression", topics, options)
            }
        };
    }

    public object LayerSnapshot(string layer, KafkaTopicsOptions topics, ApplyOptions options)
    {
        var status = layers.GetOrAdd(layer, key => new ZabbixLayerApplyStatus { Layer = key });
        lock (status)
        {
            return new
            {
                layer = status.Layer,
                topic = topics.EffectiveZabbixApplyPlans(layer),
                mode = options.Mode,
                autoApplyEnabled = options.AutoApplyEnabled,
                effectiveAutoApplyEnabled = options.EffectiveAutoApplyEnabled(),
                safeApply = options.SafeApply,
                lastUpdatedAt = status.LastUpdatedAtUtc,
                lastStatus = status.LastStatus,
                lastMode = status.LastMode,
                lastTopic = status.LastTopic,
                lastCommandId = status.LastCommandId,
                lastRuleId = status.LastRuleId,
                lastRuleName = status.LastRuleName,
                lastTargetClass = status.LastTargetClass,
                lastTargetKey = status.LastTargetKey,
                commandsReceived = status.CommandsReceived,
                dryRunCommands = status.DryRunCommands,
                acceptedCommands = status.AcceptedCommands,
                pendingManualCommands = status.PendingManualCommands,
                errorCommands = status.ErrorCommands,
                reconcile = status.Reconcile,
                errors = status.Errors.ToArray()
            };
        }
    }
}

public sealed class ZabbixLayerApplyStatus
{
    public string Layer { get; init; } = "";

    public DateTimeOffset? LastUpdatedAtUtc { get; set; }

    public string LastStatus { get; set; } = "";

    public string LastMode { get; set; } = "";

    public string LastTopic { get; set; } = "";

    public string LastCommandId { get; set; } = "";

    public string LastRuleId { get; set; } = "";

    public string LastRuleName { get; set; } = "";

    public string LastTargetClass { get; set; } = "";

    public string LastTargetKey { get; set; } = "";

    public int CommandsReceived { get; set; }

    public int DryRunCommands { get; set; }

    public int AcceptedCommands { get; set; }

    public int PendingManualCommands { get; set; }

    public int ErrorCommands { get; set; }

    public ZabbixReconcileCounters Reconcile { get; } = new();

    public List<string> Errors { get; } = [];
}

public sealed class ZabbixCommandApplyResult
{
    public string Layer { get; init; } = "";

    public string Topic { get; init; } = "";

    public string Status { get; init; } = "";

    public string Mode { get; init; } = "";

    public bool SafeApply { get; init; }

    public string CommandId { get; init; } = "";

    public string RuleId { get; init; } = "";

    public string RuleName { get; init; } = "";

    public string CommandType { get; init; } = "";

    public string TargetClass { get; init; } = "";

    public string TargetKey { get; init; } = "";

    public string SourceClass { get; init; } = "";

    public string SourceCardId { get; init; } = "";

    public ZabbixReconcileCounters Reconcile { get; init; } = new();

    public string Message { get; init; } = "";

    public string Error { get; init; } = "";

    public DateTimeOffset AppliedAtUtc { get; init; }
}

public sealed class ZabbixReconcileCounters
{
    public int EnsureObjects { get; set; }

    public int EnsureRelations { get; set; }

    public int RemoveObjects { get; set; }

    public int RemoveRelations { get; set; }

    public void Add(ZabbixReconcileCounters counters)
    {
        EnsureObjects += counters.EnsureObjects;
        EnsureRelations += counters.EnsureRelations;
        RemoveObjects += counters.RemoveObjects;
        RemoveRelations += counters.RemoveRelations;
    }
}
