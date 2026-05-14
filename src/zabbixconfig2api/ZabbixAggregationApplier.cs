using Cmdb2MonitoringServiceSuppression.Shared.Aggregation;
using Cmdb2MonitoringServiceSuppression.Shared.Configuration;
using Cmdb2MonitoringServiceSuppression.Shared.Integrations;

public sealed class ZabbixAggregationApplier(
    ZabbixClient zabbix,
    ZabbixApplyStateStore state,
    ZabbixTriggerDependencyReconcileScheduler triggerDependencyScheduler,
    ILogger<ZabbixAggregationApplier> logger)
{
    public async Task<ZabbixCommandApplyResult> ApplyAsync(
        AggregationCommand command,
        string layer,
        string topic,
        ApplyOptions options,
        CancellationToken cancellationToken)
    {
        var createManagedServices = ShouldCreateManagedServices(layer, options);
        var result = ZabbixApplyPlanner.Plan(command, layer, topic, options, forceDryRun: false);
        var membershipUpdate = state.UpdateMembership(command, layer, createManagedServices);
        var membership = membershipUpdate.Current;
        result.Membership = membership;
        try
        {
            if (string.Equals(command.CommandType, AggregationCommandTypes.RemoveSourceMembership, StringComparison.OrdinalIgnoreCase))
            {
                var removalWarnings = new List<string>();
                var removalAffectedApply = createManagedServices
                    ? await ApplyAffectedMembershipTargetsAsync(
                        membershipUpdate.AffectedTargets,
                        layer,
                        removalWarnings,
                        cancellationToken)
                    : new ZabbixManagedServiceApplyResult();
                result.Status = removalAffectedApply.RelationsDeferred > 0 || removalWarnings.Count > 0 ? "partial" : "applied";
                result.ZabbixAction = "source_membership_removed";
                result.Message = SourceMembershipRemovedMessage(command, membershipUpdate);
                result.RelationsApplied = removalAffectedApply.RelationsApplied;
                result.RelationsDeferred = removalAffectedApply.RelationsDeferred;
                result.Warnings = removalWarnings.Concat(removalAffectedApply.Warnings).ToArray();
                result.AppliedAtUtc = DateTimeOffset.UtcNow;
                RequestSuppressionTriggerDependencyReconcile(command, layer);
                return result;
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
                return result;
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
                return result;
            }

            var warnings = new List<string>();
            var affectedApply = await ApplyAffectedMembershipTargetsAsync(
                membershipUpdate.AffectedTargets
                    .Where(item => !item.TargetManagedKey.Equals(membership.TargetManagedKey, StringComparison.Ordinal))
                    .ToArray(),
                layer,
                warnings,
                cancellationToken);
            var sourceLeafApply = await EnsureCurrentSourceLeafAsync(command, layer, warnings, cancellationToken);
            var definition = ZabbixManagedServiceMapper.FromAggregationCommand(
                command,
                layer,
                membership.SourceLeafManagedKeys);
            var apply = await zabbix.ApplyManagedServiceAsync(definition, cancellationToken);
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
            return result;
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
            return result;
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
        var tags = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [ZabbixManagedServiceTags.Managed] = "true",
            [ZabbixManagedServiceTags.Layer] = layer,
            [ZabbixManagedServiceTags.Class] = target.TargetClass,
            [ZabbixManagedServiceTags.Key] = target.TargetManagedKey
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
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Source.CardId))
        {
            return new ZabbixManagedServiceApplyResult();
        }

        var leaf = ZabbixManagedServiceMapper.FromSourceBinding(command, layer);
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
