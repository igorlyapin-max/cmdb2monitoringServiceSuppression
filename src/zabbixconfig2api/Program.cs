using Cmdb2MonitoringServiceSuppression.Shared.Configuration;
using Cmdb2MonitoringServiceSuppression.Shared.Integrations;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

builder.Services.AddOptions<ApplyOptions>()
    .Bind(builder.Configuration.GetSection(ApplyOptions.SectionName))
    .Validate(options => options.HasValidMode(), "Apply mode must be manual, auto, or dry-run.")
    .ValidateOnStart();
builder.Services.AddOptions<ZabbixOptions>()
    .Bind(builder.Configuration.GetSection(ZabbixOptions.SectionName))
    .Validate(options => !string.IsNullOrWhiteSpace(options.ApiEndpoint), "Zabbix API endpoint is required.")
    .Validate(options => options.HasValidAuthMode(), "Zabbix auth mode is invalid.")
    .Validate(options => options.RequestTimeoutMs > 0, "Zabbix request timeout must be greater than zero.")
    .ValidateOnStart();
builder.Services.AddHttpClient<ZabbixClient>();

var app = builder.Build();
app.MapServiceHealth();

app.MapPost("/apply/manual", (IOptions<ApplyOptions> options) =>
{
    return Results.Accepted(value: new
    {
        status = "accepted",
        mode = "manual",
        safe_apply = options.Value.SafeApply
    });
});

app.MapPost("/apply/auto", (IOptions<ApplyOptions> options) =>
{
    if (!options.Value.AutoApplyEnabled)
    {
        return Results.Problem(
            title: "Automatic apply is disabled.",
            detail: "Enable Apply:AutoApplyEnabled only for approved development or automation scenarios.",
            statusCode: StatusCodes.Status409Conflict);
    }

    return Results.Accepted(value: new
    {
        status = "accepted",
        mode = "auto",
        safe_apply = options.Value.SafeApply
    });
});

app.MapGet("/zabbix/check", async (
    ZabbixClient client,
    CancellationToken cancellationToken) =>
{
    var result = await client.CheckConnectionAsync(cancellationToken);
    return result.Success ? Results.Ok(result) : Results.Problem(result.Error, statusCode: StatusCodes.Status502BadGateway);
});

app.Run();
