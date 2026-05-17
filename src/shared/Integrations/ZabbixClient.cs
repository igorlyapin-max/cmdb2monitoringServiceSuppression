using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cmdb2MonitoringServiceSuppression.Shared.Configuration;
using Microsoft.Extensions.Options;

namespace Cmdb2MonitoringServiceSuppression.Shared.Integrations;

public sealed class ZabbixClient(HttpClient httpClient, IOptionsMonitor<ZabbixOptions> options)
{
    public const int DefaultTriggerGetBatchSize = 25;
    private static readonly ConcurrentDictionary<string, string> LoginTokenCache = new(StringComparer.Ordinal);
    private static readonly AsyncLocal<ZabbixApiCallStats?> CurrentCallStats = new();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private string? loginToken;
    private int requestId = 10;

    public static ZabbixApiCallStatsScope BeginApiCallStatsScope()
    {
        var previous = CurrentCallStats.Value;
        var scope = new ZabbixApiCallStatsScope(previous);
        CurrentCallStats.Value = scope.Stats;
        return scope;
    }

    public async Task<IntegrationCheckResult> CheckConnectionAsync(CancellationToken cancellationToken)
    {
        var endpoint = options.CurrentValue.ApiEndpoint;
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return Failed(endpoint, "Zabbix API endpoint is not configured.");
        }

        try
        {
            var version = await GetApiVersionAsync(cancellationToken);
            if (!string.Equals(options.CurrentValue.AuthMode, "None", StringComparison.OrdinalIgnoreCase))
            {
                await EnsureAuthenticatedAsync(cancellationToken);
            }

            return new IntegrationCheckResult
            {
                System = "Zabbix",
                Endpoint = endpoint,
                Success = true,
                Version = version,
                Summary = $"Zabbix JSON-RPC API is reachable; version: {version}."
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            return Failed(endpoint, ex.Message);
        }
    }

    public async Task<ZabbixSlaInfo?> FindSlaByNameAsync(
        string name,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var parameters = new JsonObject
        {
            ["output"] = new JsonArray("slaid", "name", "period", "slo", "timezone", "status", "description"),
            ["selectServiceTags"] = "extend",
            ["selectSchedule"] = "extend",
            ["selectExcludedDowntimes"] = "extend",
            ["filter"] = new JsonObject
            {
                ["name"] = new JsonArray(name)
            },
            ["limit"] = 2
        };

        var response = await SendZabbixMethodAsync("sla.get", parameters, cancellationToken);
        var slas = ReadResultArray(response, "sla.get")
            .OfType<JsonObject>()
            .Select(ReadSla)
            .Where(sla => !string.IsNullOrWhiteSpace(sla.SlaId))
            .ToArray();

        return slas.Length switch
        {
            0 => null,
            1 => slas[0],
            _ => throw new InvalidOperationException(
                $"Zabbix contains duplicated SLA named '{name}': {string.Join(", ", slas.Select(sla => sla.SlaId))}.")
        };
    }

    public async Task<ZabbixSlaApplyResult> ApplySlaAsync(
        ZabbixSlaDefinition definition,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(definition.Name))
        {
            throw new InvalidOperationException("Zabbix SLA name is empty.");
        }

        if (definition.ServiceTags.Count == 0)
        {
            throw new InvalidOperationException($"Zabbix SLA '{definition.Name}' has no service_tags.");
        }

        if (definition.Schedule.Count == 0)
        {
            throw new InvalidOperationException($"Zabbix SLA '{definition.Name}' has no schedule.");
        }

        var existing = await FindSlaByNameAsync(definition.Name, cancellationToken);
        var excludedDowntimes = MergeExcludedDowntimes(definition, existing);
        var payload = BuildSlaPayload(definition, excludedDowntimes);
        string action;
        string slaId;
        if (existing is null)
        {
            var create = await SendZabbixMethodAsync("sla.create", payload, cancellationToken);
            slaId = ReadFirstId(create, "slaids", "sla.create");
            action = "created";
        }
        else
        {
            slaId = existing.SlaId;
            payload["slaid"] = slaId;
            await SendZabbixMethodAsync("sla.update", payload, cancellationToken);
            action = "updated";
        }

        return new ZabbixSlaApplyResult
        {
            SlaId = slaId,
            Action = action,
            ManagedExcludedDowntimes = definition.ExcludedDowntimes.Count,
            PreservedManualExcludedDowntimes = excludedDowntimes.Count - definition.ExcludedDowntimes.Count
        };
    }

    public async Task<ZabbixManagedServiceApplyResult> ApplyManagedServiceAsync(
        ZabbixManagedServiceDefinition definition,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(definition.ManagedKey))
        {
            throw new InvalidOperationException("Zabbix managed service key is empty.");
        }

        if (string.IsNullOrWhiteSpace(definition.Name))
        {
            throw new InvalidOperationException($"Zabbix managed service name is empty for key '{definition.ManagedKey}'.");
        }

        var existing = await FindManagedServiceByKeyAsync(definition.Layer, definition.ManagedKey, cancellationToken);
        var warnings = new List<string>();
        var childIds = new List<string>();
        var parentIds = new List<string>();

        foreach (var relation in definition.Relations)
        {
            var child = await FindManagedServiceByReferenceAsync(
                definition.Layer,
                relation.TargetClassCode,
                relation.TargetLookup,
                cancellationToken);

            if (child is null)
            {
                warnings.Add(
                    $"Связь {relation.DomainCode}: целевой managed service не найден: {relation.TargetClassCode}/{relation.TargetLookup}.");
                continue;
            }

            if (existing is not null && string.Equals(existing.ServiceId, child.ServiceId, StringComparison.Ordinal))
            {
                warnings.Add(
                    $"Связь {relation.DomainCode}: пропущена самоссылка на serviceid {child.ServiceId}.");
                continue;
            }

            childIds.Add(child.ServiceId);
        }

        foreach (var managedKey in definition.ParentManagedKeys
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.Ordinal))
        {
            var parent = await FindManagedServiceByKeyAsync(definition.Layer, managedKey, cancellationToken);
            if (parent is null)
            {
                warnings.Add($"Parent managed service не найден: {managedKey}.");
                continue;
            }

            if (existing is not null && string.Equals(existing.ServiceId, parent.ServiceId, StringComparison.Ordinal))
            {
                warnings.Add($"Parent managed service {managedKey}: пропущена самоссылка на serviceid {parent.ServiceId}.");
                continue;
            }

            parentIds.Add(parent.ServiceId);
        }

        foreach (var managedKey in definition.ChildManagedKeys)
        {
            var child = await FindManagedServiceByKeyAsync(definition.Layer, managedKey, cancellationToken);
            if (child is null)
            {
                warnings.Add($"Source leaf managed service не найден: {managedKey}.");
                continue;
            }

            if (existing is not null && string.Equals(existing.ServiceId, child.ServiceId, StringComparison.Ordinal))
            {
                warnings.Add($"Source leaf managed service {managedKey}: пропущена самоссылка на serviceid {child.ServiceId}.");
                continue;
            }

            childIds.Add(child.ServiceId);
        }

        childIds = childIds
            .Distinct(StringComparer.Ordinal)
            .ToList();
        parentIds = parentIds
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var expectedChildCount = definition.Relations.Count + definition.ChildManagedKeys.Count;
        var expectedParentCount = definition.ParentManagedKeys.Count;
        var replaceChildren = warnings.Count == 0 || childIds.Count > 0 || expectedChildCount == 0;
        var replaceParents = expectedParentCount > 0 && (warnings.Count == 0 || parentIds.Count > 0);
        var serviceId = existing is null
            ? await CreateManagedServiceAsync(definition, childIds, replaceChildren, parentIds, replaceParents, cancellationToken)
            : await UpdateManagedServiceAsync(existing.ServiceId, definition, childIds, replaceChildren, parentIds, replaceParents, cancellationToken);

        return new ZabbixManagedServiceApplyResult
        {
            Success = true,
            Action = existing is null ? "created" : "updated",
            ServiceId = serviceId,
            RelationsApplied = (replaceChildren ? childIds.Count : 0) + (replaceParents ? parentIds.Count : 0),
            RelationsDeferred = warnings.Count,
            ProblemTagsApplied = definition.ProblemTags.Count,
            Warnings = warnings
        };
    }

    public async Task<ZabbixManagedServiceApplyResult> ApplyManagedServiceNodeAsync(
        ZabbixManagedServiceDefinition definition,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(definition.ManagedKey))
        {
            throw new InvalidOperationException("Zabbix managed service key is empty.");
        }

        if (string.IsNullOrWhiteSpace(definition.Name))
        {
            throw new InvalidOperationException($"Zabbix managed service name is empty for key '{definition.ManagedKey}'.");
        }

        var existing = await FindManagedServiceByKeyAsync(definition.Layer, definition.ManagedKey, cancellationToken);
        var warnings = new List<string>();
        var parentIds = new List<string>();
        foreach (var managedKey in definition.ParentManagedKeys
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.Ordinal))
        {
            var parent = await FindManagedServiceByKeyAsync(definition.Layer, managedKey, cancellationToken);
            if (parent is null)
            {
                warnings.Add($"Parent managed service не найден: {managedKey}.");
                continue;
            }

            if (existing is not null && string.Equals(existing.ServiceId, parent.ServiceId, StringComparison.Ordinal))
            {
                warnings.Add($"Parent managed service {managedKey}: пропущена самоссылка на serviceid {parent.ServiceId}.");
                continue;
            }

            parentIds.Add(parent.ServiceId);
        }

        parentIds = parentIds
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var expectedParentCount = definition.ParentManagedKeys.Count;
        var replaceParents = warnings.Count == 0 || parentIds.Count > 0 || expectedParentCount == 0;
        var serviceId = existing is null
            ? await CreateManagedServiceAsync(definition, [], false, parentIds, replaceParents, cancellationToken)
            : await UpdateManagedServiceAsync(existing.ServiceId, definition, [], false, parentIds, replaceParents, cancellationToken);

        return new ZabbixManagedServiceApplyResult
        {
            Success = true,
            Action = existing is null ? "created" : "updated",
            ServiceId = serviceId,
            RelationsApplied = replaceParents ? parentIds.Count : 0,
            RelationsDeferred = warnings.Count,
            ProblemTagsApplied = definition.ProblemTags.Count,
            Warnings = warnings
        };
    }

    public async Task<ZabbixManagedServiceApplyResult> ApplyManagedServiceTagsAsync(
        ZabbixManagedServiceDefinition definition,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(definition.ManagedKey))
        {
            throw new InvalidOperationException("Zabbix managed service key is empty.");
        }

        var existing = await FindManagedServiceByKeyAsync(
            definition.Layer,
            definition.ManagedKey,
            cancellationToken);
        if (existing is null)
        {
            throw new InvalidOperationException(
                $"Managed Zabbix Service for key '{definition.ManagedKey}' was not found. Publish the service topology first.");
        }

        var replaceTagKeys = new HashSet<string>(definition.Tags.Keys, StringComparer.Ordinal);
        var tags = existing.Tags
            .Where(tag => !replaceTagKeys.Contains(tag.Key))
            .ToDictionary(tag => tag.Key, tag => tag.Value, StringComparer.Ordinal);
        foreach (var tag in definition.Tags)
        {
            if (!string.IsNullOrWhiteSpace(tag.Value))
            {
                tags[tag.Key] = tag.Value;
            }
        }

        var payload = new JsonObject
        {
            ["serviceid"] = existing.ServiceId,
            ["tags"] = ServiceTags(tags)
        };
        await SendZabbixMethodAsync("service.update", payload, cancellationToken);
        return new ZabbixManagedServiceApplyResult
        {
            Success = true,
            Action = "tagged",
            ServiceId = existing.ServiceId
        };
    }

    public async Task<int> EnsureHostTagsAsync(
        string hostId,
        IReadOnlyDictionary<string, string> tags,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(hostId) || tags.Count == 0)
        {
            return 0;
        }

        var host = await GetHostByIdAsync(hostId, cancellationToken)
            ?? throw new InvalidOperationException($"Zabbix hostid '{hostId}' was not found.");
        var managedTagKeys = new HashSet<string>(tags.Keys, StringComparer.Ordinal);
        var mergedTags = host.Tags
            .Where(tag => !managedTagKeys.Contains(tag.Tag))
            .Concat(tags
                .Where(tag => !string.IsNullOrWhiteSpace(tag.Value))
                .Select(tag => new ZabbixServiceTag(tag.Key, tag.Value)))
            .ToArray();

        var payload = new JsonObject
        {
            ["hostid"] = host.HostId,
            ["tags"] = ServiceTags(mergedTags)
        };
        await SendZabbixMethodAsync("host.update", payload, cancellationToken);
        return tags.Count;
    }

    public async Task<IReadOnlyList<ZabbixTriggerInfo>> GetTriggersByHostIdsAsync(
        IReadOnlyList<string> hostIds,
        bool includeDisabled,
        CancellationToken cancellationToken)
    {
        return await GetTriggersByHostIdsAsync(hostIds, includeDisabled, DefaultTriggerGetBatchSize, cancellationToken);
    }

    public async Task<IReadOnlyList<ZabbixTriggerInfo>> GetTriggersByHostIdsAsync(
        IReadOnlyList<string> hostIds,
        bool includeDisabled,
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

        return await GetTriggersByLookupBatchesAsync(ids, "hostids", includeDisabled, batchSize, cancellationToken);
    }

    public async Task<IReadOnlyList<ZabbixHostInfo>> GetHostsByIdsAsync(
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

        var result = new List<ZabbixHostInfo>();
        var effectiveBatchSize = Math.Max(1, batchSize);
        foreach (var batch in ids.Chunk(effectiveBatchSize))
        {
            var parameters = new JsonObject
            {
                ["output"] = new JsonArray("hostid", "host", "name"),
                ["hostids"] = StringArray(batch),
                ["selectTags"] = new JsonArray("tag", "value")
            };

            var response = await SendZabbixMethodAsync("host.get", parameters, cancellationToken);
            var hosts = response.TryGetPropertyValue("result", out var resultNode) && resultNode is JsonArray array
                ? array
                : throw new InvalidOperationException("Zabbix host.get did not return an array.");

            result.AddRange(hosts
                .OfType<JsonObject>()
                .Select(ReadHost)
                .Where(host => !string.IsNullOrWhiteSpace(host.HostId)));
        }

        return result
            .DistinctBy(host => host.HostId, StringComparer.Ordinal)
            .ToArray();
    }

    public async Task<IReadOnlyList<ZabbixTriggerInfo>> GetTriggersByIdsAsync(
        IReadOnlyList<string> triggerIds,
        bool includeDisabled,
        CancellationToken cancellationToken)
    {
        return await GetTriggersByIdsAsync(triggerIds, includeDisabled, DefaultTriggerGetBatchSize, cancellationToken);
    }

    public async Task<IReadOnlyList<ZabbixTriggerInfo>> GetTriggersByIdsAsync(
        IReadOnlyList<string> triggerIds,
        bool includeDisabled,
        int batchSize,
        CancellationToken cancellationToken)
    {
        var ids = triggerIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (ids.Length == 0)
        {
            return [];
        }

        return await GetTriggersByLookupBatchesAsync(ids, "triggerids", includeDisabled, batchSize, cancellationToken);
    }

    public async Task<int> UpdateTriggerDependenciesAsync(
        string triggerId,
        IReadOnlyList<string> dependencyTriggerIds,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(triggerId))
        {
            throw new InvalidOperationException("Zabbix triggerid is empty.");
        }

        var dependencies = dependencyTriggerIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var payload = new JsonObject
        {
            ["triggerid"] = triggerId,
            ["dependencies"] = TriggerReferences(dependencies)
        };

        await SendZabbixMethodAsync("trigger.update", payload, cancellationToken);
        return dependencies.Length;
    }

    public async Task<ZabbixSuppressionAggregateApplyResult> ApplySuppressionAggregateAsync(
        ZabbixSuppressionAggregateDefinition definition,
        CancellationToken cancellationToken)
    {
        var result = await ApplySuppressionAggregateHostItemAsync(definition, cancellationToken);
        var trigger = await ApplySuppressionAggregateTriggerAsync(
            definition,
            result.HostId,
            cancellationToken);

        return new ZabbixSuppressionAggregateApplyResult
        {
            HostId = result.HostId,
            ItemId = result.ItemId,
            TriggerId = trigger.TriggerId,
            HostAction = result.HostAction,
            ItemAction = result.ItemAction,
            TriggerAction = trigger.TriggerAction
        };
    }

    public async Task<ZabbixSuppressionAggregateApplyResult> ApplySuppressionAggregateHostItemAsync(
        ZabbixSuppressionAggregateDefinition definition,
        CancellationToken cancellationToken)
    {
        EnsureSuppressionAggregateDefinitionComplete(definition, requireTriggerExpression: false);

        var tags = AggregateTags(definition);
        var group = await EnsureHostGroupAsync(definition.HostGroupName, cancellationToken);
        var host = await EnsureAggregateHostAsync(definition, group.Id, cancellationToken);
        var item = await EnsureAggregateItemAsync(definition, host.Id, tags, cancellationToken);

        return new ZabbixSuppressionAggregateApplyResult
        {
            HostId = host.Id,
            ItemId = item.Id,
            HostAction = host.Action,
            ItemAction = item.Action
        };
    }

    public async Task<ZabbixSuppressionAggregateApplyResult> ApplySuppressionAggregateTriggerAsync(
        ZabbixSuppressionAggregateDefinition definition,
        string hostId,
        CancellationToken cancellationToken)
    {
        EnsureSuppressionAggregateDefinitionComplete(definition, requireTriggerExpression: true);
        if (string.IsNullOrWhiteSpace(hostId))
        {
            throw new InvalidOperationException(
                $"Zabbix suppression aggregate host id is empty for '{definition.TargetManagedKey}'.");
        }

        var trigger = await EnsureAggregateTriggerAsync(definition, hostId, AggregateTags(definition), cancellationToken);
        return new ZabbixSuppressionAggregateApplyResult
        {
            HostId = hostId,
            TriggerId = trigger.Id,
            TriggerAction = trigger.Action
        };
    }

    private static void EnsureSuppressionAggregateDefinitionComplete(
        ZabbixSuppressionAggregateDefinition definition,
        bool requireTriggerExpression)
    {
        if (string.IsNullOrWhiteSpace(definition.TargetManagedKey))
        {
            throw new InvalidOperationException("Zabbix suppression aggregate target key is empty.");
        }

        if (string.IsNullOrWhiteSpace(definition.HostGroupName)
            || string.IsNullOrWhiteSpace(definition.HostName)
            || string.IsNullOrWhiteSpace(definition.ItemKey)
            || string.IsNullOrWhiteSpace(definition.TriggerName))
        {
            throw new InvalidOperationException(
                $"Zabbix suppression aggregate definition is incomplete for '{definition.TargetManagedKey}'.");
        }

        if (string.IsNullOrWhiteSpace(definition.CalculationFormula)
            || (requireTriggerExpression && string.IsNullOrWhiteSpace(definition.TriggerExpression)))
        {
            throw new InvalidOperationException(
                $"Zabbix suppression aggregate formula is empty for '{definition.TargetManagedKey}'.");
        }
    }

    public async Task<ZabbixServiceInfo?> FindManagedServiceByKeyAsync(
        string layer,
        string managedKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(managedKey))
        {
            return null;
        }

        var services = await GetServicesByTagsAsync(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [ZabbixManagedServiceTags.Managed] = "true",
                [ZabbixManagedServiceTags.Layer] = layer,
                [ZabbixManagedServiceTags.Key] = managedKey
            },
            cancellationToken);

        return SingleManagedServiceOrDefault(services, $"key '{managedKey}'");
    }

    public async Task<IReadOnlyList<ZabbixServiceInfo>> ListManagedServicesByLayerAsync(
        string layer,
        int limit,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(layer))
        {
            return [];
        }

        return await GetServicesByTagsAsync(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [ZabbixManagedServiceTags.Managed] = "true",
                [ZabbixManagedServiceTags.Layer] = layer
            },
            cancellationToken,
            Math.Clamp(limit, 1, 10000));
    }

    public async Task<ZabbixManagedServiceDeleteResult> DeleteManagedServiceByKeyAsync(
        string layer,
        string managedKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(managedKey))
        {
            return new ZabbixManagedServiceDeleteResult
            {
                ManagedKey = managedKey,
                Action = "skipped",
                Message = "managed key is empty"
            };
        }

        var service = await FindManagedServiceByKeyAsync(layer, managedKey, cancellationToken);
        if (service is null)
        {
            return new ZabbixManagedServiceDeleteResult
            {
                ManagedKey = managedKey,
                Action = "skipped",
                Message = "managed service not found"
            };
        }

        if (service.Tags.TryGetValue(ZabbixManagedServiceTags.SourceLeaf, out var sourceLeaf)
            && sourceLeaf.Equals("true", StringComparison.OrdinalIgnoreCase))
        {
            return new ZabbixManagedServiceDeleteResult
            {
                ManagedKey = managedKey,
                ServiceId = service.ServiceId,
                Name = service.Name,
                Action = "skipped",
                Message = "source leaf managed service is not deleted by stale cleanup"
            };
        }

        await SendZabbixMethodAsync(
            "service.delete",
            new JsonArray(service.ServiceId),
            cancellationToken);
        return new ZabbixManagedServiceDeleteResult
        {
            ManagedKey = managedKey,
            ServiceId = service.ServiceId,
            Name = service.Name,
            Action = "deleted"
        };
    }

    public async Task<IReadOnlyList<ZabbixSuppressionAggregateItemInfo>> GetSuppressionAggregateItemsAsync(
        string aggregateHostName,
        IReadOnlyList<string> itemKeys,
        CancellationToken cancellationToken)
    {
        var keys = itemKeys
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (string.IsNullOrWhiteSpace(aggregateHostName) || keys.Length == 0)
        {
            return [];
        }

        var parameters = new JsonObject
        {
            ["host"] = aggregateHostName,
            ["output"] = new JsonArray("itemid", "name", "key_", "status", "state", "error", "lastvalue", "lastclock"),
            ["filter"] = new JsonObject
            {
                ["key_"] = StringArray(keys)
            },
            ["limit"] = keys.Length + 1
        };
        var response = await SendZabbixMethodAsync("item.get", parameters, cancellationToken);
        return ReadResultArray(response, "item.get")
            .OfType<JsonObject>()
            .Select(ReadSuppressionAggregateItem)
            .Where(item => !string.IsNullOrWhiteSpace(item.Key))
            .ToArray();
    }

    public async Task<ZabbixServiceInfo?> FindManagedServiceByReferenceAsync(
        string layer,
        string classCode,
        string lookup,
        CancellationToken cancellationToken)
    {
        foreach (var candidate in ZabbixManagedServiceMapper.LookupCandidates(classCode, lookup))
        {
            var service = await FindManagedServiceByKeyAsync(layer, candidate, cancellationToken);
            if (service is not null)
            {
                return service;
            }
        }

        if (string.IsNullOrWhiteSpace(classCode) || string.IsNullOrWhiteSpace(lookup))
        {
            return null;
        }

        var services = await GetServicesByTagsAsync(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [ZabbixManagedServiceTags.Managed] = "true",
                [ZabbixManagedServiceTags.Layer] = layer,
                [ZabbixManagedServiceTags.Class] = classCode,
                [ZabbixManagedServiceTags.CardId] = lookup
            },
            cancellationToken);

        return SingleManagedServiceOrDefault(services, $"{classCode}/{lookup}");
    }

    private sealed record ZabbixObjectAction(string Id, string Action);

    private async Task<ZabbixObjectAction> EnsureHostGroupAsync(
        string name,
        CancellationToken cancellationToken)
    {
        var parameters = new JsonObject
        {
            ["output"] = new JsonArray("groupid", "name"),
            ["filter"] = new JsonObject
            {
                ["name"] = new JsonArray(name)
            },
            ["limit"] = 2
        };
        var response = await SendZabbixMethodAsync("hostgroup.get", parameters, cancellationToken);
        var existing = ReadResultArray(response, "hostgroup.get");
        if (existing.Count > 1)
        {
            throw new InvalidOperationException($"Zabbix contains duplicated host groups named '{name}'.");
        }

        if (existing.Count == 1 && existing[0] is JsonObject group)
        {
            return new ZabbixObjectAction(JsonString(group["groupid"]), "existing");
        }

        var create = await SendZabbixMethodAsync(
            "hostgroup.create",
            new JsonObject { ["name"] = name },
            cancellationToken);
        return new ZabbixObjectAction(ReadFirstId(create, "groupids", "hostgroup.create"), "created");
    }

    private async Task<ZabbixObjectAction> EnsureAggregateHostAsync(
        ZabbixSuppressionAggregateDefinition definition,
        string groupId,
        CancellationToken cancellationToken)
    {
        var parameters = new JsonObject
        {
            ["output"] = new JsonArray("hostid", "host", "name"),
            ["filter"] = new JsonObject
            {
                ["host"] = new JsonArray(definition.HostName)
            },
            ["limit"] = 2
        };
        var response = await SendZabbixMethodAsync("host.get", parameters, cancellationToken);
        var existing = ReadResultArray(response, "host.get");
        var payload = new JsonObject
        {
            ["host"] = definition.HostName,
            ["name"] = string.IsNullOrWhiteSpace(definition.HostVisibleName)
                ? definition.HostName
                : definition.HostVisibleName,
            ["groups"] = new JsonArray(new JsonObject { ["groupid"] = groupId }),
            ["status"] = 0,
            ["tags"] = ServiceTags(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [ZabbixManagedServiceTags.Managed] = "true",
                [ZabbixManagedServiceTags.Layer] = definition.Layer,
                [ZabbixManagedServiceTags.Aggregate] = "true",
                [ZabbixManagedServiceTags.AggregateKind] = "suppression_host"
            })
        };

        if (existing.Count > 1)
        {
            throw new InvalidOperationException($"Zabbix contains duplicated aggregate hosts named '{definition.HostName}'.");
        }

        if (existing.Count == 1 && existing[0] is JsonObject host)
        {
            var hostId = JsonString(host["hostid"]);
            payload["hostid"] = hostId;
            await SendZabbixMethodAsync("host.update", payload, cancellationToken);
            return new ZabbixObjectAction(hostId, "updated");
        }

        var create = await SendZabbixMethodAsync("host.create", payload, cancellationToken);
        return new ZabbixObjectAction(ReadFirstId(create, "hostids", "host.create"), "created");
    }

    private async Task<ZabbixObjectAction> EnsureAggregateItemAsync(
        ZabbixSuppressionAggregateDefinition definition,
        string hostId,
        IReadOnlyDictionary<string, string> tags,
        CancellationToken cancellationToken)
    {
        var parameters = new JsonObject
        {
            ["output"] = new JsonArray("itemid", "name", "key_", "type", "value_type", "status"),
            ["hostids"] = new JsonArray(hostId),
            ["filter"] = new JsonObject
            {
                ["key_"] = new JsonArray(definition.ItemKey)
            },
            ["limit"] = 2
        };
        var response = await SendZabbixMethodAsync("item.get", parameters, cancellationToken);
        var existing = ReadResultArray(response, "item.get");
        var payload = new JsonObject
        {
            ["name"] = definition.ItemName,
            ["key_"] = definition.ItemKey,
            ["hostid"] = hostId,
            ["type"] = 15,
            ["value_type"] = 3,
            ["params"] = definition.CalculationFormula,
            ["delay"] = "1m",
            ["status"] = 0,
            ["tags"] = ServiceTags(tags)
        };

        if (existing.Count > 1)
        {
            throw new InvalidOperationException(
                $"Zabbix contains duplicated aggregate items '{definition.ItemKey}' on hostid {hostId}.");
        }

        if (existing.Count == 1 && existing[0] is JsonObject item)
        {
            var itemId = JsonString(item["itemid"]);
            var updatePayload = CloneJsonObject(payload);
            updatePayload.Remove("hostid");
            updatePayload["itemid"] = itemId;
            await SendZabbixMethodAsync("item.update", updatePayload, cancellationToken);
            return new ZabbixObjectAction(itemId, "updated");
        }

        var create = await SendZabbixMethodAsync("item.create", payload, cancellationToken);
        return new ZabbixObjectAction(ReadFirstId(create, "itemids", "item.create"), "created");
    }

    private async Task<ZabbixObjectAction> EnsureAggregateTriggerAsync(
        ZabbixSuppressionAggregateDefinition definition,
        string hostId,
        IReadOnlyDictionary<string, string> tags,
        CancellationToken cancellationToken)
    {
        var parameters = new JsonObject
        {
            ["output"] = new JsonArray("triggerid", "description", "expression", "priority"),
            ["hostids"] = new JsonArray(hostId),
            ["tags"] = TagFilters(tags),
            ["evaltype"] = 0,
            ["limit"] = 2
        };
        var response = await SendZabbixMethodAsync("trigger.get", parameters, cancellationToken);
        var existing = ReadResultArray(response, "trigger.get");
        if (existing.Count == 0)
        {
            existing = await FindAggregateTriggersByNameAsync(definition, hostId, cancellationToken);
        }
        var payload = new JsonObject
        {
            ["description"] = definition.TriggerName,
            ["expression"] = definition.TriggerExpression,
            ["priority"] = definition.TriggerPriority,
            ["tags"] = ServiceTags(tags)
        };

        if (existing.Count > 1)
        {
            throw new InvalidOperationException(
                $"Zabbix contains duplicated aggregate triggers for target '{definition.TargetManagedKey}'.");
        }

        if (existing.Count == 1 && existing[0] is JsonObject trigger)
        {
            var triggerId = JsonString(trigger["triggerid"]);
            payload["triggerid"] = triggerId;
            await SendZabbixMethodAsync("trigger.update", payload, cancellationToken);
            return new ZabbixObjectAction(triggerId, "updated");
        }

        var create = await SendZabbixMethodAsync("trigger.create", payload, cancellationToken);
        return new ZabbixObjectAction(ReadFirstId(create, "triggerids", "trigger.create"), "created");
    }

    private async Task<JsonArray> FindAggregateTriggersByNameAsync(
        ZabbixSuppressionAggregateDefinition definition,
        string hostId,
        CancellationToken cancellationToken)
    {
        var parameters = new JsonObject
        {
            ["output"] = new JsonArray("triggerid", "description", "expression", "priority"),
            ["hostids"] = new JsonArray(hostId),
            ["filter"] = new JsonObject
            {
                ["description"] = new JsonArray(definition.TriggerName)
            },
            ["limit"] = 2
        };
        var response = await SendZabbixMethodAsync("trigger.get", parameters, cancellationToken);
        return ReadResultArray(response, "trigger.get");
    }

    private async Task<IReadOnlyList<ZabbixTriggerInfo>> GetTriggersAsync(
        JsonObject parameters,
        string lookupDescription,
        CancellationToken cancellationToken)
    {
        var response = await SendZabbixMethodAsync("trigger.get", parameters, cancellationToken);
        var result = response.TryGetPropertyValue("result", out var resultNode) && resultNode is JsonArray array
            ? array
            : throw new InvalidOperationException($"Zabbix trigger.get by {lookupDescription} did not return an array.");

        return result
            .OfType<JsonObject>()
            .Select(ReadTrigger)
            .Where(trigger => !string.IsNullOrWhiteSpace(trigger.TriggerId))
            .ToArray();
    }

    private async Task<IReadOnlyList<ZabbixTriggerInfo>> GetTriggersByLookupBatchesAsync(
        IReadOnlyList<string> ids,
        string lookupField,
        bool includeDisabled,
        int batchSize,
        CancellationToken cancellationToken)
    {
        var result = new List<ZabbixTriggerInfo>();
        var batchIndex = 0;
        var effectiveBatchSize = Math.Max(1, batchSize);
        foreach (var batch in ids.Chunk(effectiveBatchSize))
        {
            batchIndex++;
            var parameters = TriggerGetBaseParameters(includeDisabled);
            parameters[lookupField] = StringArray(batch);
            result.AddRange(await GetTriggersAsync(
                parameters,
                $"{lookupField} batch {batchIndex}",
                cancellationToken));
        }

        return result
            .DistinctBy(trigger => trigger.TriggerId, StringComparer.Ordinal)
            .ToArray();
    }

    private async Task<string> CreateManagedServiceAsync(
        ZabbixManagedServiceDefinition definition,
        IReadOnlyList<string> childIds,
        bool includeChildren,
        IReadOnlyList<string> parentIds,
        bool includeParents,
        CancellationToken cancellationToken)
    {
        var service = BuildServicePayload(definition);
        if (includeChildren)
        {
            service["children"] = ServiceReferences(childIds);
        }
        if (includeParents)
        {
            service["parents"] = ServiceReferences(parentIds);
        }

        var response = await SendZabbixMethodAsync("service.create", service, cancellationToken);
        return ReadServiceId(response, "service.create");
    }

    private async Task<string> UpdateManagedServiceAsync(
        string serviceId,
        ZabbixManagedServiceDefinition definition,
        IReadOnlyList<string> childIds,
        bool includeChildren,
        IReadOnlyList<string> parentIds,
        bool includeParents,
        CancellationToken cancellationToken)
    {
        var service = BuildServicePayload(definition);
        service["serviceid"] = serviceId;
        if (includeChildren)
        {
            service["children"] = ServiceReferences(childIds);
        }
        if (includeParents)
        {
            service["parents"] = ServiceReferences(parentIds);
        }

        var response = await SendZabbixMethodAsync("service.update", service, cancellationToken);
        return ReadServiceId(response, "service.update");
    }

    private async Task<IReadOnlyList<ZabbixServiceInfo>> GetServicesByTagsAsync(
        IReadOnlyDictionary<string, string> tags,
        CancellationToken cancellationToken,
        int limit = 2)
    {
        var parameters = new JsonObject
        {
            ["output"] = new JsonArray("serviceid", "name", "algorithm", "sortorder", "description"),
            ["selectTags"] = new JsonArray("tag", "value"),
            ["selectChildren"] = new JsonArray("serviceid", "name"),
            ["selectParents"] = new JsonArray("serviceid", "name"),
            ["evaltype"] = 0,
            ["tags"] = TagFilters(tags),
            ["limit"] = limit
        };

        var response = await SendZabbixMethodAsync("service.get", parameters, cancellationToken);
        var result = response.TryGetPropertyValue("result", out var resultNode) && resultNode is JsonArray array
            ? array
            : throw new InvalidOperationException("Zabbix service.get did not return an array.");

        return result
            .OfType<JsonObject>()
            .Select(ReadService)
            .ToArray();
    }

    private async Task<ZabbixHostInfo?> GetHostByIdAsync(
        string hostId,
        CancellationToken cancellationToken)
    {
        var parameters = new JsonObject
        {
            ["output"] = new JsonArray("hostid", "host", "name"),
            ["hostids"] = new JsonArray(hostId),
            ["selectTags"] = new JsonArray("tag", "value"),
            ["limit"] = 2
        };

        var response = await SendZabbixMethodAsync("host.get", parameters, cancellationToken);
        var result = response.TryGetPropertyValue("result", out var resultNode) && resultNode is JsonArray array
            ? array
            : throw new InvalidOperationException("Zabbix host.get did not return an array.");

        if (result.Count == 0)
        {
            return null;
        }

        if (result.Count > 1)
        {
            throw new InvalidOperationException($"Zabbix host.get returned duplicated hostid '{hostId}'.");
        }

        var host = result[0] as JsonObject
            ?? throw new InvalidOperationException("Zabbix host.get returned an invalid host object.");
        return ReadHost(host);
    }

    private async Task<string> GetApiVersionAsync(CancellationToken cancellationToken)
    {
        var response = await SendJsonRpcAsync(new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = "apiinfo.version",
            ["params"] = new JsonObject(),
            ["id"] = 1
        }, authenticated: false, cancellationToken);

        if (response.TryGetPropertyValue("result", out var result))
        {
            return result?.GetValue<string>() ?? "";
        }

        throw new InvalidOperationException(ReadError(response) ?? "Zabbix apiinfo.version did not return result.");
    }

    private async Task EnsureAuthenticatedAsync(CancellationToken cancellationToken)
    {
        if (string.Equals(options.CurrentValue.AuthMode, "Token", StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(options.CurrentValue.ApiToken))
        {
            throw new InvalidOperationException("Zabbix API token is required for Token auth mode.");
        }

        if (string.Equals(options.CurrentValue.AuthMode, "Login", StringComparison.OrdinalIgnoreCase)
            || string.Equals(options.CurrentValue.AuthMode, "LoginOrToken", StringComparison.OrdinalIgnoreCase)
            || (string.Equals(options.CurrentValue.AuthMode, "IndeedPam", StringComparison.OrdinalIgnoreCase)
                && string.IsNullOrWhiteSpace(options.CurrentValue.ApiToken)))
        {
            var cacheKey = LoginCacheKey();
            if (string.IsNullOrWhiteSpace(loginToken)
                && LoginTokenCache.TryGetValue(cacheKey, out var cachedToken))
            {
                loginToken = cachedToken;
            }

            if (string.IsNullOrWhiteSpace(loginToken))
            {
                loginToken = await LoginAsync(cancellationToken);
                LoginTokenCache[cacheKey] = loginToken;
            }

            return;
        }

        if (string.Equals(options.CurrentValue.AuthMode, "Token", StringComparison.OrdinalIgnoreCase)
            || (string.Equals(options.CurrentValue.AuthMode, "IndeedPam", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(options.CurrentValue.ApiToken)))
        {
            await SendJsonRpcAsync(new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["method"] = "user.get",
                ["params"] = new JsonObject
                {
                    ["output"] = new JsonArray("userid"),
                    ["limit"] = 1
                },
                ["id"] = 3
            }, authenticated: true, cancellationToken);
        }
    }

    private async Task<string> LoginAsync(CancellationToken cancellationToken)
    {
        var response = await SendJsonRpcAsync(new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = "user.login",
            ["params"] = new JsonObject
            {
                ["username"] = options.CurrentValue.User,
                ["password"] = options.CurrentValue.Password
            },
            ["id"] = 2
        }, authenticated: false, cancellationToken);

        if (response.TryGetPropertyValue("result", out var result))
        {
            var token = result?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(token))
            {
                return token;
            }
        }

        throw new InvalidOperationException(ReadError(response) ?? "Zabbix user.login did not return token.");
    }

    private async Task<JsonObject> SendZabbixMethodAsync(
        string method,
        JsonNode parameters,
        CancellationToken cancellationToken)
    {
        var authenticated = !string.Equals(options.CurrentValue.AuthMode, "None", StringComparison.OrdinalIgnoreCase);
        if (authenticated)
        {
            await EnsureAuthenticatedAsync(cancellationToken);
        }

        var payload = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = method,
            ["params"] = parameters,
            ["id"] = Interlocked.Increment(ref requestId)
        };

        try
        {
            return await SendJsonRpcAsync(payload, authenticated, cancellationToken);
        }
        catch (InvalidOperationException ex) when (authenticated && CanRetryAfterAuthenticationError(ex))
        {
            LoginTokenCache.TryRemove(LoginCacheKey(), out _);
            loginToken = null;
            await EnsureAuthenticatedAsync(cancellationToken);
            return await SendJsonRpcAsync(payload, authenticated, cancellationToken);
        }
    }

    private string LoginCacheKey()
    {
        var current = options.CurrentValue;
        return string.Join(
            "\u001f",
            current.ApiEndpoint.Trim(),
            current.AuthMode.Trim(),
            current.User.Trim());
    }

    private bool CanRetryAfterAuthenticationError(InvalidOperationException ex)
    {
        if (!(string.Equals(options.CurrentValue.AuthMode, "Login", StringComparison.OrdinalIgnoreCase)
            || string.Equals(options.CurrentValue.AuthMode, "LoginOrToken", StringComparison.OrdinalIgnoreCase)
            || (string.Equals(options.CurrentValue.AuthMode, "IndeedPam", StringComparison.OrdinalIgnoreCase)
                && string.IsNullOrWhiteSpace(options.CurrentValue.ApiToken))))
        {
            return false;
        }

        var message = ex.Message;
        return message.Contains("not authorized", StringComparison.OrdinalIgnoreCase)
            || message.Contains("not authorised", StringComparison.OrdinalIgnoreCase)
            || message.Contains("session", StringComparison.OrdinalIgnoreCase)
            || message.Contains("auth", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<JsonObject> SendJsonRpcAsync(
        JsonObject payload,
        bool authenticated,
        CancellationToken cancellationToken)
    {
        var method = payload.TryGetPropertyValue("method", out var methodNode)
            ? methodNode?.GetValue<string>() ?? "unknown"
            : "unknown";
        var callWatch = Stopwatch.StartNew();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(options.CurrentValue.RequestTimeoutMs));

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, options.CurrentValue.ApiEndpoint)
            {
                Content = new StringContent(payload.ToJsonString(JsonOptions), Encoding.UTF8, "application/json")
            };

            if (authenticated)
            {
                var token = string.Equals(options.CurrentValue.AuthMode, "Token", StringComparison.OrdinalIgnoreCase)
                    || (string.Equals(options.CurrentValue.AuthMode, "IndeedPam", StringComparison.OrdinalIgnoreCase)
                        && !string.IsNullOrWhiteSpace(options.CurrentValue.ApiToken))
                    ? options.CurrentValue.ApiToken
                    : loginToken;
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }

            using var response = await httpClient.SendAsync(request, timeout.Token);
            var text = await response.Content.ReadAsStringAsync(timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"HTTP {(int)response.StatusCode}: {Trim(text)}");
            }

            var node = JsonNode.Parse(text)?.AsObject()
                ?? throw new JsonException("Zabbix response is not a JSON object.");
            if (node.ContainsKey("error"))
            {
                throw new InvalidOperationException(ReadError(node) ?? Trim(text));
            }

            return node;
        }
        finally
        {
            callWatch.Stop();
            CurrentCallStats.Value?.Record(method, callWatch.Elapsed);
        }
    }

    public sealed class ZabbixApiCallStatsScope : IDisposable
    {
        private readonly ZabbixApiCallStats? previous;
        private bool disposed;

        internal ZabbixApiCallStatsScope(ZabbixApiCallStats? previous)
        {
            this.previous = previous;
            Stats = new ZabbixApiCallStats();
        }

        public ZabbixApiCallStats Stats { get; }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            CurrentCallStats.Value = previous;
            disposed = true;
        }
    }

    public sealed class ZabbixApiCallStats
    {
        private readonly object gate = new();
        private readonly Dictionary<string, ZabbixApiMethodStats> methods = new(StringComparer.Ordinal);
        private int callCount;
        private long elapsedMs;

        public int CallCount
        {
            get
            {
                lock (gate)
                {
                    return callCount;
                }
            }
        }

        public long ElapsedMs
        {
            get
            {
                lock (gate)
                {
                    return elapsedMs;
                }
            }
        }

        internal void Record(string method, TimeSpan elapsed)
        {
            method = string.IsNullOrWhiteSpace(method) ? "unknown" : method;
            lock (gate)
            {
                callCount++;
                elapsedMs += (long)Math.Round(elapsed.TotalMilliseconds);
                if (!methods.TryGetValue(method, out var stats))
                {
                    stats = new ZabbixApiMethodStats();
                    methods[method] = stats;
                }

                stats.Count++;
                stats.ElapsedMs += (long)Math.Round(elapsed.TotalMilliseconds);
            }
        }

        public IReadOnlyDictionary<string, ZabbixApiMethodStatsSnapshot> SnapshotByMethod()
        {
            lock (gate)
            {
                return methods
                    .OrderByDescending(pair => pair.Value.ElapsedMs)
                    .ThenBy(pair => pair.Key, StringComparer.Ordinal)
                    .ToDictionary(
                        pair => pair.Key,
                        pair => new ZabbixApiMethodStatsSnapshot
                        {
                            Count = pair.Value.Count,
                            ElapsedMs = pair.Value.ElapsedMs
                        },
                        StringComparer.Ordinal);
            }
        }
    }

    private sealed class ZabbixApiMethodStats
    {
        public int Count { get; set; }

        public long ElapsedMs { get; set; }
    }

    public sealed class ZabbixApiMethodStatsSnapshot
    {
        public int Count { get; init; }

        public long ElapsedMs { get; init; }
    }

    private static string? ReadError(JsonObject response)
    {
        if (!response.TryGetPropertyValue("error", out var error) || error is not JsonObject errorObject)
        {
            return null;
        }

        var message = errorObject.TryGetPropertyValue("message", out var messageNode)
            ? messageNode?.GetValue<string>()
            : null;
        var data = errorObject.TryGetPropertyValue("data", out var dataNode)
            ? dataNode?.GetValue<string>()
            : null;

        return string.Join(": ", new[] { message, data }.Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    private static IntegrationCheckResult Failed(string endpoint, string error)
    {
        return new IntegrationCheckResult
        {
            System = "Zabbix",
            Endpoint = endpoint,
            Success = false,
            Error = error
        };
    }

    private static ZabbixServiceInfo? SingleManagedServiceOrDefault(
        IReadOnlyList<ZabbixServiceInfo> services,
        string lookupDescription)
    {
        return services.Count switch
        {
            0 => null,
            1 => services[0],
            _ => throw new InvalidOperationException(
                $"Zabbix contains duplicated managed services for {lookupDescription}: {string.Join(", ", services.Select(service => service.ServiceId))}.")
        };
    }

    private static JsonObject BuildServicePayload(ZabbixManagedServiceDefinition definition)
    {
        var service = new JsonObject
        {
            ["name"] = definition.Name,
            ["algorithm"] = definition.Algorithm,
            ["sortorder"] = definition.SortOrder,
            ["weight"] = definition.Weight,
            ["description"] = definition.Description,
            ["tags"] = ServiceTags(definition.Tags)
        };
        if (definition.ProblemTags.Count > 0)
        {
            service["problem_tags"] = ProblemTags(definition.ProblemTags);
        }

        return service;
    }

    private static JsonObject BuildSlaPayload(
        ZabbixSlaDefinition definition,
        IReadOnlyList<ZabbixSlaExcludedDowntime> excludedDowntimes)
    {
        return new JsonObject
        {
            ["name"] = definition.Name,
            ["period"] = definition.Period,
            ["slo"] = decimal.Round(definition.Slo, 4).ToString(CultureInfo.InvariantCulture),
            ["effective_date"] = definition.EffectiveDate,
            ["timezone"] = definition.Timezone,
            ["status"] = definition.Status,
            ["description"] = definition.Description,
            ["service_tags"] = SlaServiceTags(definition.ServiceTags),
            ["schedule"] = SlaSchedule(definition.Schedule),
            ["excluded_downtimes"] = SlaExcludedDowntimes(excludedDowntimes)
        };
    }

    private static IReadOnlyList<ZabbixSlaExcludedDowntime> MergeExcludedDowntimes(
        ZabbixSlaDefinition definition,
        ZabbixSlaInfo? existing)
    {
        if (existing is null || existing.ExcludedDowntimes.Count == 0)
        {
            return definition.ExcludedDowntimes;
        }

        var prefix = definition.ManagedExcludedDowntimePrefix;
        var manual = existing.ExcludedDowntimes
            .Where(downtime => string.IsNullOrWhiteSpace(prefix)
                || !downtime.Name.StartsWith(prefix, StringComparison.Ordinal))
            .ToArray();
        return manual
            .Concat(definition.ExcludedDowntimes)
            .GroupBy(downtime => $"{downtime.Name}\u001f{downtime.PeriodFrom}\u001f{downtime.PeriodTo}", StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(downtime => downtime.PeriodFrom)
            .ThenBy(downtime => downtime.Name, StringComparer.Ordinal)
            .ToArray();
    }

    private static JsonArray ServiceTags(IReadOnlyDictionary<string, string> tags)
    {
        var result = new JsonArray();
        foreach (var tag in tags.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            result.Add(new JsonObject
            {
                ["tag"] = tag.Key,
                ["value"] = tag.Value
            });
        }

        return result;
    }

    private static JsonArray SlaServiceTags(IReadOnlyList<ZabbixSlaServiceTag> tags)
    {
        var result = new JsonArray();
        foreach (var tag in tags.OrderBy(pair => pair.Tag, StringComparer.Ordinal).ThenBy(pair => pair.Value, StringComparer.Ordinal))
        {
            result.Add(new JsonObject
            {
                ["tag"] = tag.Tag,
                ["operator"] = tag.Operator,
                ["value"] = tag.Value
            });
        }

        return result;
    }

    private static JsonArray SlaSchedule(IReadOnlyList<ZabbixSlaSchedulePeriod> schedule)
    {
        var result = new JsonArray();
        foreach (var period in schedule.OrderBy(item => item.PeriodFrom))
        {
            result.Add(new JsonObject
            {
                ["period_from"] = period.PeriodFrom,
                ["period_to"] = period.PeriodTo
            });
        }

        return result;
    }

    private static JsonArray SlaExcludedDowntimes(IReadOnlyList<ZabbixSlaExcludedDowntime> downtimes)
    {
        var result = new JsonArray();
        foreach (var downtime in downtimes.OrderBy(item => item.PeriodFrom).ThenBy(item => item.Name, StringComparer.Ordinal))
        {
            result.Add(new JsonObject
            {
                ["name"] = downtime.Name,
                ["period_from"] = downtime.PeriodFrom,
                ["period_to"] = downtime.PeriodTo
            });
        }

        return result;
    }

    private static IReadOnlyDictionary<string, string> AggregateTags(ZabbixSuppressionAggregateDefinition definition)
    {
        var tags = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [ZabbixManagedServiceTags.Managed] = "true",
            [ZabbixManagedServiceTags.Layer] = definition.Layer,
            [ZabbixManagedServiceTags.Key] = definition.TargetManagedKey,
            [ZabbixManagedServiceTags.Aggregate] = "true",
            [ZabbixManagedServiceTags.AggregateKind] = "suppression_state"
        };
        if (!string.IsNullOrWhiteSpace(definition.TargetClass))
        {
            tags[ZabbixManagedServiceTags.Class] = definition.TargetClass;
        }

        if (!string.IsNullOrWhiteSpace(definition.TargetCardId))
        {
            tags[ZabbixManagedServiceTags.CardId] = definition.TargetCardId;
        }

        if (!string.IsNullOrWhiteSpace(definition.AggregationType))
        {
            tags[ZabbixManagedServiceTags.AggregationType] = definition.AggregationType;
        }

        return tags;
    }

    private static JsonArray ServiceTags(IReadOnlyList<ZabbixServiceTag> tags)
    {
        var result = new JsonArray();
        foreach (var tag in tags.OrderBy(pair => pair.Tag, StringComparer.Ordinal).ThenBy(pair => pair.Value, StringComparer.Ordinal))
        {
            result.Add(new JsonObject
            {
                ["tag"] = tag.Tag,
                ["value"] = tag.Value
            });
        }

        return result;
    }

    private static JsonArray ProblemTags(IReadOnlyList<ZabbixProblemTag> tags)
    {
        var result = new JsonArray();
        foreach (var tag in tags.OrderBy(pair => pair.Tag, StringComparer.Ordinal).ThenBy(pair => pair.Value, StringComparer.Ordinal))
        {
            result.Add(new JsonObject
            {
                ["tag"] = tag.Tag,
                ["value"] = tag.Value,
                ["operator"] = tag.Operator
            });
        }

        return result;
    }

    private static JsonArray TagFilters(IReadOnlyDictionary<string, string> tags)
    {
        var result = new JsonArray();
        foreach (var tag in tags)
        {
            result.Add(new JsonObject
            {
                ["tag"] = tag.Key,
                ["value"] = tag.Value,
                ["operator"] = 1
            });
        }

        return result;
    }

    private static JsonArray ServiceReferences(IReadOnlyList<string> serviceIds)
    {
        var result = new JsonArray();
        foreach (var serviceId in serviceIds)
        {
            result.Add(new JsonObject
            {
                ["serviceid"] = serviceId
            });
        }

        return result;
    }

    private static JsonObject TriggerGetBaseParameters(bool includeDisabled)
    {
        var parameters = new JsonObject
        {
            ["output"] = new JsonArray("triggerid", "description", "status", "priority", "value", "expression", "recovery_expression"),
            ["selectHosts"] = new JsonArray("hostid", "host", "name"),
            ["selectDependencies"] = new JsonArray("triggerid", "description"),
            ["selectTags"] = new JsonArray("tag", "value"),
            ["expandExpression"] = true,
            ["expandRecoveryExpression"] = true,
            ["expandDescription"] = true
        };
        if (!includeDisabled)
        {
            parameters["filter"] = new JsonObject
            {
                ["status"] = "0"
            };
        }

        return parameters;
    }

    private static JsonArray TriggerReferences(IReadOnlyList<string> triggerIds)
    {
        var result = new JsonArray();
        foreach (var triggerId in triggerIds)
        {
            result.Add(new JsonObject
            {
                ["triggerid"] = triggerId
            });
        }

        return result;
    }

    private static JsonArray StringArray(IReadOnlyList<string> values)
    {
        var result = new JsonArray();
        foreach (var value in values)
        {
            result.Add(value);
        }

        return result;
    }

    private static JsonObject CloneJsonObject(JsonObject source)
    {
        return source.Deserialize<JsonObject>(JsonOptions) ?? new JsonObject();
    }

    private static ZabbixServiceInfo ReadService(JsonObject service)
    {
        return new ZabbixServiceInfo
        {
            ServiceId = JsonString(service["serviceid"]),
            Name = JsonString(service["name"]),
            Tags = ReadTags(service["tags"] as JsonArray),
            Children = ReadServiceReferences(service["children"] as JsonArray),
            Parents = ReadServiceReferences(service["parents"] as JsonArray)
        };
    }

    private static ZabbixHostInfo ReadHost(JsonObject host)
    {
        return new ZabbixHostInfo
        {
            HostId = JsonString(host["hostid"]),
            Host = JsonString(host["host"]),
            Name = JsonString(host["name"]),
            Tags = ReadTagList(host["tags"] as JsonArray)
        };
    }

    private static ZabbixSlaInfo ReadSla(JsonObject sla)
    {
        return new ZabbixSlaInfo
        {
            SlaId = JsonString(sla["slaid"]),
            Name = JsonString(sla["name"]),
            Slo = JsonDecimal(sla["slo"]),
            Period = JsonInt(sla["period"]),
            Timezone = JsonString(sla["timezone"]),
            Status = JsonInt(sla["status"]),
            ServiceTags = ReadSlaServiceTags(sla["service_tags"] as JsonArray),
            Schedule = ReadSlaSchedule(sla["schedule"] as JsonArray),
            ExcludedDowntimes = ReadSlaExcludedDowntimes(sla["excluded_downtimes"] as JsonArray)
        };
    }

    private static ZabbixTriggerInfo ReadTrigger(JsonObject trigger)
    {
        return new ZabbixTriggerInfo
        {
            TriggerId = JsonString(trigger["triggerid"]),
            Description = JsonString(trigger["description"]),
            Status = JsonString(trigger["status"]),
            Priority = JsonString(trigger["priority"]),
            Value = JsonString(trigger["value"]),
            Expression = JsonString(trigger["expression"]),
            RecoveryExpression = JsonString(trigger["recovery_expression"]),
            Tags = ReadTagList(trigger["tags"] as JsonArray),
            Hosts = ReadTriggerHosts(trigger["hosts"] as JsonArray),
            Dependencies = ReadTriggerDependencies(trigger["dependencies"] as JsonArray)
        };
    }

    private static ZabbixSuppressionAggregateItemInfo ReadSuppressionAggregateItem(JsonObject item)
    {
        return new ZabbixSuppressionAggregateItemInfo
        {
            ItemId = JsonString(item["itemid"]),
            Name = JsonString(item["name"]),
            Key = JsonString(item["key_"]),
            Status = JsonString(item["status"]),
            State = JsonString(item["state"]),
            Error = JsonString(item["error"]),
            LastValue = JsonString(item["lastvalue"]),
            LastClock = JsonString(item["lastclock"])
        };
    }

    private static IReadOnlyDictionary<string, string> ReadTags(JsonArray? tags)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (tags is null)
        {
            return result;
        }

        foreach (var item in tags.OfType<JsonObject>())
        {
            var tag = JsonString(item["tag"]);
            if (!string.IsNullOrWhiteSpace(tag))
            {
                result[tag] = JsonString(item["value"]);
            }
        }

        return result;
    }

    private static IReadOnlyList<ZabbixServiceTag> ReadTagList(JsonArray? tags)
    {
        if (tags is null)
        {
            return [];
        }

        return tags
            .OfType<JsonObject>()
            .Select(item => new ZabbixServiceTag(JsonString(item["tag"]), JsonString(item["value"])))
            .Where(tag => !string.IsNullOrWhiteSpace(tag.Tag))
            .ToArray();
    }

    private static IReadOnlyList<ZabbixSlaServiceTag> ReadSlaServiceTags(JsonArray? tags)
    {
        if (tags is null)
        {
            return [];
        }

        return tags
            .OfType<JsonObject>()
            .Select(item => new ZabbixSlaServiceTag(
                JsonString(item["tag"]),
                JsonString(item["value"]),
                JsonInt(item["operator"])))
            .Where(tag => !string.IsNullOrWhiteSpace(tag.Tag))
            .ToArray();
    }

    private static IReadOnlyList<ZabbixSlaSchedulePeriod> ReadSlaSchedule(JsonArray? schedule)
    {
        if (schedule is null)
        {
            return [];
        }

        return schedule
            .OfType<JsonObject>()
            .Select(item => new ZabbixSlaSchedulePeriod(
                JsonInt(item["period_from"]),
                JsonInt(item["period_to"])))
            .Where(period => period.PeriodTo > period.PeriodFrom)
            .ToArray();
    }

    private static IReadOnlyList<ZabbixSlaExcludedDowntime> ReadSlaExcludedDowntimes(JsonArray? downtimes)
    {
        if (downtimes is null)
        {
            return [];
        }

        return downtimes
            .OfType<JsonObject>()
            .Select(item => new ZabbixSlaExcludedDowntime(
                JsonString(item["name"]),
                JsonLong(item["period_from"]),
                JsonLong(item["period_to"])))
            .Where(downtime => !string.IsNullOrWhiteSpace(downtime.Name) && downtime.PeriodTo > downtime.PeriodFrom)
            .ToArray();
    }

    private static IReadOnlyList<ZabbixServiceInfo> ReadServiceReferences(JsonArray? services)
    {
        if (services is null)
        {
            return [];
        }

        return services
            .OfType<JsonObject>()
            .Select(service => new ZabbixServiceInfo
            {
                ServiceId = JsonString(service["serviceid"]),
                Name = JsonString(service["name"])
            })
            .ToArray();
    }

    private static IReadOnlyList<ZabbixHostInfo> ReadTriggerHosts(JsonArray? hosts)
    {
        if (hosts is null)
        {
            return [];
        }

        return hosts
            .OfType<JsonObject>()
            .Select(host => new ZabbixHostInfo
            {
                HostId = JsonString(host["hostid"]),
                Host = JsonString(host["host"]),
                Name = JsonString(host["name"])
            })
            .Where(host => !string.IsNullOrWhiteSpace(host.HostId))
            .ToArray();
    }

    private static IReadOnlyList<ZabbixTriggerDependencyInfo> ReadTriggerDependencies(JsonArray? dependencies)
    {
        if (dependencies is null)
        {
            return [];
        }

        return dependencies
            .OfType<JsonObject>()
            .Select(trigger => new ZabbixTriggerDependencyInfo
            {
                TriggerId = JsonString(trigger["triggerid"]),
                Description = JsonString(trigger["description"])
            })
            .Where(trigger => !string.IsNullOrWhiteSpace(trigger.TriggerId))
            .ToArray();
    }

    private static string ReadServiceId(JsonObject response, string method)
    {
        var ids = response["result"]?["serviceids"] as JsonArray;
        var serviceId = ids is { Count: > 0 } ? JsonString(ids[0]) : "";
        if (string.IsNullOrWhiteSpace(serviceId))
        {
            throw new InvalidOperationException($"Zabbix {method} did not return serviceids.");
        }

        return serviceId;
    }

    private static JsonArray ReadResultArray(JsonObject response, string method)
    {
        return response.TryGetPropertyValue("result", out var resultNode) && resultNode is JsonArray array
            ? array
            : throw new InvalidOperationException($"Zabbix {method} did not return an array.");
    }

    private static string ReadFirstId(JsonObject response, string propertyName, string method)
    {
        var ids = response["result"]?[propertyName] as JsonArray;
        var id = ids is { Count: > 0 } ? JsonString(ids[0]) : "";
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new InvalidOperationException($"Zabbix {method} did not return {propertyName}.");
        }

        return id;
    }

    private static string JsonString(JsonNode? node)
    {
        if (node is null)
        {
            return "";
        }

        try
        {
            return node.GetValue<string>() ?? "";
        }
        catch (InvalidOperationException)
        {
            return node.ToJsonString(JsonOptions).Trim('"');
        }
    }

    private static int JsonInt(JsonNode? node)
    {
        if (node is null)
        {
            return 0;
        }

        try
        {
            return node.GetValue<int>();
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException)
        {
            return int.TryParse(JsonString(node), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
                ? value
                : 0;
        }
    }

    private static long JsonLong(JsonNode? node)
    {
        if (node is null)
        {
            return 0;
        }

        try
        {
            return node.GetValue<long>();
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException)
        {
            return long.TryParse(JsonString(node), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
                ? value
                : 0;
        }
    }

    private static decimal JsonDecimal(JsonNode? node)
    {
        if (node is null)
        {
            return 0;
        }

        try
        {
            return node.GetValue<decimal>();
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException)
        {
            return decimal.TryParse(JsonString(node), NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
                ? value
                : 0;
        }
    }

    private static string Trim(string value)
    {
        const int maxLength = 300;
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        return value.Length <= maxLength ? value : value[..maxLength];
    }
}
