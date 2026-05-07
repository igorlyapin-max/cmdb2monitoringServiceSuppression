using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cmdb2MonitoringServiceSuppression.Shared.Configuration;
using Microsoft.Extensions.Options;

namespace Cmdb2MonitoringServiceSuppression.Shared.Integrations;

public sealed class ZabbixClient(HttpClient httpClient, IOptions<ZabbixOptions> options)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private string? loginToken;

    public async Task<IntegrationCheckResult> CheckConnectionAsync(CancellationToken cancellationToken)
    {
        var endpoint = options.Value.ApiEndpoint;
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return Failed(endpoint, "Zabbix API endpoint is not configured.");
        }

        try
        {
            var version = await GetApiVersionAsync(cancellationToken);
            if (!string.Equals(options.Value.AuthMode, "None", StringComparison.OrdinalIgnoreCase))
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
        if (string.Equals(options.Value.AuthMode, "Token", StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(options.Value.ApiToken))
        {
            throw new InvalidOperationException("Zabbix API token is required for Token auth mode.");
        }

        if (string.Equals(options.Value.AuthMode, "Login", StringComparison.OrdinalIgnoreCase)
            || string.Equals(options.Value.AuthMode, "LoginOrToken", StringComparison.OrdinalIgnoreCase))
        {
            loginToken ??= await LoginAsync(cancellationToken);
            return;
        }

        if (string.Equals(options.Value.AuthMode, "Token", StringComparison.OrdinalIgnoreCase))
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
                ["username"] = options.Value.User,
                ["password"] = options.Value.Password
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

    private async Task<JsonObject> SendJsonRpcAsync(
        JsonObject payload,
        bool authenticated,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(options.Value.RequestTimeoutMs));

        using var request = new HttpRequestMessage(HttpMethod.Post, options.Value.ApiEndpoint)
        {
            Content = new StringContent(payload.ToJsonString(JsonOptions), Encoding.UTF8, "application/json")
        };

        if (authenticated)
        {
            var token = string.Equals(options.Value.AuthMode, "Token", StringComparison.OrdinalIgnoreCase)
                ? options.Value.ApiToken
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
