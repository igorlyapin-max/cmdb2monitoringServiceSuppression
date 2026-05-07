using Cmdb2MonitoringServiceSuppression.Shared.Configuration;
using Cmdb2MonitoringServiceSuppression.Shared.Integrations;
using Microsoft.Extensions.Options;

var cmdbuildOptions = Options.Create(new CmdbuildOptions
{
    BaseUrl = "http://localhost:8090/cmdbuild/services/rest/v3",
    Username = "admin",
    Password = "admin",
    RequestTimeoutMs = 10000
});
var zabbixOptions = Options.Create(new ZabbixOptions
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

Print(cmdbuildResult);
Print(zabbixResult);

if (!cmdbuildResult.Success || !zabbixResult.Success)
{
    Environment.ExitCode = 1;
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
