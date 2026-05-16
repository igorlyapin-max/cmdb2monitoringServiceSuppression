using System.Diagnostics;
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
        CancellationToken cancellationToken)
    {
        var dryRun = forceDryRun || string.Equals(options.Mode, "dry-run", StringComparison.OrdinalIgnoreCase);
        var result = new ZabbixGraphApplyResult
        {
            Layer = layer,
            Topic = topic,
            Mode = dryRun ? "dry-run" : options.Mode,
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

        if (dryRun)
        {
            foreach (var command in normalizedCommands)
            {
                result.CommandResults.Add(ZabbixApplyPlanner.Plan(command, layer, topic, options, forceDryRun));
            }

            result.CommandsApplied = result.CommandResults.Count(item =>
                item.Status.Equals("dry-run", StringComparison.OrdinalIgnoreCase));
            result.Status = "dry-run";
            result.Message = "Граф Zabbix проверен без публикации изменений.";
            return result;
        }

        if (!ShouldCreateManagedServices(layer, options)
            || normalizedCommands.Any(command => !command.CommandType.Equals(AggregationCommandTypes.EnsureMembership, StringComparison.OrdinalIgnoreCase)))
        {
            return await ApplyGraphSequentiallyAsync(normalizedCommands, layer, topic, options, result, cancellationToken);
        }

        if (!string.Equals(layer, "service", StringComparison.OrdinalIgnoreCase))
        {
            return await ApplyGraphSequentiallyAsync(normalizedCommands, layer, topic, options, result, cancellationToken);
        }

        using var apiStatsScope = ZabbixClient.BeginApiCallStatsScope();
        var totalWatch = Stopwatch.StartNew();
        try
        {
            var targetSnapshots = new Dictionary<string, ZabbixTargetMembershipSnapshot>(StringComparer.Ordinal);
            var targetCommands = new Dictionary<string, AggregationCommand>(StringComparer.Ordinal);
            var desiredDefinitions = new Dictionary<string, ZabbixManagedServiceDefinition>(StringComparer.Ordinal);
            var commandResults = new List<ZabbixCommandApplyResult>();
            foreach (var command in normalizedCommands)
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
            foreach (var command in normalizedCommands
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
            tags["cmdb2monitoring:aggregation_type"] = target.AggregationType;
        }

        if (!string.IsNullOrWhiteSpace(target.Threshold))
        {
            tags["cmdb2monitoring:threshold"] = target.Threshold;
        }

        if (!string.IsNullOrWhiteSpace(target.N))
        {
            tags["cmdb2monitoring:n"] = target.N;
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
}

public sealed class ZabbixGraphApplyResult
{
    public string Layer { get; set; } = "";

    public string Topic { get; set; } = "";

    public string Status { get; set; } = "";

    public string Mode { get; set; } = "";

    public bool DryRun { get; set; }

    public bool SafeApply { get; set; }

    public int CommandsReceived { get; set; }

    public int CommandsApplied { get; set; }

    public int CommandsErrored { get; set; }

    public int RelationsApplied { get; set; }

    public int RelationsDeferred { get; set; }

    public int SourceLeafServicesApplied { get; set; }

    public int ProblemTagsApplied { get; set; }

    public int HostTagsApplied { get; set; }

    public string Message { get; set; } = "";

    public List<string> Warnings { get; } = [];

    public List<string> Errors { get; } = [];

    public List<ZabbixCommandApplyResult> CommandResults { get; } = [];

    public ZabbixCommandApplyPerformance Performance { get; set; } = new();

    public DateTimeOffset AppliedAtUtc { get; set; }
}
