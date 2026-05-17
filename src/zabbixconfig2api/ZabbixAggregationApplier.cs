using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Cmdb2MonitoringServiceSuppression.Shared.Aggregation;
using Cmdb2MonitoringServiceSuppression.Shared.Configuration;
using Cmdb2MonitoringServiceSuppression.Shared.Integrations;

public sealed class ZabbixAggregationApplier(
    ZabbixClient zabbix,
    ZabbixApplyStateStore state,
    ZabbixTriggerDependencyReconcileScheduler triggerDependencyScheduler,
    ILogger<ZabbixAggregationApplier> logger)
{
    public async Task<ZabbixGraphApplyResult> ApplyGraphAsync(
        IReadOnlyList<AggregationCommand> commands,
        string layer,
        string topic,
        ApplyOptions options,
        bool forceDryRun,
        CancellationToken cancellationToken,
        string publishMode = ZabbixGraphPublishModes.Changes,
        IReadOnlyList<string>? scopeKeys = null,
        int scopeDepth = 0)
    {
        var dryRun = forceDryRun || string.Equals(options.Mode, "dry-run", StringComparison.OrdinalIgnoreCase);
        var effectivePublishMode = ZabbixGraphPublishModes.Normalize(publishMode);
        var result = new ZabbixGraphApplyResult
        {
            Layer = layer,
            Topic = topic,
            Mode = dryRun ? "dry-run" : options.Mode,
            PublishMode = effectivePublishMode,
            DryRun = dryRun,
            SafeApply = options.SafeApply,
            CommandsReceived = commands.Count,
            AppliedAtUtc = DateTimeOffset.UtcNow
        };

        if (commands.Count == 0)
        {
            result.Status = "skipped";
            result.Message = "Граф Zabbix пуст: команд для применения нет.";
            return result;
        }

        var normalizedCommands = commands
            .Where(command => string.Equals(ZabbixApplyPlanner.NormalizeLayer(command.Layer), layer, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var layerMismatchCount = commands.Count - normalizedCommands.Length;
        if (layerMismatchCount > 0)
        {
            result.Errors.Add($"{layerMismatchCount} команд относятся к другому слою и не применены.");
        }

        var scope = ZabbixGraphScopeResolver.Resolve(normalizedCommands, layer, scopeKeys ?? [], scopeDepth);
        if (scope.Enabled)
        {
            result.Scope = scope.Summary;
            result.Warnings.AddRange(scope.Warnings);
            normalizedCommands = scope.Commands.ToArray();
        }

        var createManagedServices = ShouldCreateManagedServices(layer, options);
        var desiredGraph = ZabbixDesiredGraphBuilder.Build(
            normalizedCommands,
            layer,
            createManagedServices);
        result.Diff = state.DiffAppliedGraph(
            layer,
            desiredGraph.Objects,
            effectivePublishMode,
            sampleLimit: 30,
            scope.Enabled ? scope.TargetManagedKeys : null);
        if (result.Diff.Removed > 0)
        {
            result.Warnings.Add(
                $"Desired graph больше не содержит {result.Diff.Removed} ранее примененных объектов; автоматическое удаление из Zabbix в режиме изменений не выполняется.");
        }

        var commandsToApply = SelectCommandsForPublish(
            normalizedCommands,
            desiredGraph,
            result.Diff,
            effectivePublishMode);
        result.CommandsSelectedForPublish = commandsToApply.Count;

        if (dryRun)
        {
            foreach (var command in normalizedCommands)
            {
                result.CommandResults.Add(ZabbixApplyPlanner.Plan(command, layer, topic, options, forceDryRun));
            }

            result.CommandsApplied = result.CommandResults.Count(item =>
                item.Status.Equals("dry-run", StringComparison.OrdinalIgnoreCase));
            result.Status = "dry-run";
            result.Message = ZabbixGraphPublishModes.IsFull(effectivePublishMode)
                ? "Полный граф Zabbix проверен без публикации изменений."
                : $"Граф Zabbix проверен без публикации: к публикации {result.Diff.PublishCandidates} изменений.";
            return result;
        }

        if (commandsToApply.Count == 0)
        {
            result.Status = result.Warnings.Count > 0 ? "partial" : "skipped";
            result.Message = result.Warnings.Count > 0
                ? "Изменений для публикации нет; есть stale-объекты, требующие отдельной очистки."
                : "Изменений для публикации нет: persisted desired graph совпадает с текущим расчетом.";
            return result;
        }

        if (!createManagedServices
            || commandsToApply.Any(command => !command.CommandType.Equals(AggregationCommandTypes.EnsureMembership, StringComparison.OrdinalIgnoreCase)))
        {
            var sequentialResult = await ApplyGraphSequentiallyAsync(commandsToApply, layer, topic, options, result, cancellationToken);
            RecordAppliedGraphIfSuccessful(sequentialResult, desiredGraph, scope);
            return sequentialResult;
        }

        if (!string.Equals(layer, "service", StringComparison.OrdinalIgnoreCase))
        {
            var sequentialResult = await ApplyGraphSequentiallyAsync(commandsToApply, layer, topic, options, result, cancellationToken);
            RecordAppliedGraphIfSuccessful(sequentialResult, desiredGraph, scope);
            return sequentialResult;
        }

        using var apiStatsScope = ZabbixClient.BeginApiCallStatsScope();
        var totalWatch = Stopwatch.StartNew();
        try
        {
            var targetSnapshots = new Dictionary<string, ZabbixTargetMembershipSnapshot>(StringComparer.Ordinal);
            var targetCommands = new Dictionary<string, AggregationCommand>(StringComparer.Ordinal);
            var desiredDefinitions = new Dictionary<string, ZabbixManagedServiceDefinition>(StringComparer.Ordinal);
            var commandResults = new List<ZabbixCommandApplyResult>();
            foreach (var command in commandsToApply)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var membershipUpdate = state.UpdateMembership(command, layer, includeSourceLeafManagedKey: true);
                var commandResult = ZabbixApplyPlanner.Plan(command, layer, topic, options, forceDryRun: false);
                commandResult.Membership = membershipUpdate.Current;
                commandResults.Add(commandResult);

                var targetManagedKey = ZabbixManagedServiceMapper.ManagedKey(command.Target);
                if (string.IsNullOrWhiteSpace(targetManagedKey))
                {
                    result.Warnings.Add($"Команда {command.CommandId}: пустой managed key целевого объекта.");
                    continue;
                }

                targetSnapshots[targetManagedKey] = membershipUpdate.Current;
                targetCommands[targetManagedKey] = command;
            }

            var targetNodeWarnings = new List<string>();
            foreach (var pair in targetCommands.OrderBy(pair => GraphDepth(pair.Value), Comparer<int>.Default))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var definition = ZabbixManagedServiceMapper.FromAggregationCommand(pair.Value, layer, []);
                desiredDefinitions[definition.ManagedKey] = definition;
                var apply = await zabbix.ApplyManagedServiceNodeAsync(definition, cancellationToken);
                AddServiceApplyCounters(result, apply);
                targetNodeWarnings.AddRange(apply.Warnings);
            }

            var leafWarnings = new List<string>();
            foreach (var command in commandsToApply
                .Where(command => !string.IsNullOrWhiteSpace(command.Source.CardId))
                .DistinctBy(command => $"{command.Source.ClassCode}\u001f{command.Source.CardId}", StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var parentManagedKey = ZabbixManagedServiceMapper.ManagedKey(command.Target);
                if (!string.IsNullOrWhiteSpace(command.Source.ZabbixHostId))
                {
                    var leaf = ZabbixManagedServiceMapper.FromSourceBinding(command, layer)
                        with { ParentManagedKeys = string.IsNullOrWhiteSpace(parentManagedKey) ? [] : [parentManagedKey] };
                    desiredDefinitions[leaf.ManagedKey] = leaf;
                }

                var sourceLeafApply = await EnsureCurrentSourceLeafAsync(
                    command,
                    layer,
                    leafWarnings,
                    cancellationToken,
                    parentManagedKey);
                AddServiceApplyCounters(result, sourceLeafApply);
                result.SourceLeafServicesApplied += sourceLeafApply.SourceLeafServicesApplied;
                result.HostTagsApplied += sourceLeafApply.HostTagsApplied;
            }

            var finalWarnings = new List<string>();
            foreach (var pair in targetCommands.OrderBy(pair => GraphDepth(pair.Value), Comparer<int>.Default))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var snapshot = targetSnapshots[pair.Key];
                var definition = ZabbixManagedServiceMapper.FromAggregationCommand(
                    pair.Value,
                    layer,
                    snapshot.SourceLeafManagedKeys);
                desiredDefinitions[definition.ManagedKey] = definition;
                var apply = await zabbix.ApplyManagedServiceAsync(definition, cancellationToken);
                AddServiceApplyCounters(result, apply);
                finalWarnings.AddRange(apply.Warnings);
            }

            await VerifyServiceGraphAsync(
                result,
                layer,
                desiredDefinitions.Values.ToArray(),
                cancellationToken);

            totalWatch.Stop();
            result.Warnings.AddRange(targetNodeWarnings);
            result.Warnings.AddRange(leafWarnings);
            result.Warnings.AddRange(finalWarnings);
            var hasErrors = result.Errors.Count > 0;
            var commandStatus = hasErrors
                ? "error"
                : result.RelationsDeferred > 0 || result.Warnings.Count > 0
                    ? "partial"
                    : "applied";
            result.CommandResults.AddRange(commandResults.Select(item =>
            {
                item.Status = commandStatus;
                item.ZabbixAction = "graph_applied";
                item.Message = "Команда применена в составе проверенного Zabbix graph batch.";
                item.AppliedAtUtc = DateTimeOffset.UtcNow;
                return item;
            }));
            result.CommandsApplied = result.CommandResults.Count(item =>
                item.Status.Equals("applied", StringComparison.OrdinalIgnoreCase)
                || item.Status.Equals("partial", StringComparison.OrdinalIgnoreCase));
            result.CommandsErrored = result.CommandResults.Count(item =>
                item.Status.Equals("error", StringComparison.OrdinalIgnoreCase));
            result.Status = hasErrors
                ? "error"
                : result.RelationsDeferred > 0 || result.Warnings.Count > 0
                ? "partial"
                : "applied";
            result.Message =
                $"Zabbix service graph применен фазами: команд {result.CommandsApplied}, relations applied {result.RelationsApplied}, deferred {result.RelationsDeferred}.";
            result.Performance = new ZabbixCommandApplyPerformance
            {
                TotalMs = totalWatch.ElapsedMilliseconds,
                ZabbixApiCallCount = apiStatsScope.Stats.CallCount,
                ZabbixApiElapsedMs = apiStatsScope.Stats.ElapsedMs,
                ZabbixApiByMethod = apiStatsScope.Stats.SnapshotByMethod()
                    .ToDictionary(
                        pair => pair.Key,
                        pair => new ZabbixApiMethodPerformance
                        {
                            Count = pair.Value.Count,
                            ElapsedMs = pair.Value.ElapsedMs
                        },
                        StringComparer.Ordinal)
            };
            RecordAppliedGraphIfSuccessful(result, desiredGraph, scope);
            return result;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            totalWatch.Stop();
            logger.LogError(ex, "Zabbix {Layer} graph apply failed: commands={CommandCount}", layer, normalizedCommands.Length);
            result.Status = "error";
            result.Message = "Граф Zabbix не применен.";
            result.Errors.Add(ex.Message);
            result.Performance = new ZabbixCommandApplyPerformance
            {
                TotalMs = totalWatch.ElapsedMilliseconds,
                ZabbixApiCallCount = apiStatsScope.Stats.CallCount,
                ZabbixApiElapsedMs = apiStatsScope.Stats.ElapsedMs,
                ZabbixApiByMethod = apiStatsScope.Stats.SnapshotByMethod()
                    .ToDictionary(
                        pair => pair.Key,
                        pair => new ZabbixApiMethodPerformance
                        {
                            Count = pair.Value.Count,
                            ElapsedMs = pair.Value.ElapsedMs
                        },
                        StringComparer.Ordinal)
            };
            return result;
        }
    }

    private void RecordAppliedGraphIfSuccessful(
        ZabbixGraphApplyResult result,
        ZabbixDesiredGraphBuildResult desiredGraph,
        ZabbixGraphScopeResolution scope)
    {
        if (result.DryRun
            || string.Equals(result.Status, "error", StringComparison.OrdinalIgnoreCase)
            || result.Errors.Count > 0)
        {
            return;
        }

        if (scope.Enabled)
        {
            state.UpsertAppliedGraph(result.Layer, desiredGraph.Objects);
        }
        else
        {
            state.ReplaceAppliedGraph(result.Layer, desiredGraph.Objects);
        }

        result.AppliedGraphObjectCount = desiredGraph.Objects.Count;
    }

    private static IReadOnlyList<AggregationCommand> SelectCommandsForPublish(
        IReadOnlyList<AggregationCommand> commands,
        ZabbixDesiredGraphBuildResult desiredGraph,
        ZabbixGraphDiffResult diff,
        string publishMode)
    {
        if (ZabbixGraphPublishModes.IsFull(publishMode))
        {
            return commands;
        }

        var candidateKeys = diff.CandidateObjectKeySet;
        if (candidateKeys.Count == 0)
        {
            return [];
        }

        return commands
            .Where(command =>
            {
                var commandKey = ZabbixDesiredGraphBuilder.CommandKey(command);
                return desiredGraph.ObjectKeysByCommandKey.TryGetValue(commandKey, out var objectKeys)
                    && objectKeys.Any(candidateKeys.Contains);
            })
            .ToArray();
    }

    public async Task<ZabbixCommandApplyResult> ApplyAsync(
        AggregationCommand command,
        string layer,
        string topic,
        ApplyOptions options,
        CancellationToken cancellationToken)
    {
        var createManagedServices = ShouldCreateManagedServices(layer, options);
        var result = ZabbixApplyPlanner.Plan(command, layer, topic, options, forceDryRun: false);
        var performance = new ZabbixCommandApplyPerformance();
        using var apiStatsScope = ZabbixClient.BeginApiCallStatsScope();
        var totalWatch = Stopwatch.StartNew();
        ZabbixCommandApplyResult Finish(ZabbixCommandApplyResult applyResult)
        {
            totalWatch.Stop();
            performance.TotalMs = totalWatch.ElapsedMilliseconds;
            performance.ZabbixApiCallCount = apiStatsScope.Stats.CallCount;
            performance.ZabbixApiElapsedMs = apiStatsScope.Stats.ElapsedMs;
            performance.ZabbixApiByMethod = apiStatsScope.Stats.SnapshotByMethod()
                .ToDictionary(
                    pair => pair.Key,
                    pair => new ZabbixApiMethodPerformance
                    {
                        Count = pair.Value.Count,
                        ElapsedMs = pair.Value.ElapsedMs
                    },
                    StringComparer.Ordinal);
            applyResult.Performance = performance;
            return applyResult;
        }

        var stateWatch = Stopwatch.StartNew();
        var membershipUpdate = state.UpdateMembership(command, layer, createManagedServices);
        stateWatch.Stop();
        performance.StateUpdateMs = stateWatch.ElapsedMilliseconds;
        var membership = membershipUpdate.Current;
        result.Membership = membership;
        try
        {
            if (string.Equals(command.CommandType, AggregationCommandTypes.RemoveSourceMembership, StringComparison.OrdinalIgnoreCase))
            {
                var removalWarnings = new List<string>();
                var affectedWatch = Stopwatch.StartNew();
                var removalAffectedApply = createManagedServices
                    ? await ApplyAffectedMembershipTargetsAsync(
                        membershipUpdate.AffectedTargets,
                        layer,
                        removalWarnings,
                        cancellationToken)
                    : new ZabbixManagedServiceApplyResult();
                affectedWatch.Stop();
                performance.AffectedTargetsApplyMs += affectedWatch.ElapsedMilliseconds;
                result.Status = removalAffectedApply.RelationsDeferred > 0 || removalWarnings.Count > 0 ? "partial" : "applied";
                result.ZabbixAction = "source_membership_removed";
                result.Message = SourceMembershipRemovedMessage(command, membershipUpdate);
                result.RelationsApplied = removalAffectedApply.RelationsApplied;
                result.RelationsDeferred = removalAffectedApply.RelationsDeferred;
                result.Warnings = removalWarnings.Concat(removalAffectedApply.Warnings).ToArray();
                result.AppliedAtUtc = DateTimeOffset.UtcNow;
                RequestSuppressionTriggerDependencyReconcile(command, layer);
                return Finish(result);
            }

            if (string.Equals(command.CommandType, AggregationCommandTypes.RemoveMembership, StringComparison.OrdinalIgnoreCase))
            {
                if (createManagedServices)
                {
                    result.Status = "skipped";
                    result.Message = options.SafeApply
                        ? "Удаление managed service из Zabbix пропущено: включен safe apply. Объекты не удаляются автоматически."
                        : "Удаление managed service из Zabbix пока не применяется автоматически; требуется отдельная reconcile-операция.";
                }
                else
                {
                    result.Status = "applied";
                    result.ZabbixAction = "membership_removed";
                    result.Message = SuppressionMembershipMessage(command, membership, removed: true);
                }

                result.AppliedAtUtc = DateTimeOffset.UtcNow;
                RequestSuppressionTriggerDependencyReconcile(command, layer);
                return Finish(result);
            }

            if (!createManagedServices)
            {
                var membershipWarnings = SuppressionMembershipWarnings(command);
                result.Status = membershipWarnings.Count > 0 ? "partial" : "applied";
                result.ZabbixAction = "membership_updated";
                result.Message = SuppressionMembershipMessage(command, membership, removed: false);
                result.Warnings = membershipWarnings;
                result.AppliedAtUtc = DateTimeOffset.UtcNow;
                RequestSuppressionTriggerDependencyReconcile(command, layer);
                return Finish(result);
            }

            var warnings = new List<string>();
            var affectedTargetsWatch = Stopwatch.StartNew();
            var affectedApply = await ApplyAffectedMembershipTargetsAsync(
                membershipUpdate.AffectedTargets
                    .Where(item => !item.TargetManagedKey.Equals(membership.TargetManagedKey, StringComparison.Ordinal))
                    .ToArray(),
                layer,
                warnings,
                cancellationToken);
            affectedTargetsWatch.Stop();
            performance.AffectedTargetsApplyMs += affectedTargetsWatch.ElapsedMilliseconds;
            var sourceLeafWatch = Stopwatch.StartNew();
            var sourceLeafApply = await EnsureCurrentSourceLeafAsync(command, layer, warnings, cancellationToken);
            sourceLeafWatch.Stop();
            performance.SourceLeafApplyMs += sourceLeafWatch.ElapsedMilliseconds;
            var definition = ZabbixManagedServiceMapper.FromAggregationCommand(
                command,
                layer,
                membership.SourceLeafManagedKeys);
            var targetWatch = Stopwatch.StartNew();
            var apply = await zabbix.ApplyManagedServiceAsync(definition, cancellationToken);
            targetWatch.Stop();
            performance.TargetApplyMs += targetWatch.ElapsedMilliseconds;
            result.Status = apply.RelationsDeferred > 0 || affectedApply.RelationsDeferred > 0 || warnings.Count > 0 ? "partial" : "applied";
            result.Message = ApplyMessage(apply);
            result.ZabbixServiceId = apply.ServiceId;
            result.ZabbixAction = apply.Action;
            result.RelationsApplied = affectedApply.RelationsApplied + apply.RelationsApplied;
            result.RelationsDeferred = affectedApply.RelationsDeferred + apply.RelationsDeferred;
            result.SourceLeafServicesApplied = sourceLeafApply.SourceLeafServicesApplied + apply.SourceLeafServicesApplied;
            result.ProblemTagsApplied = sourceLeafApply.ProblemTagsApplied + apply.ProblemTagsApplied;
            result.HostTagsApplied = sourceLeafApply.HostTagsApplied + apply.HostTagsApplied;
            result.Warnings = warnings.Concat(affectedApply.Warnings).Concat(apply.Warnings).ToArray();
            result.AppliedAtUtc = DateTimeOffset.UtcNow;
            RequestSuppressionTriggerDependencyReconcile(command, layer);
            return Finish(result);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            logger.LogError(
                ex,
                "Zabbix {Layer} apply failed: command={CommandId}, rule={RuleId}, target={TargetClass}/{TargetKey}",
                layer,
                command.CommandId,
                command.RuleId,
                command.Target.ClassCode,
                string.IsNullOrWhiteSpace(command.Target.CardId)
                    ? command.Target.IdempotencyKey
                    : command.Target.CardId);

            result.Status = "error";
            result.Error = ex.Message;
            result.Message = "Команда не применена в Zabbix.";
            result.AppliedAtUtc = DateTimeOffset.UtcNow;
            return Finish(result);
        }
    }

    private void RequestSuppressionTriggerDependencyReconcile(AggregationCommand command, string layer)
    {
        if (!string.Equals(layer, "suppression", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var target = string.Equals(command.CommandType, AggregationCommandTypes.RemoveSourceMembership, StringComparison.OrdinalIgnoreCase)
            ? "source tombstone"
            : $"{command.Target.ClassCode}/{command.Target.CardId}";
        triggerDependencyScheduler.Request(
            $"membership {command.Source.ClassCode}/{command.Source.CardId} -> {target}");
    }

    private async Task<ZabbixGraphApplyResult> ApplyGraphSequentiallyAsync(
        IReadOnlyList<AggregationCommand> commands,
        string layer,
        string topic,
        ApplyOptions options,
        ZabbixGraphApplyResult result,
        CancellationToken cancellationToken)
    {
        foreach (var command in commands)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var apply = await ApplyAsync(command, layer, topic, options, cancellationToken);
            result.CommandResults.Add(apply);
            result.CommandsApplied += apply.Status.Equals("applied", StringComparison.OrdinalIgnoreCase)
                || apply.Status.Equals("partial", StringComparison.OrdinalIgnoreCase)
                ? 1
                : 0;
            result.CommandsErrored += apply.Status.Equals("error", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            result.RelationsApplied += apply.RelationsApplied;
            result.RelationsDeferred += apply.RelationsDeferred;
            result.SourceLeafServicesApplied += apply.SourceLeafServicesApplied;
            result.ProblemTagsApplied += apply.ProblemTagsApplied;
            result.HostTagsApplied += apply.HostTagsApplied;
            result.Warnings.AddRange(apply.Warnings);
            if (!string.IsNullOrWhiteSpace(apply.Error))
            {
                result.Errors.Add($"{command.RuleId}: {apply.Error}");
            }
        }

        result.Status = result.CommandsErrored > 0
            ? "error"
            : result.RelationsDeferred > 0 || result.Warnings.Count > 0 || result.Errors.Count > 0
                ? "partial"
                : "applied";
        result.Message =
            $"Zabbix graph применен последовательным режимом: команд {result.CommandsApplied}, ошибок {result.CommandsErrored}.";
        result.AppliedAtUtc = DateTimeOffset.UtcNow;
        return result;
    }

    private static void AddServiceApplyCounters(
        ZabbixGraphApplyResult result,
        ZabbixManagedServiceApplyResult apply)
    {
        result.RelationsApplied += apply.RelationsApplied;
        result.RelationsDeferred += apply.RelationsDeferred;
        result.ProblemTagsApplied += apply.ProblemTagsApplied;
    }

    private static int GraphDepth(AggregationCommand command)
    {
        return command.Target.ParentManagedKeys.Count;
    }

    private async Task VerifyServiceGraphAsync(
        ZabbixGraphApplyResult result,
        string layer,
        IReadOnlyList<ZabbixManagedServiceDefinition> desiredDefinitions,
        CancellationToken cancellationToken)
    {
        if (desiredDefinitions.Count == 0)
        {
            return;
        }

        var services = await zabbix.ListManagedServicesByLayerAsync(
            layer,
            Math.Clamp(desiredDefinitions.Count + 500, 1, 10000),
            cancellationToken);
        var actualByKey = services
            .Select(service => new
            {
                Service = service,
                ManagedKey = service.Tags.GetValueOrDefault(ZabbixManagedServiceTags.Key) ?? ""
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.ManagedKey))
            .GroupBy(item => item.ManagedKey, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Service, StringComparer.Ordinal);

        foreach (var desired in desiredDefinitions
            .Where(item => !string.IsNullOrWhiteSpace(item.ManagedKey))
            .DistinctBy(item => item.ManagedKey, StringComparer.Ordinal))
        {
            if (!actualByKey.TryGetValue(desired.ManagedKey, out var actual))
            {
                result.Errors.Add($"Post-verify Zabbix: managed service не найден после публикации: {desired.Name} ({desired.ManagedKey}).");
                continue;
            }

            if (desired.ParentManagedKeys.Count > 0 && actual.Parents.Count == 0)
            {
                result.Errors.Add(
                    $"Post-verify Zabbix: service {actual.Name} ({desired.ManagedKey}) должен иметь parent, но находится без parents.");
            }

            if (actual.Parents.Count == 0
                && !desired.Role.Equals(ZabbixManagedServiceRoles.RootService, StringComparison.OrdinalIgnoreCase)
                && !desired.Visibility.Equals(ZabbixManagedServiceVisibility.Internal, StringComparison.OrdinalIgnoreCase))
            {
                result.Errors.Add(
                    $"Post-verify Zabbix: visible non-root service {actual.Name} ({desired.ManagedKey}) остался в корне Zabbix Services.");
            }
        }
    }

    private async Task<ZabbixManagedServiceApplyResult> ApplyAffectedMembershipTargetsAsync(
        IReadOnlyList<ZabbixTargetMembershipSnapshot> affectedTargets,
        string layer,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        var relationsApplied = 0;
        var relationsDeferred = 0;
        var applyWarnings = new List<string>();
        foreach (var target in affectedTargets
            .Where(item => !string.IsNullOrWhiteSpace(item.TargetManagedKey))
            .DistinctBy(item => item.TargetManagedKey, StringComparer.Ordinal))
        {
            try
            {
                var apply = await zabbix.ApplyManagedServiceAsync(
                    FromMembershipSnapshot(target, layer),
                    cancellationToken);
                relationsApplied += apply.RelationsApplied;
                relationsDeferred += apply.RelationsDeferred;
                applyWarnings.AddRange(apply.Warnings);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
            {
                warnings.Add(
                    $"Не удалось обновить stale target {target.TargetName} после изменения source membership: {ex.Message}");
            }
        }

        return new ZabbixManagedServiceApplyResult
        {
            RelationsApplied = relationsApplied,
            RelationsDeferred = relationsDeferred,
            Warnings = applyWarnings
        };
    }

    private static ZabbixManagedServiceDefinition FromMembershipSnapshot(
        ZabbixTargetMembershipSnapshot target,
        string layer)
    {
        var role = ZabbixManagedServiceMapper.ServiceRoleForTarget(
            layer,
            target.TargetClass,
            target.SourceCount > 0 || target.SourceLeafManagedKeys.Count > 0);
        var visibility = role switch
        {
            ZabbixManagedServiceRoles.RootService => ZabbixManagedServiceVisibility.Root,
            ZabbixManagedServiceRoles.SourceLeaf or ZabbixManagedServiceRoles.Internal => ZabbixManagedServiceVisibility.Internal,
            _ => ZabbixManagedServiceVisibility.Child
        };
        var tags = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [ZabbixManagedServiceTags.Managed] = "true",
            [ZabbixManagedServiceTags.Layer] = layer,
            [ZabbixManagedServiceTags.Class] = target.TargetClass,
            [ZabbixManagedServiceTags.Key] = target.TargetManagedKey,
            [ZabbixManagedServiceTags.Role] = role,
            [ZabbixManagedServiceTags.Visibility] = visibility
        };
        if (!string.IsNullOrWhiteSpace(target.TargetCardId))
        {
            tags[ZabbixManagedServiceTags.CardId] = target.TargetCardId;
        }

        if (!string.IsNullOrWhiteSpace(target.AggregationType))
        {
            tags[ZabbixManagedServiceTags.AggregationType] = target.AggregationType;
        }

        if (!string.IsNullOrWhiteSpace(target.IsCritical))
        {
            tags[ZabbixManagedServiceTags.IsCritical] = target.IsCritical;
        }

        if (!string.IsNullOrWhiteSpace(target.Threshold))
        {
            tags[ZabbixManagedServiceTags.Threshold] = target.Threshold;
        }

        if (!string.IsNullOrWhiteSpace(target.N))
        {
            tags[ZabbixManagedServiceTags.N] = target.N;
        }

        return new ZabbixManagedServiceDefinition
        {
            Layer = layer,
            ManagedKey = target.TargetManagedKey,
            ClassCode = target.TargetClass,
            CardId = target.TargetCardId,
            Name = string.IsNullOrWhiteSpace(target.TargetName) ? target.TargetManagedKey : target.TargetName,
            Description = string.IsNullOrWhiteSpace(target.TargetName) ? target.TargetManagedKey : target.TargetName,
            Algorithm = ServiceAlgorithm(target.AggregationType),
            Role = role,
            Visibility = visibility,
            Relations = target.Relations
                .Select(relation => new ZabbixManagedServiceRelation
                {
                    DomainCode = relation.DomainCode,
                    TargetClassCode = relation.TargetClassCode,
                    TargetLookup = relation.TargetLookup
                })
                .ToArray(),
            ChildManagedKeys = target.SourceLeafManagedKeys,
            Tags = tags
        };
    }

    private static int ServiceAlgorithm(string aggregationType)
    {
        if (string.Equals(aggregationType, "all", StringComparison.OrdinalIgnoreCase))
        {
            return ZabbixServiceAlgorithms.MostCriticalIfAllChildrenHaveProblems;
        }

        if (string.Equals(aggregationType, "none", StringComparison.OrdinalIgnoreCase)
            || string.Equals(aggregationType, "always_ok", StringComparison.OrdinalIgnoreCase))
        {
            return ZabbixServiceAlgorithms.AlwaysOk;
        }

        return ZabbixServiceAlgorithms.MostCriticalOfChildren;
    }

    private async Task<ZabbixManagedServiceApplyResult> EnsureCurrentSourceLeafAsync(
        AggregationCommand command,
        string layer,
        List<string> warnings,
        CancellationToken cancellationToken,
        string parentManagedKey = "")
    {
        if (string.IsNullOrWhiteSpace(command.Source.CardId))
        {
            return new ZabbixManagedServiceApplyResult();
        }

        var leaf = ZabbixManagedServiceMapper.FromSourceBinding(command, layer);
        if (!string.IsNullOrWhiteSpace(parentManagedKey))
        {
            leaf = leaf with { ParentManagedKeys = [parentManagedKey] };
        }
        var hostTagsApplied = 0;
        if (string.IsNullOrWhiteSpace(command.Source.ZabbixHostId))
        {
            warnings.Add(
                $"Для source {command.Source.ClassCode}/{command.Source.CardId} нет zabbix_main_hostid; source leaf и problem tags не применены.");
            return new ZabbixManagedServiceApplyResult();
        }

        try
        {
            hostTagsApplied = await zabbix.EnsureHostTagsAsync(
                command.Source.ZabbixHostId,
                ZabbixManagedServiceMapper.HostTagsForSource(command.Source),
                cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            warnings.Add(
                $"Не удалось обновить tags Zabbix hostid={command.Source.ZabbixHostId} для source {command.Source.ClassCode}/{command.Source.CardId}: {ex.Message}");
        }

        var leafApply = await zabbix.ApplyManagedServiceAsync(leaf, cancellationToken);
        warnings.AddRange(leafApply.Warnings);
        return leafApply with
        {
            SourceLeafServicesApplied = 1,
            HostTagsApplied = hostTagsApplied
        };
    }

    private static string ApplyMessage(ZabbixManagedServiceApplyResult result)
    {
        var action = string.Equals(result.Action, "created", StringComparison.OrdinalIgnoreCase)
            ? "создан"
            : "обновлен";
        var message = $"Managed service Zabbix {action}: serviceid={result.ServiceId}, связей применено={result.RelationsApplied}, problem tags={result.ProblemTagsApplied}.";
        if (result.RelationsDeferred > 0)
        {
            message += $" Отложено связей={result.RelationsDeferred}: {string.Join("; ", result.Warnings)}";
        }

        return message;
    }

    private static bool ShouldCreateManagedServices(string layer, ApplyOptions options)
    {
        return !string.Equals(layer, "suppression", StringComparison.OrdinalIgnoreCase)
            || options.CreateSuppressionServices;
    }

    private static IReadOnlyList<string> SuppressionMembershipWarnings(AggregationCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.Source.CardId)
            || !string.IsNullOrWhiteSpace(command.Source.ZabbixHostId))
        {
            return [];
        }

        return
        [
            $"Для source {command.Source.ClassCode}/{command.Source.CardId} нет zabbix_main_hostid; карточка сохранена как pending membership и не попадет в trigger dependencies."
        ];
    }

    private static string SuppressionMembershipMessage(
        AggregationCommand command,
        ZabbixTargetMembershipSnapshot membership,
        bool removed)
    {
        var action = removed ? "удален" : "обновлен";
        var target = string.IsNullOrWhiteSpace(membership.TargetName)
            ? ZabbixManagedServiceMapper.ManagedKey(command.Target)
            : membership.TargetName;
        return
            $"Suppression membership {action}: target={target}, active sources={membership.SourceCount}, pending={membership.PendingSourceCount}, relations={membership.Relations.Count}. Zabbix Services не создавались; aggregate triggers и trigger dependencies пересчитываются отдельно.";
    }

    private static string SourceMembershipRemovedMessage(
        AggregationCommand command,
        ZabbixMembershipUpdateResult update)
    {
        var affected = update.AffectedTargets
            .Select(item => string.IsNullOrWhiteSpace(item.TargetName) ? item.TargetManagedKey : item.TargetName)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.Ordinal)
            .Take(5)
            .ToArray();
        var targets = affected.Length == 0
            ? "целевых membership не найдено"
            : $"затронуты: {string.Join(", ", affected)}";
        return
            $"Source membership удален для {command.Source.ClassCode}/{command.Source.CardId}: удалено из target={update.RemovedSourceMemberships}; {targets}.";
    }
}

public sealed class ZabbixGraphApplyRequest
{
    public string Layer { get; init; } = "";

    public IReadOnlyList<AggregationCommand> Commands { get; init; } = [];

    public bool DryRun { get; init; }

    public string PublishMode { get; init; } = ZabbixGraphPublishModes.Changes;

    public IReadOnlyList<string> ScopeKeys { get; init; } = [];

    public int ScopeDepth { get; init; }
}

public sealed class ZabbixGraphApplyResult
{
    public string Layer { get; set; } = "";

    public string Topic { get; set; } = "";

    public string Status { get; set; } = "";

    public string Mode { get; set; } = "";

    public string PublishMode { get; set; } = ZabbixGraphPublishModes.Changes;

    public bool DryRun { get; set; }

    public bool SafeApply { get; set; }

    public int CommandsReceived { get; set; }

    public int CommandsSelectedForPublish { get; set; }

    public int CommandsApplied { get; set; }

    public int CommandsErrored { get; set; }

    public int RelationsApplied { get; set; }

    public int RelationsDeferred { get; set; }

    public int SourceLeafServicesApplied { get; set; }

    public int ProblemTagsApplied { get; set; }

    public int HostTagsApplied { get; set; }

    public int AppliedGraphObjectCount { get; set; }

    public ZabbixGraphDiffResult Diff { get; set; } = new();

    public ZabbixGraphScopeSummary Scope { get; set; } = new();

    public string Message { get; set; } = "";

    public List<string> Warnings { get; } = [];

    public List<string> Errors { get; } = [];

    public List<ZabbixCommandApplyResult> CommandResults { get; } = [];

    public ZabbixCommandApplyPerformance Performance { get; set; } = new();

    public DateTimeOffset AppliedAtUtc { get; set; }
}

public static class ZabbixGraphPublishModes
{
    public const string Changes = "changes";

    public const string Full = "full";

    public static string Normalize(string? value)
    {
        return string.Equals(value, Full, StringComparison.OrdinalIgnoreCase)
            ? Full
            : Changes;
    }

    public static bool IsFull(string? value)
    {
        return string.Equals(Normalize(value), Full, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class ZabbixDesiredGraphBuildResult
{
    public IReadOnlyList<ZabbixAppliedGraphObject> Objects { get; init; } = [];

    public IReadOnlyDictionary<string, IReadOnlyList<string>> ObjectKeysByCommandKey { get; init; } =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
}

public sealed class ZabbixGraphDiffResult
{
    public string Layer { get; init; } = "";

    public string PublishMode { get; init; } = ZabbixGraphPublishModes.Changes;

    public int Desired { get; init; }

    public int Applied { get; init; }

    public int Added { get; init; }

    public int Changed { get; init; }

    public int Unchanged { get; init; }

    public int Removed { get; init; }

    public int PublishCandidates { get; init; }

    public IReadOnlyList<ZabbixGraphDiffSample> Samples { get; init; } = [];

    [JsonIgnore]
    public HashSet<string> CandidateObjectKeySet { get; init; } = new(StringComparer.Ordinal);
}

public sealed class ZabbixGraphDiffSample
{
    public string Action { get; init; } = "";

    public string ObjectType { get; init; } = "";

    public string ObjectKey { get; init; } = "";

    public string DisplayName { get; init; } = "";

    public string TargetManagedKey { get; init; } = "";

    public string RuleId { get; init; } = "";

    public string ClassCode { get; init; } = "";
}

public sealed class ZabbixGraphScopeSummary
{
    public bool Enabled { get; init; }

    public string Layer { get; init; } = "";

    public IReadOnlyList<string> RequestedKeys { get; init; } = [];

    public int Depth { get; init; }

    public int MatchedSeedCount { get; init; }

    public int TargetCount { get; init; }

    public int CommandCount { get; init; }

    public IReadOnlyList<string> MatchedTargets { get; init; } = [];

    public IReadOnlyList<string> MissingKeys { get; init; } = [];
}

public sealed class ZabbixGraphScopeResolution
{
    public bool Enabled { get; init; }

    public IReadOnlyList<AggregationCommand> Commands { get; init; } = [];

    public HashSet<string> TargetManagedKeys { get; init; } = new(StringComparer.Ordinal);

    public IReadOnlyList<string> Warnings { get; init; } = [];

    public ZabbixGraphScopeSummary Summary { get; init; } = new();
}

public sealed class ZabbixAppliedGraphObject
{
    public string Layer { get; set; } = "";

    public string ObjectType { get; set; } = "";

    public string ObjectKey { get; set; } = "";

    public string DisplayName { get; set; } = "";

    public string TargetManagedKey { get; set; } = "";

    public string SourceMembershipKey { get; set; } = "";

    public string RuleId { get; set; } = "";

    public string RuleName { get; set; } = "";

    public string ClassCode { get; set; } = "";

    public string CardId { get; set; } = "";

    public string ContentHash { get; set; } = "";

    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public static class ZabbixGraphScopeResolver
{
    public static ZabbixGraphScopeResolution Resolve(
        IReadOnlyList<AggregationCommand> commands,
        string layer,
        IReadOnlyList<string> scopeKeys,
        int scopeDepth)
    {
        var requestedKeys = scopeKeys
            .SelectMany(SplitScopeText)
            .Select(item => item.Trim())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (requestedKeys.Length == 0)
        {
            return new ZabbixGraphScopeResolution
            {
                Commands = commands,
                TargetManagedKeys = commands
                    .Select(command => ZabbixManagedServiceMapper.ManagedKey(command.Target))
                    .Where(key => !string.IsNullOrWhiteSpace(key))
                    .ToHashSet(StringComparer.Ordinal)
            };
        }

        var normalizedLayer = ZabbixApplyPlanner.NormalizeLayer(layer);
        var maxDepth = scopeDepth <= 0 ? int.MaxValue : Math.Min(scopeDepth, 50);
        var commandsByTarget = commands
            .GroupBy(command => ZabbixManagedServiceMapper.ManagedKey(command.Target), StringComparer.Ordinal)
            .Where(group => !string.IsNullOrWhiteSpace(group.Key))
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        var knownTargets = commandsByTarget.Keys.ToHashSet(StringComparer.Ordinal);
        var targetCandidates = commandsByTarget.ToDictionary(
            pair => pair.Key,
            pair => CandidateTexts(pair.Key, pair.Value),
            StringComparer.Ordinal);
        var seeds = targetCandidates
            .Where(pair => requestedKeys.Any(requested => pair.Value.Contains(requested)))
            .Select(pair => pair.Key)
            .ToHashSet(StringComparer.Ordinal);
        var missing = requestedKeys
            .Where(requested => !targetCandidates.Any(pair => pair.Value.Contains(requested)))
            .ToArray();
        var edges = BuildEdges(commands, knownTargets);
        var scopedTargets = string.Equals(normalizedLayer, "service", StringComparison.OrdinalIgnoreCase)
            ? ResolveServiceTargets(seeds, edges, maxDepth)
            : ResolveConnectedTargets(seeds, edges, maxDepth);
        var scopedCommands = commands
            .Where(command => scopedTargets.Contains(ZabbixManagedServiceMapper.ManagedKey(command.Target)))
            .ToArray();
        var warnings = new List<string>();
        if (seeds.Count == 0)
        {
            warnings.Add(
                $"Scope не нашел target-узлы по ключам: {string.Join(", ", requestedKeys.Take(10))}.");
        }
        else if (missing.Length > 0)
        {
            warnings.Add(
                $"Scope применен частично: не найдены ключи {string.Join(", ", missing.Take(10))}.");
        }

        return new ZabbixGraphScopeResolution
        {
            Enabled = true,
            Commands = scopedCommands,
            TargetManagedKeys = scopedTargets,
            Warnings = warnings,
            Summary = new ZabbixGraphScopeSummary
            {
                Enabled = true,
                Layer = normalizedLayer,
                RequestedKeys = requestedKeys,
                Depth = scopeDepth <= 0 ? 0 : maxDepth,
                MatchedSeedCount = seeds.Count,
                TargetCount = scopedTargets.Count,
                CommandCount = scopedCommands.Length,
                MatchedTargets = scopedTargets
                    .OrderBy(item => item, StringComparer.Ordinal)
                    .Take(30)
                    .ToArray(),
                MissingKeys = missing
            }
        };
    }

    private static HashSet<string> ResolveServiceTargets(
        HashSet<string> seeds,
        GraphEdges edges,
        int maxDepth)
    {
        var result = new HashSet<string>(seeds, StringComparer.Ordinal);
        foreach (var seed in seeds)
        {
            Traverse(seed, edges.ParentsByChild, int.MaxValue, result);
            Traverse(seed, edges.ChildrenByParent, maxDepth, result);
        }

        return result;
    }

    private static HashSet<string> ResolveConnectedTargets(
        HashSet<string> seeds,
        GraphEdges edges,
        int maxDepth)
    {
        var result = new HashSet<string>(seeds, StringComparer.Ordinal);
        foreach (var seed in seeds)
        {
            Traverse(seed, edges.Undirected, maxDepth, result);
        }

        return result;
    }

    private static void Traverse(
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

            foreach (var child in next)
            {
                if (!result.Add(child))
                {
                    continue;
                }

                queue.Enqueue((child, depth + 1));
            }
        }
    }

    private static GraphEdges BuildEdges(
        IReadOnlyList<AggregationCommand> commands,
        IReadOnlySet<string> knownTargets)
    {
        var childrenByParent = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var parentsByChild = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var undirected = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var command in commands)
        {
            var sourceKey = ZabbixManagedServiceMapper.ManagedKey(command.Target);
            if (string.IsNullOrWhiteSpace(sourceKey))
            {
                continue;
            }

            foreach (var parent in command.Target.ParentManagedKeys
                .Where(parent => knownTargets.Contains(parent)))
            {
                AddEdge(childrenByParent, parent, sourceKey);
                AddEdge(parentsByChild, sourceKey, parent);
                AddEdge(undirected, parent, sourceKey);
                AddEdge(undirected, sourceKey, parent);
            }

            foreach (var relation in command.Target.Relations)
            {
                var targetKey = ZabbixManagedServiceMapper
                    .LookupCandidates(relation.TargetClassCode, relation.TargetLookup)
                    .FirstOrDefault(knownTargets.Contains);
                if (string.IsNullOrWhiteSpace(targetKey))
                {
                    continue;
                }

                AddEdge(childrenByParent, sourceKey, targetKey);
                AddEdge(parentsByChild, targetKey, sourceKey);
                AddEdge(undirected, sourceKey, targetKey);
                AddEdge(undirected, targetKey, sourceKey);
            }
        }

        return new GraphEdges(childrenByParent, parentsByChild, undirected);
    }

    private static void AddEdge(
        Dictionary<string, HashSet<string>> edges,
        string from,
        string to)
    {
        if (string.Equals(from, to, StringComparison.Ordinal))
        {
            return;
        }

        if (!edges.TryGetValue(from, out var next))
        {
            next = new HashSet<string>(StringComparer.Ordinal);
            edges[from] = next;
        }

        next.Add(to);
    }

    private static HashSet<string> CandidateTexts(
        string managedKey,
        IReadOnlyList<AggregationCommand> commands)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            managedKey
        };
        foreach (var command in commands)
        {
            Add(result, command.RuleId);
            Add(result, command.RuleName);
            Add(result, command.Target.ClassCode);
            Add(result, command.Target.CardId);
            Add(result, command.Target.IdempotencyKey);
            Add(result, command.Target.CardDescription);
            Add(result, FirstAttribute(command.Target.Attributes, "name", "Name", "description", "Description", "Code", "code"));
        }

        return result;
    }

    private static IEnumerable<string> SplitScopeText(string value)
    {
        return value.Split([',', '\n', '\r', '\t', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static void Add(ISet<string> values, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            values.Add(value.Trim());
        }
    }

    private static string FirstAttribute(IReadOnlyDictionary<string, object?> attributes, params string[] names)
    {
        foreach (var name in names)
        {
            foreach (var attribute in attributes)
            {
                if (string.Equals(attribute.Key, name, StringComparison.OrdinalIgnoreCase))
                {
                    return ScalarString(attribute.Value);
                }
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
            System.Text.Json.JsonElement element => element.ValueKind switch
            {
                System.Text.Json.JsonValueKind.String => element.GetString()?.Trim() ?? "",
                System.Text.Json.JsonValueKind.Number => element.GetRawText(),
                System.Text.Json.JsonValueKind.True => "true",
                System.Text.Json.JsonValueKind.False => "false",
                _ => element.GetRawText()
            },
            IFormattable formattable => formattable.ToString(null, System.Globalization.CultureInfo.InvariantCulture) ?? "",
            _ => value.ToString()?.Trim() ?? ""
        };
    }

    private sealed record GraphEdges(
        IReadOnlyDictionary<string, HashSet<string>> ChildrenByParent,
        IReadOnlyDictionary<string, HashSet<string>> ParentsByChild,
        IReadOnlyDictionary<string, HashSet<string>> Undirected);
}

public static class ZabbixDesiredGraphBuilder
{
    public static ZabbixDesiredGraphBuildResult Build(
        IReadOnlyList<AggregationCommand> commands,
        string layer,
        bool createManagedServices)
    {
        var objects = new Dictionary<string, ZabbixAppliedGraphObject>(StringComparer.Ordinal);
        var objectKeysByCommand = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var childSourceLeafKeysByTarget = commands
            .GroupBy(command => ZabbixManagedServiceMapper.ManagedKey(command.Target), StringComparer.Ordinal)
            .Where(group => !string.IsNullOrWhiteSpace(group.Key))
            .ToDictionary(
                group => group.Key,
                group => group
                    .Where(item => !string.IsNullOrWhiteSpace(item.Source.ZabbixHostId)
                        && !string.IsNullOrWhiteSpace(item.Source.CardId))
                    .Select(item => ZabbixManagedServiceMapper.SourceLeafManagedKey(layer, item.Source))
                    .Where(key => !string.IsNullOrWhiteSpace(key))
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(key => key, StringComparer.Ordinal)
                    .ToArray(),
                StringComparer.Ordinal);
        var sourceParentKeys = commands
            .Where(command => !string.IsNullOrWhiteSpace(SourceMembershipKey(command.Source)))
            .GroupBy(command => SourceMembershipKey(command.Source), StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group
                    .Select(command => ZabbixManagedServiceMapper.ManagedKey(command.Target))
                    .Where(key => !string.IsNullOrWhiteSpace(key))
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(key => key, StringComparer.Ordinal)
                    .ToArray(),
                StringComparer.Ordinal);

        foreach (var command in commands)
        {
            var commandKey = CommandKey(command);
            var targetManagedKey = ZabbixManagedServiceMapper.ManagedKey(command.Target);
            var objectKeys = new List<string>();
            var membershipKey = SourceMembershipKey(command.Source);
            if (!string.IsNullOrWhiteSpace(targetManagedKey))
            {
                var targetObjectKey = $"target:{targetManagedKey}";
                objectKeys.Add(targetObjectKey);
                objects[targetObjectKey] = BuildTargetObject(
                    command,
                    layer,
                    targetManagedKey,
                    childSourceLeafKeysByTarget.GetValueOrDefault(targetManagedKey) ?? []);
            }

            if (!string.IsNullOrWhiteSpace(targetManagedKey) && !string.IsNullOrWhiteSpace(membershipKey))
            {
                var membershipObjectKey = $"membership:{targetManagedKey}:{membershipKey}";
                objectKeys.Add(membershipObjectKey);
                objects[membershipObjectKey] = BuildMembershipObject(command, layer, membershipObjectKey, targetManagedKey, membershipKey);
            }

            if (createManagedServices
                && string.Equals(layer, "service", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(command.Source.ZabbixHostId)
                && !string.IsNullOrWhiteSpace(command.Source.CardId))
            {
                var sourceLeafManagedKey = ZabbixManagedServiceMapper.SourceLeafManagedKey(layer, command.Source);
                var sourceLeafObjectKey = $"source_leaf:{sourceLeafManagedKey}";
                objectKeys.Add(sourceLeafObjectKey);
                var parents = sourceParentKeys.GetValueOrDefault(membershipKey) ?? [];
                objects[sourceLeafObjectKey] = BuildSourceLeafObject(command, layer, sourceLeafManagedKey, parents);
            }

            objectKeysByCommand[commandKey] = objectKeys
                .Where(key => !string.IsNullOrWhiteSpace(key))
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }

        return new ZabbixDesiredGraphBuildResult
        {
            Objects = objects.Values
                .OrderBy(item => item.ObjectKey, StringComparer.Ordinal)
                .ToArray(),
            ObjectKeysByCommandKey = objectKeysByCommand
                .ToDictionary(
                    pair => pair.Key,
                    pair => (IReadOnlyList<string>)pair.Value.ToArray(),
                    StringComparer.Ordinal)
        };
    }

    public static string CommandKey(AggregationCommand command)
    {
        return string.Join(
            "\u001f",
            command.CommandType,
            ZabbixManagedServiceMapper.ManagedKey(command.Target),
            command.Source.ClassCode,
            command.Source.CardId,
            command.Source.ZabbixHostId);
    }

    private static ZabbixAppliedGraphObject BuildTargetObject(
        AggregationCommand command,
        string layer,
        string targetManagedKey,
        IReadOnlyList<string> childSourceLeafKeys)
    {
        var definition = ZabbixManagedServiceMapper.FromAggregationCommand(command, layer, childSourceLeafKeys);
        var hash = HashLines(
            "target",
            definition.Layer,
            definition.ManagedKey,
            definition.ClassCode,
            definition.CardId,
            definition.RuleId,
            definition.RuleName,
            definition.Name,
            definition.Description,
            definition.Algorithm.ToString(System.Globalization.CultureInfo.InvariantCulture),
            definition.SortOrder.ToString(System.Globalization.CultureInfo.InvariantCulture),
            definition.Weight.ToString(System.Globalization.CultureInfo.InvariantCulture),
            definition.Role,
            definition.Visibility,
            StablePairs(definition.Tags),
            StableRelations(definition.Relations),
            StableValues(definition.ChildManagedKeys),
            StableValues(definition.ParentManagedKeys));
        return new ZabbixAppliedGraphObject
        {
            Layer = layer,
            ObjectType = "target",
            ObjectKey = $"target:{targetManagedKey}",
            DisplayName = definition.Name,
            TargetManagedKey = targetManagedKey,
            RuleId = command.RuleId,
            RuleName = command.RuleName,
            ClassCode = command.Target.ClassCode,
            CardId = command.Target.CardId,
            ContentHash = hash,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
    }

    private static ZabbixAppliedGraphObject BuildMembershipObject(
        AggregationCommand command,
        string layer,
        string objectKey,
        string targetManagedKey,
        string membershipKey)
    {
        var hash = HashLines(
            "membership",
            layer,
            command.CommandType,
            targetManagedKey,
            membershipKey,
            command.RuleId,
            command.RuleName,
            command.Source.ClassCode,
            command.Source.CardId,
            command.Source.KeyAttribute,
            command.Source.KeyValue,
            command.Source.ZabbixHostId,
            StablePairs(command.Source.Attributes),
            command.Target.ClassCode,
            command.Target.CardId,
            command.Target.IdempotencyKey,
            command.Target.CardDescription,
            StableAttributes(command.Target.Attributes),
            StableTargetRelations(command.Target.Relations),
            StableValues(command.Target.ParentManagedKeys));
        return new ZabbixAppliedGraphObject
        {
            Layer = layer,
            ObjectType = "membership",
            ObjectKey = objectKey,
            DisplayName = string.IsNullOrWhiteSpace(command.RuleName) ? targetManagedKey : command.RuleName,
            TargetManagedKey = targetManagedKey,
            SourceMembershipKey = membershipKey,
            RuleId = command.RuleId,
            RuleName = command.RuleName,
            ClassCode = command.Target.ClassCode,
            CardId = command.Target.CardId,
            ContentHash = hash,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
    }

    private static ZabbixAppliedGraphObject BuildSourceLeafObject(
        AggregationCommand command,
        string layer,
        string sourceLeafManagedKey,
        IReadOnlyList<string> parentManagedKeys)
    {
        var definition = ZabbixManagedServiceMapper.FromSourceBinding(command, layer)
            with { ParentManagedKeys = parentManagedKeys };
        var hash = HashLines(
            "source_leaf",
            definition.Layer,
            definition.ManagedKey,
            definition.ClassCode,
            definition.CardId,
            definition.RuleId,
            definition.RuleName,
            definition.Name,
            definition.Description,
            StablePairs(definition.Tags),
            StableValues(definition.ParentManagedKeys),
            StableProblemTags(definition.ProblemTags));
        return new ZabbixAppliedGraphObject
        {
            Layer = layer,
            ObjectType = "source_leaf",
            ObjectKey = $"source_leaf:{sourceLeafManagedKey}",
            DisplayName = definition.Name,
            TargetManagedKey = parentManagedKeys.FirstOrDefault() ?? "",
            SourceMembershipKey = SourceMembershipKey(command.Source),
            RuleId = command.RuleId,
            RuleName = command.RuleName,
            ClassCode = command.Source.ClassCode,
            CardId = command.Source.CardId,
            ContentHash = hash,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
    }

    private static string SourceMembershipKey(AggregationSourceObject source)
    {
        return string.IsNullOrWhiteSpace(source.ClassCode) || string.IsNullOrWhiteSpace(source.CardId)
            ? ""
            : $"{source.ClassCode}\u001f{source.CardId}";
    }

    private static string StableAttributes(IReadOnlyDictionary<string, object?> attributes)
    {
        return string.Join(
            "\n",
            attributes
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => $"{pair.Key}={ScalarString(pair.Value)}"));
    }

    private static string StablePairs(IReadOnlyDictionary<string, string> attributes)
    {
        return string.Join(
            "\n",
            attributes
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => $"{pair.Key}={pair.Value}"));
    }

    private static string StableTargetRelations(IReadOnlyList<AggregationTargetRelation> relations)
    {
        return string.Join(
            "\n",
            relations
                .Select(relation => $"{relation.DomainCode}|{relation.TargetClassCode}|{relation.TargetLookup}")
                .OrderBy(item => item, StringComparer.Ordinal));
    }

    private static string StableRelations(IReadOnlyList<ZabbixManagedServiceRelation> relations)
    {
        return string.Join(
            "\n",
            relations
                .Select(relation => $"{relation.DomainCode}|{relation.TargetClassCode}|{relation.TargetLookup}")
                .OrderBy(item => item, StringComparer.Ordinal));
    }

    private static string StableProblemTags(IReadOnlyList<ZabbixProblemTag> tags)
    {
        return string.Join(
            "\n",
            tags
                .Select(tag => $"{tag.Tag}|{tag.Operator}|{tag.Value}")
                .OrderBy(item => item, StringComparer.Ordinal));
    }

    private static string StableValues(IEnumerable<string> values)
    {
        return string.Join("\n", values.OrderBy(item => item, StringComparer.Ordinal));
    }

    private static string HashLines(params string[] values)
    {
        var builder = new StringBuilder();
        foreach (var value in values)
        {
            builder.Append(value ?? "");
            builder.Append('\u001e');
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()))).ToLowerInvariant();
    }

    private static string ScalarString(object? value)
    {
        return value switch
        {
            null => "",
            string text => text.Trim(),
            bool boolean => boolean ? "true" : "false",
            System.Text.Json.JsonElement element => element.ValueKind switch
            {
                System.Text.Json.JsonValueKind.String => element.GetString()?.Trim() ?? "",
                System.Text.Json.JsonValueKind.Number => element.GetRawText(),
                System.Text.Json.JsonValueKind.True => "true",
                System.Text.Json.JsonValueKind.False => "false",
                _ => element.GetRawText()
            },
            IFormattable formattable => formattable.ToString(null, System.Globalization.CultureInfo.InvariantCulture) ?? "",
            _ => value.ToString()?.Trim() ?? ""
        };
    }
}
