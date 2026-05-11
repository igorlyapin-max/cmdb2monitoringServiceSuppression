using System.Text.Json;
using Cmdb2MonitoringServiceSuppression.Shared.Configuration;
using Cmdb2MonitoringServiceSuppression.Shared.Integrations;
using Microsoft.Extensions.Options;

var cmdbuildOptions = new StaticOptionsMonitor<CmdbuildOptions>(new CmdbuildOptions
{
    BaseUrl = "http://localhost:8090/cmdbuild/services/rest/v3",
    Username = "admin",
    Password = "admin",
    RequestTimeoutMs = 10000
});
var zabbixOptions = new StaticOptionsMonitor<ZabbixOptions>(new ZabbixOptions
{
    ApiEndpoint = "http://localhost:8081/api_jsonrpc.php",
    AuthMode = "Login",
    User = "Admin",
    Password = "zabbix",
    RequestTimeoutMs = 30000
});

using var httpClient = new HttpClient();
var cmdbuild = new CmdbuildClient(httpClient, cmdbuildOptions);
var zabbix = new ZabbixClient(httpClient, zabbixOptions);

var cmdbuildResult = await cmdbuild.CheckConnectionAsync(CancellationToken.None);
var zabbixResult = await zabbix.CheckConnectionAsync(CancellationToken.None);
var cmdbuildApplierResult = await CheckCmdbuildApplierApplyStatusAsync(httpClient, CancellationToken.None);

Print(cmdbuildResult);
Print(zabbixResult);
Print(cmdbuildApplierResult);

if (!cmdbuildResult.Success || !zabbixResult.Success || !cmdbuildApplierResult.Success)
{
    Environment.ExitCode = 1;
}

static async Task<IntegrationCheckResult> CheckCmdbuildApplierApplyStatusAsync(
    HttpClient httpClient,
    CancellationToken cancellationToken)
{
    var endpoint = Environment.GetEnvironmentVariable("CMDB_AGGREGATION_APPLY_STATUS_URL")
        ?? "http://localhost:5181/apply/status";
    try
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return Failed(endpoint, $"HTTP {(int)response.StatusCode}: {Trim(text)}");
        }

        using var document = JsonDocument.Parse(text);
        var root = document.RootElement;
        var mode = ReadString(root, "mode");
        var effectiveAutoApplyEnabled = ReadBool(root, "effectiveAutoApplyEnabled");
        var kafkaConsumerStarted = ReadBool(root, "kafkaConsumerStarted");
        if (!effectiveAutoApplyEnabled || !kafkaConsumerStarted)
        {
            return Failed(
                endpoint,
                $"CMDBuild applier Kafka auto-apply is not active; mode={mode}, effectiveAutoApplyEnabled={effectiveAutoApplyEnabled}, kafkaConsumerStarted={kafkaConsumerStarted}.");
        }

        return new IntegrationCheckResult
        {
            System = "CMDBuild applier apply status",
            Endpoint = endpoint,
            Success = true,
            Summary = $"mode={mode}; effectiveAutoApplyEnabled={effectiveAutoApplyEnabled}; kafkaConsumerStarted={kafkaConsumerStarted}."
        };
    }
    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
    {
        return Failed(endpoint, ex.Message);
    }
}

static void Print(IntegrationCheckResult result)
{
    Console.WriteLine($"{result.System}: {(result.Success ? "ok" : "failed")} {result.Endpoint}");
    if (!string.IsNullOrWhiteSpace(result.Version))
    {
        Console.WriteLine($"  version: {result.Version}");
    }

    if (!string.IsNullOrWhiteSpace(result.Summary))
    {
        Console.WriteLine($"  {result.Summary}");
    }

    if (!string.IsNullOrWhiteSpace(result.Error))
    {
        Console.WriteLine($"  error: {result.Error}");
    }
}

static IntegrationCheckResult Failed(string endpoint, string error)
{
    return new IntegrationCheckResult
    {
        System = "CMDBuild applier apply status",
        Endpoint = endpoint,
        Success = false,
        Error = error
    };
}

static string Trim(string value)
{
    return string.IsNullOrWhiteSpace(value)
        ? ""
        : value.Length <= 500 ? value : value[..500];
}

static string ReadString(JsonElement element, string propertyName)
{
    return element.TryGetProperty(propertyName, out var property)
        ? property.ValueKind == JsonValueKind.String ? property.GetString() ?? "" : property.GetRawText()
        : "";
}

static bool ReadBool(JsonElement element, string propertyName)
{
    if (!element.TryGetProperty(propertyName, out var property))
    {
        return false;
    }

    return property.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.String => bool.TryParse(property.GetString(), out var value) && value,
        _ => false
    };
}

public sealed class StaticOptionsMonitor<TOptions>(TOptions value) : IOptionsMonitor<TOptions>
{
    public TOptions CurrentValue { get; } = value;

    public TOptions Get(string? name)
    {
        return CurrentValue;
    }

    public IDisposable? OnChange(Action<TOptions, string?> listener)
    {
        return null;
    }
}
