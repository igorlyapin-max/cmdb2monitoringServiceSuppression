using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Cmdb2MonitoringServiceSuppression.Shared.Integrations;
using Microsoft.Extensions.Options;

public sealed class ZabbixTriggerDependencyApplier(
    ZabbixClient zabbix,
    ZabbixApplyStateStore state,
    IOptionsMonitor<ZabbixTriggerDependenciesOptions> options,
    ILogger<ZabbixTriggerDependencyApplier> logger)
{
    private const string Layer = "suppression";

    public async Task<ZabbixTriggerDependencyRunResult> RunAsync(
        bool dryRun,
        CancellationToken cancellationToken)
    {
        var currentOptions = options.CurrentValue;
        var result = new ZabbixTriggerDependencyRunResult
        {
            Layer = Layer,
            DryRun = dryRun,
            StartedAtUtc = DateTimeOffset.UtcNow
        };

        try
        {
            if (!currentOptions.Enabled)
            {
                result.Status = "skipped";
                result.Message = "Публикация trigger dependencies выключена в конфигурации.";
                return Complete(result);
            }

            await BuildDesiredPlanAsync(result, currentOptions, dryRun, cancellationToken);
            if (result.Errors.Count > 0)
            {
                result.Status = "error";
                result.Message = "Trigger dependencies не рассчитаны: есть блокирующие ошибки.";
                return Complete(result);
            }

            if (dryRun)
            {
                result.Status = "dry-run";
                result.Message = "Dry-run trigger dependencies и aggregate triggers завершен без изменения Zabbix.";
                return Complete(result);
            }

            await BuildReconcilePlanAsync(result, currentOptions, cancellationToken);
            if (result.Errors.Count > 0)
            {
                result.Status = "error";
                result.Message = "Trigger dependencies не применены: reconcile содержит ошибки.";
                return Complete(result);
            }

            foreach (var update in result.TriggerUpdates)
            {
                await zabbix.UpdateTriggerDependenciesAsync(
                    update.TriggerId,
                    update.FinalDependencyTriggerIds,
                    cancellationToken);
                result.TriggersUpdated++;
            }

            state.ReplaceManagedTriggerDependencies(
                Layer,
                result.DesiredDependencies.Select(item => item.ToManaged(Layer)).ToArray());
            result.Status = "applied";
            result.Message = $"Trigger dependencies применены: обновлено триггеров {result.TriggersUpdated}, добавлено зависимостей {result.DependenciesAdded}, удалено устаревших {result.DependenciesRemoved}.";
            return Complete(result);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            logger.LogError(ex, "Zabbix trigger dependencies apply failed.");
            result.Status = "error";
            result.Message = "Trigger dependencies не применены в Zabbix.";
            result.Errors.Add(ex.Message);
            return Complete(result);
        }
    }

    private async Task BuildDesiredPlanAsync(
        ZabbixTriggerDependencyRunResult result,
        ZabbixTriggerDependenciesOptions currentOptions,
        bool dryRun,
        CancellationToken cancellationToken)
    {
        var memberships = state.ListMemberships(Layer)
            .Where(item => item.SourceCount > 0 || item.Relations.Count > 0)
            .ToArray();
        result.TargetCount = memberships.Length;
        result.ManagedDependencyCountBefore = state.ListManagedTriggerDependencies(Layer).Count;

        var hostIds = memberships
            .SelectMany(item => item.Sources)
            .Select(source => source.ZabbixHostId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        result.HostCount = hostIds.Length;

        if (memberships.Length == 0)
        {
            result.Warnings.Add("В suppression state нет membership-объектов; сначала примените модель подавления в Zabbix.");
            return;
        }

        if (hostIds.Length == 0)
        {
            result.Warnings.Add("В suppression membership нет source-карточек с zabbix_main_hostid; trigger dependencies не из чего строить.");
            return;
        }

        var triggers = await zabbix.GetTriggersByHostIdsAsync(
            hostIds,
            currentOptions.IncludeDisabledTriggers,
            cancellationToken);
        result.TriggerCount = triggers.Count;
        var triggersByHost = BuildTriggersByHost(triggers);

        var aggregateByTarget = new Dictionary<string, ZabbixSuppressionAggregatePlanItem>(StringComparer.Ordinal);
        foreach (var target in memberships)
        {
            var aggregate = BuildAggregatePlan(target, triggersByHost, currentOptions, result);
            if (!dryRun)
            {
                var apply = await zabbix.ApplySuppressionAggregateAsync(aggregate.ToDefinition(), cancellationToken);
                aggregate.HostId = apply.HostId;
                aggregate.ItemId = apply.ItemId;
                aggregate.TriggerId = apply.TriggerId;
                aggregate.HostAction = apply.HostAction;
                aggregate.ItemAction = apply.ItemAction;
                aggregate.TriggerAction = apply.TriggerAction;
                aggregate.StatePushed = apply.StatePushed;
                if (string.Equals(apply.HostAction, "created", StringComparison.OrdinalIgnoreCase))
                {
                    result.AggregateHostsCreated++;
                }

                if (string.Equals(apply.ItemAction, "created", StringComparison.OrdinalIgnoreCase))
                {
                    result.AggregateItemsCreated++;
                }
                else
                {
                    result.AggregateItemsUpdated++;
                }

                if (string.Equals(apply.TriggerAction, "created", StringComparison.OrdinalIgnoreCase))
                {
                    result.AggregateTriggersCreated++;
                }
                else
                {
                    result.AggregateTriggersUpdated++;
                }

                if (apply.StatePushed)
                {
                    result.AggregateStatesPushed++;
                }
            }

            aggregateByTarget[target.TargetManagedKey] = aggregate;
            result.Aggregates.Add(aggregate);
        }
        result.AggregateCount = result.Aggregates.Count;

        var desired = new Dictionary<string, ZabbixTriggerDependencyPlanItem>(StringComparer.Ordinal);
        foreach (var causeTarget in memberships)
        {
            if (!aggregateByTarget.TryGetValue(causeTarget.TargetManagedKey, out var causeAggregate)
                || string.IsNullOrWhiteSpace(causeAggregate.TriggerId))
            {
                result.Warnings.Add(
                    $"Для объекта-причины {causeTarget.TargetName} не рассчитан aggregate trigger; связи от него пропущены.");
                continue;
            }

            foreach (var relation in causeTarget.Relations)
            {
                var dependentTarget = ResolveMembership(memberships, relation, result);
                if (dependentTarget is null)
                {
                    continue;
                }

                if (string.Equals(causeTarget.TargetManagedKey, dependentTarget.TargetManagedKey, StringComparison.Ordinal))
                {
                    result.Warnings.Add($"Связь {causeTarget.TargetName} -> {dependentTarget.TargetName} пропущена: самоссылка target.");
                    continue;
                }

                var causeTrigger = causeAggregate.ToTriggerInfo();
                var dependentTriggers = TriggersForTarget(dependentTarget, triggersByHost, result, role: "зависимый объект");
                if (aggregateByTarget.TryGetValue(dependentTarget.TargetManagedKey, out var dependentAggregate)
                    && !string.IsNullOrWhiteSpace(dependentAggregate.TriggerId))
                {
                    dependentTriggers = dependentTriggers
                        .Concat(new[] { dependentAggregate.ToTriggerInfo() })
                        .DistinctBy(trigger => trigger.TriggerId, StringComparer.Ordinal)
                        .ToArray();
                }

                foreach (var dependentTrigger in dependentTriggers)
                {
                    if (string.Equals(dependentTrigger.TriggerId, causeTrigger.TriggerId, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var item = ZabbixTriggerDependencyPlanItem.From(
                        dependentTarget,
                        causeTarget,
                        dependentTrigger,
                        causeTrigger,
                        relation.DomainCode);
                    desired.TryAdd(item.Key, item);
                }
            }
        }

        result.DesiredDependencies.AddRange(desired.Values
            .OrderBy(item => item.DependentTargetName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.DependencyTargetName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.DependentTriggerId, StringComparer.Ordinal)
            .ThenBy(item => item.DependencyTriggerId, StringComparer.Ordinal));
        result.DesiredDependencyCount = result.DesiredDependencies.Count;
        result.DependentTriggerCount = result.DesiredDependencies
            .Select(item => item.DependentTriggerId)
            .Distinct(StringComparer.Ordinal)
            .Count();
        result.DependencyTriggerCount = result.DesiredDependencies
            .Select(item => item.DependencyTriggerId)
            .Distinct(StringComparer.Ordinal)
            .Count();

        if (result.DesiredDependencyCount > currentOptions.MaxDependenciesPerRun)
        {
            result.Errors.Add(
                $"Рассчитано {result.DesiredDependencyCount} trigger dependencies, лимит ZabbixTriggerDependencies:MaxDependenciesPerRun={currentOptions.MaxDependenciesPerRun}.");
        }

        foreach (var cycle in FindCycles(result.DesiredDependencies))
        {
            result.Errors.Add($"Цикл trigger dependencies: {cycle}.");
        }

        result.SampleDependencies.AddRange(result.DesiredDependencies.Take(currentOptions.SampleLimit));
        result.HasMoreSamples = result.DesiredDependencies.Count > result.SampleDependencies.Count;
        result.SampleAggregates.AddRange(result.Aggregates.Take(currentOptions.SampleLimit));
    }

    private async Task BuildReconcilePlanAsync(
        ZabbixTriggerDependencyRunResult result,
        ZabbixTriggerDependenciesOptions currentOptions,
        CancellationToken cancellationToken)
    {
        var previous = state.ListManagedTriggerDependencies(Layer);
        var desiredByTrigger = result.DesiredDependencies
            .GroupBy(item => item.DependentTriggerId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(item => item.DependencyTriggerId).Distinct(StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal),
                StringComparer.Ordinal);
        var previousByTrigger = previous
            .GroupBy(item => item.DependentTriggerId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(item => item.DependencyTriggerId).Distinct(StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal),
                StringComparer.Ordinal);
        var dependentTriggerIds = desiredByTrigger.Keys
            .Concat(previousByTrigger.Keys)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (dependentTriggerIds.Length == 0)
        {
            return;
        }

        var currentTriggers = await zabbix.GetTriggersByIdsAsync(
            dependentTriggerIds,
            includeDisabled: true,
            cancellationToken);
        var currentById = currentTriggers.ToDictionary(trigger => trigger.TriggerId, StringComparer.Ordinal);
        foreach (var triggerId in dependentTriggerIds)
        {
            if (!currentById.TryGetValue(triggerId, out var trigger))
            {
                result.Warnings.Add($"Ранее управляемый dependent trigger {triggerId} не найден в Zabbix; stale state будет удален после успешного apply.");
                continue;
            }

            var existing = trigger.Dependencies
                .Select(item => item.TriggerId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToHashSet(StringComparer.Ordinal);
            desiredByTrigger.TryGetValue(triggerId, out var desired);
            previousByTrigger.TryGetValue(triggerId, out var managedBefore);
            desired ??= new HashSet<string>(StringComparer.Ordinal);
            managedBefore ??= new HashSet<string>(StringComparer.Ordinal);

            var final = existing
                .Where(id => !managedBefore.Contains(id))
                .Concat(desired)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToArray();
            if (SetEquals(existing, final))
            {
                result.PreservedManualDependencies += existing.Count(id => !managedBefore.Contains(id) && !desired.Contains(id));
                continue;
            }

            var update = new ZabbixTriggerDependencyUpdate
            {
                TriggerId = triggerId,
                TriggerName = trigger.Description,
                ExistingDependencyTriggerIds = existing.OrderBy(id => id, StringComparer.Ordinal).ToArray(),
                FinalDependencyTriggerIds = final
            };
            result.TriggerUpdates.Add(update);
            result.TriggersToUpdate++;
            result.DependenciesAdded += desired.Count(id => !existing.Contains(id));
            result.DependenciesRemoved += managedBefore.Count(id => existing.Contains(id) && !desired.Contains(id));
            result.PreservedManualDependencies += existing.Count(id => !managedBefore.Contains(id) && !desired.Contains(id));
        }
    }

    private ZabbixTriggerDependencyRunResult Complete(ZabbixTriggerDependencyRunResult result)
    {
        result.CompletedAtUtc = DateTimeOffset.UtcNow;
        if (string.IsNullOrWhiteSpace(result.Status))
        {
            result.Status = "completed";
        }

        if (string.IsNullOrWhiteSpace(result.Message))
        {
            result.Message = "Trigger dependencies рассчитаны.";
        }

        state.RecordTriggerDependencyRun(result);
        return result.ToPublicResult();
    }

    private static ZabbixSuppressionAggregatePlanItem BuildAggregatePlan(
        ZabbixTargetMembershipSnapshot target,
        IReadOnlyDictionary<string, IReadOnlyList<ZabbixTriggerInfo>> triggersByHost,
        ZabbixTriggerDependenciesOptions currentOptions,
        ZabbixTriggerDependencyRunResult result)
    {
        var hostIds = target.Sources
            .Select(source => source.ZabbixHostId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var problemHosts = 0;
        var healthyHosts = 0;
        var unknownHosts = 0;
        foreach (var hostId in hostIds)
        {
            if (!triggersByHost.TryGetValue(hostId, out var hostTriggers) || hostTriggers.Count == 0)
            {
                unknownHosts++;
                continue;
            }

            if (hostTriggers.Any(trigger => string.Equals(trigger.Value, "1", StringComparison.Ordinal)))
            {
                problemHosts++;
            }
            else
            {
                healthyHosts++;
            }
        }

        var aggregationType = NormalizeAggregationType(target.AggregationType);
        var stateValue = CalculateAggregateState(
            aggregationType,
            hostIds.Length,
            problemHosts,
            target.Threshold,
            target.N,
            result,
            target.TargetName);
        var itemHash = StableHash(target.TargetManagedKey);
        var itemKey = $"{currentOptions.AggregateItemKeyPrefix}[{itemHash}]";
        var targetName = string.IsNullOrWhiteSpace(target.TargetName)
            ? target.TargetManagedKey
            : target.TargetName;
        var triggerName = $"CMDB2M suppression: {targetName} недоступен как группа";
        return new ZabbixSuppressionAggregatePlanItem
        {
            Layer = Layer,
            TargetManagedKey = target.TargetManagedKey,
            TargetClass = target.TargetClass,
            TargetCardId = target.TargetCardId,
            TargetName = targetName,
            AggregationType = aggregationType,
            HostGroupName = currentOptions.AggregateHostGroupName,
            HostName = currentOptions.AggregateHostName,
            HostVisibleName = currentOptions.AggregateHostVisibleName,
            ItemKey = itemKey,
            ItemName = $"CMDB2M suppression state: {targetName}",
            TriggerName = triggerName,
            TriggerPriority = currentOptions.AggregateTriggerPriority,
            TriggerId = $"planned:{itemHash}",
            StateValue = stateValue,
            HostCount = hostIds.Length,
            HealthyHostCount = healthyHosts,
            ProblemHostCount = problemHosts,
            UnknownHostCount = unknownHosts
        };
    }

    private static int CalculateAggregateState(
        string aggregationType,
        int hostCount,
        int problemHostCount,
        string threshold,
        string n,
        ZabbixTriggerDependencyRunResult result,
        string targetName)
    {
        if (hostCount <= 0)
        {
            result.Warnings.Add($"{targetName}: aggregate trigger будет OK, потому что нет source-host с zabbix_main_hostid.");
            return 0;
        }

        return aggregationType switch
        {
            "any" => problemHostCount >= hostCount ? 1 : 0,
            "threshold" => CalculateThresholdState(hostCount, problemHostCount, threshold, result, targetName),
            "n_of_m" => CalculateNOfMState(hostCount, problemHostCount, n, result, targetName),
            _ => problemHostCount > 0 ? 1 : 0
        };
    }

    private static int CalculateThresholdState(
        int hostCount,
        int problemHostCount,
        string threshold,
        ZabbixTriggerDependencyRunResult result,
        string targetName)
    {
        if (!TryParseDecimal(threshold, out var thresholdValue))
        {
            result.Warnings.Add($"{targetName}: aggregation_type=threshold, но поле threshold не задано или не распознано; aggregate trigger оставлен OK.");
            return 0;
        }

        thresholdValue = Math.Clamp(thresholdValue, 0m, 100m);
        var requiredHealthy = (int)Math.Ceiling(hostCount * (double)thresholdValue / 100d);
        var maxHealthy = hostCount - problemHostCount;
        return maxHealthy < requiredHealthy ? 1 : 0;
    }

    private static int CalculateNOfMState(
        int hostCount,
        int problemHostCount,
        string n,
        ZabbixTriggerDependencyRunResult result,
        string targetName)
    {
        if (!int.TryParse(n, NumberStyles.Integer, CultureInfo.InvariantCulture, out var requiredHealthy))
        {
            result.Warnings.Add($"{targetName}: aggregation_type=n_of_m, но поле n не задано или не распознано; aggregate trigger оставлен OK.");
            return 0;
        }

        requiredHealthy = Math.Clamp(requiredHealthy, 0, hostCount);
        var maxHealthy = hostCount - problemHostCount;
        return maxHealthy < requiredHealthy ? 1 : 0;
    }

    private static bool TryParseDecimal(string value, out decimal parsed)
    {
        return decimal.TryParse(value?.Trim().Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out parsed);
    }

    private static string StableHash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant()[..24];
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<ZabbixTriggerInfo>> BuildTriggersByHost(
        IReadOnlyList<ZabbixTriggerInfo> triggers)
    {
        return triggers
            .SelectMany(trigger => trigger.Hosts.Select(host => new { host.HostId, Trigger = trigger }))
            .Where(item => !string.IsNullOrWhiteSpace(item.HostId))
            .GroupBy(item => item.HostId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<ZabbixTriggerInfo>)group
                    .Select(item => item.Trigger)
                    .DistinctBy(trigger => trigger.TriggerId, StringComparer.Ordinal)
                    .OrderBy(trigger => trigger.Description, StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                StringComparer.Ordinal);
    }

    private static ZabbixTargetMembershipSnapshot? ResolveMembership(
        IReadOnlyList<ZabbixTargetMembershipSnapshot> memberships,
        ZabbixMembershipRelation relation,
        ZabbixTriggerDependencyRunResult result)
    {
        var candidates = ZabbixManagedServiceMapper.LookupCandidates(
                relation.TargetClassCode,
                relation.TargetLookup)
            .ToHashSet(StringComparer.Ordinal);
        var matches = memberships
            .Where(item =>
                candidates.Contains(item.TargetManagedKey)
                || (item.TargetClass.Equals(relation.TargetClassCode, StringComparison.OrdinalIgnoreCase)
                    && (item.TargetCardId.Equals(relation.TargetLookup, StringComparison.OrdinalIgnoreCase)
                        || item.TargetManagedKey.Equals(relation.TargetLookup, StringComparison.OrdinalIgnoreCase))))
            .ToArray();
        if (matches.Length == 0)
        {
            result.Warnings.Add(
                $"Для relation {relation.DomainCode} не найден suppression membership target {relation.TargetClassCode}/{relation.TargetLookup}.");
            return null;
        }

        if (matches.Length > 1)
        {
            result.Warnings.Add(
                $"Relation {relation.DomainCode} -> {relation.TargetClassCode}/{relation.TargetLookup} нашла несколько membership target; выбран {matches[0].TargetName}.");
        }

        return matches[0];
    }

    private static IReadOnlyList<ZabbixTriggerInfo> TriggersForTarget(
        ZabbixTargetMembershipSnapshot target,
        IReadOnlyDictionary<string, IReadOnlyList<ZabbixTriggerInfo>> triggersByHost,
        ZabbixTriggerDependencyRunResult result,
        string role)
    {
        var triggers = new List<ZabbixTriggerInfo>();
        var missingHostIds = new List<string>();
        foreach (var source in target.Sources)
        {
            if (string.IsNullOrWhiteSpace(source.ZabbixHostId))
            {
                continue;
            }

            if (triggersByHost.TryGetValue(source.ZabbixHostId, out var hostTriggers) && hostTriggers.Count > 0)
            {
                triggers.AddRange(hostTriggers);
            }
            else
            {
                missingHostIds.Add(source.ZabbixHostId);
            }
        }

        if (missingHostIds.Count > 0)
        {
            result.Warnings.Add(
                $"{target.TargetName} ({role}): для hostid {string.Join(", ", missingHostIds.Distinct(StringComparer.Ordinal).Take(10))} не найдены активные triggers.");
        }

        return triggers
            .DistinctBy(trigger => trigger.TriggerId, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool SupportsDirectTriggerDependencies(ZabbixTargetMembershipSnapshot causeTarget)
    {
        if (causeTarget.HostBindingCount <= 1)
        {
            return true;
        }

        return NormalizeAggregationType(causeTarget.AggregationType) == "all";
    }

    private static string NormalizeAggregationType(string aggregationType)
    {
        var value = aggregationType.Trim();
        return string.IsNullOrWhiteSpace(value)
            ? "all"
            : value.ToLowerInvariant();
    }

    private static IReadOnlyList<string> FindCycles(IReadOnlyList<ZabbixTriggerDependencyPlanItem> dependencies)
    {
        var graph = dependencies
            .GroupBy(item => item.DependentTriggerId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(item => item.DependencyTriggerId).Distinct(StringComparer.Ordinal).ToArray(),
                StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var visiting = new HashSet<string>(StringComparer.Ordinal);
        var stack = new Stack<string>();
        var cycles = new List<string>();

        foreach (var node in graph.Keys.OrderBy(item => item, StringComparer.Ordinal))
        {
            Visit(node);
        }

        return cycles;

        void Visit(string node)
        {
            if (visited.Contains(node))
            {
                return;
            }

            if (!visiting.Add(node))
            {
                var cycle = stack.Reverse().SkipWhile(item => item != node).Concat(new[] { node });
                cycles.Add(string.Join(" -> ", cycle));
                return;
            }

            stack.Push(node);
            if (graph.TryGetValue(node, out var nextNodes))
            {
                foreach (var next in nextNodes)
                {
                    Visit(next);
                }
            }

            stack.Pop();
            visiting.Remove(node);
            visited.Add(node);
        }
    }

    private static bool SetEquals(IReadOnlySet<string> left, IReadOnlyList<string> right)
    {
        return left.Count == right.Count && right.All(left.Contains);
    }
}

public sealed class ZabbixTriggerDependenciesOptions
{
    public const string SectionName = "ZabbixTriggerDependencies";

    public bool Enabled { get; init; } = true;

    public bool IncludeDisabledTriggers { get; init; }

    public int MaxDependenciesPerRun { get; init; } = 10000;

    public int SampleLimit { get; init; } = 100;

    public string AggregateHostGroupName { get; init; } = "CMDB2Monitoring";

    public string AggregateHostName { get; init; } = "cmdb2monitoring-suppression-aggregates";

    public string AggregateHostVisibleName { get; init; } = "CMDB2Monitoring suppression aggregates";

    public string AggregateItemKeyPrefix { get; init; } = "cmdb2monitoring.suppression.aggregate";

    public int AggregateTriggerPriority { get; init; } = 3;
}

public sealed class ZabbixTriggerDependencyRunResult
{
    public string Layer { get; init; } = "";

    public bool DryRun { get; init; }

    public string Status { get; set; } = "";

    public string Message { get; set; } = "";

    public DateTimeOffset StartedAtUtc { get; init; }

    public DateTimeOffset CompletedAtUtc { get; set; }

    public int TargetCount { get; set; }

    public int HostCount { get; set; }

    public int TriggerCount { get; set; }

    public int DesiredDependencyCount { get; set; }

    public int DependentTriggerCount { get; set; }

    public int DependencyTriggerCount { get; set; }

    public int AggregateCount { get; set; }

    public int AggregateHostsCreated { get; set; }

    public int AggregateItemsCreated { get; set; }

    public int AggregateItemsUpdated { get; set; }

    public int AggregateTriggersCreated { get; set; }

    public int AggregateTriggersUpdated { get; set; }

    public int AggregateStatesPushed { get; set; }

    public int ManagedDependencyCountBefore { get; set; }

    public int TriggersToUpdate { get; set; }

    public int TriggersUpdated { get; set; }

    public int DependenciesAdded { get; set; }

    public int DependenciesRemoved { get; set; }

    public int PreservedManualDependencies { get; set; }

    public bool HasMoreSamples { get; set; }

    [JsonIgnore]
    public List<ZabbixTriggerDependencyPlanItem> DesiredDependencies { get; } = [];

    public List<ZabbixTriggerDependencyPlanItem> SampleDependencies { get; } = [];

    public List<ZabbixSuppressionAggregatePlanItem> SampleAggregates { get; } = [];

    [JsonIgnore]
    public List<ZabbixTriggerDependencyUpdate> TriggerUpdates { get; } = [];

    [JsonIgnore]
    public List<ZabbixSuppressionAggregatePlanItem> Aggregates { get; } = [];

    public List<string> Warnings { get; } = [];

    public List<string> Errors { get; } = [];

    public ZabbixTriggerDependencyRunResult ToPublicResult()
    {
        return this;
    }
}

public sealed class ZabbixSuppressionAggregatePlanItem
{
    public string Layer { get; init; } = "";

    public string TargetManagedKey { get; init; } = "";

    public string TargetClass { get; init; } = "";

    public string TargetCardId { get; init; } = "";

    public string TargetName { get; init; } = "";

    public string AggregationType { get; init; } = "";

    public string HostGroupName { get; init; } = "";

    public string HostName { get; init; } = "";

    public string HostVisibleName { get; init; } = "";

    public string HostId { get; set; } = "";

    public string ItemKey { get; init; } = "";

    public string ItemName { get; init; } = "";

    public string ItemId { get; set; } = "";

    public string TriggerName { get; init; } = "";

    public int TriggerPriority { get; init; }

    public string TriggerId { get; set; } = "";

    public int StateValue { get; init; }

    public int HostCount { get; init; }

    public int HealthyHostCount { get; init; }

    public int ProblemHostCount { get; init; }

    public int UnknownHostCount { get; init; }

    public string HostAction { get; set; } = "planned";

    public string ItemAction { get; set; } = "planned";

    public string TriggerAction { get; set; } = "planned";

    public bool StatePushed { get; set; }

    public ZabbixSuppressionAggregateDefinition ToDefinition()
    {
        return new ZabbixSuppressionAggregateDefinition
        {
            Layer = Layer,
            TargetManagedKey = TargetManagedKey,
            TargetClass = TargetClass,
            TargetCardId = TargetCardId,
            TargetName = TargetName,
            AggregationType = AggregationType,
            HostGroupName = HostGroupName,
            HostName = HostName,
            HostVisibleName = HostVisibleName,
            ItemKey = ItemKey,
            ItemName = ItemName,
            TriggerName = TriggerName,
            TriggerPriority = TriggerPriority,
            StateValue = StateValue
        };
    }

    public ZabbixTriggerInfo ToTriggerInfo()
    {
        return new ZabbixTriggerInfo
        {
            TriggerId = TriggerId,
            Description = TriggerName,
            Value = StateValue.ToString(CultureInfo.InvariantCulture),
            Hosts =
            [
                new ZabbixHostInfo
                {
                    HostId = HostId,
                    Host = HostName,
                    Name = HostVisibleName
                }
            ]
        };
    }
}

public sealed class ZabbixTriggerDependencyPlanItem
{
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

    public string Key => $"{DependentTriggerId}\u001f{DependencyTriggerId}";

    public static ZabbixTriggerDependencyPlanItem From(
        ZabbixTargetMembershipSnapshot dependentTarget,
        ZabbixTargetMembershipSnapshot dependencyTarget,
        ZabbixTriggerInfo dependentTrigger,
        ZabbixTriggerInfo dependencyTrigger,
        string relationDomainCode)
    {
        return new ZabbixTriggerDependencyPlanItem
        {
            DependentTriggerId = dependentTrigger.TriggerId,
            DependencyTriggerId = dependencyTrigger.TriggerId,
            DependentTriggerName = dependentTrigger.Description,
            DependencyTriggerName = dependencyTrigger.Description,
            DependentHostId = dependentTrigger.Hosts.FirstOrDefault()?.HostId ?? "",
            DependencyHostId = dependencyTrigger.Hosts.FirstOrDefault()?.HostId ?? "",
            DependentTargetManagedKey = dependentTarget.TargetManagedKey,
            DependencyTargetManagedKey = dependencyTarget.TargetManagedKey,
            DependentTargetName = dependentTarget.TargetName,
            DependencyTargetName = dependencyTarget.TargetName,
            RelationDomainCode = relationDomainCode
        };
    }

    public ZabbixManagedTriggerDependency ToManaged(string layer)
    {
        return new ZabbixManagedTriggerDependency
        {
            Layer = layer,
            DependentTriggerId = DependentTriggerId,
            DependencyTriggerId = DependencyTriggerId,
            DependentTriggerName = DependentTriggerName,
            DependencyTriggerName = DependencyTriggerName,
            DependentHostId = DependentHostId,
            DependencyHostId = DependencyHostId,
            DependentTargetManagedKey = DependentTargetManagedKey,
            DependencyTargetManagedKey = DependencyTargetManagedKey,
            DependentTargetName = DependentTargetName,
            DependencyTargetName = DependencyTargetName,
            RelationDomainCode = RelationDomainCode
        };
    }
}

public sealed class ZabbixTriggerDependencyUpdate
{
    public string TriggerId { get; init; } = "";

    public string TriggerName { get; init; } = "";

    public IReadOnlyList<string> ExistingDependencyTriggerIds { get; init; } = [];

    public IReadOnlyList<string> FinalDependencyTriggerIds { get; init; } = [];
}
