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
        var result = ZabbixApplyPlanner.Plan(command, layer, topic, options, forceDryRun: false);
        var membership = state.UpdateMembership(command, layer);
        result.Membership = membership;
        try
        {
            if (string.Equals(command.CommandType, AggregationCommandTypes.RemoveMembership, StringComparison.OrdinalIgnoreCase))
            {
                result.Status = "skipped";
                result.Message = options.SafeApply
                    ? "Удаление managed service из Zabbix пропущено: включен safe apply. Объекты не удаляются автоматически."
                    : "Удаление managed service из Zabbix пока не применяется автоматически; требуется отдельная reconcile-операция.";
                result.AppliedAtUtc = DateTimeOffset.UtcNow;
                RequestSuppressionTriggerDependencyReconcile(command, layer);
                return result;
            }

            var warnings = new List<string>();
            var sourceLeafApply = await EnsureCurrentSourceLeafAsync(command, layer, warnings, cancellationToken);
            var definition = ZabbixManagedServiceMapper.FromAggregationCommand(
                command,
                layer,
                membership.SourceLeafManagedKeys);
            var apply = await zabbix.ApplyManagedServiceAsync(definition, cancellationToken);
            result.Status = apply.RelationsDeferred > 0 || warnings.Count > 0 ? "partial" : "applied";
            result.Message = ApplyMessage(apply);
            result.ZabbixServiceId = apply.ServiceId;
            result.ZabbixAction = apply.Action;
            result.RelationsApplied = apply.RelationsApplied;
            result.RelationsDeferred = apply.RelationsDeferred;
            result.SourceLeafServicesApplied = sourceLeafApply.SourceLeafServicesApplied + apply.SourceLeafServicesApplied;
            result.ProblemTagsApplied = sourceLeafApply.ProblemTagsApplied + apply.ProblemTagsApplied;
            result.HostTagsApplied = sourceLeafApply.HostTagsApplied + apply.HostTagsApplied;
            result.Warnings = warnings.Concat(apply.Warnings).ToArray();
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

        triggerDependencyScheduler.Request(
            $"membership {command.Source.ClassCode}/{command.Source.CardId} -> {command.Target.ClassCode}/{command.Target.CardId}");
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
}
