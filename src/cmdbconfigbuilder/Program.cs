using Cmdb2MonitoringServiceSuppression.Shared.Configuration;
using Cmdb2MonitoringServiceSuppression.Shared.ConversionRules;
using Cmdb2MonitoringServiceSuppression.Shared.Integrations;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

builder.Services.AddOptions<ApplyOptions>()
    .Bind(builder.Configuration.GetSection(ApplyOptions.SectionName))
    .Validate(options => options.HasValidMode(), "Apply mode must be manual, auto, or dry-run.")
    .ValidateOnStart();
builder.Services.AddOptions<CmdbuildOptions>()
    .Bind(builder.Configuration.GetSection(CmdbuildOptions.SectionName))
    .Validate(options => !string.IsNullOrWhiteSpace(options.BaseUrl), "CMDBuild base URL is required.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.Username), "CMDBuild username is required.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.Password), "CMDBuild password is required.")
    .Validate(options => options.RequestTimeoutMs > 0, "CMDBuild request timeout must be greater than zero.")
    .ValidateOnStart();
builder.Services.AddOptions<ZabbixOptions>()
    .Bind(builder.Configuration.GetSection(ZabbixOptions.SectionName))
    .Validate(options => !string.IsNullOrWhiteSpace(options.ApiEndpoint), "Zabbix API endpoint is required.")
    .Validate(options => options.HasValidAuthMode(), "Zabbix auth mode is invalid.")
    .Validate(options => options.RequestTimeoutMs > 0, "Zabbix request timeout must be greater than zero.")
    .ValidateOnStart();

builder.Services.AddSingleton<ConversionRulesValidator>();
builder.Services.AddHttpClient<CmdbuildClient>();
builder.Services.AddHttpClient<ZabbixClient>();

var app = builder.Build();
app.MapServiceHealth();

app.MapPost("/rules/validate", (
    ConversionRulesDocument document,
    ConversionRulesValidator validator) =>
{
    var result = validator.Validate(document);
    return result.IsValid ? Results.Ok(result) : Results.BadRequest(result);
});

app.MapPost("/build/dry-run", () => Results.Accepted(value: new
{
    status = "accepted",
    mode = "dry-run"
}));

app.MapGet("/integrations/check", async (
    CmdbuildClient cmdbuild,
    ZabbixClient zabbix,
    CancellationToken cancellationToken) =>
{
    var cmdbuildResult = await cmdbuild.CheckConnectionAsync(cancellationToken);
    var zabbixResult = await zabbix.CheckConnectionAsync(cancellationToken);
    var success = cmdbuildResult.Success && zabbixResult.Success;

    return success
        ? Results.Ok(new { success, systems = new[] { cmdbuildResult, zabbixResult } })
        : Results.Problem(
            detail: "One or more integrations are unavailable.",
            extensions: new Dictionary<string, object?> { ["systems"] = new[] { cmdbuildResult, zabbixResult } },
            statusCode: StatusCodes.Status502BadGateway);
});

app.Run();
