using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Channels;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
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
builder.Services.AddOptions<ZabbixApplyStateOptions>()
    .Bind(builder.Configuration.GetSection(ZabbixApplyStateOptions.SectionName))
    .Validate(options => !string.IsNullOrWhiteSpace(options.FilePath), "ZabbixApplyState:FilePath is required.")
    .ValidateOnStart();
builder.Services.AddOptions<ZabbixTriggerDependenciesOptions>()
    .Bind(builder.Configuration.GetSection(ZabbixTriggerDependenciesOptions.SectionName))
    .Validate(options => options.MaxDependenciesPerRun > 0, "ZabbixTriggerDependencies:MaxDependenciesPerRun must be greater than zero.")
    .Validate(options => options.TransitiveGroupDependencyDepth is >= 1 and <= 3, "ZabbixTriggerDependencies:TransitiveGroupDependencyDepth must be between 1 and 3.")
    .Validate(options => options.TriggerGetBatchSize is >= 1 and <= 100, "ZabbixTriggerDependencies:TriggerGetBatchSize must be between 1 and 100.")
    .Validate(options => options.MaxSourceHostsPerAggregate is >= 1 and <= 100000, "ZabbixTriggerDependencies:MaxSourceHostsPerAggregate must be between 1 and 100000.")
    .Validate(options => options.MaxAggregateFormulaLength is >= 1000 and <= 1000000, "ZabbixTriggerDependencies:MaxAggregateFormulaLength must be between 1000 and 1000000.")
    .Validate(options => options.SampleLimit > 0, "ZabbixTriggerDependencies:SampleLimit must be greater than zero.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.AggregateHostGroupName), "ZabbixTriggerDependencies:AggregateHostGroupName is required.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.AggregateHostName), "ZabbixTriggerDependencies:AggregateHostName is required.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.AggregateItemKeyPrefix), "ZabbixTriggerDependencies:AggregateItemKeyPrefix is required.")
    .Validate(options => options.SampleSourceTriggersPerAggregate > 0, "ZabbixTriggerDependencies:SampleSourceTriggersPerAggregate must be greater than zero.")
    .Validate(options => options.AutoReconcileDebounceSeconds >= 0, "ZabbixTriggerDependencies:AutoReconcileDebounceSeconds must not be negative.")
    .Validate(options => options.AggregateStateTriggerMinPriority is >= 0 and <= 5, "ZabbixTriggerDependencies:AggregateStateTriggerMinPriority must be between 0 and 5.")
    .Validate(options => options.DependencyTriggerMinPriority is >= 0 and <= 5, "ZabbixTriggerDependencies:DependencyTriggerMinPriority must be between 0 and 5.")
    .Validate(HasAggregateStateSelector, "ZabbixTriggerDependencies aggregate state selector must define include tags or include name regex; use AggregateStateTriggerIncludeNameRegex=.* to explicitly select all.")
    .Validate(HasValidTriggerSelectorRegex, "ZabbixTriggerDependencies trigger selector regex is invalid.")
    .ValidateOnStart();
builder.Services.AddOptions<ZabbixSlaOptions>()
    .Bind(builder.Configuration.GetSection(ZabbixSlaOptions.SectionName))
    .Validate(options => options.DowntimePublicationHorizonMonths is >= 1 and <= 24, "ZabbixSla:DowntimePublicationHorizonMonths must be between 1 and 24.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.ManagedExcludedDowntimePrefix), "ZabbixSla:ManagedExcludedDowntimePrefix is required.")
    .Validate(options => options.SampleLimit > 0, "ZabbixSla:SampleLimit must be greater than zero.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.CmdbuildPrefix), "ZabbixSla:CmdbuildPrefix is required.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.DefaultTimezone), "ZabbixSla:DefaultTimezone is required.")
    .ValidateOnStart();
builder.Services.AddOptions<ZabbixOptions>()
    .Bind(builder.Configuration.GetSection(ZabbixOptions.SectionName))
    .Validate(options => options.HasValidAuthMode(), "Zabbix auth mode is invalid.")
    .Validate(options => options.RequestTimeoutMs > 0, "Zabbix request timeout must be greater than zero.")
    .ValidateOnStart();
builder.Services.AddOptions<CmdbuildOptions>()
    .Bind(builder.Configuration.GetSection(CmdbuildOptions.SectionName))
    .Validate(options => options.HasValidAuthMode(), "CMDBuild auth mode is invalid.")
    .Validate(options => options.RequestTimeoutMs > 0, "CMDBuild request timeout must be greater than zero.")
    .ValidateOnStart();
builder.Services.AddOptions<KafkaTopicsOptions>()
    .Bind(builder.Configuration.GetSection(KafkaTopicsOptions.SectionName))
    .Validate(options => !string.IsNullOrWhiteSpace(options.EffectiveZabbixApplyPlans("service")), "Zabbix service apply topic is required.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.EffectiveZabbixApplyPlans("suppression")), "Zabbix suppression apply topic is required.")
    .ValidateOnStart();
builder.Services.AddHttpClient<ZabbixClient>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient<CmdbuildClient>();
builder.Services.AddTransient<ZabbixAggregationApplier>();
builder.Services.AddTransient<ZabbixTriggerDependencyApplier>();
builder.Services.AddTransient<ZabbixSlaPublisher>();
builder.Services.AddSingleton<ZabbixApplyStateStore>();
builder.Services.AddSingleton<ZabbixTriggerDependencyReconcileScheduler>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<ZabbixTriggerDependencyReconcileScheduler>());
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

app.MapPost("/apply/state/stale-report", async (
    ZabbixStateStaleReportRequest? request,
    ZabbixApplyStateStore state,
    ZabbixClient zabbix,
    CancellationToken cancellationToken) =>
{
    var layer = ZabbixApplyPlanner.NormalizeLayer(request?.Layer ?? "");
    if (string.IsNullOrWhiteSpace(layer))
    {
        return Results.BadRequest(new { error = "layer must be service or suppression" });
    }

    var desiredKeys = NormalizeManagedKeys(request?.DesiredManagedKeys);
    var sampleLimit = Math.Clamp(request?.SampleLimit ?? 100, 1, 1000);
    var report = state.StaleMembershipTargets(layer, desiredKeys, sampleLimit);
    var zabbixServices = Array.Empty<ZabbixManagedServiceSnapshot>();
    var zabbixStaleServices = Array.Empty<ZabbixManagedServiceSnapshot>();
    string? zabbixError = null;
    if (request?.IncludeZabbixServices == true)
    {
        try
        {
            zabbixServices = (await zabbix.ListManagedServicesByLayerAsync(
                    layer,
                    Math.Clamp(request.ZabbixServiceLimit <= 0 ? 2000 : request.ZabbixServiceLimit, 1, 10000),
                    cancellationToken))
                .Select(ToManagedServiceSnapshot)
                .ToArray();
            zabbixStaleServices = zabbixServices
                .Where(item => !string.IsNullOrWhiteSpace(item.ManagedKey)
                    && !item.SourceLeaf.Equals("true", StringComparison.OrdinalIgnoreCase)
                    && !desiredKeys.Contains(item.ManagedKey))
                .ToArray();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            zabbixError = ex.Message;
        }
    }

    return Results.Ok(new
    {
        layer,
        desiredManagedKeyCount = desiredKeys.Count,
        state = report,
        zabbix = new
        {
            enabled = request?.IncludeZabbixServices == true,
            error = zabbixError,
            managedServiceCount = zabbixServices.Length,
            staleServiceCount = zabbixStaleServices.Length,
            staleServices = zabbixStaleServices.Take(sampleLimit).ToArray(),
            orphanSourceLeafCount = zabbixServices.Count(item =>
                item.SourceLeaf.Equals("true", StringComparison.OrdinalIgnoreCase)
                && item.ParentCount == 0),
            orphanSourceLeafServices = zabbixServices
                .Where(item =>
                    item.SourceLeaf.Equals("true", StringComparison.OrdinalIgnoreCase)
                    && item.ParentCount == 0)
                .Take(sampleLimit)
                .ToArray(),
            rootNonRootManagedServiceCount = zabbixServices.Count(item =>
                item.ParentCount == 0
                && !item.EffectiveRole.Equals(ZabbixManagedServiceRoles.RootService, StringComparison.OrdinalIgnoreCase)
                && !item.EffectiveVisibility.Equals(ZabbixManagedServiceVisibility.Internal, StringComparison.OrdinalIgnoreCase)),
            rootNonRootManagedServices = zabbixServices
                .Where(item =>
                    item.ParentCount == 0
                    && !item.EffectiveRole.Equals(ZabbixManagedServiceRoles.RootService, StringComparison.OrdinalIgnoreCase)
                    && !item.EffectiveVisibility.Equals(ZabbixManagedServiceVisibility.Internal, StringComparison.OrdinalIgnoreCase))
                .Take(sampleLimit)
                .ToArray()
        }
    });
});

app.MapPost("/apply/state/cleanup", (
    ZabbixStateCleanupRequest? request,
    ZabbixApplyStateStore state) =>
{
    var layer = ZabbixApplyPlanner.NormalizeLayer(request?.Layer ?? "");
    if (string.IsNullOrWhiteSpace(layer))
    {
        return Results.BadRequest(new { error = "layer must be service or suppression" });
    }

    var keys = NormalizeManagedKeys(request?.ManagedKeys);
    if (keys.Count == 0)
    {
        return Results.BadRequest(new { error = "managedKeys must contain at least one key" });
    }

    return Results.Ok(state.CleanupMembershipTargets(layer, keys, request?.DryRun == true));
});

app.MapPost("/apply/state/delete-zabbix-services", async (
    ZabbixStaleZabbixDeleteRequest? request,
    ZabbixClient zabbix,
    CancellationToken cancellationToken) =>
{
    var layer = ZabbixApplyPlanner.NormalizeLayer(request?.Layer ?? "");
    if (string.IsNullOrWhiteSpace(layer))
    {
        return Results.BadRequest(new { error = "layer must be service or suppression" });
    }

    var keys = NormalizeManagedKeys(request?.ManagedKeys);
    if (keys.Count == 0)
    {
        return Results.BadRequest(new { error = "managedKeys must contain at least one key" });
    }

    var results = new List<ZabbixManagedServiceDeleteResult>();
    var errors = new List<string>();
    foreach (var key in keys.OrderBy(item => item, StringComparer.Ordinal))
    {
        try
        {
            results.Add(await zabbix.DeleteManagedServiceByKeyAsync(layer, key, cancellationToken));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            errors.Add($"{key}: {ex.Message}");
            results.Add(new ZabbixManagedServiceDeleteResult
            {
                ManagedKey = key,
                Action = "failed",
                Message = ex.Message
            });
        }
    }

    return Results.Ok(new
    {
        layer,
        requested = keys.Count,
        deleted = results.Count(item => item.Action.Equals("deleted", StringComparison.OrdinalIgnoreCase)),
        skipped = results.Count(item => item.Action.Equals("skipped", StringComparison.OrdinalIgnoreCase)),
        failed = results.Count(item => item.Action.Equals("failed", StringComparison.OrdinalIgnoreCase)),
        results,
        errors
    });
});

app.MapGet("/dependencies/suppression/status", (
    ZabbixApplyStateStore state,
    IOptionsMonitor<ZabbixTriggerDependenciesOptions> options,
    IOptionsMonitor<ZabbixOptions> zabbixOptions) =>
{
    return Results.Ok(state.TriggerDependencySnapshot("suppression", options.CurrentValue, zabbixOptions.CurrentValue));
});

app.MapGet("/sla/status", (
    IOptionsMonitor<ZabbixSlaOptions> options,
    IOptionsMonitor<ZabbixOptions> zabbixOptions,
    IOptionsMonitor<CmdbuildOptions> cmdbuildOptions) =>
{
    var currentOptions = options.CurrentValue;
    var currentZabbixOptions = zabbixOptions.CurrentValue;
    var currentCmdbuildOptions = cmdbuildOptions.CurrentValue;
    return Results.Ok(new
    {
        enabled = currentOptions.Enabled,
        defaultPolicyKey = currentOptions.DefaultPolicyKey,
        downtimePublicationHorizonMonths = currentOptions.DowntimePublicationHorizonMonths,
        managedExcludedDowntimePrefix = currentOptions.ManagedExcludedDowntimePrefix,
        cmdbuildPrefix = currentOptions.CmdbuildPrefix,
        serviceRootPath = currentOptions.ServiceRootPath,
        defaultReportingPeriod = currentOptions.DefaultReportingPeriod,
        defaultTimezone = currentOptions.DefaultTimezone,
        sampleLimit = currentOptions.SampleLimit,
        zabbixRequestTimeoutMs = currentZabbixOptions.RequestTimeoutMs,
        cmdbuildRequestTimeoutMs = currentCmdbuildOptions.RequestTimeoutMs,
        zabbixApiConfigured = !string.IsNullOrWhiteSpace(currentZabbixOptions.ApiEndpoint),
        cmdbuildApiConfigured = !string.IsNullOrWhiteSpace(currentCmdbuildOptions.BaseUrl)
    });
});

app.MapPost("/sla/service/dry-run", async (
    ZabbixSlaPublisher publisher,
    CancellationToken cancellationToken) =>
{
    return Results.Ok(await publisher.RunAsync(dryRun: true, cancellationToken));
});

app.MapPost("/sla/service/apply", async (
    ZabbixSlaPublisher publisher,
    CancellationToken cancellationToken) =>
{
    return Results.Ok(await publisher.RunAsync(dryRun: false, cancellationToken));
});

app.MapPost("/dependencies/suppression/dry-run", async (
    ZabbixTriggerDependencyRunRequest? request,
    ZabbixTriggerDependencyApplier applier,
    CancellationToken cancellationToken) =>
{
    return Results.Ok(await applier.RunAsync(dryRun: true, request, cancellationToken));
});

app.MapPost("/dependencies/suppression/apply", async (
    ZabbixTriggerDependencyRunRequest? request,
    ZabbixTriggerDependencyApplier applier,
    CancellationToken cancellationToken) =>
{
    return Results.Ok(await applier.RunAsync(dryRun: false, request, cancellationToken));
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

app.MapPost("/commands/apply-graph/dry-run", async (
    ZabbixGraphApplyRequest request,
    IOptionsMonitor<ApplyOptions> options,
    ZabbixApplyStateStore state,
    ZabbixAggregationApplier applier,
    CancellationToken cancellationToken) =>
{
    var layer = ZabbixApplyPlanner.NormalizeLayer(FirstNonEmpty(request.Layer, request.Commands.FirstOrDefault()?.Layer));
    if (string.IsNullOrWhiteSpace(layer))
    {
        return Results.BadRequest(new { error = "layer must be service or suppression" });
    }

    var result = await applier.ApplyGraphAsync(
        request.Commands,
        layer,
        "http-direct-graph",
        options.CurrentValue,
        forceDryRun: true,
        cancellationToken,
        request.PublishMode,
        request.ScopeKeys,
        request.ScopeDepth);
    foreach (var commandResult in result.CommandResults)
    {
        state.Record(commandResult);
    }

    return Results.Ok(result);
});

app.MapPost("/commands/apply-graph", async (
    ZabbixGraphApplyRequest request,
    IOptionsMonitor<ApplyOptions> options,
    ZabbixApplyStateStore state,
    ZabbixAggregationApplier applier,
    CancellationToken cancellationToken) =>
{
    var layer = ZabbixApplyPlanner.NormalizeLayer(FirstNonEmpty(request.Layer, request.Commands.FirstOrDefault()?.Layer));
    if (string.IsNullOrWhiteSpace(layer))
    {
        return Results.BadRequest(new { error = "layer must be service or suppression" });
    }

    var result = await applier.ApplyGraphAsync(
        request.Commands,
        layer,
        "http-direct-graph",
        options.CurrentValue,
        request.DryRun,
        cancellationToken,
        request.PublishMode,
        request.ScopeKeys,
        request.ScopeDepth);
    foreach (var commandResult in result.CommandResults)
    {
        state.Record(commandResult);
    }

    return string.Equals(result.Status, "error", StringComparison.OrdinalIgnoreCase)
        || result.Errors.Count > 0
        ? Results.Problem(
            title: "Zabbix graph was not applied.",
            detail: result.Errors.FirstOrDefault() ?? result.Message,
            extensions: new Dictionary<string, object?> { ["result"] = result },
            statusCode: StatusCodes.Status502BadGateway)
        : Results.Ok(result);
});

app.MapPost("/commands/apply", async (
    AggregationCommand command,
    IOptionsMonitor<ApplyOptions> options,
    ZabbixApplyStateStore state,
    ZabbixAggregationApplier applier,
    CancellationToken cancellationToken) =>
{
    var layer = ZabbixApplyPlanner.NormalizeLayer(command.Layer);
    if (string.IsNullOrWhiteSpace(layer))
    {
        return Results.BadRequest(new { error = "command.layer must be service or suppression" });
    }

    ZabbixCommandApplyResult result;
    if (string.Equals(options.CurrentValue.Mode, "dry-run", StringComparison.OrdinalIgnoreCase))
    {
        result = ZabbixApplyPlanner.Plan(command, layer, topic: "http-direct", options.CurrentValue, forceDryRun: true);
    }
    else
    {
        result = await applier.ApplyAsync(command, layer, "http-direct", options.CurrentValue, cancellationToken);
    }

    state.Record(result);
    return string.Equals(result.Status, "error", StringComparison.OrdinalIgnoreCase)
        ? Results.Problem(
            title: "Zabbix command was not applied.",
            detail: string.IsNullOrWhiteSpace(result.Error) ? result.Message : result.Error,
            extensions: new Dictionary<string, object?> { ["result"] = result },
            statusCode: StatusCodes.Status502BadGateway)
        : Results.Ok(result);
});

app.Run();

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

static bool HasAggregateStateSelector(ZabbixTriggerDependenciesOptions options)
{
    return options.AggregateStateTriggerIncludeTags.Any(selector => !string.IsNullOrWhiteSpace(selector.Tag))
        || !string.IsNullOrWhiteSpace(options.AggregateStateTriggerIncludeNameRegex);
}

static bool HasValidTriggerSelectorRegex(ZabbixTriggerDependenciesOptions options)
{
    return IsValidRegex(options.AggregateStateTriggerIncludeNameRegex)
        && IsValidRegex(options.AggregateStateTriggerExcludeNameRegex)
        && IsValidRegex(options.DependencyTriggerIncludeNameRegex)
        && IsValidRegex(options.DependencyTriggerExcludeNameRegex);
}

static bool IsValidRegex(string pattern)
{
    if (string.IsNullOrWhiteSpace(pattern))
    {
        return true;
    }

    try
    {
        _ = new Regex(pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250));
        return true;
    }
    catch (ArgumentException)
    {
        return false;
    }
}

static HashSet<string> NormalizeManagedKeys(IEnumerable<string>? values)
{
    return (values ?? [])
        .Select(value => (value ?? "").Trim())
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .ToHashSet(StringComparer.Ordinal);
}

static ZabbixManagedServiceSnapshot ToManagedServiceSnapshot(ZabbixServiceInfo service)
{
    var tags = service.Tags ?? new Dictionary<string, string>(StringComparer.Ordinal);
    return new ZabbixManagedServiceSnapshot
    {
        ServiceId = service.ServiceId,
        Name = service.Name,
        ManagedKey = tags.GetValueOrDefault(ZabbixManagedServiceTags.Key) ?? "",
        ClassCode = tags.GetValueOrDefault(ZabbixManagedServiceTags.Class) ?? "",
        CardId = tags.GetValueOrDefault(ZabbixManagedServiceTags.CardId) ?? "",
        RuleId = tags.GetValueOrDefault(ZabbixManagedServiceTags.RuleId) ?? "",
        RuleName = tags.GetValueOrDefault(ZabbixManagedServiceTags.RuleName) ?? "",
        SourceLeaf = tags.GetValueOrDefault(ZabbixManagedServiceTags.SourceLeaf) ?? "",
        Role = tags.GetValueOrDefault(ZabbixManagedServiceTags.Role) ?? "",
        Visibility = tags.GetValueOrDefault(ZabbixManagedServiceTags.Visibility) ?? "",
        EffectiveRole = EffectiveManagedRole(
            tags.GetValueOrDefault(ZabbixManagedServiceTags.Role) ?? "",
            tags.GetValueOrDefault(ZabbixManagedServiceTags.Visibility) ?? "",
            tags.GetValueOrDefault(ZabbixManagedServiceTags.SourceLeaf) ?? "",
            tags.GetValueOrDefault(ZabbixManagedServiceTags.Class) ?? "").Role,
        EffectiveVisibility = EffectiveManagedRole(
            tags.GetValueOrDefault(ZabbixManagedServiceTags.Role) ?? "",
            tags.GetValueOrDefault(ZabbixManagedServiceTags.Visibility) ?? "",
            tags.GetValueOrDefault(ZabbixManagedServiceTags.SourceLeaf) ?? "",
            tags.GetValueOrDefault(ZabbixManagedServiceTags.Class) ?? "").Visibility,
        AggregateKind = tags.GetValueOrDefault(ZabbixManagedServiceTags.AggregateKind) ?? "",
        ParentCount = service.Parents.Count,
        ChildCount = service.Children.Count
    };
}

static (string Role, string Visibility) EffectiveManagedRole(
    string role,
    string visibility,
    string sourceLeaf,
    string classCode)
{
    string effectiveRole;
    if (!string.IsNullOrWhiteSpace(role))
    {
        effectiveRole = role;
    }
    else if (sourceLeaf.Equals("true", StringComparison.OrdinalIgnoreCase))
    {
        effectiveRole = ZabbixManagedServiceRoles.SourceLeaf;
    }
    else
    {
        effectiveRole = classCode.EndsWith("ServicePlatformService", StringComparison.OrdinalIgnoreCase)
            ? ZabbixManagedServiceRoles.RootService
            : ZabbixManagedServiceRoles.Aggregate;
    }

    var effectiveVisibility = !string.IsNullOrWhiteSpace(visibility)
        ? visibility
        : effectiveRole switch
    {
        ZabbixManagedServiceRoles.RootService => ZabbixManagedServiceVisibility.Root,
        ZabbixManagedServiceRoles.SourceLeaf or ZabbixManagedServiceRoles.Internal => ZabbixManagedServiceVisibility.Internal,
        _ => ZabbixManagedServiceVisibility.Child
    };
    return (effectiveRole, effectiveVisibility);
}

public sealed class ZabbixServiceAggregationCommandWorker(
    IOptions<KafkaOptions> kafkaOptions,
    IOptions<KafkaTopicsOptions> topicOptions,
    IOptionsMonitor<ApplyOptions> applyOptions,
    IOptions<DebugOptions> debugOptions,
    ZabbixApplyStateStore state,
    ZabbixAggregationApplier applier,
    ILogger<ZabbixServiceAggregationCommandWorker> logger)
    : ZabbixLayerAggregationCommandWorker("service", kafkaOptions, topicOptions, applyOptions, debugOptions, state, applier, logger);

public sealed class ZabbixSuppressionAggregationCommandWorker(
    IOptions<KafkaOptions> kafkaOptions,
    IOptions<KafkaTopicsOptions> topicOptions,
    IOptionsMonitor<ApplyOptions> applyOptions,
    IOptions<DebugOptions> debugOptions,
    ZabbixApplyStateStore state,
    ZabbixAggregationApplier applier,
    ILogger<ZabbixSuppressionAggregationCommandWorker> logger)
    : ZabbixLayerAggregationCommandWorker("suppression", kafkaOptions, topicOptions, applyOptions, debugOptions, state, applier, logger);

public abstract class ZabbixLayerAggregationCommandWorker : KafkaJsonConsumerWorker<AggregationCommand>
{
    private readonly string layer;
    private readonly IOptions<KafkaOptions> kafkaOptions;
    private readonly IOptions<KafkaTopicsOptions> topicOptions;
    private readonly IOptionsMonitor<ApplyOptions> applyOptions;
    private readonly IOptions<DebugOptions> debugOptions;
    private readonly ZabbixApplyStateStore state;
    private readonly ZabbixAggregationApplier applier;
    private readonly ILogger logger;

    protected ZabbixLayerAggregationCommandWorker(
        string layer,
        IOptions<KafkaOptions> kafkaOptions,
        IOptions<KafkaTopicsOptions> topicOptions,
        IOptionsMonitor<ApplyOptions> applyOptions,
        IOptions<DebugOptions> debugOptions,
        ZabbixApplyStateStore state,
        ZabbixAggregationApplier applier,
        ILogger logger)
        : base(kafkaOptions, logger)
    {
        this.layer = layer;
        this.kafkaOptions = kafkaOptions;
        this.topicOptions = topicOptions;
        this.applyOptions = applyOptions;
        this.debugOptions = debugOptions;
        this.state = state;
        this.applier = applier;
        this.logger = logger;
    }

    protected override string Topic => topicOptions.Value.EffectiveZabbixApplyPlans(layer);

    protected override string ConsumerGroupId => $"{kafkaOptions.Value.ConsumerGroupId}-{layer}";

    protected override async Task HandleMessageAsync(
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
            return;
        }

        var plan = ZabbixApplyPlanner.Plan(message, layer, Topic, applyOptions.CurrentValue, forceDryRun: false);
        var result = string.Equals(plan.Status, "accepted", StringComparison.OrdinalIgnoreCase)
            ? await applier.ApplyAsync(message, layer, Topic, applyOptions.CurrentValue, cancellationToken)
            : plan;
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
            TargetKey = TargetKey(command),
            SourceClass = command.Source.ClassCode,
            SourceCardId = command.Source.CardId,
            Reconcile = ReconcileCounters(command, layer, options),
            Membership = ZabbixMembershipPreview(command, layer, ShouldCreateManagedServices(layer, options)),
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
            TargetKey = TargetKey(command),
            SourceClass = command.Source.ClassCode,
            SourceCardId = command.Source.CardId,
            Error = $"Команда слоя '{command.Layer}' попала в топик слоя '{expectedLayer}'.",
            AppliedAtUtc = DateTimeOffset.UtcNow
        };
    }

    private static ZabbixReconcileCounters ReconcileCounters(
        AggregationCommand command,
        string layer,
        ApplyOptions options)
    {
        var relationCount = command.Target.Relations.Count;
        if (string.Equals(command.CommandType, AggregationCommandTypes.RemoveSourceMembership, StringComparison.OrdinalIgnoreCase))
        {
            return new ZabbixReconcileCounters
            {
                RemoveMembershipSources = string.IsNullOrWhiteSpace(command.Source.CardId) ? 0 : 1
            };
        }

        if (string.Equals(layer, "suppression", StringComparison.OrdinalIgnoreCase)
            && !options.CreateSuppressionServices)
        {
            if (string.Equals(command.CommandType, AggregationCommandTypes.RemoveMembership, StringComparison.OrdinalIgnoreCase))
            {
                return new ZabbixReconcileCounters
                {
                    RemoveMembershipSources = string.IsNullOrWhiteSpace(command.Source.CardId) ? 0 : 1
                };
            }

            return new ZabbixReconcileCounters
            {
                EnsureMembershipTargets = string.IsNullOrWhiteSpace(command.Target.ClassCode) ? 0 : 1,
                EnsureMembershipSources = string.IsNullOrWhiteSpace(command.Source.CardId) ? 0 : 1,
                EnsureMembershipRelations = relationCount
            };
        }

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
            EnsureRelations = relationCount,
            EnsureSourceLeafServices = string.IsNullOrWhiteSpace(command.Source.CardId)
                || string.IsNullOrWhiteSpace(command.Source.ZabbixHostId) ? 0 : 1,
            EnsureProblemTags = string.IsNullOrWhiteSpace(command.Source.ZabbixHostId) ? 0 : 1,
            EnsureHostTags = string.IsNullOrWhiteSpace(command.Source.ZabbixHostId) ? 0 : 1
        };
    }

    private static string TargetKey(AggregationCommand command)
    {
        if (string.Equals(command.CommandType, AggregationCommandTypes.RemoveSourceMembership, StringComparison.OrdinalIgnoreCase))
        {
            return $"{command.Layer}:{command.Source.ClassCode}:{command.Source.CardId}";
        }

        return string.IsNullOrWhiteSpace(command.Target.CardId)
            ? command.Target.IdempotencyKey
            : command.Target.CardId;
    }

    public static bool ShouldCreateManagedServices(string layer, ApplyOptions options)
    {
        return !string.Equals(layer, "suppression", StringComparison.OrdinalIgnoreCase)
            || options.CreateSuppressionServices;
    }

    private static ZabbixTargetMembershipSnapshot ZabbixMembershipPreview(
        AggregationCommand command,
        string layer,
        bool includeSourceLeafManagedKey)
    {
        if (string.Equals(command.CommandType, AggregationCommandTypes.RemoveSourceMembership, StringComparison.OrdinalIgnoreCase))
        {
            return new ZabbixTargetMembershipSnapshot
            {
                Layer = layer,
                TargetName = $"source {command.Source.ClassCode}/{command.Source.CardId}",
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };
        }

        if (string.IsNullOrWhiteSpace(command.Source.CardId))
        {
            return new ZabbixTargetMembershipSnapshot();
        }

        var source = new ZabbixSourceMembership
        {
            SourceClass = command.Source.ClassCode,
            SourceCardId = command.Source.CardId,
            SourceKeyAttribute = command.Source.KeyAttribute,
            SourceKeyValue = command.Source.KeyValue,
            ZabbixHostId = command.Source.ZabbixHostId,
            SourceLeafManagedKey = !includeSourceLeafManagedKey || string.IsNullOrWhiteSpace(command.Source.ZabbixHostId)
                ? ""
                : ZabbixManagedServiceMapper.SourceLeafManagedKey(layer, command.Source),
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
        var hasHostBinding = !string.IsNullOrWhiteSpace(command.Source.ZabbixHostId);

        return new ZabbixTargetMembershipSnapshot
        {
            Layer = layer,
            TargetManagedKey = ZabbixManagedServiceMapper.ManagedKey(command.Target),
            TargetClass = command.Target.ClassCode,
            TargetCardId = command.Target.CardId,
            TargetName = command.RuleName,
            AggregationType = TargetAttribute(command.Target.Attributes, "aggregation_type"),
            IsCritical = TargetAttribute(command.Target.Attributes, "is_critical"),
            Threshold = TargetAttribute(command.Target.Attributes, "threshold"),
            N = TargetAttribute(command.Target.Attributes, "n"),
            SourceCount = hasHostBinding ? 1 : 0,
            HostBindingCount = hasHostBinding ? 1 : 0,
            MissingHostBindingCount = hasHostBinding ? 0 : 1,
            PendingSourceCount = hasHostBinding ? 0 : 1,
            SourceLeafManagedKeys = hasHostBinding && !string.IsNullOrWhiteSpace(source.SourceLeafManagedKey)
                ? [source.SourceLeafManagedKey]
                : [],
            Sources = hasHostBinding ? [source] : [],
            PendingSources = hasHostBinding ? [] : [source],
            Relations = command.Target.Relations
                .Select(relation => new ZabbixMembershipRelation
                {
                    DomainCode = relation.DomainCode,
                    TargetClassCode = relation.TargetClassCode,
                    TargetLookup = relation.TargetLookup
                })
                .ToArray(),
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
    }

    private static string TargetAttribute(IReadOnlyDictionary<string, object?> attributes, string name)
    {
        foreach (var attribute in attributes)
        {
            if (string.Equals(attribute.Key, name, StringComparison.OrdinalIgnoreCase))
            {
                return ScalarString(attribute.Value);
            }
        }

        return "";
    }

    private static string ScalarString(object? value)
    {
        return value switch
        {
            null => "",
            string text => text.Trim(),
            bool boolean => boolean ? "true" : "false",
            JsonElement element => element.ValueKind switch
            {
                JsonValueKind.String => element.GetString()?.Trim() ?? "",
                JsonValueKind.Number => element.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => ""
            },
            IFormattable formattable => formattable.ToString(null, System.Globalization.CultureInfo.InvariantCulture) ?? "",
            _ => value.ToString()?.Trim() ?? ""
        };
    }
}

public sealed class ZabbixStateStaleReportRequest
{
    public string Layer { get; init; } = "";

    public List<string> DesiredManagedKeys { get; init; } = [];

    public bool IncludeZabbixServices { get; init; }

    public int ZabbixServiceLimit { get; init; } = 2000;

    public int SampleLimit { get; init; } = 100;
}

public sealed class ZabbixStateCleanupRequest
{
    public string Layer { get; init; } = "";

    public List<string> ManagedKeys { get; init; } = [];

    public bool DryRun { get; init; }
}

public sealed class ZabbixStaleZabbixDeleteRequest
{
    public string Layer { get; init; } = "";

    public List<string> ManagedKeys { get; init; } = [];
}

public sealed class ZabbixManagedServiceSnapshot
{
    public string ServiceId { get; init; } = "";

    public string Name { get; init; } = "";

    public string ManagedKey { get; init; } = "";

    public string ClassCode { get; init; } = "";

    public string CardId { get; init; } = "";

    public string RuleId { get; init; } = "";

    public string RuleName { get; init; } = "";

    public string SourceLeaf { get; init; } = "";

    public string Role { get; init; } = "";

    public string Visibility { get; init; } = "";

    public string EffectiveRole { get; init; } = "";

    public string EffectiveVisibility { get; init; } = "";

    public string AggregateKind { get; init; } = "";

    public int ParentCount { get; init; }

    public int ChildCount { get; init; }
}

public sealed class ZabbixStateStaleMembershipReport
{
    public string Layer { get; init; } = "";

    public int MembershipTargetCount { get; init; }

    public int StaleTargetCount { get; init; }

    public IReadOnlyList<string> StaleTargetKeys { get; init; } = [];

    public IReadOnlyList<ZabbixTargetMembershipSummary> StaleTargets { get; init; } = [];
}

public sealed class ZabbixTargetMembershipSummary
{
    public string Layer { get; init; } = "";

    public string TargetManagedKey { get; init; } = "";

    public string TargetClass { get; init; } = "";

    public string TargetCardId { get; init; } = "";

    public string TargetName { get; init; } = "";

    public string AggregationType { get; init; } = "";

    public string IsCritical { get; init; } = "";

    public string Threshold { get; init; } = "";

    public string N { get; init; } = "";

    public int SourceCount { get; init; }

    public int HostBindingCount { get; init; }

    public int PendingSourceCount { get; init; }
}

public sealed class ZabbixStateCleanupResult
{
    public string Layer { get; init; } = "";

    public bool DryRun { get; init; }

    public int Requested { get; init; }

    public int Matched { get; init; }

    public int Removed { get; init; }

    public IReadOnlyList<ZabbixTargetMembershipSnapshot> Targets { get; init; } = [];

    public IReadOnlyList<string> MissingKeys { get; init; } = [];
}

public sealed class ZabbixApplyStateStore
{
    private static readonly JsonSerializerOptions StateJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string stateFilePath;
    private readonly ILogger<ZabbixApplyStateStore> logger;
    private readonly object membershipLock = new();
    private readonly ConcurrentDictionary<string, ZabbixLayerApplyStatus> layers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ZabbixTargetMembership> memberships = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ZabbixAppliedGraphObject> appliedGraphObjects = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ZabbixTriggerDependencyLayerStatus> triggerDependencyLayers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ZabbixManagedTriggerDependency> triggerDependencies = new(StringComparer.Ordinal);

    public ZabbixApplyStateStore(
        IOptions<ZabbixApplyStateOptions> options,
        ILogger<ZabbixApplyStateStore> logger)
    {
        stateFilePath = options.Value.FilePath;
        this.logger = logger;
        layers.TryAdd("service", new ZabbixLayerApplyStatus { Layer = "service" });
        layers.TryAdd("suppression", new ZabbixLayerApplyStatus { Layer = "suppression" });
        triggerDependencyLayers.TryAdd("suppression", new ZabbixTriggerDependencyLayerStatus { Layer = "suppression" });
        LoadMemberships();
    }

    public ZabbixMembershipUpdateResult UpdateMembership(
        AggregationCommand command,
        string layer,
        bool includeSourceLeafManagedKey = true)
    {
        var normalizedLayer = ZabbixApplyPlanner.NormalizeLayer(layer);
        var targetManagedKey = ZabbixManagedServiceMapper.ManagedKey(command.Target);
        if (string.IsNullOrWhiteSpace(normalizedLayer))
        {
            return new ZabbixMembershipUpdateResult();
        }

        lock (membershipLock)
        {
            var sourceKey = SourceMembershipKey(command.Source);
            if (string.Equals(command.CommandType, AggregationCommandTypes.RemoveSourceMembership, StringComparison.OrdinalIgnoreCase))
            {
                var affected = RemoveSourceFromLayer(normalizedLayer, sourceKey);
                SaveMemberships();
                return new ZabbixMembershipUpdateResult
                {
                    Current = affected.FirstOrDefault() ?? new ZabbixTargetMembershipSnapshot { Layer = normalizedLayer },
                    AffectedTargets = affected,
                    RemovedSourceMemberships = affected.Count
                };
            }

            if (string.IsNullOrWhiteSpace(targetManagedKey))
            {
                return new ZabbixMembershipUpdateResult();
            }

            var membershipKey = MembershipKey(normalizedLayer, targetManagedKey);
            var membership = memberships.GetOrAdd(
                membershipKey,
                _ => new ZabbixTargetMembership
                {
                    Layer = normalizedLayer,
                    TargetManagedKey = targetManagedKey,
                    TargetClass = command.Target.ClassCode,
                    TargetCardId = command.Target.CardId,
                    TargetName = TargetObjectName(command)
                });
            membership.Layer = normalizedLayer;
            membership.TargetManagedKey = targetManagedKey;
            membership.TargetClass = command.Target.ClassCode;
            membership.TargetCardId = command.Target.CardId;
            membership.TargetName = TargetObjectName(command);
            membership.AggregationType = TargetAttribute(command.Target.Attributes, "aggregation_type");
            membership.IsCritical = TargetAttribute(command.Target.Attributes, "is_critical");
            membership.Threshold = TargetAttribute(command.Target.Attributes, "threshold");
            membership.N = TargetAttribute(command.Target.Attributes, "n");
            membership.UpdatedAtUtc = DateTimeOffset.UtcNow;
            membership.Relations = command.Target.Relations
                .Select(relation => new ZabbixMembershipRelation
                {
                    DomainCode = relation.DomainCode,
                    TargetClassCode = relation.TargetClassCode,
                    TargetLookup = relation.TargetLookup
                })
                .DistinctBy(relation => $"{relation.DomainCode}\u001f{relation.TargetClassCode}\u001f{relation.TargetLookup}", StringComparer.Ordinal)
                .ToList();

            var affectedTargets = new List<ZabbixTargetMembershipSnapshot>();
            var removedSourceMemberships = 0;
            if (!string.IsNullOrWhiteSpace(sourceKey))
            {
                if (string.Equals(command.CommandType, AggregationCommandTypes.RemoveMembership, StringComparison.OrdinalIgnoreCase))
                {
                    if (RemoveSourceFromMembership(membership, sourceKey))
                    {
                        removedSourceMemberships++;
                    }
                }
                else
                {
                    affectedTargets.AddRange(RemoveSourceFromLayer(normalizedLayer, sourceKey, membershipKey));
                    removedSourceMemberships += affectedTargets.Count;
                    if (string.IsNullOrWhiteSpace(command.Source.ZabbixHostId))
                    {
                        membership.Sources.Remove(sourceKey);
                        membership.PendingSources[sourceKey] = new ZabbixSourceMembership
                        {
                            SourceClass = command.Source.ClassCode,
                            SourceCardId = command.Source.CardId,
                            SourceKeyAttribute = command.Source.KeyAttribute,
                            SourceKeyValue = command.Source.KeyValue,
                            ZabbixHostId = "",
                            SourceLeafManagedKey = "",
                            UpdatedAtUtc = DateTimeOffset.UtcNow
                        };
                    }
                    else
                    {
                        membership.PendingSources.Remove(sourceKey);
                        var sourceLeafManagedKey = includeSourceLeafManagedKey
                            ? ZabbixManagedServiceMapper.SourceLeafManagedKey(normalizedLayer, command.Source)
                            : "";
                        membership.Sources[sourceKey] = new ZabbixSourceMembership
                        {
                            SourceClass = command.Source.ClassCode,
                            SourceCardId = command.Source.CardId,
                            SourceKeyAttribute = command.Source.KeyAttribute,
                            SourceKeyValue = command.Source.KeyValue,
                            ZabbixHostId = command.Source.ZabbixHostId,
                            SourceLeafManagedKey = sourceLeafManagedKey,
                            UpdatedAtUtc = DateTimeOffset.UtcNow
                        };
                    }
                }
            }

            SaveMemberships();
            var current = membership.ToSnapshot();
            return new ZabbixMembershipUpdateResult
            {
                Current = current,
                AffectedTargets = affectedTargets
                    .Concat([current])
                    .DistinctBy(item => item.TargetManagedKey, StringComparer.Ordinal)
                    .ToArray(),
                RemovedSourceMemberships = removedSourceMemberships
            };
        }
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
            status.LastMembership = result.Membership;
            status.LastPerformance = result.Performance;
            status.Performance.Add(result.Performance);
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
            else if (string.Equals(result.Status, "applied", StringComparison.OrdinalIgnoreCase))
            {
                status.AppliedCommands++;
            }
            else if (string.Equals(result.Status, "partial", StringComparison.OrdinalIgnoreCase))
            {
                status.PartialCommands++;
            }
            else if (string.Equals(result.Status, "skipped", StringComparison.OrdinalIgnoreCase))
            {
                status.SkippedCommands++;
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

            foreach (var warning in result.Warnings)
            {
                status.Warnings.Insert(0, $"{result.AppliedAtUtc:O}: {warning}");
                if (status.Warnings.Count > 20)
                {
                    status.Warnings.RemoveRange(20, status.Warnings.Count - 20);
                }
            }
        }
    }

    public ZabbixGraphDiffResult DiffAppliedGraph(
        string layer,
        IReadOnlyList<ZabbixAppliedGraphObject> desiredObjects,
        string publishMode,
        int sampleLimit,
        IReadOnlySet<string>? scopeTargetManagedKeys = null)
    {
        var normalizedLayer = ZabbixApplyPlanner.NormalizeLayer(layer);
        var effectivePublishMode = ZabbixGraphPublishModes.Normalize(publishMode);
        if (string.IsNullOrWhiteSpace(normalizedLayer))
        {
            return new ZabbixGraphDiffResult { Layer = layer, PublishMode = effectivePublishMode };
        }

        lock (membershipLock)
        {
            var desiredByKey = desiredObjects
                .Where(item => item.Layer.Equals(normalizedLayer, StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(item.ObjectKey))
                .GroupBy(item => item.ObjectKey, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);
            var appliedByKey = appliedGraphObjects.Values
                .Where(item => item.Layer.Equals(normalizedLayer, StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(item.ObjectKey)
                    && (scopeTargetManagedKeys is null
                        || desiredByKey.ContainsKey(item.ObjectKey)
                        || scopeTargetManagedKeys.Contains(item.TargetManagedKey)))
                .GroupBy(item => item.ObjectKey, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);
            var candidateKeys = new HashSet<string>(StringComparer.Ordinal);
            var samples = new List<ZabbixGraphDiffSample>();
            var added = 0;
            var changed = 0;
            var unchanged = 0;
            foreach (var pair in desiredByKey.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                if (!appliedByKey.TryGetValue(pair.Key, out var applied))
                {
                    added++;
                    candidateKeys.Add(pair.Key);
                    AddDiffSample(samples, "added", pair.Value, sampleLimit);
                    continue;
                }

                if (!string.Equals(pair.Value.ContentHash, applied.ContentHash, StringComparison.Ordinal))
                {
                    changed++;
                    candidateKeys.Add(pair.Key);
                    AddDiffSample(samples, "changed", pair.Value, sampleLimit);
                    continue;
                }

                unchanged++;
                if (ZabbixGraphPublishModes.IsFull(effectivePublishMode))
                {
                    candidateKeys.Add(pair.Key);
                }
            }

            var removed = 0;
            foreach (var pair in appliedByKey.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                if (desiredByKey.ContainsKey(pair.Key))
                {
                    continue;
                }

                removed++;
                AddDiffSample(samples, "removed", pair.Value, sampleLimit);
            }

            return new ZabbixGraphDiffResult
            {
                Layer = normalizedLayer,
                PublishMode = effectivePublishMode,
                Desired = desiredByKey.Count,
                Applied = appliedByKey.Count,
                Added = added,
                Changed = changed,
                Unchanged = unchanged,
                Removed = removed,
                PublishCandidates = candidateKeys.Count,
                Samples = samples,
                CandidateObjectKeySet = candidateKeys
            };
        }
    }

    public void ReplaceAppliedGraph(string layer, IReadOnlyList<ZabbixAppliedGraphObject> desiredObjects)
    {
        var normalizedLayer = ZabbixApplyPlanner.NormalizeLayer(layer);
        if (string.IsNullOrWhiteSpace(normalizedLayer))
        {
            return;
        }

        lock (membershipLock)
        {
            foreach (var key in appliedGraphObjects
                .Where(pair => pair.Value.Layer.Equals(normalizedLayer, StringComparison.OrdinalIgnoreCase))
                .Select(pair => pair.Key)
                .ToArray())
            {
                appliedGraphObjects.TryRemove(key, out _);
            }

            foreach (var graphObject in desiredObjects
                .Where(item => item.Layer.Equals(normalizedLayer, StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(item.ObjectKey)))
            {
                graphObject.Layer = normalizedLayer;
                graphObject.UpdatedAtUtc = DateTimeOffset.UtcNow;
                appliedGraphObjects[AppliedGraphObjectKey(normalizedLayer, graphObject.ObjectKey)] = graphObject;
            }

            SaveMemberships();
        }
    }

    public void UpsertAppliedGraph(string layer, IReadOnlyList<ZabbixAppliedGraphObject> desiredObjects)
    {
        var normalizedLayer = ZabbixApplyPlanner.NormalizeLayer(layer);
        if (string.IsNullOrWhiteSpace(normalizedLayer))
        {
            return;
        }

        lock (membershipLock)
        {
            foreach (var graphObject in desiredObjects
                .Where(item => item.Layer.Equals(normalizedLayer, StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(item.ObjectKey)))
            {
                graphObject.Layer = normalizedLayer;
                graphObject.UpdatedAtUtc = DateTimeOffset.UtcNow;
                appliedGraphObjects[AppliedGraphObjectKey(normalizedLayer, graphObject.ObjectKey)] = graphObject;
            }

            SaveMemberships();
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
            createSuppressionServices = options.CreateSuppressionServices,
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
                createSuppressionServices = options.CreateSuppressionServices,
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
                appliedCommands = status.AppliedCommands,
                partialCommands = status.PartialCommands,
                skippedCommands = status.SkippedCommands,
                pendingManualCommands = status.PendingManualCommands,
                errorCommands = status.ErrorCommands,
                reconcile = status.Reconcile,
                performance = status.Performance,
                lastPerformance = status.LastPerformance,
                membership = status.LastMembership,
                membershipTargets = MembershipSnapshots(layer),
                appliedGraphObjectCount = AppliedGraphObjectCount(layer),
                errors = status.Errors.ToArray(),
                warnings = status.Warnings.ToArray()
            };
        }
    }

    public IReadOnlyList<ZabbixTargetMembershipSnapshot> ListMemberships(string layer)
    {
        lock (membershipLock)
        {
            return memberships.Values
                .Where(item => item.Layer.Equals(layer, StringComparison.OrdinalIgnoreCase))
                .OrderBy(item => item.TargetName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.TargetManagedKey, StringComparer.Ordinal)
                .Select(item => item.ToSnapshot())
                .ToArray();
        }
    }

    public ZabbixStateStaleMembershipReport StaleMembershipTargets(
        string layer,
        IReadOnlySet<string> desiredManagedKeys,
        int sampleLimit)
    {
        var normalizedLayer = ZabbixApplyPlanner.NormalizeLayer(layer);
        if (string.IsNullOrWhiteSpace(normalizedLayer))
        {
            return new ZabbixStateStaleMembershipReport { Layer = layer };
        }

        lock (membershipLock)
        {
            var layerMemberships = memberships.Values
                .Where(item => item.Layer.Equals(normalizedLayer, StringComparison.OrdinalIgnoreCase))
                .OrderBy(item => item.TargetName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.TargetManagedKey, StringComparer.Ordinal)
                .ToArray();
            var staleTargets = layerMemberships
                .Where(item => !string.IsNullOrWhiteSpace(item.TargetManagedKey)
                    && !desiredManagedKeys.Contains(item.TargetManagedKey))
                .ToArray();
            return new ZabbixStateStaleMembershipReport
            {
                Layer = normalizedLayer,
                MembershipTargetCount = layerMemberships.Length,
                StaleTargetCount = staleTargets.Length,
                StaleTargetKeys = staleTargets
                    .Select(item => item.TargetManagedKey)
                    .Where(item => !string.IsNullOrWhiteSpace(item))
                    .ToArray(),
                StaleTargets = staleTargets
                    .Take(Math.Max(1, sampleLimit))
                    .Select(ToMembershipSummary)
                    .ToArray()
            };
        }
    }

    public ZabbixStateCleanupResult CleanupMembershipTargets(
        string layer,
        IReadOnlySet<string> managedKeys,
        bool dryRun)
    {
        var normalizedLayer = ZabbixApplyPlanner.NormalizeLayer(layer);
        if (string.IsNullOrWhiteSpace(normalizedLayer))
        {
            return new ZabbixStateCleanupResult { Layer = layer, DryRun = dryRun };
        }

        lock (membershipLock)
        {
            var targets = new List<ZabbixTargetMembershipSnapshot>();
            var missingKeys = new List<string>();
            var removed = 0;
            foreach (var managedKey in managedKeys.OrderBy(item => item, StringComparer.Ordinal))
            {
                var key = MembershipKey(normalizedLayer, managedKey);
                if (!memberships.TryGetValue(key, out var membership))
                {
                    missingKeys.Add(managedKey);
                    continue;
                }

                targets.Add(membership.ToSnapshot());
                if (!dryRun && memberships.TryRemove(key, out _))
                {
                    removed++;
                }
            }

            if (!dryRun && removed > 0)
            {
                SaveMemberships();
            }

            return new ZabbixStateCleanupResult
            {
                Layer = normalizedLayer,
                DryRun = dryRun,
                Requested = managedKeys.Count,
                Matched = targets.Count,
                Removed = removed,
                Targets = targets,
                MissingKeys = missingKeys
            };
        }
    }

    public ZabbixSourceHostBindingCleanupResult RemoveSourceHostBindings(
        string layer,
        IReadOnlySet<string> zabbixHostIds)
    {
        var normalizedLayer = ZabbixApplyPlanner.NormalizeLayer(layer);
        var requestedHostIds = zabbixHostIds
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item.Trim())
            .ToHashSet(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(normalizedLayer) || requestedHostIds.Count == 0)
        {
            return new ZabbixSourceHostBindingCleanupResult
            {
                Layer = normalizedLayer,
                RequestedHostIds = requestedHostIds.Count
            };
        }

        lock (membershipLock)
        {
            var removed = 0;
            var removedHostIds = new HashSet<string>(StringComparer.Ordinal);
            var affectedTargets = new List<ZabbixTargetMembershipSnapshot>();
            foreach (var membership in memberships.Values
                .Where(item => item.Layer.Equals(normalizedLayer, StringComparison.OrdinalIgnoreCase)))
            {
                var sourceKeys = membership.Sources
                    .Where(pair => requestedHostIds.Contains(pair.Value.ZabbixHostId))
                    .Select(pair => pair.Key)
                    .ToArray();
                if (sourceKeys.Length == 0)
                {
                    continue;
                }

                foreach (var sourceKey in sourceKeys)
                {
                    if (!membership.Sources.TryGetValue(sourceKey, out var source)
                        || !membership.Sources.Remove(sourceKey))
                    {
                        continue;
                    }

                    removed++;
                    if (!string.IsNullOrWhiteSpace(source.ZabbixHostId))
                    {
                        removedHostIds.Add(source.ZabbixHostId);
                    }
                }

                membership.UpdatedAtUtc = DateTimeOffset.UtcNow;
                affectedTargets.Add(membership.ToSnapshot());
            }

            if (removed > 0)
            {
                SaveMemberships();
            }

            return new ZabbixSourceHostBindingCleanupResult
            {
                Layer = normalizedLayer,
                RequestedHostIds = requestedHostIds.Count,
                RemovedSourceMemberships = removed,
                RemovedHostIds = removedHostIds.OrderBy(item => item, StringComparer.Ordinal).ToArray(),
                AffectedTargets = affectedTargets
                    .OrderBy(item => item.TargetName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(item => item.TargetManagedKey, StringComparer.Ordinal)
                    .ToArray()
            };
        }
    }

    public IReadOnlyList<ZabbixManagedTriggerDependency> ListManagedTriggerDependencies(string layer)
    {
        lock (membershipLock)
        {
            return triggerDependencies.Values
                .Where(item => item.Layer.Equals(layer, StringComparison.OrdinalIgnoreCase))
                .OrderBy(item => item.DependentTriggerId, StringComparer.Ordinal)
                .ThenBy(item => item.DependencyTriggerId, StringComparer.Ordinal)
                .ToArray();
        }
    }

    public void ReplaceManagedTriggerDependencies(
        string layer,
        IReadOnlyList<ZabbixManagedTriggerDependency> dependencies)
    {
        var normalizedLayer = ZabbixApplyPlanner.NormalizeLayer(layer);
        if (string.IsNullOrWhiteSpace(normalizedLayer))
        {
            return;
        }

        lock (membershipLock)
        {
            foreach (var key in triggerDependencies
                .Where(pair => pair.Value.Layer.Equals(normalizedLayer, StringComparison.OrdinalIgnoreCase))
                .Select(pair => pair.Key)
                .ToArray())
            {
                triggerDependencies.TryRemove(key, out _);
            }

            foreach (var dependency in dependencies)
            {
                if (string.IsNullOrWhiteSpace(dependency.DependentTriggerId)
                    || string.IsNullOrWhiteSpace(dependency.DependencyTriggerId))
                {
                    continue;
                }

                dependency.Layer = normalizedLayer;
                dependency.UpdatedAtUtc = DateTimeOffset.UtcNow;
                triggerDependencies[TriggerDependencyKey(dependency)] = dependency;
            }

            SaveMemberships();
        }
    }

    public void RecordTriggerDependencyRun(ZabbixTriggerDependencyRunResult result)
    {
        var status = triggerDependencyLayers.GetOrAdd(
            result.Layer,
            layer => new ZabbixTriggerDependencyLayerStatus { Layer = layer });
        lock (status)
        {
            status.LastUpdatedAtUtc = result.CompletedAtUtc;
            status.LastStatus = result.Status;
            status.LastMode = result.DryRun ? "dry-run" : "apply";
            status.LastMessage = result.Message;
            status.AggregateStateTriggerSelectorSummary = result.AggregateStateTriggerSelectorSummary;
            status.DependencyTriggerSelectorSummary = result.DependencyTriggerSelectorSummary;
            status.TriggerGetBatchSize = result.TriggerGetBatchSize;
            status.TriggerGetBatchCount = result.TriggerGetBatchCount;
            status.TriggerGetElapsedMs = result.TriggerGetElapsedMs;
            status.ZabbixRequestTimeoutMs = result.ZabbixRequestTimeoutMs;
            status.StaleSourceHostBindingCount = result.StaleSourceHostBindingCount;
            status.StaleSourceMembershipsRemoved = result.StaleSourceMembershipsRemoved;
            status.MaxSourceHostsPerAggregate = result.MaxSourceHostsPerAggregate;
            status.MaxAggregateFormulaLength = result.MaxAggregateFormulaLength;
            status.LargestAggregateSourceHostCount = result.LargestAggregateSourceHostCount;
            status.LargestAggregateFormulaLength = result.LargestAggregateFormulaLength;
            status.LargestAggregateTriggerExpressionLength = result.LargestAggregateTriggerExpressionLength;
            status.AggregateComplexityWarningCount = result.AggregateComplexityWarningCount;
            status.AggregateComplexityErrorCount = result.AggregateComplexityErrorCount;
            status.DesiredDependencyCount = result.DesiredDependencyCount;
            status.DependentTriggerCount = result.DependentTriggerCount;
            status.DependencyTriggerCount = result.DependencyTriggerCount;
            status.AggregateCount = result.AggregateCount;
            status.AggregateHostsCreated = result.AggregateHostsCreated;
            status.AggregateItemsCreated = result.AggregateItemsCreated;
            status.AggregateItemsUpdated = result.AggregateItemsUpdated;
            status.AggregateTriggersCreated = result.AggregateTriggersCreated;
            status.AggregateTriggersUpdated = result.AggregateTriggersUpdated;
            status.TriggersToUpdate = result.TriggersToUpdate;
            status.TriggersUpdated = result.TriggersUpdated;
            status.DependenciesAdded = result.DependenciesAdded;
            status.DependenciesRemoved = result.DependenciesRemoved;
            status.PreservedManualDependencies = result.PreservedManualDependencies;
            status.SelectedSourceTriggerCount = result.SelectedSourceTriggerCount;
            status.SkippedSourceTriggerCount = result.SkippedSourceTriggerCount;
            status.UnsupportedTriggerExpressionCount = result.UnsupportedTriggerExpressionCount;
            status.HostsWithoutSelectedSourceTriggers = result.HostsWithoutSelectedSourceTriggers;
            status.UnsupportedAggregateItemCount = result.UnsupportedAggregateItemCount;
            status.ManagedDependencyCount = ListManagedTriggerDependencies(result.Layer).Count;
            status.Errors.Clear();
            status.Errors.AddRange(result.Errors.Take(20));
            status.Warnings.Clear();
            status.Warnings.AddRange(result.Warnings.Take(20));
            status.Samples.Clear();
            status.Samples.AddRange(result.SampleDependencies.Take(20));
            status.AggregateSamples.Clear();
            status.AggregateSamples.AddRange(result.SampleAggregates.Take(20));
            status.UnsupportedAggregateItems.Clear();
            status.UnsupportedAggregateItems.AddRange(result.UnsupportedAggregateItems.Take(20));
        }
    }

    public object TriggerDependencySnapshot(
        string layer,
        ZabbixTriggerDependenciesOptions options,
        ZabbixOptions zabbixOptions)
    {
        var normalizedLayer = ZabbixApplyPlanner.NormalizeLayer(layer);
        var status = triggerDependencyLayers.GetOrAdd(
            string.IsNullOrWhiteSpace(normalizedLayer) ? "suppression" : normalizedLayer,
            key => new ZabbixTriggerDependencyLayerStatus { Layer = key });
        lock (status)
        {
            return new
            {
                layer = status.Layer,
                enabled = options.Enabled,
                includeDisabledTriggers = options.IncludeDisabledTriggers,
                transitiveGroupDependencyDepth = options.TransitiveGroupDependencyDepth,
                triggerGetBatchSize = options.TriggerGetBatchSize,
                triggerGetBatchCount = status.TriggerGetBatchCount,
                triggerGetElapsedMs = status.TriggerGetElapsedMs,
                zabbixRequestTimeoutMs = zabbixOptions.RequestTimeoutMs,
                staleSourceHostBindingCount = status.StaleSourceHostBindingCount,
                staleSourceMembershipsRemoved = status.StaleSourceMembershipsRemoved,
                maxSourceHostsPerAggregate = options.MaxSourceHostsPerAggregate,
                maxAggregateFormulaLength = options.MaxAggregateFormulaLength,
                largestAggregateSourceHostCount = status.LargestAggregateSourceHostCount,
                largestAggregateFormulaLength = status.LargestAggregateFormulaLength,
                largestAggregateTriggerExpressionLength = status.LargestAggregateTriggerExpressionLength,
                aggregateComplexityWarningCount = status.AggregateComplexityWarningCount,
                aggregateComplexityErrorCount = status.AggregateComplexityErrorCount,
                aggregateStateTriggerSelector = string.IsNullOrWhiteSpace(status.AggregateStateTriggerSelectorSummary)
                    ? options.AggregateStateTriggerSelectorSummary()
                    : status.AggregateStateTriggerSelectorSummary,
                aggregateStateTriggerSettings = new
                {
                    includeTags = NormalizedTagSelectors(options.AggregateStateTriggerIncludeTags),
                    excludeTags = NormalizedTagSelectors(options.AggregateStateTriggerExcludeTags),
                    includeNameRegex = options.AggregateStateTriggerIncludeNameRegex,
                    excludeNameRegex = options.AggregateStateTriggerExcludeNameRegex,
                    minPriority = options.AggregateStateTriggerMinPriority
                },
                dependencyTriggerSelector = string.IsNullOrWhiteSpace(status.DependencyTriggerSelectorSummary)
                    ? options.DependencyTriggerSelectorSummary()
                    : status.DependencyTriggerSelectorSummary,
                maxDependenciesPerRun = options.MaxDependenciesPerRun,
                sampleLimit = options.SampleLimit,
                lastUpdatedAt = status.LastUpdatedAtUtc,
                lastStatus = status.LastStatus,
                lastMode = status.LastMode,
                lastMessage = status.LastMessage,
                desiredDependencyCount = status.DesiredDependencyCount,
                dependentTriggerCount = status.DependentTriggerCount,
                dependencyTriggerCount = status.DependencyTriggerCount,
                aggregateCount = status.AggregateCount,
                aggregateHostsCreated = status.AggregateHostsCreated,
                aggregateItemsCreated = status.AggregateItemsCreated,
                aggregateItemsUpdated = status.AggregateItemsUpdated,
                aggregateTriggersCreated = status.AggregateTriggersCreated,
                aggregateTriggersUpdated = status.AggregateTriggersUpdated,
                triggersToUpdate = status.TriggersToUpdate,
                triggersUpdated = status.TriggersUpdated,
                dependenciesAdded = status.DependenciesAdded,
                dependenciesRemoved = status.DependenciesRemoved,
                preservedManualDependencies = status.PreservedManualDependencies,
                selectedSourceTriggerCount = status.SelectedSourceTriggerCount,
                skippedSourceTriggerCount = status.SkippedSourceTriggerCount,
                unsupportedTriggerExpressionCount = status.UnsupportedTriggerExpressionCount,
                hostsWithoutSelectedSourceTriggers = status.HostsWithoutSelectedSourceTriggers,
                unsupportedAggregateItemCount = status.UnsupportedAggregateItemCount,
                managedDependencyCount = ListManagedTriggerDependencies(status.Layer).Count,
                errors = status.Errors.ToArray(),
                warnings = status.Warnings.ToArray(),
                sampleDependencies = status.Samples.ToArray(),
                sampleAggregates = status.AggregateSamples.ToArray(),
                unsupportedAggregateItems = status.UnsupportedAggregateItems.ToArray()
            };
        }
    }

    private static object[] NormalizedTagSelectors(IEnumerable<ZabbixTriggerTagSelector> selectors)
    {
        return selectors
            .Where(item => !string.IsNullOrWhiteSpace(item.Tag))
            .Select(item => new
            {
                tag = item.Tag.Trim(),
                value = (item.Value ?? "").Trim()
            })
            .GroupBy(item => $"{item.tag}\u001f{item.value}", StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray<object>();
    }

    private IReadOnlyList<ZabbixTargetMembershipSnapshot> MembershipSnapshots(string layer)
    {
        lock (membershipLock)
        {
            return memberships.Values
                .Where(item => item.Layer.Equals(layer, StringComparison.OrdinalIgnoreCase))
                .OrderBy(item => item.TargetName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.TargetManagedKey, StringComparer.Ordinal)
                .Take(50)
                .Select(item => item.ToSnapshot())
                .ToArray();
        }
    }

    private static ZabbixTargetMembershipSummary ToMembershipSummary(ZabbixTargetMembership membership)
    {
        return new ZabbixTargetMembershipSummary
        {
            Layer = membership.Layer,
            TargetManagedKey = membership.TargetManagedKey,
            TargetClass = membership.TargetClass,
            TargetCardId = membership.TargetCardId,
            TargetName = membership.TargetName,
            AggregationType = membership.AggregationType,
            IsCritical = membership.IsCritical,
            Threshold = membership.Threshold,
            N = membership.N,
            SourceCount = membership.Sources.Count,
            HostBindingCount = membership.Sources.Values.Count(item => !string.IsNullOrWhiteSpace(item.ZabbixHostId)),
            PendingSourceCount = membership.PendingSources.Count
        };
    }

    private void LoadMemberships()
    {
        try
        {
            if (!File.Exists(stateFilePath))
            {
                return;
            }

            var state = JsonSerializer.Deserialize<ZabbixApplyPersistentState>(
                File.ReadAllText(stateFilePath),
                StateJsonOptions);
            if (state?.Memberships is null)
            {
                return;
            }

            foreach (var membership in state.Memberships)
            {
                if (string.IsNullOrWhiteSpace(membership.Layer)
                    || string.IsNullOrWhiteSpace(membership.TargetManagedKey))
                {
                    continue;
                }

                memberships[MembershipKey(membership.Layer, membership.TargetManagedKey)] = membership;
            }

            foreach (var dependency in state.TriggerDependencies)
            {
                if (string.IsNullOrWhiteSpace(dependency.Layer)
                    || string.IsNullOrWhiteSpace(dependency.DependentTriggerId)
                    || string.IsNullOrWhiteSpace(dependency.DependencyTriggerId))
                {
                    continue;
                }

                triggerDependencies[TriggerDependencyKey(dependency)] = dependency;
            }

            foreach (var graphObject in state.AppliedGraphObjects)
            {
                if (string.IsNullOrWhiteSpace(graphObject.Layer)
                    || string.IsNullOrWhiteSpace(graphObject.ObjectKey))
                {
                    continue;
                }

                appliedGraphObjects[AppliedGraphObjectKey(graphObject.Layer, graphObject.ObjectKey)] = graphObject;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            logger.LogWarning(ex, "Failed to load Zabbix apply membership state from {StateFilePath}", stateFilePath);
        }
    }

    private void SaveMemberships()
    {
        try
        {
            var directory = Path.GetDirectoryName(stateFilePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var state = new ZabbixApplyPersistentState
            {
                Memberships = memberships.Values
                    .OrderBy(item => item.Layer, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(item => item.TargetManagedKey, StringComparer.Ordinal)
                    .ToList(),
                TriggerDependencies = triggerDependencies.Values
                    .OrderBy(item => item.Layer, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(item => item.DependentTriggerId, StringComparer.Ordinal)
                    .ThenBy(item => item.DependencyTriggerId, StringComparer.Ordinal)
                    .ToList(),
                AppliedGraphObjects = appliedGraphObjects.Values
                    .OrderBy(item => item.Layer, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(item => item.ObjectKey, StringComparer.Ordinal)
                    .ToList()
            };
            File.WriteAllText(stateFilePath, JsonSerializer.Serialize(state, StateJsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Failed to save Zabbix apply membership state to {StateFilePath}", stateFilePath);
        }
    }

    private static string MembershipKey(string layer, string targetManagedKey)
    {
        return $"{layer}\u001f{targetManagedKey}";
    }

    private IReadOnlyList<ZabbixTargetMembershipSnapshot> RemoveSourceFromLayer(
        string layer,
        string sourceKey,
        string? exceptMembershipKey = null)
    {
        if (string.IsNullOrWhiteSpace(sourceKey))
        {
            return [];
        }

        var affected = new List<ZabbixTargetMembershipSnapshot>();
        foreach (var pair in memberships)
        {
            if (!pair.Value.Layer.Equals(layer, StringComparison.OrdinalIgnoreCase)
                || (!string.IsNullOrWhiteSpace(exceptMembershipKey)
                    && pair.Key.Equals(exceptMembershipKey, StringComparison.Ordinal)))
            {
                continue;
            }

            if (!RemoveSourceFromMembership(pair.Value, sourceKey))
            {
                continue;
            }

            affected.Add(pair.Value.ToSnapshot());
        }

        return affected;
    }

    private static bool RemoveSourceFromMembership(ZabbixTargetMembership membership, string sourceKey)
    {
        var removed = membership.Sources.Remove(sourceKey);
        removed = membership.PendingSources.Remove(sourceKey) || removed;
        if (removed)
        {
            membership.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }

        return removed;
    }

    private static string SourceMembershipKey(AggregationSourceObject source)
    {
        return string.IsNullOrWhiteSpace(source.ClassCode) || string.IsNullOrWhiteSpace(source.CardId)
            ? ""
            : $"{source.ClassCode}\u001f{source.CardId}";
    }

    private static string TriggerDependencyKey(ZabbixManagedTriggerDependency dependency)
    {
        return $"{dependency.Layer}\u001f{dependency.DependentTriggerId}\u001f{dependency.DependencyTriggerId}";
    }

    private static string AppliedGraphObjectKey(string layer, string objectKey)
    {
        return $"{layer}\u001f{objectKey}";
    }

    private int AppliedGraphObjectCount(string layer)
    {
        lock (membershipLock)
        {
            return appliedGraphObjects.Values.Count(item => item.Layer.Equals(layer, StringComparison.OrdinalIgnoreCase));
        }
    }

    private static void AddDiffSample(
        ICollection<ZabbixGraphDiffSample> samples,
        string action,
        ZabbixAppliedGraphObject graphObject,
        int sampleLimit)
    {
        if (samples.Count >= sampleLimit)
        {
            return;
        }

        samples.Add(new ZabbixGraphDiffSample
        {
            Action = action,
            ObjectType = graphObject.ObjectType,
            ObjectKey = graphObject.ObjectKey,
            DisplayName = graphObject.DisplayName,
            TargetManagedKey = graphObject.TargetManagedKey,
            RuleId = graphObject.RuleId,
            ClassCode = graphObject.ClassCode
        });
    }

    private static string TargetObjectName(AggregationCommand command)
    {
        if (!string.IsNullOrWhiteSpace(command.RuleName))
        {
            return command.RuleName;
        }

        if (!string.IsNullOrWhiteSpace(command.Target.CardDescription))
        {
            return command.Target.CardDescription;
        }

        return string.IsNullOrWhiteSpace(command.Target.CardId)
            ? command.Target.IdempotencyKey
            : command.Target.CardId;
    }

    private static string TargetAttribute(IReadOnlyDictionary<string, object?> attributes, string name)
    {
        foreach (var attribute in attributes)
        {
            if (string.Equals(attribute.Key, name, StringComparison.OrdinalIgnoreCase))
            {
                return ScalarString(attribute.Value);
            }
        }

        return "";
    }

    private static string ScalarString(object? value)
    {
        return value switch
        {
            null => "",
            string text => text.Trim(),
            bool boolean => boolean ? "true" : "false",
            JsonElement element => JsonElementString(element),
            IFormattable formattable => formattable.ToString(null, System.Globalization.CultureInfo.InvariantCulture) ?? "",
            _ => value.ToString()?.Trim() ?? ""
        };
    }

    private static string JsonElementString(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString()?.Trim() ?? "",
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => ""
        };
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

    public ZabbixTargetMembershipSnapshot LastMembership { get; set; } = new();

    public ZabbixCommandApplyPerformance LastPerformance { get; set; } = new();

    public int CommandsReceived { get; set; }

    public int DryRunCommands { get; set; }

    public int AcceptedCommands { get; set; }

    public int AppliedCommands { get; set; }

    public int PartialCommands { get; set; }

    public int SkippedCommands { get; set; }

    public int PendingManualCommands { get; set; }

    public int ErrorCommands { get; set; }

    public ZabbixReconcileCounters Reconcile { get; } = new();

    public ZabbixLayerApplyPerformance Performance { get; } = new();

    public List<string> Errors { get; } = [];

    public List<string> Warnings { get; } = [];
}

public sealed class ZabbixApplyStateOptions
{
    public const string SectionName = "ZabbixApplyState";

    public string FilePath { get; init; } = "state/zabbixconfig2api/apply-membership.json";
}

public sealed class ZabbixSlaOptions
{
    public const string SectionName = "ZabbixSla";

    public bool Enabled { get; init; } = true;

    public string DefaultPolicyKey { get; init; } = "";

    public int DowntimePublicationHorizonMonths { get; init; } = 6;

    public string ManagedExcludedDowntimePrefix { get; init; } = "CMDB2M REG:";

    public string CmdbuildPrefix { get; init; } = "C2M_";

    public string ServiceRootPath { get; init; } = "";

    public string DefaultReportingPeriod { get; init; } = "monthly";

    public string DefaultTimezone { get; init; } = "Europe/Moscow";

    public int SampleLimit { get; init; } = 100;
}

public sealed class ZabbixMembershipUpdateResult
{
    public ZabbixTargetMembershipSnapshot Current { get; init; } = new();

    public IReadOnlyList<ZabbixTargetMembershipSnapshot> AffectedTargets { get; init; } = [];

    public int RemovedSourceMemberships { get; init; }
}

public sealed class ZabbixSourceHostBindingCleanupResult
{
    public string Layer { get; init; } = "";

    public int RequestedHostIds { get; init; }

    public int RemovedSourceMemberships { get; init; }

    public IReadOnlyList<string> RemovedHostIds { get; init; } = [];

    public IReadOnlyList<ZabbixTargetMembershipSnapshot> AffectedTargets { get; init; } = [];
}

public sealed class ZabbixApplyPersistentState
{
    public List<ZabbixTargetMembership> Memberships { get; init; } = [];

    public List<ZabbixManagedTriggerDependency> TriggerDependencies { get; init; } = [];

    public List<ZabbixAppliedGraphObject> AppliedGraphObjects { get; init; } = [];
}

public sealed class ZabbixTargetMembership
{
    public string Layer { get; set; } = "";

    public string TargetManagedKey { get; set; } = "";

    public string TargetClass { get; set; } = "";

    public string TargetCardId { get; set; } = "";

    public string TargetName { get; set; } = "";

    public string AggregationType { get; set; } = "";

    public string IsCritical { get; set; } = "";

    public string Threshold { get; set; } = "";

    public string N { get; set; } = "";

    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public Dictionary<string, ZabbixSourceMembership> Sources { get; set; } = new(StringComparer.Ordinal);

    public Dictionary<string, ZabbixSourceMembership> PendingSources { get; set; } = new(StringComparer.Ordinal);

    public List<ZabbixMembershipRelation> Relations { get; set; } = [];

    public ZabbixTargetMembershipSnapshot ToSnapshot()
    {
        var sources = Sources.Values
            .OrderBy(item => item.SourceClass, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.SourceKeyValue, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.SourceCardId, StringComparer.Ordinal)
            .ToArray();
        var pendingSources = PendingSources.Values
            .OrderBy(item => item.SourceClass, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.SourceKeyValue, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.SourceCardId, StringComparer.Ordinal)
            .ToArray();
        return new ZabbixTargetMembershipSnapshot
        {
            Layer = Layer,
            TargetManagedKey = TargetManagedKey,
            TargetClass = TargetClass,
            TargetCardId = TargetCardId,
            TargetName = TargetName,
            AggregationType = AggregationType,
            IsCritical = IsCritical,
            Threshold = Threshold,
            N = N,
            SourceCount = sources.Length,
            HostBindingCount = sources.Count(item => !string.IsNullOrWhiteSpace(item.ZabbixHostId)),
            MissingHostBindingCount = sources.Count(item => string.IsNullOrWhiteSpace(item.ZabbixHostId)) + pendingSources.Length,
            PendingSourceCount = pendingSources.Length,
            SourceLeafManagedKeys = sources
                .Select(item => item.SourceLeafManagedKey)
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.Ordinal)
                .ToArray(),
            Sources = sources,
            PendingSources = pendingSources,
            Relations = Relations.ToArray(),
            UpdatedAtUtc = UpdatedAtUtc
        };
    }
}

public sealed class ZabbixTargetMembershipSnapshot
{
    public string Layer { get; init; } = "";

    public string TargetManagedKey { get; init; } = "";

    public string TargetClass { get; init; } = "";

    public string TargetCardId { get; init; } = "";

    public string TargetName { get; init; } = "";

    public string AggregationType { get; init; } = "";

    public string IsCritical { get; init; } = "";

    public string Threshold { get; init; } = "";

    public string N { get; init; } = "";

    public int SourceCount { get; init; }

    public int HostBindingCount { get; init; }

    public int MissingHostBindingCount { get; init; }

    public int PendingSourceCount { get; init; }

    public IReadOnlyList<string> SourceLeafManagedKeys { get; init; } = [];

    public IReadOnlyList<ZabbixSourceMembership> Sources { get; init; } = [];

    public IReadOnlyList<ZabbixSourceMembership> PendingSources { get; init; } = [];

    public IReadOnlyList<ZabbixMembershipRelation> Relations { get; init; } = [];

    public DateTimeOffset UpdatedAtUtc { get; init; }
}

public sealed class ZabbixSourceMembership
{
    public string SourceClass { get; init; } = "";

    public string SourceCardId { get; init; } = "";

    public string SourceKeyAttribute { get; init; } = "";

    public string SourceKeyValue { get; init; } = "";

    public string ZabbixHostId { get; init; } = "";

    public string SourceLeafManagedKey { get; init; } = "";

    public DateTimeOffset UpdatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class ZabbixMembershipRelation
{
    public string DomainCode { get; init; } = "";

    public string TargetClassCode { get; init; } = "";

    public string TargetLookup { get; init; } = "";
}

public sealed class ZabbixManagedTriggerDependency
{
    public string Layer { get; set; } = "";

    public string DependentTriggerId { get; init; } = "";

    public string DependencyTriggerId { get; init; } = "";

    public string DependentTriggerName { get; init; } = "";

    public string DependencyTriggerName { get; init; } = "";

    public string DependentHostId { get; init; } = "";

    public string DependencyHostId { get; init; } = "";

    public string DependentTargetManagedKey { get; init; } = "";

    public string DependencyTargetManagedKey { get; init; } = "";

    public string DependentTargetName { get; init; } = "";

    public string DependencyTargetName { get; init; } = "";

    public string RelationDomainCode { get; init; } = "";

    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class ZabbixTriggerDependencyLayerStatus
{
    public string Layer { get; init; } = "";

    public DateTimeOffset? LastUpdatedAtUtc { get; set; }

    public string LastStatus { get; set; } = "";

    public string LastMode { get; set; } = "";

    public string LastMessage { get; set; } = "";

    public string AggregateStateTriggerSelectorSummary { get; set; } = "";

    public string DependencyTriggerSelectorSummary { get; set; } = "";

    public int TriggerGetBatchSize { get; set; }

    public int TriggerGetBatchCount { get; set; }

    public int TriggerGetElapsedMs { get; set; }

    public int ZabbixRequestTimeoutMs { get; set; }

    public int StaleSourceHostBindingCount { get; set; }

    public int StaleSourceMembershipsRemoved { get; set; }

    public int MaxSourceHostsPerAggregate { get; set; }

    public int MaxAggregateFormulaLength { get; set; }

    public int LargestAggregateSourceHostCount { get; set; }

    public int LargestAggregateFormulaLength { get; set; }

    public int LargestAggregateTriggerExpressionLength { get; set; }

    public int AggregateComplexityWarningCount { get; set; }

    public int AggregateComplexityErrorCount { get; set; }

    public int DesiredDependencyCount { get; set; }

    public int DependentTriggerCount { get; set; }

    public int DependencyTriggerCount { get; set; }

    public int AggregateCount { get; set; }

    public int AggregateHostsCreated { get; set; }

    public int AggregateItemsCreated { get; set; }

    public int AggregateItemsUpdated { get; set; }

    public int AggregateTriggersCreated { get; set; }

    public int AggregateTriggersUpdated { get; set; }

    public int TriggersToUpdate { get; set; }

    public int TriggersUpdated { get; set; }

    public int DependenciesAdded { get; set; }

    public int DependenciesRemoved { get; set; }

    public int PreservedManualDependencies { get; set; }

    public int SelectedSourceTriggerCount { get; set; }

    public int SkippedSourceTriggerCount { get; set; }

    public int UnsupportedTriggerExpressionCount { get; set; }

    public int HostsWithoutSelectedSourceTriggers { get; set; }

    public int UnsupportedAggregateItemCount { get; set; }

    public int ManagedDependencyCount { get; set; }

    public List<string> Errors { get; } = [];

    public List<string> Warnings { get; } = [];

    public List<ZabbixTriggerDependencyPlanItem> Samples { get; } = [];

    public List<ZabbixSuppressionAggregatePlanItem> AggregateSamples { get; } = [];

    public List<ZabbixUnsupportedAggregateItemSample> UnsupportedAggregateItems { get; } = [];
}

public sealed class ZabbixTriggerDependencyReconcileScheduler(
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<ZabbixTriggerDependenciesOptions> options,
    ILogger<ZabbixTriggerDependencyReconcileScheduler> logger)
    : BackgroundService
{
    private readonly Channel<string> requests = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false
    });

    public void Request(string reason)
    {
        var currentOptions = options.CurrentValue;
        if (!currentOptions.Enabled || !currentOptions.AutoReconcileOnMembershipChange)
        {
            return;
        }

        requests.Writer.TryWrite(string.IsNullOrWhiteSpace(reason) ? "suppression membership changed" : reason);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (await requests.Reader.WaitToReadAsync(stoppingToken))
        {
            var reasons = DrainReasons();
            var delay = TimeSpan.FromSeconds(Math.Max(0, options.CurrentValue.AutoReconcileDebounceSeconds));
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, stoppingToken);
                reasons.AddRange(DrainReasons());
            }

            try
            {
                using var scope = scopeFactory.CreateScope();
                var applier = scope.ServiceProvider.GetRequiredService<ZabbixTriggerDependencyApplier>();
                var result = await applier.RunAsync(dryRun: false, stoppingToken);
                logger.LogInformation(
                    "Automatic suppression trigger dependency reconcile completed: status={Status}, aggregates={AggregateCount}, updatedTriggers={UpdatedTriggers}, reasons={Reasons}",
                    result.Status,
                    result.AggregateCount,
                    result.TriggersUpdated,
                    string.Join("; ", reasons.Distinct(StringComparer.Ordinal).Take(10)));
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
            {
                logger.LogError(
                    ex,
                    "Automatic suppression trigger dependency reconcile failed; reasons={Reasons}",
                    string.Join("; ", reasons.Distinct(StringComparer.Ordinal).Take(10)));
            }
        }
    }

    private List<string> DrainReasons()
    {
        var result = new List<string>();
        while (requests.Reader.TryRead(out var reason))
        {
            result.Add(reason);
        }

        return result;
    }
}

public sealed class ZabbixCommandApplyResult
{
    public string Layer { get; set; } = "";

    public string Topic { get; set; } = "";

    public string Status { get; set; } = "";

    public string Mode { get; set; } = "";

    public bool SafeApply { get; set; }

    public string CommandId { get; set; } = "";

    public string RuleId { get; set; } = "";

    public string RuleName { get; set; } = "";

    public string CommandType { get; set; } = "";

    public string TargetClass { get; set; } = "";

    public string TargetKey { get; set; } = "";

    public string SourceClass { get; set; } = "";

    public string SourceCardId { get; set; } = "";

    public ZabbixReconcileCounters Reconcile { get; set; } = new();

    public string Message { get; set; } = "";

    public string Error { get; set; } = "";

    public string ZabbixServiceId { get; set; } = "";

    public string ZabbixAction { get; set; } = "";

    public int RelationsApplied { get; set; }

    public int RelationsDeferred { get; set; }

    public int SourceLeafServicesApplied { get; set; }

    public int ProblemTagsApplied { get; set; }

    public int HostTagsApplied { get; set; }

    public ZabbixTargetMembershipSnapshot Membership { get; set; } = new();

    public ZabbixCommandApplyPerformance Performance { get; set; } = new();

    public IReadOnlyList<string> Warnings { get; set; } = [];

    public DateTimeOffset AppliedAtUtc { get; set; }
}

public sealed class ZabbixCommandApplyPerformance
{
    public long TotalMs { get; set; }

    public long StateUpdateMs { get; set; }

    public long AffectedTargetsApplyMs { get; set; }

    public long SourceLeafApplyMs { get; set; }

    public long TargetApplyMs { get; set; }

    public int ZabbixApiCallCount { get; set; }

    public long ZabbixApiElapsedMs { get; set; }

    public Dictionary<string, ZabbixApiMethodPerformance> ZabbixApiByMethod { get; set; } = new(StringComparer.Ordinal);
}

public sealed class ZabbixLayerApplyPerformance
{
    public long Commands { get; private set; }

    public long TotalMs { get; private set; }

    public long StateUpdateMs { get; private set; }

    public long AffectedTargetsApplyMs { get; private set; }

    public long SourceLeafApplyMs { get; private set; }

    public long TargetApplyMs { get; private set; }

    public int ZabbixApiCallCount { get; private set; }

    public long ZabbixApiElapsedMs { get; private set; }

    public Dictionary<string, ZabbixApiMethodPerformance> ZabbixApiByMethod { get; } = new(StringComparer.Ordinal);

    public void Add(ZabbixCommandApplyPerformance performance)
    {
        Commands++;
        TotalMs += performance.TotalMs;
        StateUpdateMs += performance.StateUpdateMs;
        AffectedTargetsApplyMs += performance.AffectedTargetsApplyMs;
        SourceLeafApplyMs += performance.SourceLeafApplyMs;
        TargetApplyMs += performance.TargetApplyMs;
        ZabbixApiCallCount += performance.ZabbixApiCallCount;
        ZabbixApiElapsedMs += performance.ZabbixApiElapsedMs;

        foreach (var pair in performance.ZabbixApiByMethod)
        {
            if (!ZabbixApiByMethod.TryGetValue(pair.Key, out var stats))
            {
                stats = new ZabbixApiMethodPerformance();
                ZabbixApiByMethod[pair.Key] = stats;
            }

            stats.Count += pair.Value.Count;
            stats.ElapsedMs += pair.Value.ElapsedMs;
        }
    }
}

public sealed class ZabbixApiMethodPerformance
{
    public int Count { get; set; }

    public long ElapsedMs { get; set; }
}

public sealed class ZabbixReconcileCounters
{
    public int EnsureMembershipTargets { get; set; }

    public int EnsureMembershipSources { get; set; }

    public int EnsureMembershipRelations { get; set; }

    public int EnsureObjects { get; set; }

    public int EnsureRelations { get; set; }

    public int EnsureSourceLeafServices { get; set; }

    public int EnsureProblemTags { get; set; }

    public int EnsureHostTags { get; set; }

    public int RemoveObjects { get; set; }

    public int RemoveRelations { get; set; }

    public int RemoveMembershipSources { get; set; }

    public void Add(ZabbixReconcileCounters counters)
    {
        EnsureMembershipTargets += counters.EnsureMembershipTargets;
        EnsureMembershipSources += counters.EnsureMembershipSources;
        EnsureMembershipRelations += counters.EnsureMembershipRelations;
        EnsureObjects += counters.EnsureObjects;
        EnsureRelations += counters.EnsureRelations;
        EnsureSourceLeafServices += counters.EnsureSourceLeafServices;
        EnsureProblemTags += counters.EnsureProblemTags;
        EnsureHostTags += counters.EnsureHostTags;
        RemoveObjects += counters.RemoveObjects;
        RemoveRelations += counters.RemoveRelations;
        RemoveMembershipSources += counters.RemoveMembershipSources;
    }
}
