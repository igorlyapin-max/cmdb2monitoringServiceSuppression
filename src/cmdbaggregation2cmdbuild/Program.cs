using Cmdb2MonitoringServiceSuppression.Shared.Aggregation;
using Cmdb2MonitoringServiceSuppression.Shared.CmdbuildSchema;
using Cmdb2MonitoringServiceSuppression.Shared.Configuration;
using Cmdb2MonitoringServiceSuppression.Shared.Integrations;
using Cmdb2MonitoringServiceSuppression.Shared.Logging;
using Cmdb2MonitoringServiceSuppression.Shared.Messaging;
using Cmdb2MonitoringServiceSuppression.Shared.Secrets;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);
await builder.Configuration.ResolveSecretReferencesAsync("cmdbaggregation2cmdbuild");
builder.AddServiceDefaults();
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<CmdbuildSchemaFactory>();
builder.Services.AddOptions<CmdbuildOptions>()
    .Bind(builder.Configuration.GetSection(CmdbuildOptions.SectionName))
    .Validate(options => options.HasValidAuthMode(), "CMDBuild auth mode is invalid.")
    .Validate(options => options.RequestTimeoutMs > 0, "CMDBuild request timeout must be greater than zero.")
    .ValidateOnStart();
builder.Services.AddOptions<ApplyOptions>()
    .Bind(builder.Configuration.GetSection(ApplyOptions.SectionName))
    .Validate(options => options.HasValidMode(), "Apply mode must be manual, auto, or dry-run.")
    .ValidateOnStart();
builder.Services.AddOptions<KafkaTopicsOptions>()
    .Bind(builder.Configuration.GetSection(KafkaTopicsOptions.SectionName))
    .Validate(options => !string.IsNullOrWhiteSpace(options.EffectiveAggregationCommands()), "Aggregation command topic is required.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.DeadLetterTopic), "KafkaTopics:DeadLetterTopic is required.")
    .ValidateOnStart();
builder.Services.AddHttpClient<CmdbuildClient>();
builder.Services.AddSingleton<KafkaJsonProducer>();
var initialApplyOptions = builder.Configuration
    .GetSection(ApplyOptions.SectionName)
    .Get<ApplyOptions>() ?? new ApplyOptions();
if (initialApplyOptions.EffectiveAutoApplyEnabled())
{
    builder.Services.AddHostedService<CmdbuildAggregationCommandWorker>();
}

var app = builder.Build();
app.UseServiceDefaults();
if (!initialApplyOptions.EffectiveAutoApplyEnabled())
{
    app.Logger.LogInformation(
        "CMDBuild aggregation Kafka consumer is not started because Apply:AutoApplyEnabled is false and Apply:Mode is {Mode}.",
        initialApplyOptions.Mode);
}
app.MapServiceHealth();
app.MapConfigurationReload(builder.Configuration);

app.MapGet("/apply/status", (IOptionsMonitor<ApplyOptions> options) =>
{
    var current = options.CurrentValue;
    return Results.Ok(new
    {
        mode = current.Mode,
        autoApplyEnabled = current.AutoApplyEnabled,
        effectiveAutoApplyEnabled = current.EffectiveAutoApplyEnabled(),
        safeApply = current.SafeApply,
        kafkaConsumerStarted = initialApplyOptions.EffectiveAutoApplyEnabled()
    });
});

app.MapGet("/schema/preview", (
    string? prefix,
    string? language,
    string? serviceModelRoot,
    string? suppressionModelRoot,
    CmdbuildSchemaFactory factory) =>
{
    var options = new CmdbuildSchemaOptions
    {
        Prefix = prefix ?? "",
        Language = ParseLanguage(language),
        ServiceModelRoot = serviceModelRoot ?? "",
        SuppressionModelRoot = suppressionModelRoot ?? ""
    };

    return Results.Ok(factory.Build(options));
});

app.MapPost("/schema/preview", (
    CmdbuildSchemaOptions options,
    CmdbuildSchemaFactory factory) =>
{
    return Results.Ok(factory.Build(options));
});

app.MapPost("/schema/apply", async (
    CmdbuildSchemaApplyRequest request,
    CmdbuildSchemaFactory factory,
    CmdbuildClient client,
    CancellationToken cancellationToken) =>
{
    try
    {
        var schema = factory.Build(request.Options);
        var result = await client.ApplySchemaAsync(schema, request.Selection, cancellationToken);
        return result.Success
            ? Results.Ok(result)
            : Results.Problem(
                title: "One or more CMDBuild schema objects failed to apply.",
                detail: string.Join("; ", result.Items.Where(item => !item.Success).Select(item => $"{item.Kind} {item.Code}: {item.Message}")),
                extensions: new Dictionary<string, object?> { ["result"] = result },
                statusCode: StatusCodes.Status502BadGateway);
    }
    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or TimeoutException or InvalidOperationException)
    {
        return Results.Problem(ex.Message, statusCode: StatusCodes.Status502BadGateway);
    }
});

app.MapGet("/cmdbuild/check", async (
    CmdbuildClient client,
    CancellationToken cancellationToken) =>
{
    var result = await client.CheckConnectionAsync(cancellationToken);
    return result.Success ? Results.Ok(result) : Results.Problem(result.Error, statusCode: StatusCodes.Status502BadGateway);
});

app.MapGet("/cmdbuild/classes", async (
    string? rootPath,
    string? prefix,
    string? layer,
    bool? managedOnly,
    bool? includePrototypes,
    CmdbuildClient client,
    CancellationToken cancellationToken) =>
{
    try
    {
        var managedFilter = managedOnly == true
            ? new CmdbuildManagedClassFilter
            {
                Prefix = prefix ?? "",
                Layer = layer ?? ""
            }
            : null;
        var catalog = await client.ListClassesAsync(rootPath, managedFilter, includePrototypes == true, cancellationToken);

        if (string.IsNullOrWhiteSpace(rootPath) && managedFilter is null)
        {
            return Results.Ok(new { classes = catalog.Classes });
        }

        return Results.Ok(catalog);
    }
    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
    {
        return Results.Problem(ex.Message, statusCode: StatusCodes.Status502BadGateway);
    }
});

app.MapGet("/cmdbuild/classes/schema", async (
    CmdbuildClient client,
    CancellationToken cancellationToken) =>
{
    try
    {
        var classes = await client.ListClassSchemasAsync(cancellationToken);
        return Results.Ok(new { classes });
    }
    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
    {
        return Results.Problem(ex.Message, statusCode: StatusCodes.Status502BadGateway);
    }
});

app.MapGet("/cmdbuild/classes/instances", async (
    string? prefix,
    string? serviceModelRoot,
    string? suppressionModelRoot,
    CmdbuildClient client,
    CancellationToken cancellationToken) =>
{
    try
    {
        var catalog = await client.ListManagedClassInstancesAsync(
            prefix,
            serviceModelRoot,
            suppressionModelRoot,
            cancellationToken);
        return Results.Ok(catalog);
    }
    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
    {
        return Results.Problem(ex.Message, statusCode: StatusCodes.Status502BadGateway);
    }
});

app.MapGet("/cmdbuild/classes/{classCode}/cards", async (
    string classCode,
    string? layer,
    CmdbuildClient client,
    CancellationToken cancellationToken) =>
{
    try
    {
        var catalog = await client.ListClassCardsCatalogAsync(classCode, layer, cancellationToken);
        return Results.Ok(catalog);
    }
    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
    {
        return Results.Problem(ex.Message, statusCode: StatusCodes.Status502BadGateway);
    }
});

app.MapPost("/cmdbuild/classes/{classCode}/cards", async (
    string classCode,
    CmdbuildCreateCardRequest request,
    CmdbuildClient client,
    CancellationToken cancellationToken) =>
{
    try
    {
        var result = await client.CreateClassCardAsync(classCode, request.Values, cancellationToken);
        return Results.Ok(result);
    }
    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
    {
        return Results.Problem(ex.Message, statusCode: StatusCodes.Status502BadGateway);
    }
});

app.MapPut("/cmdbuild/classes/{classCode}/cards/{cardId}", async (
    string classCode,
    string cardId,
    CmdbuildCreateCardRequest request,
    CmdbuildClient client,
    CancellationToken cancellationToken) =>
{
    try
    {
        var result = await client.UpdateClassCardAsync(classCode, cardId, request.Values, cancellationToken);
        return Results.Ok(result);
    }
    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
    {
        return Results.Problem(ex.Message, statusCode: StatusCodes.Status502BadGateway);
    }
});

app.MapDelete("/cmdbuild/classes/{classCode}/cards/{cardId}", async (
    string classCode,
    string cardId,
    CmdbuildClient client,
    CancellationToken cancellationToken) =>
{
    try
    {
        var result = await client.DeleteClassCardAsync(classCode, cardId, cancellationToken);
        return Results.Ok(result);
    }
    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
    {
        return Results.Problem(ex.Message, statusCode: StatusCodes.Status502BadGateway);
    }
});

app.MapPost("/cmdbuild/domains/{domainCode}/relations", async (
    string domainCode,
    CmdbuildCreateRelationRequest request,
    CmdbuildClient client,
    CancellationToken cancellationToken) =>
{
    try
    {
        var result = await client.CreateDomainRelationAsync(domainCode, request, cancellationToken);
        return Results.Ok(result);
    }
    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
    {
        return Results.Problem(ex.Message, statusCode: StatusCodes.Status502BadGateway);
    }
});

app.MapDelete("/cmdbuild/domains/{domainCode}/relations/{relationId}", async (
    string domainCode,
    string relationId,
    CmdbuildClient client,
    CancellationToken cancellationToken) =>
{
    try
    {
        var result = await client.DeleteDomainRelationAsync(domainCode, relationId, cancellationToken);
        return Results.Ok(result);
    }
    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
    {
        return Results.Problem(ex.Message, statusCode: StatusCodes.Status502BadGateway);
    }
});

app.MapPost("/commands/apply/dry-run", (AggregationCommand command, IOptionsMonitor<ApplyOptions> options) =>
{
    return Results.Accepted(value: new
    {
        status = "accepted",
        target = "cmdbuild",
        mode = "dry-run",
        safe_apply = options.CurrentValue.SafeApply,
        command
    });
});

app.MapPost("/commands/apply", async (
    AggregationCommand command,
    CmdbuildClient client,
    CancellationToken cancellationToken) =>
{
    try
    {
        var result = await client.ApplyAggregationCommandAsync(command, cancellationToken);
        return Results.Ok(result);
    }
    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
    {
        return Results.Problem(ex.Message, statusCode: StatusCodes.Status502BadGateway);
    }
});

app.MapGet("/cmdbuild/domains", async (
    string? prefix,
    CmdbuildClient client,
    CancellationToken cancellationToken) =>
{
    try
    {
        var domains = await client.ListDomainsAsync(prefix, cancellationToken);
        return Results.Ok(new { domains });
    }
    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
    {
        return Results.Problem(ex.Message, statusCode: StatusCodes.Status502BadGateway);
    }
});

app.MapGet("/cmdbuild/domains/relations", async (
    string? prefix,
    CmdbuildClient client,
    CancellationToken cancellationToken) =>
{
    try
    {
        var catalog = await client.ListDomainRelationsAsync(prefix, cancellationToken);
        return Results.Ok(catalog);
    }
    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
    {
        return Results.Problem(ex.Message, statusCode: StatusCodes.Status502BadGateway);
    }
});

app.Run();

static SchemaLanguage ParseLanguage(string? language)
{
    return string.Equals(language, "en", StringComparison.OrdinalIgnoreCase)
        ? SchemaLanguage.En
        : SchemaLanguage.Ru;
}

public sealed class CmdbuildAggregationCommandWorker(
    IOptions<KafkaOptions> kafkaOptions,
    IOptions<KafkaTopicsOptions> topicOptions,
    IOptionsMonitor<ApplyOptions> applyOptions,
    IOptions<DebugOptions> debugOptions,
    CmdbuildClient client,
    IServiceProvider services,
    ILogger<CmdbuildAggregationCommandWorker> logger)
    : KafkaJsonConsumerWorker<AggregationCommand>(kafkaOptions, services, logger)
{
    protected override string Topic => topicOptions.Value.EffectiveAggregationCommands();

    protected override string ConsumerGroupId => "";

    protected override async Task HandleMessageAsync(
        AggregationCommand message,
        string key,
        CancellationToken cancellationToken)
    {
        logger.LogDebugBasic(
            debugOptions,
            "cmdbuild applier received command={CommandId}, type={CommandType}, layer={Layer}, target={TargetClass}/{TargetCard}",
            message.CommandId,
            message.CommandType,
            message.Layer,
            message.Target.ClassCode,
            message.Target.CardId);

        logger.LogInformation(
            "CMDBuild aggregation apply accepted in mode={Mode}, auto={AutoApplyEnabled}, safeApply={SafeApply}: command={CommandId}, type={CommandType}, rule={RuleId}",
            applyOptions.CurrentValue.Mode,
            applyOptions.CurrentValue.AutoApplyEnabled,
            applyOptions.CurrentValue.SafeApply,
            message.CommandId,
            message.CommandType,
            message.RuleId);

        if (!applyOptions.CurrentValue.EffectiveAutoApplyEnabled())
        {
            throw new InvalidOperationException("CMDBuild aggregation Kafka auto-apply is disabled.");
        }

        var result = await client.ApplyAggregationCommandAsync(message, cancellationToken);
        logger.LogInformation(
            "CMDBuild aggregation applied: command={CommandId}, target={TargetClass}/{TargetCard}, targetAction={TargetAction}, relation={RelationDomain}/{RelationId}, relationAction={RelationAction}",
            message.CommandId,
            message.Target.ClassCode,
            result.TargetCardId,
            result.TargetAction,
            result.RelationDomain,
            result.RelationId,
            result.RelationAction);
    }
}
