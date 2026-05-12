using Cmdb2MonitoringServiceSuppression.Shared.Aggregation;
using Cmdb2MonitoringServiceSuppression.Shared.Configuration;
using Cmdb2MonitoringServiceSuppression.Shared.Integrations;

public sealed class ZabbixAggregationApplier(
    ZabbixClient zabbix,
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
        try
        {
            if (string.Equals(command.CommandType, AggregationCommandTypes.RemoveMembership, StringComparison.OrdinalIgnoreCase))
            {
                result.Status = "skipped";
                result.Message = options.SafeApply
                    ? "Удаление managed service из Zabbix пропущено: включен safe apply. Объекты не удаляются автоматически."
                    : "Удаление managed service из Zabbix пока не применяется автоматически; требуется отдельная reconcile-операция.";
                result.AppliedAtUtc = DateTimeOffset.UtcNow;
                return result;
            }

            var definition = ZabbixManagedServiceMapper.FromAggregationCommand(command, layer);
            var apply = await zabbix.ApplyManagedServiceAsync(definition, cancellationToken);
            result.Status = apply.RelationsDeferred > 0 ? "partial" : "applied";
            result.Message = ApplyMessage(apply);
            result.ZabbixServiceId = apply.ServiceId;
            result.ZabbixAction = apply.Action;
            result.RelationsApplied = apply.RelationsApplied;
            result.RelationsDeferred = apply.RelationsDeferred;
            result.Warnings = apply.Warnings;
            result.AppliedAtUtc = DateTimeOffset.UtcNow;
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

    private static string ApplyMessage(ZabbixManagedServiceApplyResult result)
    {
        var action = string.Equals(result.Action, "created", StringComparison.OrdinalIgnoreCase)
            ? "создан"
            : "обновлен";
        var message = $"Managed service Zabbix {action}: serviceid={result.ServiceId}, связей применено={result.RelationsApplied}.";
        if (result.RelationsDeferred > 0)
        {
            message += $" Отложено связей={result.RelationsDeferred}: {string.Join("; ", result.Warnings)}";
        }

        return message;
    }
}
