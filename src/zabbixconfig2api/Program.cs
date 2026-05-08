using Cmdb2MonitoringServiceSuppression.Shared.Aggregation;
using Cmdb2MonitoringServiceSuppression.Shared.Configuration;
using Cmdb2MonitoringServiceSuppression.Shared.Integrations;
using Cmdb2MonitoringServiceSuppression.Shared.Logging;
using Cmdb2MonitoringServiceSuppression.Shared.Messaging;
using Cmdb2MonitoringServiceSuppression.Shared.Secrets;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);
await builder.Configuration.ResolveSecretReferencesAsync("zabbixconfig2api");
builder.AddServiceDefaults();

builder.Services.AddOptions<ApplyOptions>()
    .Bind(builder.Configuration.GetSection(ApplyOptions.SectionName))
    .Validate(options => options.HasValidMode(), "Apply mode must be manual, auto, or dry-run.")
    .ValidateOnStart();
builder.Services.AddOptions<ZabbixOptions>()
    .Bind(builder.Configuration.GetSection(ZabbixOptions.SectionName))
    .Validate(options => options.HasValidAuthMode(), "Zabbix auth mode is invalid.")
    .Validate(options => options.RequestTimeoutMs > 0, "Zabbix request timeout must be greater than zero.")
    .ValidateOnStart();
builder.Services.AddOptions<KafkaTopicsOptions>()
    .Bind(builder.Configuration.GetSection(KafkaTopicsOptions.SectionName))
    .Validate(options => !string.IsNullOrWhiteSpace(options.EffectiveAggregationCommands()), "Aggregation command topic is required.")
    .ValidateOnStart();
builder.Services.AddHttpClient<ZabbixClient>();
builder.Services.AddHostedService<ZabbixAggregationCommandWorker>();

var app = builder.Build();
app.MapServiceHealth();
app.MapConfigurationReload(builder.Configuration);

app.MapPost("/apply/manual", (IOptionsMonitor<ApplyOptions> options) =>
{
    return Results.Accepted(value: new
    {
        status = "accepted",
        mode = "manual",
        safe_apply = options.CurrentValue.SafeApply
    });
});

app.MapPost("/apply/auto", (IOptionsMonitor<ApplyOptions> options) =>
{
    if (!options.CurrentValue.AutoApplyEnabled)
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
        safe_apply = options.CurrentValue.SafeApply
    });
});

app.MapGet("/zabbix/check", async (
    ZabbixClient client,
    CancellationToken cancellationToken) =>
{
    var result = await client.CheckConnectionAsync(cancellationToken);
    return result.Success ? Results.Ok(result) : Results.Problem(result.Error, statusCode: StatusCodes.Status502BadGateway);
});

app.MapPost("/commands/apply/dry-run", (AggregationCommand command, IOptionsMonitor<ApplyOptions> options) =>
{
    return Results.Accepted(value: new
    {
        status = "accepted",
        target = "zabbix",
        mode = "dry-run",
        safe_apply = options.CurrentValue.SafeApply,
        command
    });
});

app.Run();

public sealed class ZabbixAggregationCommandWorker(
    IOptions<KafkaOptions> kafkaOptions,
    IOptions<KafkaTopicsOptions> topicOptions,
    IOptionsMonitor<ApplyOptions> applyOptions,
    IOptions<DebugOptions> debugOptions,
    ILogger<ZabbixAggregationCommandWorker> logger)
    : KafkaJsonConsumerWorker<AggregationCommand>(kafkaOptions, logger)
{
    protected override string Topic => topicOptions.Value.EffectiveAggregationCommands();

    protected override string ConsumerGroupId => "";

    protected override Task HandleMessageAsync(
        AggregationCommand message,
        string key,
        CancellationToken cancellationToken)
    {
        logger.LogDebugBasic(
            debugOptions,
            "zabbix applier received command={CommandId}, type={CommandType}, layer={Layer}, target={TargetClass}/{TargetCard}",
            message.CommandId,
            message.CommandType,
            message.Layer,
            message.Target.ClassCode,
            message.Target.CardId);

        logger.LogInformation(
            "Zabbix apply plan accepted in mode={Mode}, auto={AutoApplyEnabled}, safeApply={SafeApply}: command={CommandId}, type={CommandType}, rule={RuleId}",
            applyOptions.CurrentValue.Mode,
            applyOptions.CurrentValue.AutoApplyEnabled,
            applyOptions.CurrentValue.SafeApply,
            message.CommandId,
            message.CommandType,
            message.RuleId);

        return Task.CompletedTask;
    }
}
