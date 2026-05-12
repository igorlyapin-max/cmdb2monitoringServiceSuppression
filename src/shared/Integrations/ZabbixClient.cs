using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cmdb2MonitoringServiceSuppression.Shared.Configuration;
using Microsoft.Extensions.Options;

namespace Cmdb2MonitoringServiceSuppression.Shared.Integrations;

public sealed class ZabbixClient(HttpClient httpClient, IOptionsMonitor<ZabbixOptions> options)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private string? loginToken;
    private int requestId = 10;

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

        childIds = childIds
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var replaceChildren = warnings.Count == 0;
        var serviceId = existing is null
            ? await CreateManagedServiceAsync(definition, childIds, replaceChildren, cancellationToken)
            : await UpdateManagedServiceAsync(existing.ServiceId, definition, childIds, replaceChildren, cancellationToken);

        return new ZabbixManagedServiceApplyResult
        {
            Success = true,
            Action = existing is null ? "created" : "updated",
            ServiceId = serviceId,
            RelationsApplied = replaceChildren ? childIds.Count : 0,
            RelationsDeferred = replaceChildren ? 0 : warnings.Count,
            Warnings = warnings
        };
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

    private async Task<string> CreateManagedServiceAsync(
        ZabbixManagedServiceDefinition definition,
        IReadOnlyList<string> childIds,
        bool includeChildren,
        CancellationToken cancellationToken)
    {
        var service = BuildServicePayload(definition);
        if (includeChildren)
        {
            service["children"] = ServiceReferences(childIds);
        }

        var response = await SendZabbixMethodAsync("service.create", service, cancellationToken);
        return ReadServiceId(response, "service.create");
    }

    private async Task<string> UpdateManagedServiceAsync(
        string serviceId,
        ZabbixManagedServiceDefinition definition,
        IReadOnlyList<string> childIds,
        bool includeChildren,
        CancellationToken cancellationToken)
    {
        var service = BuildServicePayload(definition);
        service["serviceid"] = serviceId;
        if (includeChildren)
        {
            service["children"] = ServiceReferences(childIds);
        }

        var response = await SendZabbixMethodAsync("service.update", service, cancellationToken);
        return ReadServiceId(response, "service.update");
    }

    private async Task<IReadOnlyList<ZabbixServiceInfo>> GetServicesByTagsAsync(
        IReadOnlyDictionary<string, string> tags,
        CancellationToken cancellationToken)
    {
        var parameters = new JsonObject
        {
            ["output"] = new JsonArray("serviceid", "name", "algorithm", "sortorder", "description"),
            ["selectTags"] = new JsonArray("tag", "value"),
            ["selectChildren"] = new JsonArray("serviceid", "name"),
            ["selectParents"] = new JsonArray("serviceid", "name"),
            ["evaltype"] = 0,
            ["tags"] = TagFilters(tags),
            ["limit"] = 2
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
            loginToken ??= await LoginAsync(cancellationToken);
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
        JsonObject parameters,
        CancellationToken cancellationToken)
    {
        var authenticated = !string.Equals(options.CurrentValue.AuthMode, "None", StringComparison.OrdinalIgnoreCase);
        if (authenticated)
        {
            await EnsureAuthenticatedAsync(cancellationToken);
        }

        return await SendJsonRpcAsync(new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = method,
            ["params"] = parameters,
            ["id"] = Interlocked.Increment(ref requestId)
        }, authenticated, cancellationToken);
    }

    private async Task<JsonObject> SendJsonRpcAsync(
        JsonObject payload,
        bool authenticated,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(options.CurrentValue.RequestTimeoutMs));

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
        return new JsonObject
        {
            ["name"] = definition.Name,
            ["algorithm"] = definition.Algorithm,
            ["sortorder"] = definition.SortOrder,
            ["weight"] = definition.Weight,
            ["description"] = definition.Description,
            ["tags"] = ServiceTags(definition.Tags)
        };
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
