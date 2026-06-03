using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Cmdb2MonitoringServiceSuppression.Shared.Aggregation;
using Cmdb2MonitoringServiceSuppression.Shared.Configuration;
using Cmdb2MonitoringServiceSuppression.Shared.Integrations;
using Cmdb2MonitoringServiceSuppression.Shared.Logging;
using Cmdb2MonitoringServiceSuppression.Shared.Messaging;
using Cmdb2MonitoringServiceSuppression.Shared.Observability;
using Cmdb2MonitoringServiceSuppression.Shared.Secrets;
using Microsoft.Data.Sqlite;
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
builder.Services.AddOptions<RuntimeRedisOptions>()
    .Bind(builder.Configuration.GetSection(RuntimeRedisOptions.SectionName))
    .Validate(options => !options.Enabled || !string.IsNullOrWhiteSpace(options.ConnectionString), "Redis:ConnectionString is required when Redis:Enabled=true.")
    .Validate(options => options.OperationTtlSeconds > 0, "Redis:OperationTtlSeconds must be greater than zero.")
    .Validate(options => options.LockTtlSeconds > 0, "Redis:LockTtlSeconds must be greater than zero.")
    .Validate(options => options.LockExtendSeconds > 0, "Redis:LockExtendSeconds must be greater than zero.")
    .Validate(options => options.CacheDefaultTtlSeconds > 0, "Redis:CacheDefaultTtlSeconds must be greater than zero.")
    .Validate(options => options.HasValidFailureMode(), "Redis:FailureMode must be fallback or fail.")
    .ValidateOnStart();
builder.Services.AddOptions<DurableStoreOptions>()
    .Bind(builder.Configuration.GetSection(DurableStoreOptions.SectionName))
    .Validate(options => options.HasValidProvider(), "DurableStore:Provider must be file or sqlite.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.ConnectionString), "DurableStore:ConnectionString is required.")
    .ValidateOnStart();
builder.Services.AddOptions<MonitoringCoverageSnapshotOptions>()
    .Bind(builder.Configuration.GetSection(MonitoringCoverageSnapshotOptions.SectionName))
    .Validate(options => options.SnapshotRetentionDays > 0, "MonitoringCoverageAudit:SnapshotRetentionDays must be greater than zero.")
    .Validate(options => options.MaxOperationalDeltaMinutes >= 0, "MonitoringCoverageAudit:MaxOperationalDeltaMinutes must not be negative.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.HostIdAttribute), "MonitoringCoverageAudit:HostIdAttribute is required.")
    .Validate(options => options.HasValidTriggerMode(), "MonitoringCoverageAudit:TriggerMode must be manual, scheduled, or manual_and_scheduled.")
    .Validate(options => options.HasValidExpectedPolicy(), "MonitoringCoverageAudit:DefaultExpectedPolicy is invalid.")
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
    .Validate(options => !string.IsNullOrWhiteSpace(options.DeadLetterTopic), "KafkaTopics:DeadLetterTopic is required.")
    .ValidateOnStart();
builder.Services.AddHttpClient<ZabbixClient>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient<CmdbuildClient>();
builder.Services.AddTransient<IServiceReadinessCheck, ZabbixConfigDependencyReadinessCheck>();
builder.Services.AddSingleton<KafkaJsonProducer>();
builder.Services.AddTransient<ZabbixAggregationApplier>();
builder.Services.AddTransient<ZabbixTriggerDependencyApplier>();
builder.Services.AddTransient<ZabbixSlaPublisher>();
builder.Services.AddSingleton<LocalRuntimeCoordinationStore>();
builder.Services.AddSingleton<IRuntimeCoordinationStore, RedisRuntimeCoordinationStore>();
builder.Services.AddSingleton<LocalRuntimeLookupCache>();
builder.Services.AddSingleton<IRuntimeLookupCache, RedisRuntimeLookupCache>();
builder.Services.AddSingleton<FileZabbixApplyStateStorage>();
builder.Services.AddSingleton<SqliteZabbixApplyStateStorage>();
builder.Services.AddSingleton<IZabbixApplyStateStorage>(provider =>
{
    var durable = provider.GetRequiredService<IOptionsMonitor<DurableStoreOptions>>().CurrentValue;
    if (durable.Provider.Equals("sqlite", StringComparison.OrdinalIgnoreCase))
    {
        return provider.GetRequiredService<SqliteZabbixApplyStateStorage>();
    }

    return provider.GetRequiredService<FileZabbixApplyStateStorage>();
});
builder.Services.AddSingleton<ZabbixDirtyScopeStore>();
builder.Services.AddSingleton<MonitoringCoverageSnapshotStore>();
builder.Services.AddSingleton<ZabbixApplyStateStore>();
builder.Services.AddSingleton<ZabbixTriggerDependencyReconcileScheduler>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<ZabbixTriggerDependencyReconcileScheduler>());
builder.Services.AddHostedService<ZabbixServiceAggregationCommandWorker>();
builder.Services.AddHostedService<ZabbixSuppressionAggregationCommandWorker>();

var app = builder.Build();
app.UseServiceDefaults();
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

app.MapGet("/runtime-storage/status", (
    ZabbixApplyStateStore applyStateStore,
    IZabbixApplyStateStorage storage,
    ZabbixDirtyScopeStore dirtyScopes,
    IRuntimeCoordinationStore runtimeCoordination,
    IRuntimeLookupCache runtimeLookupCache,
    IOptionsMonitor<RuntimeRedisOptions> redisOptions,
    IOptionsMonitor<DurableStoreOptions> durableStoreOptions,
    IOptionsMonitor<MonitoringCoverageSnapshotOptions> coverageOptions,
    IOptionsMonitor<ZabbixApplyStateOptions> stateOptions) =>
{
    var redis = redisOptions.CurrentValue;
    var durable = durableStoreOptions.CurrentValue;
    var coverage = coverageOptions.CurrentValue;
    var applyState = stateOptions.CurrentValue;
    return Results.Ok(new
    {
        redis = new
        {
            enabled = redis.Enabled,
            endpoint = RedactedConnectionEndpoint(redis.ConnectionString),
            connectionConfigured = !string.IsNullOrWhiteSpace(redis.ConnectionString),
            keyPrefix = redis.KeyPrefix,
            instanceId = redis.InstanceId,
            operationTtlSeconds = redis.OperationTtlSeconds,
            lockTtlSeconds = redis.LockTtlSeconds,
            lockExtendSeconds = redis.LockExtendSeconds,
            cacheDefaultTtlSeconds = redis.CacheDefaultTtlSeconds,
            failureMode = redis.FailureMode
        },
        runtimeCoordination = runtimeCoordination.Status(),
        lookupCache = runtimeLookupCache.Status(),
        durableStore = new
        {
            provider = durable.Provider,
            endpoint = RedactedConnectionEndpoint(durable.ConnectionString),
            connectionConfigured = !string.IsNullOrWhiteSpace(durable.ConnectionString),
            migrationsEnabled = durable.MigrationsEnabled,
            currentMembershipBackend = storage.Backend,
            currentMembershipFile = applyState.FilePath,
            plannedBackends = new[] { "file", "sqlite" },
            migrationStatus = durable.Provider.Equals(storage.Backend, StringComparison.OrdinalIgnoreCase)
                ? "not_required"
                : durable.Provider.Equals("sqlite", StringComparison.OrdinalIgnoreCase)
                    ? "available"
                    : "pending_backend_implementation",
            state = applyStateStore.RuntimeStorageSnapshot(durable)
        },
        dirtyScopes = dirtyScopes.Summary(),
        monitoringCoverageSnapshot = new
        {
            enabled = coverage.Enabled,
            triggerMode = coverage.TriggerMode,
            snapshotRetentionDays = coverage.SnapshotRetentionDays,
            defaultExpectedPolicy = coverage.DefaultExpectedPolicy,
            hostIdAttribute = coverage.HostIdAttribute,
            allowOperationalDelta = coverage.AllowOperationalDelta,
            maxOperationalDeltaMinutes = coverage.MaxOperationalDeltaMinutes,
            autoSnapshotAfterFullGraphApply = coverage.AutoSnapshotAfterFullGraphApply,
            autoSnapshotAfterScopedReconcile = coverage.AutoSnapshotAfterScopedReconcile,
            scheduledSnapshotCronConfigured = !string.IsNullOrWhiteSpace(coverage.ScheduledSnapshotCron)
        }
    });
});

app.MapGet("/redis/check", (
    IRuntimeCoordinationStore runtimeCoordination,
    IOptionsMonitor<RuntimeRedisOptions> redisOptions) =>
{
    var redis = redisOptions.CurrentValue;
    var status = runtimeCoordination.Status();
    return Results.Ok(new
    {
        configured = redis.Enabled,
        success = !redis.Enabled || status.RedisAvailable || status.FallbackActive,
        backend = status.Backend,
        redisRequested = status.RedisRequested,
        redisAvailable = status.RedisAvailable,
        fallbackActive = status.FallbackActive,
        blockingOnRedisUnavailable = status.BlockingOnRedisUnavailable,
        keyPrefix = status.KeyPrefix,
        instanceId = status.InstanceId,
        activeLockCount = status.ActiveLockCount,
        activeOperationCount = status.ActiveOperationCount,
        message = status.Message
    });
});

app.MapPost("/runtime-storage/migration/dry-run", (
    IZabbixApplyStateStorage storage,
    IOptionsMonitor<DurableStoreOptions> durableStoreOptions) =>
{
    var durable = durableStoreOptions.CurrentValue;
    var state = storage.Load();
    return Results.Ok(ZabbixApplyStateMigrationPlan.FromState(
        dryRun: true,
        sourceBackend: storage.Backend,
        sourceLocation: storage.Location,
        targetProvider: durable.Provider,
        targetLocation: RedactedConnectionEndpoint(durable.ConnectionString),
        state));
});

app.MapPost("/runtime-storage/migration/apply", (
    IZabbixApplyStateStorage storage,
    SqliteZabbixApplyStateStorage sqliteStorage,
    IOptionsMonitor<DurableStoreOptions> durableStoreOptions) =>
{
    var durable = durableStoreOptions.CurrentValue;
    var state = storage.Load();
    var plan = ZabbixApplyStateMigrationPlan.FromState(
        dryRun: false,
        sourceBackend: storage.Backend,
        sourceLocation: storage.Location,
        targetProvider: durable.Provider,
        targetLocation: RedactedConnectionEndpoint(durable.ConnectionString),
        state);

    if (durable.Provider.Equals(storage.Backend, StringComparison.OrdinalIgnoreCase))
    {
        return Results.Ok(plan with
        {
            Status = "not_required",
            Message = "Active membership-state backend already matches DurableStore:Provider."
        });
    }

    if (durable.Provider.Equals("sqlite", StringComparison.OrdinalIgnoreCase))
    {
        sqliteStorage.Save(state);
        var sqliteState = sqliteStorage.Load();
        var migratedPlan = ZabbixApplyStateMigrationPlan.FromState(
            dryRun: false,
            sourceBackend: storage.Backend,
            sourceLocation: storage.Location,
            targetProvider: sqliteStorage.Backend,
            targetLocation: sqliteStorage.Location,
            sqliteState);
        var validationErrors = ValidateMigrationCounts(plan, migratedPlan);
        if (validationErrors.Count > 0)
        {
            return Results.Json(
                migratedPlan with
                {
                    Status = "validation_failed",
                    Message = $"SQLite migration finished with validation mismatch: {string.Join("; ", validationErrors)}"
                },
                statusCode: StatusCodes.Status500InternalServerError);
        }

        return Results.Ok(migratedPlan with
        {
            Status = "applied",
            Message = "membership-state migrated to SQLite durable store."
        });
    }

    return Results.Json(
        plan with
        {
            Status = "invalid_provider",
            Message = "DurableStore:Provider must be file or sqlite."
        },
        statusCode: StatusCodes.Status400BadRequest);
});

app.MapGet("/runtime-storage/dirty-scopes", (
    ZabbixDirtyScopeStore dirtyScopes,
    int? limit) =>
{
    return Results.Ok(dirtyScopes.Snapshot(Math.Clamp(limit ?? 100, 1, 1000)));
});

app.MapPost("/runtime-storage/dirty-scopes", (
    ZabbixDirtyScopeStore dirtyScopes,
    DirtyScopeMarkRequest request) =>
{
    var result = dirtyScopes.Mark(request);
    return Results.Ok(result);
});

app.MapDelete("/runtime-storage/dirty-scopes/{layer}", (
    ZabbixDirtyScopeStore dirtyScopes,
    string layer) =>
{
    var result = dirtyScopes.Clear(layer);
    return Results.Ok(result);
});

app.MapPost("/monitoring-coverage/snapshot", async (
    ZabbixApplyStateStore state,
    ZabbixClient zabbixClient,
    IRuntimeLookupCache runtimeLookupCache,
    IOptionsMonitor<RuntimeRedisOptions> redisOptions,
    IOptionsMonitor<MonitoringCoverageSnapshotOptions> coverageOptions,
    IOptionsMonitor<ZabbixTriggerDependenciesOptions> triggerOptions,
    MonitoringCoverageSnapshotStore snapshotStore,
    CancellationToken cancellationToken) =>
{
    var options = coverageOptions.CurrentValue;
    if (!options.Enabled)
    {
        return Results.Problem(
            title: "Monitoring coverage snapshots are disabled.",
            detail: "Enable MonitoringCoverageAudit:Enabled before running a manual snapshot.",
            statusCode: StatusCodes.Status409Conflict);
    }

    var startedAtUtc = DateTimeOffset.UtcNow;
    var serviceMemberships = state.ListMemberships("service");
    var suppressionMemberships = state.ListMemberships("suppression");
    var records = MonitoringCoverageSourceRecord.FromMemberships(serviceMemberships, suppressionMemberships);
    var hostIds = records
        .SelectMany(item => item.ZabbixHostIds)
        .Distinct(StringComparer.Ordinal)
        .ToArray();

    IReadOnlyList<ZabbixHostInfo> hosts = [];
    var zabbixStartedAtUtc = DateTimeOffset.UtcNow;
    var errors = new List<string>();
    try
    {
        hosts = await GetZabbixHostsByIdsWithLookupCacheAsync(
            runtimeLookupCache,
            redisOptions.CurrentValue,
            zabbixClient,
            hostIds,
            triggerOptions.CurrentValue.TriggerGetBatchSize,
            cancellationToken);
    }
    catch (Exception ex)
    {
        errors.Add($"Zabbix host.get failed: {ex.Message}");
    }

    var finishedAtUtc = DateTimeOffset.UtcNow;
    var snapshot = MonitoringCoverageSnapshot.FromRecords(
        records,
        hosts,
        options,
        startedAtUtc,
        zabbixStartedAtUtc,
        finishedAtUtc,
        errors);
    snapshotStore.Save(snapshot, options);
    return Results.Ok(snapshot);
});

app.MapGet("/monitoring-coverage/snapshots", (
    MonitoringCoverageSnapshotStore snapshotStore,
    int? limit) =>
{
    return Results.Ok(snapshotStore.List(Math.Clamp(limit ?? 20, 1, 200)));
});

static async Task<IReadOnlyList<ZabbixHostInfo>> GetZabbixHostsByIdsWithLookupCacheAsync(
    IRuntimeLookupCache runtimeLookupCache,
    RuntimeRedisOptions redisOptions,
    ZabbixClient zabbixClient,
    IReadOnlyList<string> hostIds,
    int batchSize,
    CancellationToken cancellationToken)
{
    var ids = hostIds
        .Where(id => !string.IsNullOrWhiteSpace(id))
        .Distinct(StringComparer.Ordinal)
        .ToArray();
    if (ids.Length == 0)
    {
        return [];
    }

    var hosts = new List<ZabbixHostInfo>();
    var missed = new List<string>();
    foreach (var hostId in ids)
    {
        var cached = await runtimeLookupCache.GetStringAsync("zabbix:host", hostId, cancellationToken);
        if (string.IsNullOrWhiteSpace(cached))
        {
            missed.Add(hostId);
            continue;
        }

        try
        {
            var host = JsonSerializer.Deserialize<ZabbixHostInfo>(cached);
            if (host is not null && !string.IsNullOrWhiteSpace(host.HostId))
            {
                hosts.Add(host);
                continue;
            }
        }
        catch (JsonException)
        {
            // Corrupt cache entries are treated as misses; the authoritative Zabbix lookup refreshes them.
        }

        missed.Add(hostId);
    }

    if (missed.Count > 0)
    {
        var loaded = await zabbixClient.GetHostsByIdsAsync(missed, batchSize, cancellationToken);
        hosts.AddRange(loaded);
        var ttl = TimeSpan.FromSeconds(redisOptions.CacheDefaultTtlSeconds);
        foreach (var host in loaded.Where(host => !string.IsNullOrWhiteSpace(host.HostId)))
        {
            await runtimeLookupCache.SetStringAsync(
                "zabbix:host",
                host.HostId,
                JsonSerializer.Serialize(host),
                ttl,
                cancellationToken);
        }
    }

    return hosts
        .DistinctBy(host => host.HostId, StringComparer.Ordinal)
        .ToArray();
}

static async Task<(IReadOnlyList<ZabbixServiceInfo> Services, bool CacheHit, bool CacheMiss)> ListManagedServicesByLayerWithLookupCacheAsync(
    IRuntimeLookupCache runtimeLookupCache,
    RuntimeRedisOptions redisOptions,
    ZabbixClient zabbixClient,
    string layer,
    int limit,
    CancellationToken cancellationToken)
{
    var normalizedLayer = ZabbixApplyPlanner.NormalizeLayer(layer);
    if (string.IsNullOrWhiteSpace(normalizedLayer))
    {
        return ([], CacheHit: false, CacheMiss: false);
    }

    var key = $"{normalizedLayer}:{Math.Clamp(limit, 1, 10000).ToString(CultureInfo.InvariantCulture)}";
    var cached = await runtimeLookupCache.GetStringAsync("zabbix:service-by-layer", key, cancellationToken);
    if (!string.IsNullOrWhiteSpace(cached))
    {
        try
        {
            var services = JsonSerializer.Deserialize<ZabbixServiceInfo[]>(cached);
            if (services is { Length: > 0 })
            {
                return (services, CacheHit: true, CacheMiss: false);
            }
        }
        catch (JsonException)
        {
            // Corrupt cache entries are treated as misses; the authoritative Zabbix lookup refreshes them.
        }
    }

    var loaded = await zabbixClient.ListManagedServicesByLayerAsync(normalizedLayer, limit, cancellationToken);
    if (loaded.Count > 0)
    {
        await runtimeLookupCache.SetStringAsync(
            "zabbix:service-by-layer",
            key,
            JsonSerializer.Serialize(loaded),
            TimeSpan.FromSeconds(redisOptions.CacheDefaultTtlSeconds),
            cancellationToken);
    }

    return (loaded, CacheHit: false, CacheMiss: true);
}

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
    IRuntimeLookupCache runtimeLookupCache,
    IOptionsMonitor<RuntimeRedisOptions> redisOptions,
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
    var zabbixServiceCacheHit = false;
    var zabbixServiceCacheMiss = false;
    if (request?.IncludeZabbixServices == true)
    {
        try
        {
            var serviceLimit = Math.Clamp(request.ZabbixServiceLimit <= 0 ? 2000 : request.ZabbixServiceLimit, 1, 10000);
            var lookup = await ListManagedServicesByLayerWithLookupCacheAsync(
                runtimeLookupCache,
                redisOptions.CurrentValue,
                zabbix,
                layer,
                serviceLimit,
                cancellationToken);
            zabbixServiceCacheHit = lookup.CacheHit;
            zabbixServiceCacheMiss = lookup.CacheMiss;
            zabbixServices = lookup.Services
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
            cacheHit = zabbixServiceCacheHit,
            cacheMiss = zabbixServiceCacheMiss,
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
    ZabbixApplyStateStore state,
    ZabbixDirtyScopeStore dirtyScopes) =>
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

    var result = state.CleanupMembershipTargets(layer, keys, request?.DryRun == true);
    DirtyScopeWorkflow.MarkFromStateCleanupResult(dirtyScopes, result, "stale membership cleanup");
    return Results.Ok(result);
});

app.MapPost("/apply/state/delete-zabbix-services", async (
    ZabbixStaleZabbixDeleteRequest? request,
    ZabbixClient zabbix,
    ZabbixDirtyScopeStore dirtyScopes,
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

    DirtyScopeWorkflow.MarkFromStaleZabbixDeleteResults(
        dirtyScopes,
        layer,
        results,
        "stale Zabbix service cleanup");

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
    IRuntimeCoordinationStore runtimeCoordination,
    CancellationToken cancellationToken) =>
{
    return await WithRuntimeOperationLockAsync(
        runtimeCoordination,
        "zabbix:sla:service:dry-run",
        async () => Results.Ok(await publisher.RunAsync(dryRun: true, cancellationToken)),
        cancellationToken);
});

app.MapPost("/sla/service/apply", async (
    ZabbixSlaPublisher publisher,
    IRuntimeCoordinationStore runtimeCoordination,
    ZabbixDirtyScopeStore dirtyScopes,
    CancellationToken cancellationToken) =>
{
    return await WithRuntimeOperationLockAsync(
        runtimeCoordination,
        "zabbix:sla:service:apply",
        async () =>
        {
            var result = await publisher.RunAsync(dryRun: false, cancellationToken);
            DirtyScopeWorkflow.MarkFromSlaResult(dirtyScopes, result, "service SLA apply");
            return Results.Ok(result);
        },
        cancellationToken);
});

app.MapPost("/dependencies/suppression/dry-run", async (
    ZabbixTriggerDependencyRunRequest? request,
    ZabbixTriggerDependencyApplier applier,
    IRuntimeCoordinationStore runtimeCoordination,
    ZabbixDirtyScopeStore dirtyScopes,
    CancellationToken cancellationToken) =>
{
    return await WithRuntimeOperationLockAsync(
        runtimeCoordination,
        "zabbix:dependencies:suppression:dry-run",
        async () => Results.Ok(await applier.RunAsync(
            dryRun: true,
            DirtyScopeWorkflow.WithDirtyScopeDefault(dirtyScopes, request, "suppression"),
            cancellationToken)),
        cancellationToken);
});

app.MapPost("/dependencies/suppression/apply", async (
    ZabbixTriggerDependencyRunRequest? request,
    ZabbixTriggerDependencyApplier applier,
    IRuntimeCoordinationStore runtimeCoordination,
    ZabbixDirtyScopeStore dirtyScopes,
    CancellationToken cancellationToken) =>
{
    return await WithRuntimeOperationLockAsync(
        runtimeCoordination,
        "zabbix:dependencies:suppression:apply",
        async () =>
        {
            var result = await applier.RunAsync(
                dryRun: false,
                DirtyScopeWorkflow.WithDirtyScopeDefault(dirtyScopes, request, "suppression"),
                cancellationToken);
            DirtyScopeWorkflow.MarkFromTriggerDependencyResult(dirtyScopes, result, "suppression dependencies apply");
            return Results.Ok(result);
        },
        cancellationToken);
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
    IRuntimeCoordinationStore runtimeCoordination,
    CancellationToken cancellationToken) =>
{
    return await WithRuntimeOperationLockAsync(
        runtimeCoordination,
        $"zabbix:graph:{ZabbixApplyPlanner.NormalizeLayer(FirstNonEmpty(request.Layer, request.Commands.FirstOrDefault()?.Layer))}:dry-run",
        async () =>
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
        },
        cancellationToken);
});

app.MapPost("/commands/apply-graph", async (
    ZabbixGraphApplyRequest request,
    IOptionsMonitor<ApplyOptions> options,
    ZabbixApplyStateStore state,
    ZabbixAggregationApplier applier,
    IRuntimeCoordinationStore runtimeCoordination,
    ZabbixDirtyScopeStore dirtyScopes,
    CancellationToken cancellationToken) =>
{
    return await WithRuntimeOperationLockAsync(
        runtimeCoordination,
        $"zabbix:graph:{ZabbixApplyPlanner.NormalizeLayer(FirstNonEmpty(request.Layer, request.Commands.FirstOrDefault()?.Layer))}:apply",
        async () =>
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
                DirtyScopeWorkflow.MarkFromCommandResult(dirtyScopes, commandResult, "graph apply");
            }

            if (result.Errors.Count > 0)
            {
                dirtyScopes.MarkResult(
                    layer,
                    request.ScopeKeys,
                    "failed",
                    $"graph apply failed: {string.Join("; ", result.Errors.Take(3))}");
            }

            return string.Equals(result.Status, "error", StringComparison.OrdinalIgnoreCase)
                || result.Errors.Count > 0
                ? Results.Problem(
                    title: "Zabbix graph was not applied.",
                    detail: result.Errors.FirstOrDefault() ?? result.Message,
                    extensions: new Dictionary<string, object?> { ["result"] = result },
                    statusCode: StatusCodes.Status502BadGateway)
                : Results.Ok(result);
        },
        cancellationToken);
});

app.MapPost("/commands/apply", async (
    AggregationCommand command,
    IOptionsMonitor<ApplyOptions> options,
    ZabbixApplyStateStore state,
    ZabbixAggregationApplier applier,
    ZabbixDirtyScopeStore dirtyScopes,
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
    DirtyScopeWorkflow.MarkFromCommandResult(dirtyScopes, result, "single command apply");
    return string.Equals(result.Status, "error", StringComparison.OrdinalIgnoreCase)
        ? Results.Problem(
            title: "Zabbix command was not applied.",
            detail: string.IsNullOrWhiteSpace(result.Error) ? result.Message : result.Error,
            extensions: new Dictionary<string, object?> { ["result"] = result },
            statusCode: StatusCodes.Status502BadGateway)
        : Results.Ok(result);
});

app.Run();

static async Task<IResult> WithRuntimeOperationLockAsync(
    IRuntimeCoordinationStore runtimeCoordination,
    string operationKey,
    Func<Task<IResult>> action,
    CancellationToken cancellationToken)
{
    await using var lease = await runtimeCoordination.TryAcquireLockAsync(operationKey, cancellationToken);
    if (!lease.Acquired)
    {
        return Results.Json(
            new
            {
                status = lease.Status,
                operationKey,
                backend = lease.Backend,
                message = lease.Message
            },
            statusCode: lease.StatusCode);
    }

    var operation = runtimeCoordination.StartOperation(operationKey, lease.Backend);
    try
    {
        var result = await action();
        runtimeCoordination.CompleteOperation(operation.OperationId, "completed");
        return result;
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
        runtimeCoordination.CompleteOperation(operation.OperationId, "cancelled", "Operation was cancelled.");
        throw;
    }
    catch (Exception ex)
    {
        runtimeCoordination.CompleteOperation(operation.OperationId, "failed", ex.Message);
        throw;
    }
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

static string RedactedConnectionEndpoint(string connectionString)
{
    if (string.IsNullOrWhiteSpace(connectionString))
    {
        return "";
    }

    var parts = connectionString
        .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(part =>
        {
            var index = part.IndexOf('=', StringComparison.Ordinal);
            if (index <= 0)
            {
                return part.Contains('@', StringComparison.Ordinal) ? "***" : part;
            }

            var key = part[..index].Trim();
            var value = part[(index + 1)..].Trim();
            return key.ToLowerInvariant() switch
            {
                "password" or "pwd" or "user id" or "userid" or "username" or "uid" or "token" or "access key" => $"{key}=***",
                _ => $"{key}={value}"
            };
        });

    return string.Join(';', parts);
}

static IReadOnlyList<string> ValidateMigrationCounts(
    ZabbixApplyStateMigrationPlan expected,
    ZabbixApplyStateMigrationPlan actual)
{
    var errors = new List<string>();
    AddMismatch(errors, "target memberships", expected.MembershipCount, actual.MembershipCount);
    AddMismatch(errors, "source memberships", expected.SourceMembershipCount, actual.SourceMembershipCount);
    AddMismatch(errors, "pending sources", expected.PendingSourceCount, actual.PendingSourceCount);
    AddMismatch(errors, "trigger dependencies", expected.TriggerDependencyCount, actual.TriggerDependencyCount);
    AddMismatch(errors, "applied graph objects", expected.AppliedGraphObjectCount, actual.AppliedGraphObjectCount);
    return errors;
}

static void AddMismatch(ICollection<string> errors, string label, int expected, int actual)
{
    if (expected != actual)
    {
        errors.Add($"{label}: expected {expected}, actual {actual}");
    }
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
    ZabbixDirtyScopeStore dirtyScopes,
    ZabbixAggregationApplier applier,
    IServiceProvider services,
    ILogger<ZabbixServiceAggregationCommandWorker> logger)
    : ZabbixLayerAggregationCommandWorker("service", kafkaOptions, topicOptions, applyOptions, debugOptions, state, dirtyScopes, applier, services, logger);

public sealed class ZabbixSuppressionAggregationCommandWorker(
    IOptions<KafkaOptions> kafkaOptions,
    IOptions<KafkaTopicsOptions> topicOptions,
    IOptionsMonitor<ApplyOptions> applyOptions,
    IOptions<DebugOptions> debugOptions,
    ZabbixApplyStateStore state,
    ZabbixDirtyScopeStore dirtyScopes,
    ZabbixAggregationApplier applier,
    IServiceProvider services,
    ILogger<ZabbixSuppressionAggregationCommandWorker> logger)
    : ZabbixLayerAggregationCommandWorker("suppression", kafkaOptions, topicOptions, applyOptions, debugOptions, state, dirtyScopes, applier, services, logger);

public abstract class ZabbixLayerAggregationCommandWorker : KafkaJsonConsumerWorker<AggregationCommand>
{
    private readonly string layer;
    private readonly IOptions<KafkaOptions> kafkaOptions;
    private readonly IOptions<KafkaTopicsOptions> topicOptions;
    private readonly IOptionsMonitor<ApplyOptions> applyOptions;
    private readonly IOptions<DebugOptions> debugOptions;
    private readonly ZabbixApplyStateStore state;
    private readonly ZabbixDirtyScopeStore dirtyScopes;
    private readonly ZabbixAggregationApplier applier;
    private readonly ILogger logger;

    protected ZabbixLayerAggregationCommandWorker(
        string layer,
        IOptions<KafkaOptions> kafkaOptions,
        IOptions<KafkaTopicsOptions> topicOptions,
        IOptionsMonitor<ApplyOptions> applyOptions,
        IOptions<DebugOptions> debugOptions,
        ZabbixApplyStateStore state,
        ZabbixDirtyScopeStore dirtyScopes,
        ZabbixAggregationApplier applier,
        IServiceProvider services,
        ILogger logger)
        : base(kafkaOptions, services, logger)
    {
        this.layer = layer;
        this.kafkaOptions = kafkaOptions;
        this.topicOptions = topicOptions;
        this.applyOptions = applyOptions;
        this.debugOptions = debugOptions;
        this.state = state;
        this.dirtyScopes = dirtyScopes;
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
        DirtyScopeWorkflow.MarkFromCommandResult(dirtyScopes, result, "kafka apply");

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

public static class DirtyScopeWorkflow
{
    public static ZabbixTriggerDependencyRunRequest? WithDirtyScopeDefault(
        ZabbixDirtyScopeStore dirtyScopes,
        ZabbixTriggerDependencyRunRequest? request,
        string layer)
    {
        if (request?.UseDirtyScopeDefault == false || (request?.ScopeKeys.Count ?? 0) > 0)
        {
            return request;
        }

        var keys = dirtyScopes.PendingTargetKeys(layer, limit: 5000);
        if (keys.Count == 0)
        {
            return request;
        }

        return new ZabbixTriggerDependencyRunRequest
        {
            TransitiveGroupDependencyDepth = request?.TransitiveGroupDependencyDepth,
            ScopeKeys = keys,
            ScopeFromDirtyScopes = true,
            UseDirtyScopeDefault = request?.UseDirtyScopeDefault ?? true
        };
    }

    public static void MarkFromCommandResult(
        ZabbixDirtyScopeStore dirtyScopes,
        ZabbixCommandApplyResult result,
        string operation)
    {
        if (string.IsNullOrWhiteSpace(result.TargetKey)
            || string.Equals(result.Status, "dry-run", StringComparison.OrdinalIgnoreCase)
            || string.Equals(result.Status, "pending_manual", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var scopeStatus = string.Equals(result.Status, "error", StringComparison.OrdinalIgnoreCase)
            ? "failed"
            : "processed";
        var detail = string.IsNullOrWhiteSpace(result.Error)
            ? $"{operation}: {result.Status}"
            : $"{operation}: {result.Error}";
        dirtyScopes.MarkResult(result.Layer, [result.TargetKey], scopeStatus, detail);
    }

    public static void MarkFromStateCleanupResult(
        ZabbixDirtyScopeStore dirtyScopes,
        ZabbixStateCleanupResult result,
        string operation)
    {
        if (result.DryRun || result.Removed == 0)
        {
            return;
        }

        var keys = result.Targets
            .Select(item => item.TargetManagedKey)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (keys.Length == 0)
        {
            return;
        }

        dirtyScopes.MarkResult(
            result.Layer,
            keys,
            "processed",
            $"{operation}: removed={result.Removed}; requested={result.Requested}; missing={result.MissingKeys.Count}");
    }

    public static void MarkFromStaleZabbixDeleteResults(
        ZabbixDirtyScopeStore dirtyScopes,
        string layer,
        IReadOnlyList<ZabbixManagedServiceDeleteResult> results,
        string operation)
    {
        var successKeys = results
            .Where(item => !item.Action.Equals("failed", StringComparison.OrdinalIgnoreCase))
            .Select(item => item.ManagedKey)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (successKeys.Length > 0)
        {
            dirtyScopes.MarkResult(layer, successKeys, "processed", $"{operation}: deleted or skipped in Zabbix");
        }

        var failedKeys = results
            .Where(item => item.Action.Equals("failed", StringComparison.OrdinalIgnoreCase))
            .Select(item => item.ManagedKey)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (failedKeys.Length > 0)
        {
            dirtyScopes.MarkResult(layer, failedKeys, "failed", $"{operation}: failed to delete stale Zabbix service");
        }
    }

    public static void MarkFromSlaResult(
        ZabbixDirtyScopeStore dirtyScopes,
        ZabbixSlaPublishResult result,
        string operation)
    {
        if (result.DryRun)
        {
            return;
        }

        var keys = result.AppliedServiceManagedKeys
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (keys.Length == 0)
        {
            return;
        }

        var status = string.Equals(result.Status, "error", StringComparison.OrdinalIgnoreCase)
            ? "failed"
            : "processed";
        dirtyScopes.MarkResult(
            "service",
            keys,
            status,
            $"{operation}: {result.Status}; services={result.ServicesApplied}; slas={result.SlasApplied}");
    }

    public static void MarkFromTriggerDependencyResult(
        ZabbixDirtyScopeStore dirtyScopes,
        ZabbixTriggerDependencyRunResult result,
        string operation)
    {
        if (result.DryRun)
        {
            return;
        }

        var keys = result.Aggregates
            .Select(item => item.TargetManagedKey)
            .Concat(result.DesiredDependencies.SelectMany(item => new[]
            {
                item.DependentTargetManagedKey,
                item.DependencyTargetManagedKey
            }))
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (keys.Length == 0)
        {
            return;
        }

        var status = string.Equals(result.Status, "error", StringComparison.OrdinalIgnoreCase)
            ? "failed"
            : "processed";
        var detail = $"{operation}: {result.Status}; aggregates={result.AggregateCount}; dependencies={result.DesiredDependencyCount}; updatedTriggers={result.TriggersUpdated}";
        if (result.Errors.Count > 0)
        {
            detail += $"; errors={string.Join("; ", result.Errors.Take(3))}";
        }

        dirtyScopes.MarkResult(result.Layer, keys, status, detail);
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

public interface IZabbixApplyStateStorage
{
    string Backend { get; }

    string Location { get; }

    ZabbixApplyPersistentState Load();

    void Save(ZabbixApplyPersistentState state);

    ZabbixApplyStateStorageStatus Status();
}

public sealed class FileZabbixApplyStateStorage : IZabbixApplyStateStorage
{
    private readonly string stateFilePath;
    private readonly ILogger<FileZabbixApplyStateStorage> logger;

    public FileZabbixApplyStateStorage(
        IOptions<ZabbixApplyStateOptions> options,
        ILogger<FileZabbixApplyStateStorage> logger)
    {
        stateFilePath = options.Value.FilePath;
        this.logger = logger;
    }

    public string Backend => "file";

    public string Location => stateFilePath;

    public ZabbixApplyPersistentState Load()
    {
        try
        {
            if (!File.Exists(stateFilePath))
            {
                return new ZabbixApplyPersistentState();
            }

            var state = JsonSerializer.Deserialize<ZabbixApplyPersistentState>(
                File.ReadAllText(stateFilePath),
                ZabbixApplyStateStorageJson.Options);
            return state ?? new ZabbixApplyPersistentState();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            logger.LogWarning(ex, "Failed to load Zabbix apply membership state from {StateFilePath}", stateFilePath);
            return new ZabbixApplyPersistentState();
        }
    }

    public void Save(ZabbixApplyPersistentState state)
    {
        try
        {
            var directory = Path.GetDirectoryName(stateFilePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(stateFilePath, JsonSerializer.Serialize(state, ZabbixApplyStateStorageJson.Options));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Failed to save Zabbix apply membership state to {StateFilePath}", stateFilePath);
        }
    }

    public ZabbixApplyStateStorageStatus Status()
    {
        var exists = File.Exists(stateFilePath);
        return new ZabbixApplyStateStorageStatus
        {
            Backend = Backend,
            Location = stateFilePath,
            Exists = exists,
            SizeBytes = exists ? new FileInfo(stateFilePath).Length : 0
        };
    }
}

public sealed class SqliteZabbixApplyStateStorage : IZabbixApplyStateStorage
{
    private const int SchemaVersion = 1;
    private readonly string connectionString;
    private readonly string location;
    private readonly string bootstrapFilePath;
    private readonly ILogger<SqliteZabbixApplyStateStorage> logger;
    private readonly object syncRoot = new();

    public SqliteZabbixApplyStateStorage(
        IOptionsMonitor<DurableStoreOptions> durableOptions,
        IOptions<ZabbixApplyStateOptions> fileOptions,
        ILogger<SqliteZabbixApplyStateStorage> logger)
    {
        connectionString = durableOptions.CurrentValue.ConnectionString;
        location = SqliteLocation(connectionString);
        bootstrapFilePath = fileOptions.Value.FilePath;
        this.logger = logger;
    }

    public string Backend => "sqlite";

    public string Location => location;

    public ZabbixApplyPersistentState Load()
    {
        lock (syncRoot)
        {
            try
            {
                EnsureSchema();
                using var connection = OpenConnection();
                var payload = ReadStatePayload(connection);
                if (!string.IsNullOrWhiteSpace(payload))
                {
                    return JsonSerializer.Deserialize<ZabbixApplyPersistentState>(
                            payload,
                            ZabbixApplyStateStorageJson.Options)
                        ?? new ZabbixApplyPersistentState();
                }

                var bootstrapped = TryLoadBootstrapFile();
                if (bootstrapped is not null)
                {
                    Save(bootstrapped);
                    return bootstrapped;
                }
            }
            catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
            {
                logger.LogWarning(ex, "Failed to load Zabbix apply membership state from SQLite {Location}", location);
            }

            return new ZabbixApplyPersistentState();
        }
    }

    public void Save(ZabbixApplyPersistentState state)
    {
        lock (syncRoot)
        {
            try
            {
                EnsureSchema();
                using var connection = OpenConnection();
                using var transaction = connection.BeginTransaction();
                ExecuteNonQuery(connection, transaction, "delete from zabbix_apply_state_documents;");
                ExecuteNonQuery(connection, transaction, "delete from zabbix_source_memberships;");
                ExecuteNonQuery(connection, transaction, "delete from zabbix_target_memberships;");
                ExecuteNonQuery(connection, transaction, "delete from zabbix_managed_trigger_dependencies;");
                ExecuteNonQuery(connection, transaction, "delete from zabbix_applied_graph_objects;");

                using (var command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = """
                        insert into zabbix_apply_state_documents(id, payload_json, updated_at_utc)
                        values (1, $payload, $updated_at_utc);
                        """;
                    command.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(state, ZabbixApplyStateStorageJson.Options));
                    command.Parameters.AddWithValue("$updated_at_utc", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
                    command.ExecuteNonQuery();
                }

                foreach (var membership in state.Memberships ?? [])
                {
                    InsertMembership(connection, transaction, membership);
                }

                foreach (var dependency in state.TriggerDependencies ?? [])
                {
                    InsertTriggerDependency(connection, transaction, dependency);
                }

                foreach (var graphObject in state.AppliedGraphObjects ?? [])
                {
                    InsertAppliedGraphObject(connection, transaction, graphObject);
                }

                transaction.Commit();
            }
            catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                logger.LogWarning(ex, "Failed to save Zabbix apply membership state to SQLite {Location}", location);
            }
        }
    }

    public ZabbixApplyStateStorageStatus Status()
    {
        try
        {
            EnsureSchema();
            using var connection = OpenConnection();
            return new ZabbixApplyStateStorageStatus
            {
                Backend = Backend,
                Location = location,
                Exists = File.Exists(location),
                SizeBytes = File.Exists(location) ? new FileInfo(location).Length : 0,
                SchemaVersion = ReadSchemaVersion(connection),
                TargetMembershipCount = CountRows(connection, "zabbix_target_memberships"),
                SourceMembershipCount = CountRows(connection, "zabbix_source_memberships", "pending = 0"),
                PendingSourceCount = CountRows(connection, "zabbix_source_memberships", "pending = 1"),
                ManagedTriggerDependencyCount = CountRows(connection, "zabbix_managed_trigger_dependencies"),
                AppliedGraphObjectCount = CountRows(connection, "zabbix_applied_graph_objects")
            };
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            logger.LogWarning(ex, "Failed to inspect Zabbix apply membership SQLite status for {Location}", location);
            return new ZabbixApplyStateStorageStatus
            {
                Backend = Backend,
                Location = location,
                Exists = File.Exists(location),
                SizeBytes = File.Exists(location) ? new FileInfo(location).Length : 0,
                Error = ex.Message
            };
        }
    }

    private ZabbixApplyPersistentState? TryLoadBootstrapFile()
    {
        if (string.IsNullOrWhiteSpace(bootstrapFilePath) || !File.Exists(bootstrapFilePath))
        {
            return null;
        }

        try
        {
            var state = JsonSerializer.Deserialize<ZabbixApplyPersistentState>(
                File.ReadAllText(bootstrapFilePath),
                ZabbixApplyStateStorageJson.Options);
            return state;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            logger.LogWarning(ex, "Failed to bootstrap SQLite membership-state from {StateFilePath}", bootstrapFilePath);
            return null;
        }
    }

    private void EnsureSchema()
    {
        var directory = Path.GetDirectoryName(location);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var connection = OpenConnection();
        ExecuteNonQuery(connection, null, """
            create table if not exists c2m_schema_version (
                name text primary key,
                version integer not null,
                updated_at_utc text not null
            );
            """);
        ExecuteNonQuery(connection, null, """
            insert into c2m_schema_version(name, version, updated_at_utc)
            values ('zabbix_apply_state', 1, strftime('%Y-%m-%dT%H:%M:%fZ','now'))
            on conflict(name) do update set
                version = excluded.version,
                updated_at_utc = excluded.updated_at_utc;
            """);
        ExecuteNonQuery(connection, null, """
            create table if not exists zabbix_apply_state_documents (
                id integer primary key check (id = 1),
                payload_json text not null,
                updated_at_utc text not null
            );
            """);
        ExecuteNonQuery(connection, null, """
            create table if not exists zabbix_target_memberships (
                layer text not null,
                target_managed_key text not null,
                target_class text not null,
                target_card_id text not null,
                target_name text not null,
                aggregation_type text not null,
                is_critical text not null,
                threshold_value text not null,
                n_value text not null,
                relations_json text not null,
                updated_at_utc text not null,
                primary key(layer, target_managed_key)
            );
            """);
        ExecuteNonQuery(connection, null, """
            create table if not exists zabbix_source_memberships (
                layer text not null,
                target_managed_key text not null,
                source_key text not null,
                pending integer not null,
                source_class text not null,
                source_card_id text not null,
                source_key_attribute text not null,
                source_key_value text not null,
                zabbix_host_id text not null,
                source_leaf_managed_key text not null,
                updated_at_utc text not null,
                primary key(layer, target_managed_key, source_key, pending)
            );
            """);
        ExecuteNonQuery(connection, null, "create index if not exists ix_zabbix_source_memberships_source on zabbix_source_memberships(layer, source_class, source_card_id);");
        ExecuteNonQuery(connection, null, """
            create table if not exists zabbix_managed_trigger_dependencies (
                layer text not null,
                dependent_trigger_id text not null,
                dependency_trigger_id text not null,
                dependent_trigger_name text not null,
                dependency_trigger_name text not null,
                dependent_host_id text not null,
                dependency_host_id text not null,
                dependent_target_managed_key text not null,
                dependency_target_managed_key text not null,
                dependent_target_name text not null,
                dependency_target_name text not null,
                relation_domain_code text not null,
                updated_at_utc text not null,
                primary key(layer, dependent_trigger_id, dependency_trigger_id)
            );
            """);
        ExecuteNonQuery(connection, null, """
            create table if not exists zabbix_applied_graph_objects (
                layer text not null,
                object_key text not null,
                object_type text not null,
                display_name text not null,
                target_managed_key text not null,
                source_membership_key text not null,
                rule_id text not null,
                rule_name text not null,
                class_code text not null,
                card_id text not null,
                content_hash text not null,
                updated_at_utc text not null,
                primary key(layer, object_key)
            );
            """);
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(connectionString);
        connection.Open();
        return connection;
    }

    private static string? ReadStatePayload(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "select payload_json from zabbix_apply_state_documents where id = 1;";
        return command.ExecuteScalar() as string;
    }

    private static void InsertMembership(SqliteConnection connection, SqliteTransaction transaction, ZabbixTargetMembership membership)
    {
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                insert into zabbix_target_memberships(
                    layer, target_managed_key, target_class, target_card_id, target_name,
                    aggregation_type, is_critical, threshold_value, n_value, relations_json, updated_at_utc)
                values (
                    $layer, $target_managed_key, $target_class, $target_card_id, $target_name,
                    $aggregation_type, $is_critical, $threshold_value, $n_value, $relations_json, $updated_at_utc);
                """;
            command.Parameters.AddWithValue("$layer", membership.Layer);
            command.Parameters.AddWithValue("$target_managed_key", membership.TargetManagedKey);
            command.Parameters.AddWithValue("$target_class", membership.TargetClass);
            command.Parameters.AddWithValue("$target_card_id", membership.TargetCardId);
            command.Parameters.AddWithValue("$target_name", membership.TargetName);
            command.Parameters.AddWithValue("$aggregation_type", membership.AggregationType);
            command.Parameters.AddWithValue("$is_critical", membership.IsCritical);
            command.Parameters.AddWithValue("$threshold_value", membership.Threshold);
            command.Parameters.AddWithValue("$n_value", membership.N);
            command.Parameters.AddWithValue("$relations_json", JsonSerializer.Serialize(membership.Relations, ZabbixApplyStateStorageJson.Options));
            command.Parameters.AddWithValue("$updated_at_utc", membership.UpdatedAtUtc.ToString("O", CultureInfo.InvariantCulture));
            command.ExecuteNonQuery();
        }

        foreach (var source in membership.Sources)
        {
            InsertSourceMembership(connection, transaction, membership, source.Key, source.Value, pending: false);
        }

        foreach (var source in membership.PendingSources)
        {
            InsertSourceMembership(connection, transaction, membership, source.Key, source.Value, pending: true);
        }
    }

    private static void InsertSourceMembership(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ZabbixTargetMembership membership,
        string sourceKey,
        ZabbixSourceMembership source,
        bool pending)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            insert into zabbix_source_memberships(
                layer, target_managed_key, source_key, pending, source_class, source_card_id,
                source_key_attribute, source_key_value, zabbix_host_id, source_leaf_managed_key, updated_at_utc)
            values (
                $layer, $target_managed_key, $source_key, $pending, $source_class, $source_card_id,
                $source_key_attribute, $source_key_value, $zabbix_host_id, $source_leaf_managed_key, $updated_at_utc);
            """;
        command.Parameters.AddWithValue("$layer", membership.Layer);
        command.Parameters.AddWithValue("$target_managed_key", membership.TargetManagedKey);
        command.Parameters.AddWithValue("$source_key", sourceKey);
        command.Parameters.AddWithValue("$pending", pending ? 1 : 0);
        command.Parameters.AddWithValue("$source_class", source.SourceClass);
        command.Parameters.AddWithValue("$source_card_id", source.SourceCardId);
        command.Parameters.AddWithValue("$source_key_attribute", source.SourceKeyAttribute);
        command.Parameters.AddWithValue("$source_key_value", source.SourceKeyValue);
        command.Parameters.AddWithValue("$zabbix_host_id", source.ZabbixHostId);
        command.Parameters.AddWithValue("$source_leaf_managed_key", source.SourceLeafManagedKey);
        command.Parameters.AddWithValue("$updated_at_utc", source.UpdatedAtUtc.ToString("O", CultureInfo.InvariantCulture));
        command.ExecuteNonQuery();
    }

    private static void InsertTriggerDependency(SqliteConnection connection, SqliteTransaction transaction, ZabbixManagedTriggerDependency dependency)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            insert into zabbix_managed_trigger_dependencies(
                layer, dependent_trigger_id, dependency_trigger_id, dependent_trigger_name,
                dependency_trigger_name, dependent_host_id, dependency_host_id,
                dependent_target_managed_key, dependency_target_managed_key,
                dependent_target_name, dependency_target_name, relation_domain_code, updated_at_utc)
            values (
                $layer, $dependent_trigger_id, $dependency_trigger_id, $dependent_trigger_name,
                $dependency_trigger_name, $dependent_host_id, $dependency_host_id,
                $dependent_target_managed_key, $dependency_target_managed_key,
                $dependent_target_name, $dependency_target_name, $relation_domain_code, $updated_at_utc);
            """;
        command.Parameters.AddWithValue("$layer", dependency.Layer);
        command.Parameters.AddWithValue("$dependent_trigger_id", dependency.DependentTriggerId);
        command.Parameters.AddWithValue("$dependency_trigger_id", dependency.DependencyTriggerId);
        command.Parameters.AddWithValue("$dependent_trigger_name", dependency.DependentTriggerName);
        command.Parameters.AddWithValue("$dependency_trigger_name", dependency.DependencyTriggerName);
        command.Parameters.AddWithValue("$dependent_host_id", dependency.DependentHostId);
        command.Parameters.AddWithValue("$dependency_host_id", dependency.DependencyHostId);
        command.Parameters.AddWithValue("$dependent_target_managed_key", dependency.DependentTargetManagedKey);
        command.Parameters.AddWithValue("$dependency_target_managed_key", dependency.DependencyTargetManagedKey);
        command.Parameters.AddWithValue("$dependent_target_name", dependency.DependentTargetName);
        command.Parameters.AddWithValue("$dependency_target_name", dependency.DependencyTargetName);
        command.Parameters.AddWithValue("$relation_domain_code", dependency.RelationDomainCode);
        command.Parameters.AddWithValue("$updated_at_utc", dependency.UpdatedAtUtc.ToString("O", CultureInfo.InvariantCulture));
        command.ExecuteNonQuery();
    }

    private static void InsertAppliedGraphObject(SqliteConnection connection, SqliteTransaction transaction, ZabbixAppliedGraphObject graphObject)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            insert into zabbix_applied_graph_objects(
                layer, object_key, object_type, display_name, target_managed_key,
                source_membership_key, rule_id, rule_name, class_code, card_id, content_hash, updated_at_utc)
            values (
                $layer, $object_key, $object_type, $display_name, $target_managed_key,
                $source_membership_key, $rule_id, $rule_name, $class_code, $card_id, $content_hash, $updated_at_utc);
            """;
        command.Parameters.AddWithValue("$layer", graphObject.Layer);
        command.Parameters.AddWithValue("$object_key", graphObject.ObjectKey);
        command.Parameters.AddWithValue("$object_type", graphObject.ObjectType);
        command.Parameters.AddWithValue("$display_name", graphObject.DisplayName);
        command.Parameters.AddWithValue("$target_managed_key", graphObject.TargetManagedKey);
        command.Parameters.AddWithValue("$source_membership_key", graphObject.SourceMembershipKey);
        command.Parameters.AddWithValue("$rule_id", graphObject.RuleId);
        command.Parameters.AddWithValue("$rule_name", graphObject.RuleName);
        command.Parameters.AddWithValue("$class_code", graphObject.ClassCode);
        command.Parameters.AddWithValue("$card_id", graphObject.CardId);
        command.Parameters.AddWithValue("$content_hash", graphObject.ContentHash);
        command.Parameters.AddWithValue("$updated_at_utc", graphObject.UpdatedAtUtc.ToString("O", CultureInfo.InvariantCulture));
        command.ExecuteNonQuery();
    }

    private static int ReadSchemaVersion(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "select version from c2m_schema_version where name = 'zabbix_apply_state';";
        return Convert.ToInt32(command.ExecuteScalar() ?? SchemaVersion, CultureInfo.InvariantCulture);
    }

    private static int CountRows(SqliteConnection connection, string tableName, string where = "")
    {
        using var command = connection.CreateCommand();
        command.CommandText = string.IsNullOrWhiteSpace(where)
            ? $"select count(*) from {tableName};"
            : $"select count(*) from {tableName} where {where};";
        return Convert.ToInt32(command.ExecuteScalar() ?? 0, CultureInfo.InvariantCulture);
    }

    private static void ExecuteNonQuery(SqliteConnection connection, SqliteTransaction? transaction, string commandText)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = commandText;
        command.ExecuteNonQuery();
    }

    private static string SqliteLocation(string connectionString)
    {
        try
        {
            var builder = new SqliteConnectionStringBuilder(connectionString);
            return string.IsNullOrWhiteSpace(builder.DataSource)
                ? connectionString
                : builder.DataSource;
        }
        catch (ArgumentException)
        {
            return connectionString;
        }
    }
}

public static class ZabbixApplyStateStorageJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };
}

public sealed class ZabbixDirtyScopeStore(
    IOptionsMonitor<DurableStoreOptions> durableOptions,
    ILogger<ZabbixDirtyScopeStore> logger)
{
    private readonly ConcurrentDictionary<string, DirtyScopeEntry> memoryEntries = new(StringComparer.Ordinal);
    private readonly object syncRoot = new();

    public DirtyScopeSnapshot Snapshot(int limit)
    {
        lock (syncRoot)
        {
            if (UseSqlite(out var connectionString, out var location))
            {
                try
                {
                    EnsureSqliteSchema(connectionString, location);
                    using var connection = OpenSqlite(connectionString);
                    return new DirtyScopeSnapshot
                    {
                        Backend = "sqlite",
                        Location = location,
                        Layers = ReadSqliteEntries(connection, limit)
                    };
                }
                catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException or InvalidOperationException)
                {
                    logger.LogWarning(ex, "Failed to read dirty scopes from SQLite {Location}", location);
                    return MemorySnapshot(limit, $"SQLite dirty scope read failed: {ex.Message}", location);
                }
            }

            return MemorySnapshot(limit);
        }
    }

    public DirtyScopeSummary Summary()
    {
        var snapshot = Snapshot(1000);
        return new DirtyScopeSummary
        {
            Backend = snapshot.Backend,
            Location = snapshot.Location,
            Error = snapshot.Error,
            TotalCount = snapshot.Layers.Sum(layer => layer.Count),
            Layers = snapshot.Layers
                .Select(layer => new DirtyScopeLayerSummary
                {
                    Layer = layer.Layer,
                    Count = layer.Count,
                    UpdatedAtUtc = layer.UpdatedAtUtc
                })
                .ToArray()
        };
    }

    public IReadOnlyList<string> PendingTargetKeys(string layer, int limit)
    {
        var normalizedLayer = ZabbixApplyPlanner.NormalizeLayer(layer);
        if (string.IsNullOrWhiteSpace(normalizedLayer))
        {
            normalizedLayer = string.Equals(layer, "suppression", StringComparison.OrdinalIgnoreCase)
                ? "suppression"
                : "service";
        }

        var effectiveLimit = Math.Clamp(limit, 1, 10000);
        return Snapshot(effectiveLimit)
            .Layers
            .Where(item => item.Layer.Equals(normalizedLayer, StringComparison.OrdinalIgnoreCase))
            .SelectMany(item => item.Entries)
            .Where(item => item.ScopeType.Equals("target", StringComparison.OrdinalIgnoreCase)
                && item.Status.Equals("pending", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => item.UpdatedAtUtc)
            .Select(item => item.ScopeKey)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.Ordinal)
            .Take(effectiveLimit)
            .ToArray();
    }

    public DirtyScopeMarkResult Mark(DirtyScopeMarkRequest request)
    {
        var normalizedLayer = ZabbixApplyPlanner.NormalizeLayer(request.Layer);
        if (string.IsNullOrWhiteSpace(normalizedLayer))
        {
            normalizedLayer = string.Equals(request.Layer, "suppression", StringComparison.OrdinalIgnoreCase)
                ? "suppression"
                : "service";
        }

        var now = DateTimeOffset.UtcNow;
        var entries = NormalizeMarkEntries(request, now);
        if (entries.Count == 0)
        {
            return new DirtyScopeMarkResult
            {
                Layer = normalizedLayer,
                AddedOrUpdated = 0,
                Snapshot = Snapshot(1000)
            };
        }

        lock (syncRoot)
        {
            if (UseSqlite(out var connectionString, out var location))
            {
                try
                {
                    EnsureSqliteSchema(connectionString, location);
                    using var connection = OpenSqlite(connectionString);
                    using var transaction = connection.BeginTransaction();
                    foreach (var entry in entries)
                    {
                        UpsertSqliteEntry(connection, transaction, entry with { Layer = normalizedLayer });
                    }

                    transaction.Commit();
                    return new DirtyScopeMarkResult
                    {
                        Layer = normalizedLayer,
                        AddedOrUpdated = entries.Count,
                        Snapshot = Snapshot(1000)
                    };
                }
                catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException or InvalidOperationException)
                {
                    logger.LogWarning(ex, "Failed to mark dirty scopes in SQLite {Location}; using memory fallback for this process.", location);
                }
            }

            foreach (var entry in entries)
            {
                memoryEntries[DirtyScopeKey(normalizedLayer, entry.ScopeType, entry.ScopeKey)] = entry with { Layer = normalizedLayer };
            }

            return new DirtyScopeMarkResult
            {
                Layer = normalizedLayer,
                AddedOrUpdated = entries.Count,
                Snapshot = MemorySnapshot(1000)
            };
        }
    }

    public DirtyScopeClearResult Clear(string layer)
    {
        var normalizedLayer = ZabbixApplyPlanner.NormalizeLayer(layer);
        if (string.IsNullOrWhiteSpace(normalizedLayer))
        {
            normalizedLayer = string.Equals(layer, "suppression", StringComparison.OrdinalIgnoreCase)
                ? "suppression"
                : "service";
        }

        lock (syncRoot)
        {
            var removed = 0;
            if (UseSqlite(out var connectionString, out var location))
            {
                try
                {
                    EnsureSqliteSchema(connectionString, location);
                    using var connection = OpenSqlite(connectionString);
                    using var command = connection.CreateCommand();
                    command.CommandText = "delete from zabbix_dirty_scopes where layer = $layer;";
                    command.Parameters.AddWithValue("$layer", normalizedLayer);
                    removed = command.ExecuteNonQuery();
                    return new DirtyScopeClearResult
                    {
                        Layer = normalizedLayer,
                        Removed = removed,
                        Snapshot = Snapshot(1000)
                    };
                }
                catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException or InvalidOperationException)
                {
                    logger.LogWarning(ex, "Failed to clear dirty scopes from SQLite {Location}; clearing memory fallback only.", location);
                }
            }

            foreach (var key in memoryEntries.Keys.Where(key => key.StartsWith($"{normalizedLayer}\u001f", StringComparison.Ordinal)).ToArray())
            {
                if (memoryEntries.TryRemove(key, out _))
                {
                    removed++;
                }
            }

            return new DirtyScopeClearResult
            {
                Layer = normalizedLayer,
                Removed = removed,
                Snapshot = MemorySnapshot(1000)
            };
        }
    }

    public DirtyScopeMarkResult MarkResult(
        string layer,
        IEnumerable<string>? scopeKeys,
        string status,
        string result)
    {
        var normalizedLayer = ZabbixApplyPlanner.NormalizeLayer(layer);
        if (string.IsNullOrWhiteSpace(normalizedLayer))
        {
            normalizedLayer = string.Equals(layer, "suppression", StringComparison.OrdinalIgnoreCase)
                ? "suppression"
                : "service";
        }

        var normalizedStatus = NormalizeStatus(status);
        var keys = (scopeKeys ?? [])
            .Select(item => (item ?? "").Trim())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (keys.Length == 0)
        {
            return new DirtyScopeMarkResult
            {
                Layer = normalizedLayer,
                AddedOrUpdated = 0,
                Snapshot = Snapshot(1000)
            };
        }

        var now = DateTimeOffset.UtcNow;
        lock (syncRoot)
        {
            var updated = 0;
            if (UseSqlite(out var connectionString, out var location))
            {
                try
                {
                    EnsureSqliteSchema(connectionString, location);
                    using var connection = OpenSqlite(connectionString);
                    using var transaction = connection.BeginTransaction();
                    foreach (var key in keys)
                    {
                        updated += UpdateSqliteEntryResult(
                            connection,
                            transaction,
                            normalizedLayer,
                            "target",
                            key,
                            normalizedStatus,
                            result.Trim(),
                            now);
                    }

                    transaction.Commit();
                    return new DirtyScopeMarkResult
                    {
                        Layer = normalizedLayer,
                        AddedOrUpdated = updated,
                        Snapshot = Snapshot(1000)
                    };
                }
                catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException or InvalidOperationException)
                {
                    logger.LogWarning(ex, "Failed to update dirty scope results in SQLite {Location}; using memory fallback for this process.", location);
                }
            }

            foreach (var key in keys)
            {
                var entryKey = DirtyScopeKey(normalizedLayer, "target", key);
                if (!memoryEntries.TryGetValue(entryKey, out var entry))
                {
                    continue;
                }

                memoryEntries[entryKey] = entry with
                {
                    Status = normalizedStatus,
                    UpdatedAtUtc = now,
                    LastReconcileResult = result.Trim()
                };
                updated++;
            }

            return new DirtyScopeMarkResult
            {
                Layer = normalizedLayer,
                AddedOrUpdated = updated,
                Snapshot = MemorySnapshot(1000)
            };
        }
    }

    private bool UseSqlite(out string connectionString, out string location)
    {
        var durable = durableOptions.CurrentValue;
        connectionString = durable.ConnectionString;
        location = SqliteLocation(connectionString);
        return durable.Provider.Equals("sqlite", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(connectionString);
    }

    private static string NormalizeStatus(string status)
    {
        return status.Trim().ToLowerInvariant() switch
        {
            "processed" or "failed" or "pending" => status.Trim().ToLowerInvariant(),
            _ => "pending"
        };
    }

    private static IReadOnlyList<DirtyScopeEntry> NormalizeMarkEntries(DirtyScopeMarkRequest request, DateTimeOffset now)
    {
        var result = new List<DirtyScopeEntry>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var requestReason = request.Reason.Trim();
        foreach (var item in request.Entries ?? [])
        {
            var scopeKey = item.ScopeKey.Trim();
            if (string.IsNullOrWhiteSpace(scopeKey))
            {
                continue;
            }

            var scopeType = string.IsNullOrWhiteSpace(item.ScopeType) ? "target" : item.ScopeType.Trim();
            var token = DirtyScopeKey("", scopeType, scopeKey);
            if (!seen.Add(token))
            {
                continue;
            }

            result.Add(new DirtyScopeEntry
            {
                Layer = request.Layer,
                ScopeType = scopeType,
                ScopeKey = scopeKey,
                Reason = string.IsNullOrWhiteSpace(item.Reason) ? requestReason : item.Reason.Trim(),
                Status = string.IsNullOrWhiteSpace(item.Status) ? "pending" : item.Status.Trim(),
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                LastReconcileResult = item.LastReconcileResult.Trim()
            });
        }

        return result;
    }

    private DirtyScopeSnapshot MemorySnapshot(int limit, string error = "", string location = "")
    {
        var layers = memoryEntries.Values
            .OrderByDescending(item => item.UpdatedAtUtc)
            .GroupBy(item => item.Layer, StringComparer.OrdinalIgnoreCase)
            .Select(group => new DirtyScopeLayerSnapshot
            {
                Layer = group.Key,
                Count = group.Count(),
                UpdatedAtUtc = group.Max(item => item.UpdatedAtUtc),
                Entries = group
                    .OrderByDescending(item => item.UpdatedAtUtc)
                    .ThenBy(item => item.ScopeKey, StringComparer.Ordinal)
                    .Take(limit)
                    .ToArray()
            })
            .OrderBy(item => item.Layer, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return new DirtyScopeSnapshot
        {
            Backend = error.Length > 0 ? "memory-fallback" : "memory",
            Location = location,
            Error = error,
            Layers = layers
        };
    }

    private static IReadOnlyList<DirtyScopeLayerSnapshot> ReadSqliteEntries(SqliteConnection connection, int limit)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            select layer, scope_type, scope_key, reason, status, created_at_utc, updated_at_utc, last_reconcile_result
            from zabbix_dirty_scopes
            order by updated_at_utc desc, scope_key asc;
            """;
        var entries = new List<DirtyScopeEntry>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            entries.Add(new DirtyScopeEntry
            {
                Layer = reader.GetString(0),
                ScopeType = reader.GetString(1),
                ScopeKey = reader.GetString(2),
                Reason = reader.GetString(3),
                Status = reader.GetString(4),
                CreatedAtUtc = ParseDateTimeOffset(reader.GetString(5)),
                UpdatedAtUtc = ParseDateTimeOffset(reader.GetString(6)),
                LastReconcileResult = reader.GetString(7)
            });
        }

        return entries
            .GroupBy(item => item.Layer, StringComparer.OrdinalIgnoreCase)
            .Select(group => new DirtyScopeLayerSnapshot
            {
                Layer = group.Key,
                Count = group.Count(),
                UpdatedAtUtc = group.Max(item => item.UpdatedAtUtc),
                Entries = group
                    .OrderByDescending(item => item.UpdatedAtUtc)
                    .ThenBy(item => item.ScopeKey, StringComparer.Ordinal)
                    .Take(limit)
                    .ToArray()
            })
            .OrderBy(item => item.Layer, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void EnsureSqliteSchema(string connectionString, string location)
    {
        var directory = Path.GetDirectoryName(location);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var connection = OpenSqlite(connectionString);
        ExecuteNonQuery(connection, """
            create table if not exists zabbix_dirty_scopes (
                layer text not null,
                scope_type text not null,
                scope_key text not null,
                reason text not null,
                status text not null,
                created_at_utc text not null,
                updated_at_utc text not null,
                last_reconcile_result text not null,
                primary key(layer, scope_type, scope_key)
            );
            """);
        ExecuteNonQuery(connection, "create index if not exists ix_zabbix_dirty_scopes_status on zabbix_dirty_scopes(layer, status, updated_at_utc);");
    }

    private static void UpsertSqliteEntry(SqliteConnection connection, SqliteTransaction transaction, DirtyScopeEntry entry)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            insert into zabbix_dirty_scopes(
                layer, scope_type, scope_key, reason, status, created_at_utc, updated_at_utc, last_reconcile_result)
            values (
                $layer, $scope_type, $scope_key, $reason, $status, $created_at_utc, $updated_at_utc, $last_reconcile_result)
            on conflict(layer, scope_type, scope_key) do update set
                reason = excluded.reason,
                status = excluded.status,
                updated_at_utc = excluded.updated_at_utc,
                last_reconcile_result = excluded.last_reconcile_result;
            """;
        command.Parameters.AddWithValue("$layer", entry.Layer);
        command.Parameters.AddWithValue("$scope_type", entry.ScopeType);
        command.Parameters.AddWithValue("$scope_key", entry.ScopeKey);
        command.Parameters.AddWithValue("$reason", entry.Reason);
        command.Parameters.AddWithValue("$status", entry.Status);
        command.Parameters.AddWithValue("$created_at_utc", entry.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$updated_at_utc", entry.UpdatedAtUtc.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$last_reconcile_result", entry.LastReconcileResult);
        command.ExecuteNonQuery();
    }

    private static int UpdateSqliteEntryResult(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string layer,
        string scopeType,
        string scopeKey,
        string status,
        string result,
        DateTimeOffset updatedAtUtc)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            update zabbix_dirty_scopes
            set status = $status,
                updated_at_utc = $updated_at_utc,
                last_reconcile_result = $last_reconcile_result
            where layer = $layer
                and scope_type = $scope_type
                and scope_key = $scope_key;
            """;
        command.Parameters.AddWithValue("$layer", layer);
        command.Parameters.AddWithValue("$scope_type", scopeType);
        command.Parameters.AddWithValue("$scope_key", scopeKey);
        command.Parameters.AddWithValue("$status", status);
        command.Parameters.AddWithValue("$updated_at_utc", updatedAtUtc.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$last_reconcile_result", result);
        return command.ExecuteNonQuery();
    }

    private static SqliteConnection OpenSqlite(string connectionString)
    {
        var connection = new SqliteConnection(connectionString);
        connection.Open();
        return connection;
    }

    private static void ExecuteNonQuery(SqliteConnection connection, string commandText)
    {
        using var command = connection.CreateCommand();
        command.CommandText = commandText;
        command.ExecuteNonQuery();
    }

    private static DateTimeOffset ParseDateTimeOffset(string value)
    {
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : DateTimeOffset.MinValue;
    }

    private static string DirtyScopeKey(string layer, string scopeType, string scopeKey)
    {
        return $"{layer}\u001f{scopeType.Trim().ToLowerInvariant()}\u001f{scopeKey.Trim()}";
    }

    private static string SqliteLocation(string connectionString)
    {
        try
        {
            var builder = new SqliteConnectionStringBuilder(connectionString);
            return string.IsNullOrWhiteSpace(builder.DataSource)
                ? connectionString
                : builder.DataSource;
        }
        catch (ArgumentException)
        {
            return connectionString;
        }
    }
}

public sealed record DirtyScopeMarkRequest
{
    public string Layer { get; init; } = "";

    public string Reason { get; init; } = "";

    public IReadOnlyList<DirtyScopeMarkEntry> Entries { get; init; } = [];
}

public sealed record DirtyScopeMarkEntry
{
    public string ScopeType { get; init; } = "target";

    public string ScopeKey { get; init; } = "";

    public string Reason { get; init; } = "";

    public string Status { get; init; } = "pending";

    public string LastReconcileResult { get; init; } = "";
}

public sealed record DirtyScopeEntry
{
    public string Layer { get; init; } = "";

    public string ScopeType { get; init; } = "target";

    public string ScopeKey { get; init; } = "";

    public string Reason { get; init; } = "";

    public string Status { get; init; } = "pending";

    public DateTimeOffset CreatedAtUtc { get; init; }

    public DateTimeOffset UpdatedAtUtc { get; init; }

    public string LastReconcileResult { get; init; } = "";
}

public sealed record DirtyScopeSnapshot
{
    public string Backend { get; init; } = "";

    public string Location { get; init; } = "";

    public string Error { get; init; } = "";

    public IReadOnlyList<DirtyScopeLayerSnapshot> Layers { get; init; } = [];
}

public sealed record DirtyScopeLayerSnapshot
{
    public string Layer { get; init; } = "";

    public int Count { get; init; }

    public DateTimeOffset UpdatedAtUtc { get; init; }

    public IReadOnlyList<DirtyScopeEntry> Entries { get; init; } = [];
}

public sealed record DirtyScopeSummary
{
    public string Backend { get; init; } = "";

    public string Location { get; init; } = "";

    public string Error { get; init; } = "";

    public int TotalCount { get; init; }

    public IReadOnlyList<DirtyScopeLayerSummary> Layers { get; init; } = [];
}

public sealed record DirtyScopeLayerSummary
{
    public string Layer { get; init; } = "";

    public int Count { get; init; }

    public DateTimeOffset UpdatedAtUtc { get; init; }
}

public sealed record DirtyScopeMarkResult
{
    public string Layer { get; init; } = "";

    public int AddedOrUpdated { get; init; }

    public DirtyScopeSnapshot Snapshot { get; init; } = new();
}

public sealed record DirtyScopeClearResult
{
    public string Layer { get; init; } = "";

    public int Removed { get; init; }

    public DirtyScopeSnapshot Snapshot { get; init; } = new();
}

public sealed class ZabbixApplyStateStorageStatus
{
    public string Backend { get; init; } = "";

    public string Location { get; init; } = "";

    public bool Exists { get; init; }

    public long SizeBytes { get; init; }

    public int SchemaVersion { get; init; }

    public int TargetMembershipCount { get; init; }

    public int SourceMembershipCount { get; init; }

    public int PendingSourceCount { get; init; }

    public int ManagedTriggerDependencyCount { get; init; }

    public int AppliedGraphObjectCount { get; init; }

    public string Error { get; init; } = "";
}

public sealed record ZabbixApplyStateMigrationPlan
{
    public bool DryRun { get; init; }

    public string Status { get; init; } = "planned";

    public string Message { get; init; } = "";

    public string SourceBackend { get; init; } = "";

    public string SourceLocation { get; init; } = "";

    public string TargetProvider { get; init; } = "";

    public string TargetLocation { get; init; } = "";

    public int MembershipCount { get; init; }

    public int SourceMembershipCount { get; init; }

    public int PendingSourceCount { get; init; }

    public int TriggerDependencyCount { get; init; }

    public int AppliedGraphObjectCount { get; init; }

    public IReadOnlyList<ZabbixApplyStateMigrationLayerPlan> Layers { get; init; } = [];

    public static ZabbixApplyStateMigrationPlan FromState(
        bool dryRun,
        string sourceBackend,
        string sourceLocation,
        string targetProvider,
        string targetLocation,
        ZabbixApplyPersistentState state)
    {
        var memberships = state.Memberships ?? [];
        return new ZabbixApplyStateMigrationPlan
        {
            DryRun = dryRun,
            SourceBackend = sourceBackend,
            SourceLocation = sourceLocation,
            TargetProvider = targetProvider,
            TargetLocation = targetLocation,
            MembershipCount = memberships.Count,
            SourceMembershipCount = memberships.Sum(item => item.Sources.Count),
            PendingSourceCount = memberships.Sum(item => item.PendingSources.Count),
            TriggerDependencyCount = state.TriggerDependencies?.Count ?? 0,
            AppliedGraphObjectCount = state.AppliedGraphObjects?.Count ?? 0,
            Layers = memberships
                .GroupBy(item => item.Layer, StringComparer.OrdinalIgnoreCase)
                .Select(group => new ZabbixApplyStateMigrationLayerPlan
                {
                    Layer = group.Key,
                    MembershipCount = group.Count(),
                    SourceMembershipCount = group.Sum(item => item.Sources.Count),
                    PendingSourceCount = group.Sum(item => item.PendingSources.Count)
                })
                .OrderBy(item => item.Layer, StringComparer.OrdinalIgnoreCase)
                .ToArray()
        };
    }
}

public sealed record ZabbixApplyStateMigrationLayerPlan
{
    public string Layer { get; init; } = "";

    public int MembershipCount { get; init; }

    public int SourceMembershipCount { get; init; }

    public int PendingSourceCount { get; init; }
}

public sealed class ZabbixApplyStateStore
{
    private readonly IZabbixApplyStateStorage storage;
    private readonly object membershipLock = new();
    private readonly ConcurrentDictionary<string, ZabbixLayerApplyStatus> layers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ZabbixTargetMembership> memberships = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ZabbixAppliedGraphObject> appliedGraphObjects = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ZabbixTriggerDependencyLayerStatus> triggerDependencyLayers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ZabbixManagedTriggerDependency> triggerDependencies = new(StringComparer.Ordinal);

    public ZabbixApplyStateStore(
        IZabbixApplyStateStorage storage)
    {
        this.storage = storage;
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

    public object RuntimeStorageSnapshot(DurableStoreOptions durableOptions)
    {
        lock (membershipLock)
        {
            var targetSnapshots = memberships.Values.Select(item => item.ToSnapshot()).ToArray();
            var storageStatus = storage.Status();
            return new
            {
                configuredProvider = durableOptions.Provider,
                activeMembershipBackend = storageStatus.Backend,
                migrationRequired = !durableOptions.Provider.Equals(storageStatus.Backend, StringComparison.OrdinalIgnoreCase),
                migrationStatus = durableOptions.Provider.Equals(storageStatus.Backend, StringComparison.OrdinalIgnoreCase)
                    ? "not_required"
                    : durableOptions.Provider.Equals("sqlite", StringComparison.OrdinalIgnoreCase)
                        ? "available"
                        : "pending_backend_implementation",
                storageBackend = storageStatus.Backend,
                storageLocation = storageStatus.Location,
                stateFilePath = storageStatus.Location,
                stateFileExists = storageStatus.Exists,
                stateFileSizeBytes = storageStatus.SizeBytes,
                schemaVersion = storageStatus.SchemaVersion,
                storageError = storageStatus.Error,
                targetMembershipCount = targetSnapshots.Length,
                sourceMembershipCount = targetSnapshots.Sum(item => item.SourceCount),
                pendingSourceCount = targetSnapshots.Sum(item => item.PendingSourceCount),
                missingHostBindingCount = targetSnapshots.Sum(item => item.MissingHostBindingCount),
                appliedGraphObjectCount = appliedGraphObjects.Count,
                managedTriggerDependencyCount = triggerDependencies.Count,
                layers = targetSnapshots
                    .GroupBy(item => item.Layer, StringComparer.OrdinalIgnoreCase)
                    .Select(group => new
                    {
                        layer = group.Key,
                        targetMembershipCount = group.Count(),
                        sourceMembershipCount = group.Sum(item => item.SourceCount),
                        pendingSourceCount = group.Sum(item => item.PendingSourceCount),
                        missingHostBindingCount = group.Sum(item => item.MissingHostBindingCount),
                        appliedGraphObjectCount = appliedGraphObjects.Values.Count(item => item.Layer.Equals(group.Key, StringComparison.OrdinalIgnoreCase)),
                        managedTriggerDependencyCount = triggerDependencies.Values.Count(item => item.Layer.Equals(group.Key, StringComparison.OrdinalIgnoreCase))
                    })
                    .OrderBy(item => item.layer, StringComparer.OrdinalIgnoreCase)
                    .ToArray()
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
        var state = storage.Load();
        if (state.Memberships is not null)
        {
            foreach (var membership in state.Memberships)
            {
                if (string.IsNullOrWhiteSpace(membership.Layer)
                    || string.IsNullOrWhiteSpace(membership.TargetManagedKey))
                {
                    continue;
                }

                memberships[MembershipKey(membership.Layer, membership.TargetManagedKey)] = membership;
            }
        }

        if (state.TriggerDependencies is not null)
        {
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
        }

        if (state.AppliedGraphObjects is not null)
        {
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
    }

    private void SaveMemberships()
    {
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
        storage.Save(state);
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

public interface IRuntimeCoordinationStore
{
    RuntimeCoordinationStatus Status();

    ValueTask<RuntimeOperationLease> TryAcquireLockAsync(string operationKey, CancellationToken cancellationToken);

    RuntimeDebounceRequest RequestDebouncedOperation(string operationKey, string reason, TimeSpan debounceWindow);

    RuntimeDebounceBatch ConsumeDebouncedOperation(string operationKey);

    RuntimeOperationProgress StartOperation(string operationKey, string backend);

    void CompleteOperation(string operationId, string status, string message = "");
}

public sealed class LocalRuntimeCoordinationStore : IRuntimeCoordinationStore
{
    private readonly IOptionsMonitor<RuntimeRedisOptions> options;
    private readonly ConcurrentDictionary<string, RuntimeLockEntry> locks = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, RuntimeDebounceEntry> debouncedOperations = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, RuntimeOperationProgress> activeOperations = new(StringComparer.Ordinal);
    private readonly object recentOperationsLock = new();
    private readonly List<RuntimeOperationProgress> recentOperations = [];
    private readonly string instanceId = $"{Environment.MachineName}-{Guid.NewGuid():N}";

    public LocalRuntimeCoordinationStore(IOptionsMonitor<RuntimeRedisOptions> options)
    {
        this.options = options;
    }

    public RuntimeCoordinationStatus Status()
    {
        var current = options.CurrentValue;
        CleanupExpiredLocks(DateTimeOffset.UtcNow);
        var redisRequested = current.Enabled;
        var failMode = string.Equals(current.FailureMode, "fail", StringComparison.OrdinalIgnoreCase);
        return new RuntimeCoordinationStatus
        {
            Backend = redisRequested ? "local-memory-fallback" : "local-memory",
            RedisRequested = redisRequested,
            RedisAvailable = false,
            FallbackActive = redisRequested && !failMode,
            BlockingOnRedisUnavailable = redisRequested && failMode,
            KeyPrefix = current.KeyPrefix,
            InstanceId = string.IsNullOrWhiteSpace(current.InstanceId) ? instanceId : current.InstanceId,
            ActiveLockCount = locks.Count,
            ActiveOperationCount = activeOperations.Count,
            ActiveOperations = activeOperations.Values
                .OrderBy(item => item.StartedAtUtc)
                .ToArray(),
            RecentOperations = RecentOperations(),
            Message = redisRequested
                ? "Redis client backend is not active in this build; runtime coordination uses local memory when FailureMode=fallback."
                : "Redis is disabled; runtime coordination uses local memory and is not shared across service instances."
        };
    }

    public ValueTask<RuntimeOperationLease> TryAcquireLockAsync(string operationKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = NormalizeOperationKey(operationKey);
        var current = options.CurrentValue;
        var now = DateTimeOffset.UtcNow;
        CleanupExpiredLocks(now);

        if (current.Enabled && string.Equals(current.FailureMode, "fail", StringComparison.OrdinalIgnoreCase))
        {
            return ValueTask.FromResult(RuntimeOperationLease.NotAcquired(
                status: "runtime_coordination_unavailable",
                backend: "redis",
                message: "Redis is enabled with FailureMode=fail, but Redis backend is not active in this build.",
                statusCode: StatusCodes.Status503ServiceUnavailable));
        }

        var owner = string.IsNullOrWhiteSpace(current.InstanceId) ? instanceId : current.InstanceId;
        var expiresAt = now.AddSeconds(Math.Max(1, current.LockTtlSeconds));
        var entry = new RuntimeLockEntry(owner, expiresAt);
        if (!locks.TryAdd(key, entry))
        {
            if (locks.TryGetValue(key, out var existing) && existing.ExpiresAtUtc <= now)
            {
                locks.TryRemove(new KeyValuePair<string, RuntimeLockEntry>(key, existing));
                if (locks.TryAdd(key, entry))
                {
                    return ValueTask.FromResult(RuntimeOperationLease.CreateAcquired(
                        backend: current.Enabled ? "local-memory-fallback" : "local-memory",
                        release: () => ReleaseLock(key, owner)));
                }
            }

            return ValueTask.FromResult(RuntimeOperationLease.NotAcquired(
                status: "busy",
                backend: current.Enabled ? "local-memory-fallback" : "local-memory",
                message: $"Operation lock is already held for {key}.",
                statusCode: StatusCodes.Status409Conflict));
        }

        return ValueTask.FromResult(RuntimeOperationLease.CreateAcquired(
            backend: current.Enabled ? "local-memory-fallback" : "local-memory",
            release: () => ReleaseLock(key, owner)));
    }

    public RuntimeDebounceRequest RequestDebouncedOperation(string operationKey, string reason, TimeSpan debounceWindow)
    {
        var key = NormalizeOperationKey(operationKey);
        var normalizedReason = string.IsNullOrWhiteSpace(reason) ? "operation requested" : reason.Trim();
        var now = DateTimeOffset.UtcNow;
        var dueAt = now.Add(debounceWindow < TimeSpan.Zero ? TimeSpan.Zero : debounceWindow);
        var shouldSchedule = false;
        debouncedOperations.AddOrUpdate(
            key,
            _ =>
            {
                shouldSchedule = true;
                return new RuntimeDebounceEntry(dueAt, [normalizedReason]);
            },
            (_, existing) =>
            {
                lock (existing)
                {
                    existing.Reasons.Add(normalizedReason);
                    if (existing.DueAtUtc <= now)
                    {
                        existing.DueAtUtc = dueAt;
                        shouldSchedule = true;
                    }

                    return existing;
                }
            });

        return new RuntimeDebounceRequest
        {
            OperationKey = key,
            Backend = Status().Backend,
            ShouldSchedule = shouldSchedule,
            Status = shouldSchedule ? "scheduled" : "debounced",
            Message = shouldSchedule ? "Debounced operation scheduled." : "Debounced operation already has a pending schedule."
        };
    }

    public RuntimeDebounceBatch ConsumeDebouncedOperation(string operationKey)
    {
        var key = NormalizeOperationKey(operationKey);
        if (!debouncedOperations.TryRemove(key, out var entry))
        {
            return new RuntimeDebounceBatch { OperationKey = key, Backend = Status().Backend };
        }

        lock (entry)
        {
            return new RuntimeDebounceBatch
            {
                OperationKey = key,
                Backend = Status().Backend,
                Reasons = entry.Reasons
                    .Distinct(StringComparer.Ordinal)
                    .ToArray()
            };
        }
    }

    public RuntimeOperationProgress StartOperation(string operationKey, string backend)
    {
        var progress = new RuntimeOperationProgress
        {
            OperationId = Guid.NewGuid().ToString("N"),
            OperationKey = NormalizeOperationKey(operationKey),
            Backend = string.IsNullOrWhiteSpace(backend) ? Status().Backend : backend,
            Status = "running",
            StartedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
        activeOperations[progress.OperationId] = progress;
        return progress;
    }

    public void CompleteOperation(string operationId, string status, string message = "")
    {
        if (!activeOperations.TryRemove(operationId, out var progress))
        {
            return;
        }

        progress.Status = string.IsNullOrWhiteSpace(status) ? "completed" : status;
        progress.Message = message ?? "";
        progress.CompletedAtUtc = DateTimeOffset.UtcNow;
        progress.UpdatedAtUtc = progress.CompletedAtUtc.Value;
        lock (recentOperationsLock)
        {
            recentOperations.Insert(0, progress);
            if (recentOperations.Count > 20)
            {
                recentOperations.RemoveRange(20, recentOperations.Count - 20);
            }
        }
    }

    private static string NormalizeOperationKey(string operationKey)
    {
        return string.IsNullOrWhiteSpace(operationKey)
            ? "operation:unknown"
            : operationKey.Trim();
    }

    private void ReleaseLock(string key, string owner)
    {
        if (locks.TryGetValue(key, out var existing) && string.Equals(existing.Owner, owner, StringComparison.Ordinal))
        {
            locks.TryRemove(new KeyValuePair<string, RuntimeLockEntry>(key, existing));
        }
    }

    private void CleanupExpiredLocks(DateTimeOffset now)
    {
        foreach (var pair in locks)
        {
            if (pair.Value.ExpiresAtUtc <= now)
            {
                locks.TryRemove(pair);
            }
        }
    }

    private RuntimeOperationProgress[] RecentOperations()
    {
        lock (recentOperationsLock)
        {
            return recentOperations.ToArray();
        }
    }
}

public sealed record RuntimeLockEntry(string Owner, DateTimeOffset ExpiresAtUtc);

public sealed class RuntimeDebounceEntry
{
    public RuntimeDebounceEntry(DateTimeOffset dueAtUtc, List<string> reasons)
    {
        DueAtUtc = dueAtUtc;
        Reasons.AddRange(reasons);
    }

    public DateTimeOffset DueAtUtc { get; set; }

    public List<string> Reasons { get; } = [];
}

public sealed class RedisRuntimeCoordinationStore : IRuntimeCoordinationStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IOptionsMonitor<RuntimeRedisOptions> options;
    private readonly LocalRuntimeCoordinationStore fallback;
    private readonly ILogger<RedisRuntimeCoordinationStore> logger;
    private readonly string instanceId = $"{Environment.MachineName}-{Guid.NewGuid():N}";
    private volatile string lastError = "";

    public RedisRuntimeCoordinationStore(
        IOptionsMonitor<RuntimeRedisOptions> options,
        LocalRuntimeCoordinationStore fallback,
        ILogger<RedisRuntimeCoordinationStore> logger)
    {
        this.options = options;
        this.fallback = fallback;
        this.logger = logger;
    }

    public RuntimeCoordinationStatus Status()
    {
        var current = options.CurrentValue;
        if (!current.Enabled)
        {
            return fallback.Status();
        }

        try
        {
            using var client = RedisRespClient.Connect(current);
            client.Ping();
            var activeOperations = ReadOperationList(client, ActiveOperationsKey(current));
            var recentOperations = ReadRecentOperations(client, RecentOperationsKey(current));
            var activeLockCount = CleanupAndCountLocks(client, current);
            lastError = "";
            return new RuntimeCoordinationStatus
            {
                Backend = "redis",
                RedisRequested = true,
                RedisAvailable = true,
                FallbackActive = false,
                BlockingOnRedisUnavailable = false,
                KeyPrefix = NormalizeRedisPrefix(current.KeyPrefix),
                InstanceId = EffectiveInstanceId(current),
                ActiveLockCount = activeLockCount,
                ActiveOperationCount = activeOperations.Length,
                ActiveOperations = activeOperations,
                RecentOperations = recentOperations,
                Message = "Redis runtime coordination is active."
            };
        }
        catch (Exception ex) when (ex is SocketException or IOException or InvalidOperationException or TimeoutException)
        {
            return RedisUnavailableStatus(current, ex);
        }
    }

    public async ValueTask<RuntimeOperationLease> TryAcquireLockAsync(string operationKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var current = options.CurrentValue;
        if (!current.Enabled)
        {
            return await fallback.TryAcquireLockAsync(operationKey, cancellationToken);
        }

        try
        {
            var key = RuntimeLockKey(current, operationKey);
            var owner = EffectiveInstanceId(current);
            var ttlMs = Math.Max(1, current.LockTtlSeconds) * 1000;
            var client = RedisRespClient.Connect(current);
            var result = client.ExecuteBulkString("SET", key, owner, "NX", "PX", ttlMs.ToString(CultureInfo.InvariantCulture));
            if (!string.Equals(result, "OK", StringComparison.OrdinalIgnoreCase))
            {
                client.Dispose();
                return RuntimeOperationLease.NotAcquired(
                    status: "busy",
                    backend: "redis",
                    message: $"Redis operation lock is already held for {key}.",
                    statusCode: StatusCodes.Status409Conflict);
            }

            client.ExecuteInteger("SADD", ActiveLocksKey(current), key);
            client.ExecuteInteger("EXPIRE", ActiveLocksKey(current), Math.Max(1, current.LockTtlSeconds * 2).ToString(CultureInfo.InvariantCulture));
            lastError = "";
            return RuntimeOperationLease.CreateAcquired(
                backend: "redis",
                release: () => ReleaseRedisLock(client, current, key, owner));
        }
        catch (Exception ex) when (ex is SocketException or IOException or InvalidOperationException or TimeoutException)
        {
            return RedisUnavailableLease(current, operationKey, ex, cancellationToken);
        }
    }

    public RuntimeDebounceRequest RequestDebouncedOperation(string operationKey, string reason, TimeSpan debounceWindow)
    {
        var current = options.CurrentValue;
        if (!current.Enabled)
        {
            return fallback.RequestDebouncedOperation(operationKey, reason, debounceWindow);
        }

        try
        {
            using var client = RedisRespClient.Connect(current);
            var normalizedKey = NormalizeOperationKey(operationKey);
            var debounceKey = DebounceKey(current, normalizedKey);
            var reasonsKey = DebounceReasonsKey(current, normalizedKey);
            var ttlMs = Math.Max(1, (int)Math.Ceiling(Math.Max(0, debounceWindow.TotalMilliseconds)));
            var ttlSeconds = Math.Max(1, (int)Math.Ceiling(Math.Max(debounceWindow.TotalSeconds, 0) + 60));
            var normalizedReason = string.IsNullOrWhiteSpace(reason) ? "operation requested" : reason.Trim();
            client.ExecuteInteger("LPUSH", reasonsKey, normalizedReason);
            client.ExecuteInteger("EXPIRE", reasonsKey, ttlSeconds.ToString(CultureInfo.InvariantCulture));
            var result = client.ExecuteBulkString("SET", debounceKey, EffectiveInstanceId(current), "NX", "PX", ttlMs.ToString(CultureInfo.InvariantCulture));
            lastError = "";
            var scheduled = string.Equals(result, "OK", StringComparison.OrdinalIgnoreCase);
            return new RuntimeDebounceRequest
            {
                OperationKey = normalizedKey,
                Backend = "redis",
                ShouldSchedule = scheduled,
                Status = scheduled ? "scheduled" : "debounced",
                Message = scheduled ? "Redis debounce window scheduled." : "Redis debounce window already exists."
            };
        }
        catch (Exception ex) when (ex is SocketException or IOException or InvalidOperationException or TimeoutException)
        {
            lastError = ex.Message;
            if (string.Equals(current.FailureMode, "fallback", StringComparison.OrdinalIgnoreCase))
            {
                logger.LogWarning(ex, "Redis debounce request failed; falling back to local memory for {OperationKey}", operationKey);
                return fallback.RequestDebouncedOperation(operationKey, reason, debounceWindow);
            }

            return new RuntimeDebounceRequest
            {
                OperationKey = NormalizeOperationKey(operationKey),
                Backend = "redis",
                ShouldSchedule = false,
                Status = "runtime_coordination_unavailable",
                Message = $"Redis is unavailable and FailureMode=fail. Last Redis error: {ex.Message}"
            };
        }
    }

    public RuntimeDebounceBatch ConsumeDebouncedOperation(string operationKey)
    {
        var current = options.CurrentValue;
        if (!current.Enabled)
        {
            return fallback.ConsumeDebouncedOperation(operationKey);
        }

        try
        {
            using var client = RedisRespClient.Connect(current);
            var normalizedKey = NormalizeOperationKey(operationKey);
            var debounceKey = DebounceKey(current, normalizedKey);
            var reasonsKey = DebounceReasonsKey(current, normalizedKey);
            var reasons = client.ExecuteArray("LRANGE", reasonsKey, "0", "99")
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            client.ExecuteInteger("DEL", reasonsKey);
            client.ExecuteInteger("DEL", debounceKey);
            lastError = "";
            return new RuntimeDebounceBatch
            {
                OperationKey = normalizedKey,
                Backend = "redis",
                Reasons = reasons
            };
        }
        catch (Exception ex) when (ex is SocketException or IOException or InvalidOperationException or TimeoutException)
        {
            lastError = ex.Message;
            if (string.Equals(current.FailureMode, "fallback", StringComparison.OrdinalIgnoreCase))
            {
                logger.LogWarning(ex, "Redis debounce consume failed; falling back to local memory for {OperationKey}", operationKey);
                return fallback.ConsumeDebouncedOperation(operationKey);
            }

            return new RuntimeDebounceBatch
            {
                OperationKey = NormalizeOperationKey(operationKey),
                Backend = "redis",
                Status = "runtime_coordination_unavailable",
                Message = $"Redis is unavailable and FailureMode=fail. Last Redis error: {ex.Message}"
            };
        }
    }

    public RuntimeOperationProgress StartOperation(string operationKey, string backend)
    {
        var current = options.CurrentValue;
        if (!current.Enabled || !string.Equals(backend, "redis", StringComparison.OrdinalIgnoreCase))
        {
            return fallback.StartOperation(operationKey, backend);
        }

        var progress = new RuntimeOperationProgress
        {
            OperationId = Guid.NewGuid().ToString("N"),
            OperationKey = NormalizeOperationKey(operationKey),
            Backend = "redis",
            Status = "running",
            StartedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };

        try
        {
            using var client = RedisRespClient.Connect(current);
            WriteActiveOperation(client, current, progress);
            lastError = "";
            return progress;
        }
        catch (Exception ex) when (ex is SocketException or IOException or InvalidOperationException or TimeoutException)
        {
            logger.LogWarning(ex, "Redis operation progress start failed; falling back to local memory for {OperationKey}", operationKey);
            lastError = ex.Message;
            return fallback.StartOperation(operationKey, "local-memory-fallback");
        }
    }

    public void CompleteOperation(string operationId, string status, string message = "")
    {
        var current = options.CurrentValue;
        if (!current.Enabled)
        {
            fallback.CompleteOperation(operationId, status, message);
            return;
        }

        try
        {
            using var client = RedisRespClient.Connect(current);
            var key = OperationKey(current, operationId);
            var json = client.ExecuteBulkString("GET", key);
            if (string.IsNullOrWhiteSpace(json))
            {
                fallback.CompleteOperation(operationId, status, message);
                return;
            }

            var progress = JsonSerializer.Deserialize<RuntimeOperationProgress>(json, JsonOptions);
            if (progress is null)
            {
                return;
            }

            progress.Status = string.IsNullOrWhiteSpace(status) ? "completed" : status;
            progress.Message = message ?? "";
            progress.CompletedAtUtc = DateTimeOffset.UtcNow;
            progress.UpdatedAtUtc = progress.CompletedAtUtc.Value;
            var completedJson = JsonSerializer.Serialize(progress, JsonOptions);
            var ttlSeconds = Math.Max(1, current.OperationTtlSeconds);
            client.ExecuteBulkString("SET", key, completedJson, "EX", ttlSeconds.ToString(CultureInfo.InvariantCulture));
            client.ExecuteInteger("SREM", ActiveOperationsKey(current), operationId);
            client.ExecuteInteger("LPUSH", RecentOperationsKey(current), completedJson);
            client.ExecuteInteger("LTRIM", RecentOperationsKey(current), "0", "19");
            client.ExecuteInteger("EXPIRE", RecentOperationsKey(current), ttlSeconds.ToString(CultureInfo.InvariantCulture));
            lastError = "";
        }
        catch (Exception ex) when (ex is SocketException or IOException or InvalidOperationException or TimeoutException)
        {
            logger.LogWarning(ex, "Redis operation progress completion failed; falling back to local memory for {OperationId}", operationId);
            lastError = ex.Message;
            fallback.CompleteOperation(operationId, status, message);
        }
    }

    private RuntimeCoordinationStatus RedisUnavailableStatus(RuntimeRedisOptions current, Exception ex)
    {
        lastError = ex.Message;
        if (string.Equals(current.FailureMode, "fallback", StringComparison.OrdinalIgnoreCase))
        {
            var local = fallback.Status();
            return new RuntimeCoordinationStatus
            {
                Backend = "local-memory-fallback",
                RedisRequested = true,
                RedisAvailable = false,
                FallbackActive = true,
                BlockingOnRedisUnavailable = false,
                KeyPrefix = NormalizeRedisPrefix(current.KeyPrefix),
                InstanceId = EffectiveInstanceId(current),
                ActiveLockCount = local.ActiveLockCount,
                ActiveOperationCount = local.ActiveOperationCount,
                ActiveOperations = local.ActiveOperations,
                RecentOperations = local.RecentOperations,
                Message = $"Redis is unavailable; using local-memory fallback. Last Redis error: {ex.Message}"
            };
        }

        return new RuntimeCoordinationStatus
        {
            Backend = "redis",
            RedisRequested = true,
            RedisAvailable = false,
            FallbackActive = false,
            BlockingOnRedisUnavailable = true,
            KeyPrefix = NormalizeRedisPrefix(current.KeyPrefix),
            InstanceId = EffectiveInstanceId(current),
            ActiveLockCount = 0,
            ActiveOperationCount = 0,
            Message = $"Redis is unavailable and FailureMode=fail. Last Redis error: {ex.Message}"
        };
    }

    private RuntimeOperationLease RedisUnavailableLease(
        RuntimeRedisOptions current,
        string operationKey,
        Exception ex,
        CancellationToken cancellationToken)
    {
        lastError = ex.Message;
        if (string.Equals(current.FailureMode, "fallback", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning(ex, "Redis lock acquisition failed; falling back to local memory for {OperationKey}", operationKey);
            return fallback.TryAcquireLockAsync(operationKey, cancellationToken).GetAwaiter().GetResult();
        }

        return RuntimeOperationLease.NotAcquired(
            status: "runtime_coordination_unavailable",
            backend: "redis",
            message: $"Redis is unavailable and FailureMode=fail. Last Redis error: {ex.Message}",
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    private void ReleaseRedisLock(RedisRespClient client, RuntimeRedisOptions current, string key, string owner)
    {
        try
        {
            const string releaseScript = "if redis.call('GET', KEYS[1]) == ARGV[1] then return redis.call('DEL', KEYS[1]) else return 0 end";
            client.ExecuteInteger("EVAL", releaseScript, "1", key, owner);
            client.ExecuteInteger("SREM", ActiveLocksKey(current), key);
            client.Dispose();
        }
        catch (Exception ex) when (ex is SocketException or IOException or InvalidOperationException or TimeoutException)
        {
            logger.LogWarning(ex, "Redis lock release failed for {LockKey}", key);
            lastError = ex.Message;
            client.Dispose();
        }
    }

    private void WriteActiveOperation(RedisRespClient client, RuntimeRedisOptions current, RuntimeOperationProgress progress)
    {
        var ttlSeconds = Math.Max(1, current.OperationTtlSeconds);
        var json = JsonSerializer.Serialize(progress, JsonOptions);
        client.ExecuteBulkString("SET", OperationKey(current, progress.OperationId), json, "EX", ttlSeconds.ToString(CultureInfo.InvariantCulture));
        client.ExecuteInteger("SADD", ActiveOperationsKey(current), progress.OperationId);
        client.ExecuteInteger("EXPIRE", ActiveOperationsKey(current), ttlSeconds.ToString(CultureInfo.InvariantCulture));
    }

    private RuntimeOperationProgress[] ReadOperationList(RedisRespClient client, string setKey)
    {
        return client.ExecuteArray("SMEMBERS", setKey)
            .Select(id => client.ExecuteBulkString("GET", OperationKey(options.CurrentValue, id)))
            .Where(json => !string.IsNullOrWhiteSpace(json))
            .Select(json => JsonSerializer.Deserialize<RuntimeOperationProgress>(json!, JsonOptions))
            .Where(item => item is not null)
            .Select(item => item!)
            .OrderBy(item => item.StartedAtUtc)
            .ToArray();
    }

    private RuntimeOperationProgress[] ReadRecentOperations(RedisRespClient client, string listKey)
    {
        return client.ExecuteArray("LRANGE", listKey, "0", "19")
            .Where(json => !string.IsNullOrWhiteSpace(json))
            .Select(json => JsonSerializer.Deserialize<RuntimeOperationProgress>(json, JsonOptions))
            .Where(item => item is not null)
            .Select(item => item!)
            .ToArray();
    }

    private int CleanupAndCountLocks(RedisRespClient client, RuntimeRedisOptions current)
    {
        var setKey = ActiveLocksKey(current);
        var locks = client.ExecuteArray("SMEMBERS", setKey);
        var count = 0;
        foreach (var key in locks)
        {
            if (client.ExecuteInteger("EXISTS", key) > 0)
            {
                count++;
            }
            else
            {
                client.ExecuteInteger("SREM", setKey, key);
            }
        }

        return count;
    }

    private string EffectiveInstanceId(RuntimeRedisOptions current)
    {
        return string.IsNullOrWhiteSpace(current.InstanceId) ? instanceId : current.InstanceId;
    }

    private static string NormalizeOperationKey(string operationKey)
    {
        return string.IsNullOrWhiteSpace(operationKey) ? "operation:unknown" : operationKey.Trim();
    }

    private static string OperationKey(RuntimeRedisOptions options, string operationId)
    {
        return $"{NormalizeRedisPrefix(options.KeyPrefix)}:runtime:operation:{operationId}";
    }

    private static string ActiveOperationsKey(RuntimeRedisOptions options)
    {
        return $"{NormalizeRedisPrefix(options.KeyPrefix)}:runtime:operations:active";
    }

    private static string RecentOperationsKey(RuntimeRedisOptions options)
    {
        return $"{NormalizeRedisPrefix(options.KeyPrefix)}:runtime:operations:recent";
    }

    private static string RuntimeLockKey(RuntimeRedisOptions options, string operationKey)
    {
        var safeKey = Regex.Replace(NormalizeOperationKey(operationKey), @"[^A-Za-z0-9_.:-]+", "_");
        return $"{NormalizeRedisPrefix(options.KeyPrefix)}:runtime:lock:{safeKey}";
    }

    private static string ActiveLocksKey(RuntimeRedisOptions options)
    {
        return $"{NormalizeRedisPrefix(options.KeyPrefix)}:runtime:locks:active";
    }

    private static string DebounceKey(RuntimeRedisOptions options, string operationKey)
    {
        var safeKey = Regex.Replace(NormalizeOperationKey(operationKey), @"[^A-Za-z0-9_.:-]+", "_");
        return $"{NormalizeRedisPrefix(options.KeyPrefix)}:runtime:debounce:{safeKey}";
    }

    private static string DebounceReasonsKey(RuntimeRedisOptions options, string operationKey)
    {
        var safeKey = Regex.Replace(NormalizeOperationKey(operationKey), @"[^A-Za-z0-9_.:-]+", "_");
        return $"{NormalizeRedisPrefix(options.KeyPrefix)}:runtime:debounce:{safeKey}:reasons";
    }

    private static string NormalizeRedisPrefix(string prefix)
    {
        return string.IsNullOrWhiteSpace(prefix) ? "cmdb2m:test" : prefix.Trim().TrimEnd(':');
    }

    private string LastError()
    {
        return lastError;
    }
}

public interface IRuntimeLookupCache
{
    RuntimeLookupCacheStatus Status();

    Task<string?> GetStringAsync(string scope, string key, CancellationToken cancellationToken);

    Task SetStringAsync(string scope, string key, string value, TimeSpan? ttl, CancellationToken cancellationToken);
}

public sealed class LocalRuntimeLookupCache(IOptionsMonitor<RuntimeRedisOptions> options) : IRuntimeLookupCache
{
    private readonly ConcurrentDictionary<string, RuntimeLookupCacheEntry> entries = new(StringComparer.Ordinal);

    public RuntimeLookupCacheStatus Status()
    {
        var current = options.CurrentValue;
        return new RuntimeLookupCacheStatus
        {
            Backend = current.Enabled ? "local-memory-fallback" : "no-cache",
            RedisRequested = current.Enabled,
            RedisAvailable = false,
            FallbackActive = current.Enabled,
            KeyPrefix = NormalizeRedisPrefix(current.KeyPrefix),
            DefaultTtlSeconds = current.CacheDefaultTtlSeconds,
            Message = current.Enabled
                ? "Redis lookup cache is unavailable; using process memory fallback."
                : "Redis is disabled; lookup cache is not active."
        };
    }

    public Task<string?> GetStringAsync(string scope, string key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var current = options.CurrentValue;
        if (!current.Enabled)
        {
            return Task.FromResult<string?>(null);
        }

        var cacheKey = LocalKey(scope, key);
        if (!entries.TryGetValue(cacheKey, out var entry))
        {
            return Task.FromResult<string?>(null);
        }

        if (entry.ExpiresAtUtc <= DateTimeOffset.UtcNow)
        {
            entries.TryRemove(cacheKey, out _);
            return Task.FromResult<string?>(null);
        }

        return Task.FromResult<string?>(entry.Value);
    }

    public Task SetStringAsync(string scope, string key, string value, TimeSpan? ttl, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var current = options.CurrentValue;
        if (!current.Enabled)
        {
            return Task.CompletedTask;
        }

        var effectiveTtl = ttl ?? TimeSpan.FromSeconds(current.CacheDefaultTtlSeconds);
        entries[LocalKey(scope, key)] = new RuntimeLookupCacheEntry(value, DateTimeOffset.UtcNow.Add(effectiveTtl));
        return Task.CompletedTask;
    }

    private static string LocalKey(string scope, string key)
    {
        return $"{NormalizeCachePart(scope)}:{key}";
    }

    private static string NormalizeCachePart(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "default" : value.Trim();
    }

    private static string NormalizeRedisPrefix(string prefix)
    {
        return string.IsNullOrWhiteSpace(prefix) ? "cmdb2m:test" : prefix.Trim().TrimEnd(':');
    }
}

public sealed class RedisRuntimeLookupCache(
    IOptionsMonitor<RuntimeRedisOptions> options,
    LocalRuntimeLookupCache localFallback,
    ILogger<RedisRuntimeLookupCache> logger) : IRuntimeLookupCache
{
    public RuntimeLookupCacheStatus Status()
    {
        var current = options.CurrentValue;
        if (!current.Enabled)
        {
            return new RuntimeLookupCacheStatus
            {
                Backend = "no-cache",
                RedisRequested = false,
                RedisAvailable = false,
                FallbackActive = false,
                KeyPrefix = NormalizeRedisPrefix(current.KeyPrefix),
                DefaultTtlSeconds = current.CacheDefaultTtlSeconds,
                Message = "Redis is disabled; lookup cache is not active."
            };
        }

        try
        {
            using var client = RedisRespClient.Connect(current);
            client.Ping();
            return new RuntimeLookupCacheStatus
            {
                Backend = "redis",
                RedisRequested = true,
                RedisAvailable = true,
                FallbackActive = false,
                KeyPrefix = NormalizeRedisPrefix(current.KeyPrefix),
                DefaultTtlSeconds = current.CacheDefaultTtlSeconds,
                Message = "Redis lookup cache is available."
            };
        }
        catch (Exception ex) when (ex is SocketException or IOException or InvalidOperationException or TimeoutException)
        {
            return new RuntimeLookupCacheStatus
            {
                Backend = "no-cache-fallback",
                RedisRequested = true,
                RedisAvailable = false,
                FallbackActive = true,
                KeyPrefix = NormalizeRedisPrefix(current.KeyPrefix),
                DefaultTtlSeconds = current.CacheDefaultTtlSeconds,
                Message = $"Redis lookup cache is unavailable; operations continue without shared cache. Last Redis error: {ex.Message}"
            };
        }
    }

    public async Task<string?> GetStringAsync(string scope, string key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var current = options.CurrentValue;
        if (!current.Enabled)
        {
            return null;
        }

        try
        {
            using var client = RedisRespClient.Connect(current);
            return client.ExecuteBulkString("GET", RedisCacheKey(current, scope, key));
        }
        catch (Exception ex) when (ex is SocketException or IOException or InvalidOperationException or TimeoutException)
        {
            logger.LogWarning(ex, "Redis lookup cache get failed for scope {Scope}; using local fallback.", scope);
            return await localFallback.GetStringAsync(scope, key, cancellationToken);
        }
    }

    public async Task SetStringAsync(string scope, string key, string value, TimeSpan? ttl, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var current = options.CurrentValue;
        if (!current.Enabled)
        {
            return;
        }

        var effectiveTtlSeconds = Math.Max(1, (int)Math.Round((ttl ?? TimeSpan.FromSeconds(current.CacheDefaultTtlSeconds)).TotalSeconds));
        try
        {
            using var client = RedisRespClient.Connect(current);
            client.ExecuteBulkString(
                "SET",
                RedisCacheKey(current, scope, key),
                value,
                "EX",
                effectiveTtlSeconds.ToString(CultureInfo.InvariantCulture));
        }
        catch (Exception ex) when (ex is SocketException or IOException or InvalidOperationException or TimeoutException)
        {
            logger.LogWarning(ex, "Redis lookup cache set failed for scope {Scope}; using local fallback.", scope);
            await localFallback.SetStringAsync(scope, key, value, TimeSpan.FromSeconds(effectiveTtlSeconds), cancellationToken);
        }
    }

    private static string RedisCacheKey(RuntimeRedisOptions options, string scope, string key)
    {
        var safeScope = Regex.Replace(string.IsNullOrWhiteSpace(scope) ? "default" : scope.Trim(), "[^A-Za-z0-9_.:-]+", "_");
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key ?? ""))).ToLowerInvariant();
        return $"{NormalizeRedisPrefix(options.KeyPrefix)}:cache:{safeScope}:{hash}";
    }

    private static string NormalizeRedisPrefix(string prefix)
    {
        return string.IsNullOrWhiteSpace(prefix) ? "cmdb2m:test" : prefix.Trim().TrimEnd(':');
    }
}

public sealed record RuntimeLookupCacheEntry(string Value, DateTimeOffset ExpiresAtUtc);

public sealed class RuntimeLookupCacheStatus
{
    public string Backend { get; init; } = "no-cache";

    public bool RedisRequested { get; init; }

    public bool RedisAvailable { get; init; }

    public bool FallbackActive { get; init; }

    public string KeyPrefix { get; init; } = "";

    public int DefaultTtlSeconds { get; init; }

    public string Message { get; init; } = "";
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
        var client = new TcpClient();
        client.ReceiveTimeout = 3000;
        client.SendTimeout = 3000;
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

    public long ExecuteInteger(params string[] args)
    {
        WriteCommand(args);
        return ReadValue() switch
        {
            long number => number,
            string text when long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
            null => 0,
            var other => throw new InvalidOperationException($"Redis returned non-integer response: {other}")
        };
    }

    public string[] ExecuteArray(params string[] args)
    {
        WriteCommand(args);
        return ReadValue() switch
        {
            string[] values => values,
            null => [],
            string text => [text],
            var other => [other.ToString() ?? ""]
        };
    }

    private void WriteCommand(IReadOnlyList<string> args)
    {
        var builder = new StringBuilder();
        builder.Append('*').Append(args.Count).Append("\r\n");
        foreach (var arg in args)
        {
            var bytes = Encoding.UTF8.GetBytes(arg ?? "");
            builder.Append('$').Append(bytes.Length).Append("\r\n");
            builder.Append(arg).Append("\r\n");
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
            if (value is string text)
            {
                values.Add(text);
            }
            else if (value is long number)
            {
                values.Add(number.ToString(CultureInfo.InvariantCulture));
            }
            else if (value is not null)
            {
                values.Add(value.ToString() ?? "");
            }
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
                var next = stream.ReadByte();
                if (next != '\n')
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

public sealed class RuntimeDebounceRequest
{
    public string OperationKey { get; init; } = "";

    public string Backend { get; init; } = "";

    public bool ShouldSchedule { get; init; }

    public string Status { get; init; } = "";

    public string Message { get; init; } = "";
}

public sealed class RuntimeDebounceBatch
{
    public string OperationKey { get; init; } = "";

    public string Backend { get; init; } = "";

    public string Status { get; init; } = "ok";

    public string Message { get; init; } = "";

    public IReadOnlyList<string> Reasons { get; init; } = [];
}

public sealed class RuntimeCoordinationStatus
{
    public string Backend { get; init; } = "local-memory";

    public bool RedisRequested { get; init; }

    public bool RedisAvailable { get; init; }

    public bool FallbackActive { get; init; }

    public bool BlockingOnRedisUnavailable { get; init; }

    public string KeyPrefix { get; init; } = "";

    public string InstanceId { get; init; } = "";

    public int ActiveLockCount { get; init; }

    public int ActiveOperationCount { get; init; }

    public IReadOnlyList<RuntimeOperationProgress> ActiveOperations { get; init; } = [];

    public IReadOnlyList<RuntimeOperationProgress> RecentOperations { get; init; } = [];

    public string Message { get; init; } = "";
}

public sealed class RuntimeOperationProgress
{
    public string OperationId { get; init; } = "";

    public string OperationKey { get; init; } = "";

    public string Backend { get; init; } = "";

    public string Status { get; set; } = "";

    public string Message { get; set; } = "";

    public DateTimeOffset StartedAtUtc { get; init; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public DateTimeOffset? CompletedAtUtc { get; set; }
}

public sealed class RuntimeOperationLease : IAsyncDisposable
{
    private readonly Action? release;

    private RuntimeOperationLease(
        bool acquired,
        string status,
        string backend,
        string message,
        int statusCode,
        Action? release)
    {
        Acquired = acquired;
        Status = status;
        Backend = backend;
        Message = message;
        StatusCode = statusCode;
        this.release = release;
    }

    public bool Acquired { get; }

    public string Status { get; }

    public string Backend { get; }

    public string Message { get; }

    public int StatusCode { get; }

    public static RuntimeOperationLease CreateAcquired(string backend, Action release)
    {
        return new RuntimeOperationLease(
            acquired: true,
            status: "acquired",
            backend: backend,
            message: "",
            statusCode: StatusCodes.Status200OK,
            release: release);
    }

    public static RuntimeOperationLease NotAcquired(string status, string backend, string message, int statusCode)
    {
        return new RuntimeOperationLease(
            acquired: false,
            status: status,
            backend: backend,
            message: message,
            statusCode: statusCode,
            release: null);
    }

    public ValueTask DisposeAsync()
    {
        release?.Invoke();
        return ValueTask.CompletedTask;
    }
}

public sealed class RuntimeRedisOptions
{
    public const string SectionName = "Redis";

    public bool Enabled { get; init; }

    public string ConnectionString { get; init; } = "";

    public string KeyPrefix { get; init; } = "cmdb2m:test";

    public string InstanceId { get; init; } = "";

    public int OperationTtlSeconds { get; init; } = 86400;

    public int LockTtlSeconds { get; init; } = 300;

    public int LockExtendSeconds { get; init; } = 120;

    public int CacheDefaultTtlSeconds { get; init; } = 300;

    public string FailureMode { get; init; } = "fallback";

    public bool HasValidFailureMode()
    {
        return string.Equals(FailureMode, "fallback", StringComparison.OrdinalIgnoreCase)
            || string.Equals(FailureMode, "fail", StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class ZabbixConfigDependencyReadinessCheck(
    ZabbixClient zabbixClient,
    CmdbuildClient cmdbuildClient,
    IRuntimeCoordinationStore runtimeCoordination,
    IOptionsMonitor<RuntimeRedisOptions> redisOptions)
    : IServiceReadinessCheck
{
    public string Name => "zabbixconfig2api-dependencies";

    public async Task<ServiceReadinessCheckResult> CheckAsync(CancellationToken cancellationToken)
    {
        var failures = new List<string>();
        var zabbix = await zabbixClient.CheckConnectionAsync(cancellationToken);
        if (!zabbix.Success)
        {
            failures.Add($"Zabbix: {zabbix.Error ?? "check failed"}");
        }

        var cmdbuild = await cmdbuildClient.CheckConnectionAsync(cancellationToken);
        if (!cmdbuild.Success)
        {
            failures.Add($"CMDBuild: {cmdbuild.Error ?? "check failed"}");
        }

        var redis = redisOptions.CurrentValue;
        if (redis.Enabled)
        {
            var status = runtimeCoordination.Status();
            if (!status.RedisAvailable)
            {
                failures.Add($"Redis: {status.Message}");
            }
        }

        return failures.Count == 0
            ? ServiceReadinessCheckResult.Ok(Name, "Zabbix, CMDBuild, and Redis dependency checks are ready")
            : ServiceReadinessCheckResult.NotReady(Name, string.Join("; ", failures));
    }
}

public sealed class DurableStoreOptions
{
    public const string SectionName = "DurableStore";

    public string Provider { get; init; } = "sqlite";

    public string ConnectionString { get; init; } = "Data Source=state/cmdb2m.db";

    public bool MigrationsEnabled { get; init; } = true;

    public bool HasValidProvider()
    {
        return string.Equals(Provider, "file", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Provider, "sqlite", StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class MonitoringCoverageSnapshotOptions
{
    public const string SectionName = "MonitoringCoverageAudit";

    public bool Enabled { get; init; } = true;

    public int SnapshotRetentionDays { get; init; } = 180;

    public string TriggerMode { get; init; } = "manual";

    public string DefaultExpectedPolicy { get; init; } = "rules_matched";

    public string HostIdAttribute { get; init; } = "zabbix_main_hostid";

    public bool AllowOperationalDelta { get; init; } = true;

    public int MaxOperationalDeltaMinutes { get; init; } = 30;

    public bool AutoSnapshotAfterFullGraphApply { get; init; }

    public bool AutoSnapshotAfterScopedReconcile { get; init; }

    public string ScheduledSnapshotCron { get; init; } = "";

    public bool HasValidTriggerMode()
    {
        return string.Equals(TriggerMode, "manual", StringComparison.OrdinalIgnoreCase)
            || string.Equals(TriggerMode, "scheduled", StringComparison.OrdinalIgnoreCase)
            || string.Equals(TriggerMode, "manual_and_scheduled", StringComparison.OrdinalIgnoreCase);
    }

    public bool HasValidExpectedPolicy()
    {
        return string.Equals(DefaultExpectedPolicy, "rules_matched", StringComparison.OrdinalIgnoreCase)
            || string.Equals(DefaultExpectedPolicy, "class_policy", StringComparison.OrdinalIgnoreCase)
            || string.Equals(DefaultExpectedPolicy, "explicit_attribute", StringComparison.OrdinalIgnoreCase)
            || string.Equals(DefaultExpectedPolicy, "manual_scope", StringComparison.OrdinalIgnoreCase);
    }
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

public sealed class MonitoringCoverageSnapshotStore(
    IOptionsMonitor<DurableStoreOptions> durableOptions,
    ILogger<MonitoringCoverageSnapshotStore> logger)
{
    private readonly ConcurrentDictionary<string, MonitoringCoverageSnapshot> memorySnapshots = new(StringComparer.Ordinal);
    private readonly object syncRoot = new();

    public MonitoringCoverageSnapshotHistory List(int limit)
    {
        lock (syncRoot)
        {
            if (UseSqlite(out var connectionString, out var location))
            {
                try
                {
                    EnsureSqliteSchema(connectionString, location);
                    using var connection = OpenSqlite(connectionString);
                    return new MonitoringCoverageSnapshotHistory
                    {
                        Backend = "sqlite",
                        Location = location,
                        Snapshots = ReadSqliteSummaries(connection, limit)
                    };
                }
                catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException or InvalidOperationException or JsonException)
                {
                    logger.LogWarning(ex, "Failed to read monitoring coverage snapshots from SQLite {Location}", location);
                    return MemoryHistory(limit, $"SQLite coverage snapshot read failed: {ex.Message}", location);
                }
            }

            return MemoryHistory(limit);
        }
    }

    public MonitoringCoverageSnapshotSaveResult Save(
        MonitoringCoverageSnapshot snapshot,
        MonitoringCoverageSnapshotOptions options)
    {
        lock (syncRoot)
        {
            if (UseSqlite(out var connectionString, out var location))
            {
                try
                {
                    EnsureSqliteSchema(connectionString, location);
                    using var connection = OpenSqlite(connectionString);
                    using var transaction = connection.BeginTransaction();
                    UpsertSqliteSnapshot(connection, transaction, snapshot);
                    PruneSqliteSnapshots(connection, transaction, options.SnapshotRetentionDays);
                    transaction.Commit();
                    return new MonitoringCoverageSnapshotSaveResult
                    {
                        Backend = "sqlite",
                        Location = location,
                        SnapshotId = snapshot.SnapshotId
                    };
                }
                catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException or InvalidOperationException)
                {
                    logger.LogWarning(ex, "Failed to save monitoring coverage snapshot to SQLite {Location}; using memory fallback for this process.", location);
                    memorySnapshots[snapshot.SnapshotId] = snapshot;
                    return new MonitoringCoverageSnapshotSaveResult
                    {
                        Backend = "memory-fallback",
                        Location = location,
                        SnapshotId = snapshot.SnapshotId,
                        Error = ex.Message
                    };
                }
            }

            memorySnapshots[snapshot.SnapshotId] = snapshot;
            return new MonitoringCoverageSnapshotSaveResult
            {
                Backend = "memory",
                SnapshotId = snapshot.SnapshotId
            };
        }
    }

    private MonitoringCoverageSnapshotHistory MemoryHistory(int limit, string error = "", string location = "")
    {
        return new MonitoringCoverageSnapshotHistory
        {
            Backend = error.Length > 0 ? "memory-fallback" : "memory",
            Location = location,
            Error = error,
            Snapshots = memorySnapshots.Values
                .OrderByDescending(item => item.FinishedAtUtc)
                .Take(limit)
                .Select(MonitoringCoverageSnapshotSummary.FromSnapshot)
                .ToArray()
        };
    }

    private bool UseSqlite(out string connectionString, out string location)
    {
        var durable = durableOptions.CurrentValue;
        connectionString = durable.ConnectionString;
        location = SqliteLocation(connectionString);
        return durable.Provider.Equals("sqlite", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(connectionString);
    }

    private static void EnsureSqliteSchema(string connectionString, string location)
    {
        var directory = Path.GetDirectoryName(location);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var connection = OpenSqlite(connectionString);
        ExecuteNonQuery(connection, """
            create table if not exists monitoring_coverage_snapshots (
                snapshot_id text primary key,
                status text not null,
                expected_policy text not null,
                host_id_attribute text not null,
                started_at_utc text not null,
                zabbix_started_at_utc text not null,
                finished_at_utc text not null,
                expected_objects integer not null,
                with_host_id integer not null,
                existing_zabbix_hosts integer not null,
                missing_host_id integer not null,
                missing_zabbix_hosts integer not null,
                service_membership_objects integer not null,
                suppression_membership_objects integer not null,
                host_id_coverage_percent real not null,
                zabbix_coverage_percent real not null,
                payload_json text not null
            );
            """);
        ExecuteNonQuery(connection, "create index if not exists ix_monitoring_coverage_snapshots_finished on monitoring_coverage_snapshots(finished_at_utc desc);");
    }

    private static void UpsertSqliteSnapshot(
        SqliteConnection connection,
        SqliteTransaction transaction,
        MonitoringCoverageSnapshot snapshot)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            insert into monitoring_coverage_snapshots(
                snapshot_id, status, expected_policy, host_id_attribute,
                started_at_utc, zabbix_started_at_utc, finished_at_utc,
                expected_objects, with_host_id, existing_zabbix_hosts,
                missing_host_id, missing_zabbix_hosts,
                service_membership_objects, suppression_membership_objects,
                host_id_coverage_percent, zabbix_coverage_percent, payload_json)
            values (
                $snapshot_id, $status, $expected_policy, $host_id_attribute,
                $started_at_utc, $zabbix_started_at_utc, $finished_at_utc,
                $expected_objects, $with_host_id, $existing_zabbix_hosts,
                $missing_host_id, $missing_zabbix_hosts,
                $service_membership_objects, $suppression_membership_objects,
                $host_id_coverage_percent, $zabbix_coverage_percent, $payload_json)
            on conflict(snapshot_id) do update set
                status = excluded.status,
                expected_policy = excluded.expected_policy,
                host_id_attribute = excluded.host_id_attribute,
                started_at_utc = excluded.started_at_utc,
                zabbix_started_at_utc = excluded.zabbix_started_at_utc,
                finished_at_utc = excluded.finished_at_utc,
                expected_objects = excluded.expected_objects,
                with_host_id = excluded.with_host_id,
                existing_zabbix_hosts = excluded.existing_zabbix_hosts,
                missing_host_id = excluded.missing_host_id,
                missing_zabbix_hosts = excluded.missing_zabbix_hosts,
                service_membership_objects = excluded.service_membership_objects,
                suppression_membership_objects = excluded.suppression_membership_objects,
                host_id_coverage_percent = excluded.host_id_coverage_percent,
                zabbix_coverage_percent = excluded.zabbix_coverage_percent,
                payload_json = excluded.payload_json;
            """;
        command.Parameters.AddWithValue("$snapshot_id", snapshot.SnapshotId);
        command.Parameters.AddWithValue("$status", snapshot.Status);
        command.Parameters.AddWithValue("$expected_policy", snapshot.ExpectedPolicy);
        command.Parameters.AddWithValue("$host_id_attribute", snapshot.HostIdAttribute);
        command.Parameters.AddWithValue("$started_at_utc", snapshot.StartedAtUtc.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$zabbix_started_at_utc", snapshot.ZabbixStartedAtUtc.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$finished_at_utc", snapshot.FinishedAtUtc.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$expected_objects", snapshot.ExpectedObjects);
        command.Parameters.AddWithValue("$with_host_id", snapshot.WithHostId);
        command.Parameters.AddWithValue("$existing_zabbix_hosts", snapshot.ExistingZabbixHosts);
        command.Parameters.AddWithValue("$missing_host_id", snapshot.MissingHostId);
        command.Parameters.AddWithValue("$missing_zabbix_hosts", snapshot.MissingZabbixHosts);
        command.Parameters.AddWithValue("$service_membership_objects", snapshot.ServiceMembershipObjects);
        command.Parameters.AddWithValue("$suppression_membership_objects", snapshot.SuppressionMembershipObjects);
        command.Parameters.AddWithValue("$host_id_coverage_percent", snapshot.HostIdCoveragePercent);
        command.Parameters.AddWithValue("$zabbix_coverage_percent", snapshot.ZabbixCoveragePercent);
        command.Parameters.AddWithValue("$payload_json", JsonSerializer.Serialize(snapshot, ZabbixApplyStateStorageJson.Options));
        command.ExecuteNonQuery();
    }

    private static void PruneSqliteSnapshots(
        SqliteConnection connection,
        SqliteTransaction transaction,
        int retentionDays)
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-Math.Max(1, retentionDays));
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "delete from monitoring_coverage_snapshots where finished_at_utc < $cutoff;";
        command.Parameters.AddWithValue("$cutoff", cutoff.ToString("O", CultureInfo.InvariantCulture));
        command.ExecuteNonQuery();
    }

    private static IReadOnlyList<MonitoringCoverageSnapshotSummary> ReadSqliteSummaries(
        SqliteConnection connection,
        int limit)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            select snapshot_id, status, expected_policy, host_id_attribute,
                started_at_utc, finished_at_utc, expected_objects, with_host_id,
                existing_zabbix_hosts, missing_host_id, missing_zabbix_hosts,
                service_membership_objects, suppression_membership_objects,
                host_id_coverage_percent, zabbix_coverage_percent
            from monitoring_coverage_snapshots
            order by finished_at_utc desc
            limit $limit;
            """;
        command.Parameters.AddWithValue("$limit", limit);
        var result = new List<MonitoringCoverageSnapshotSummary>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new MonitoringCoverageSnapshotSummary
            {
                SnapshotId = reader.GetString(0),
                Status = reader.GetString(1),
                ExpectedPolicy = reader.GetString(2),
                HostIdAttribute = reader.GetString(3),
                StartedAtUtc = ParseDateTimeOffset(reader.GetString(4)),
                FinishedAtUtc = ParseDateTimeOffset(reader.GetString(5)),
                ExpectedObjects = reader.GetInt32(6),
                WithHostId = reader.GetInt32(7),
                ExistingZabbixHosts = reader.GetInt32(8),
                MissingHostId = reader.GetInt32(9),
                MissingZabbixHosts = reader.GetInt32(10),
                ServiceMembershipObjects = reader.GetInt32(11),
                SuppressionMembershipObjects = reader.GetInt32(12),
                HostIdCoveragePercent = reader.GetDouble(13),
                ZabbixCoveragePercent = reader.GetDouble(14)
            });
        }

        return result;
    }

    private static SqliteConnection OpenSqlite(string connectionString)
    {
        var connection = new SqliteConnection(connectionString);
        connection.Open();
        return connection;
    }

    private static void ExecuteNonQuery(SqliteConnection connection, string commandText)
    {
        using var command = connection.CreateCommand();
        command.CommandText = commandText;
        command.ExecuteNonQuery();
    }

    private static DateTimeOffset ParseDateTimeOffset(string value)
    {
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : DateTimeOffset.MinValue;
    }

    private static string SqliteLocation(string connectionString)
    {
        try
        {
            var builder = new SqliteConnectionStringBuilder(connectionString);
            return string.IsNullOrWhiteSpace(builder.DataSource)
                ? connectionString
                : builder.DataSource;
        }
        catch (ArgumentException)
        {
            return connectionString;
        }
    }
}

public sealed record MonitoringCoverageSnapshotSaveResult
{
    public string Backend { get; init; } = "";

    public string Location { get; init; } = "";

    public string SnapshotId { get; init; } = "";

    public string Error { get; init; } = "";
}

public sealed record MonitoringCoverageSnapshotHistory
{
    public string Backend { get; init; } = "";

    public string Location { get; init; } = "";

    public string Error { get; init; } = "";

    public IReadOnlyList<MonitoringCoverageSnapshotSummary> Snapshots { get; init; } = [];
}

public sealed record MonitoringCoverageSnapshotSummary
{
    public string SnapshotId { get; init; } = "";

    public string Status { get; init; } = "";

    public string ExpectedPolicy { get; init; } = "";

    public string HostIdAttribute { get; init; } = "";

    public DateTimeOffset StartedAtUtc { get; init; }

    public DateTimeOffset FinishedAtUtc { get; init; }

    public int ExpectedObjects { get; init; }

    public int WithHostId { get; init; }

    public int ExistingZabbixHosts { get; init; }

    public int MissingHostId { get; init; }

    public int MissingZabbixHosts { get; init; }

    public int ServiceMembershipObjects { get; init; }

    public int SuppressionMembershipObjects { get; init; }

    public double HostIdCoveragePercent { get; init; }

    public double ZabbixCoveragePercent { get; init; }

    public static MonitoringCoverageSnapshotSummary FromSnapshot(MonitoringCoverageSnapshot snapshot)
    {
        return new MonitoringCoverageSnapshotSummary
        {
            SnapshotId = snapshot.SnapshotId,
            Status = snapshot.Status,
            ExpectedPolicy = snapshot.ExpectedPolicy,
            HostIdAttribute = snapshot.HostIdAttribute,
            StartedAtUtc = snapshot.StartedAtUtc,
            FinishedAtUtc = snapshot.FinishedAtUtc,
            ExpectedObjects = snapshot.ExpectedObjects,
            WithHostId = snapshot.WithHostId,
            ExistingZabbixHosts = snapshot.ExistingZabbixHosts,
            MissingHostId = snapshot.MissingHostId,
            MissingZabbixHosts = snapshot.MissingZabbixHosts,
            ServiceMembershipObjects = snapshot.ServiceMembershipObjects,
            SuppressionMembershipObjects = snapshot.SuppressionMembershipObjects,
            HostIdCoveragePercent = snapshot.HostIdCoveragePercent,
            ZabbixCoveragePercent = snapshot.ZabbixCoveragePercent
        };
    }
}

public sealed class MonitoringCoverageSourceRecord
{
    private readonly HashSet<string> zabbixHostIds = new(StringComparer.Ordinal);

    public string SourceClass { get; private set; } = "";

    public string SourceCardId { get; private set; } = "";

    public bool InServiceMembership { get; private set; }

    public bool InSuppressionMembership { get; private set; }

    public IReadOnlySet<string> ZabbixHostIds => zabbixHostIds;

    public static IReadOnlyList<MonitoringCoverageSourceRecord> FromMemberships(
        IReadOnlyList<ZabbixTargetMembershipSnapshot> serviceMemberships,
        IReadOnlyList<ZabbixTargetMembershipSnapshot> suppressionMemberships)
    {
        var records = new Dictionary<string, MonitoringCoverageSourceRecord>(StringComparer.Ordinal);
        AddLayer(records, "service", serviceMemberships);
        AddLayer(records, "suppression", suppressionMemberships);
        return records.Values
            .OrderBy(item => item.SourceClass, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.SourceCardId, StringComparer.Ordinal)
            .ToArray();
    }

    private static void AddLayer(
        IDictionary<string, MonitoringCoverageSourceRecord> records,
        string layer,
        IReadOnlyList<ZabbixTargetMembershipSnapshot> memberships)
    {
        foreach (var membership in memberships)
        {
            foreach (var source in membership.Sources.Concat(membership.PendingSources))
            {
                if (string.IsNullOrWhiteSpace(source.SourceClass) || string.IsNullOrWhiteSpace(source.SourceCardId))
                {
                    continue;
                }

                var key = $"{source.SourceClass}\u001f{source.SourceCardId}";
                if (!records.TryGetValue(key, out var record))
                {
                    record = new MonitoringCoverageSourceRecord
                    {
                        SourceClass = source.SourceClass,
                        SourceCardId = source.SourceCardId
                    };
                    records[key] = record;
                }

                if (string.Equals(layer, "service", StringComparison.OrdinalIgnoreCase))
                {
                    record.InServiceMembership = true;
                }
                else if (string.Equals(layer, "suppression", StringComparison.OrdinalIgnoreCase))
                {
                    record.InSuppressionMembership = true;
                }

                if (!string.IsNullOrWhiteSpace(source.ZabbixHostId))
                {
                    record.zabbixHostIds.Add(source.ZabbixHostId.Trim());
                }
            }
        }
    }
}

public sealed class MonitoringCoverageSnapshot
{
    public string SnapshotId { get; init; } = "";

    public string Status { get; init; } = "completed";

    public string ExpectedPolicy { get; init; } = "";

    public string HostIdAttribute { get; init; } = "";

    public DateTimeOffset StartedAtUtc { get; init; }

    public DateTimeOffset ZabbixStartedAtUtc { get; init; }

    public DateTimeOffset FinishedAtUtc { get; init; }

    public bool AllowOperationalDelta { get; init; }

    public int MaxOperationalDeltaMinutes { get; init; }

    public int ExpectedObjects { get; init; }

    public int WithHostId { get; init; }

    public int ExistingZabbixHosts { get; init; }

    public int MissingHostId { get; init; }

    public int MissingZabbixHosts { get; init; }

    public int ServiceMembershipObjects { get; init; }

    public int SuppressionMembershipObjects { get; init; }

    public int BothLayerMembershipObjects { get; init; }

    public double HostIdCoveragePercent { get; init; }

    public double ZabbixCoveragePercent { get; init; }

    public IReadOnlyList<string> Errors { get; init; } = [];

    public IReadOnlyList<string> Warnings { get; init; } = [];

    public IReadOnlyList<MonitoringCoverageSample> MissingHostIdSamples { get; init; } = [];

    public IReadOnlyList<MonitoringCoverageSample> MissingZabbixHostSamples { get; init; } = [];

    public IReadOnlyList<MonitoringCoverageSample> ServiceOnlySamples { get; init; } = [];

    public IReadOnlyList<MonitoringCoverageSample> SuppressionOnlySamples { get; init; } = [];

    public static MonitoringCoverageSnapshot FromRecords(
        IReadOnlyList<MonitoringCoverageSourceRecord> records,
        IReadOnlyList<ZabbixHostInfo> zabbixHosts,
        MonitoringCoverageSnapshotOptions options,
        DateTimeOffset startedAtUtc,
        DateTimeOffset zabbixStartedAtUtc,
        DateTimeOffset finishedAtUtc,
        IReadOnlyList<string> errors)
    {
        var existingHostIds = zabbixHosts
            .Select(item => item.HostId)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToHashSet(StringComparer.Ordinal);
        var withHostId = records.Count(item => item.ZabbixHostIds.Count > 0);
        var missingZabbix = records
            .Where(item => item.ZabbixHostIds.Count > 0)
            .Where(item => !item.ZabbixHostIds.Any(existingHostIds.Contains))
            .ToArray();
        var warnings = new List<string>();
        var duration = finishedAtUtc - startedAtUtc;
        if (options.AllowOperationalDelta && duration > TimeSpan.FromMinutes(options.MaxOperationalDeltaMinutes))
        {
            warnings.Add($"Snapshot duration {duration.TotalMinutes:F1} min exceeds allowed operational delta {options.MaxOperationalDeltaMinutes} min.");
        }

        return new MonitoringCoverageSnapshot
        {
            SnapshotId = $"coverage-{finishedAtUtc:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}",
            Status = errors.Count == 0 ? "completed" : "partial",
            ExpectedPolicy = options.DefaultExpectedPolicy,
            HostIdAttribute = options.HostIdAttribute,
            StartedAtUtc = startedAtUtc,
            ZabbixStartedAtUtc = zabbixStartedAtUtc,
            FinishedAtUtc = finishedAtUtc,
            AllowOperationalDelta = options.AllowOperationalDelta,
            MaxOperationalDeltaMinutes = options.MaxOperationalDeltaMinutes,
            ExpectedObjects = records.Count,
            WithHostId = withHostId,
            ExistingZabbixHosts = records.Count(item => item.ZabbixHostIds.Any(existingHostIds.Contains)),
            MissingHostId = records.Count(item => item.ZabbixHostIds.Count == 0),
            MissingZabbixHosts = missingZabbix.Length,
            ServiceMembershipObjects = records.Count(item => item.InServiceMembership),
            SuppressionMembershipObjects = records.Count(item => item.InSuppressionMembership),
            BothLayerMembershipObjects = records.Count(item => item.InServiceMembership && item.InSuppressionMembership),
            HostIdCoveragePercent = Percent(withHostId, records.Count),
            ZabbixCoveragePercent = Percent(records.Count(item => item.ZabbixHostIds.Any(existingHostIds.Contains)), records.Count),
            Errors = errors.ToArray(),
            Warnings = warnings.ToArray(),
            MissingHostIdSamples = records
                .Where(item => item.ZabbixHostIds.Count == 0)
                .Select(MonitoringCoverageSample.FromRecord)
                .Take(50)
                .ToArray(),
            MissingZabbixHostSamples = missingZabbix
                .Select(MonitoringCoverageSample.FromRecord)
                .Take(50)
                .ToArray(),
            ServiceOnlySamples = records
                .Where(item => item.InServiceMembership && !item.InSuppressionMembership)
                .Select(MonitoringCoverageSample.FromRecord)
                .Take(50)
                .ToArray(),
            SuppressionOnlySamples = records
                .Where(item => item.InSuppressionMembership && !item.InServiceMembership)
                .Select(MonitoringCoverageSample.FromRecord)
                .Take(50)
                .ToArray()
        };
    }

    private static double Percent(int value, int total)
    {
        return total <= 0 ? 0 : Math.Round(value * 100.0 / total, 2);
    }
}

public sealed class MonitoringCoverageSample
{
    public string SourceClass { get; init; } = "";

    public string SourceCardId { get; init; } = "";

    public IReadOnlyList<string> ZabbixHostIds { get; init; } = [];

    public bool InServiceMembership { get; init; }

    public bool InSuppressionMembership { get; init; }

    public static MonitoringCoverageSample FromRecord(MonitoringCoverageSourceRecord record)
    {
        return new MonitoringCoverageSample
        {
            SourceClass = record.SourceClass,
            SourceCardId = record.SourceCardId,
            ZabbixHostIds = record.ZabbixHostIds.Order(StringComparer.Ordinal).ToArray(),
            InServiceMembership = record.InServiceMembership,
            InSuppressionMembership = record.InSuppressionMembership
        };
    }
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
    IRuntimeCoordinationStore runtimeCoordination,
    ILogger<ZabbixTriggerDependencyReconcileScheduler> logger)
    : BackgroundService
{
    private const string OperationKey = "zabbix:dependencies:suppression:auto-reconcile";
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

        var delay = TimeSpan.FromSeconds(Math.Max(0, currentOptions.AutoReconcileDebounceSeconds));
        var request = runtimeCoordination.RequestDebouncedOperation(
            OperationKey,
            string.IsNullOrWhiteSpace(reason) ? "suppression membership changed" : reason,
            delay);
        if (request.ShouldSchedule)
        {
            requests.Writer.TryWrite(OperationKey);
        }
        else if (string.Equals(request.Status, "runtime_coordination_unavailable", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning(
                "Automatic suppression trigger dependency reconcile request skipped: status={Status}, backend={Backend}, message={Message}",
                request.Status,
                request.Backend,
                request.Message);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (await requests.Reader.WaitToReadAsync(stoppingToken))
        {
            DrainRequests();
            var delay = TimeSpan.FromSeconds(Math.Max(0, options.CurrentValue.AutoReconcileDebounceSeconds));
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, stoppingToken);
                DrainRequests();
            }

            var debounceBatch = runtimeCoordination.ConsumeDebouncedOperation(OperationKey);
            var reasons = debounceBatch.Reasons.Count > 0
                ? debounceBatch.Reasons.ToList()
                : ["suppression membership changed"];

            try
            {
                await using var lease = await runtimeCoordination.TryAcquireLockAsync(OperationKey, stoppingToken);
                if (!lease.Acquired)
                {
                    logger.LogWarning(
                        "Automatic suppression trigger dependency reconcile skipped: status={Status}, backend={Backend}, message={Message}, reasons={Reasons}",
                        lease.Status,
                        lease.Backend,
                        lease.Message,
                        string.Join("; ", reasons.Distinct(StringComparer.Ordinal).Take(10)));
                    continue;
                }

                var operation = runtimeCoordination.StartOperation(OperationKey, lease.Backend);
                using var scope = scopeFactory.CreateScope();
                var applier = scope.ServiceProvider.GetRequiredService<ZabbixTriggerDependencyApplier>();
                var dirtyScopes = scope.ServiceProvider.GetRequiredService<ZabbixDirtyScopeStore>();
                try
                {
                    var request = DirtyScopeWorkflow.WithDirtyScopeDefault(
                        dirtyScopes,
                        null,
                        "suppression");
                    var result = await applier.RunAsync(dryRun: false, request, stoppingToken);
                    DirtyScopeWorkflow.MarkFromTriggerDependencyResult(
                        dirtyScopes,
                        result,
                        $"automatic suppression dependencies reconcile: {string.Join("; ", reasons.Distinct(StringComparer.Ordinal).Take(3))}");
                    runtimeCoordination.CompleteOperation(operation.OperationId, "completed");
                    logger.LogInformation(
                        "Automatic suppression trigger dependency reconcile completed: status={Status}, aggregates={AggregateCount}, updatedTriggers={UpdatedTriggers}, reasons={Reasons}",
                        result.Status,
                        result.AggregateCount,
                        result.TriggersUpdated,
                        string.Join("; ", reasons.Distinct(StringComparer.Ordinal).Take(10)));
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    runtimeCoordination.CompleteOperation(operation.OperationId, "cancelled", "Service is stopping.");
                    throw;
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
                {
                    runtimeCoordination.CompleteOperation(operation.OperationId, "failed", ex.Message);
                    throw;
                }
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

    private void DrainRequests()
    {
        while (requests.Reader.TryRead(out _))
        {
        }
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
