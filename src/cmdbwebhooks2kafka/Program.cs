using System.Text.Json;
using Cmdb2MonitoringServiceSuppression.Shared.Aggregation;
using Cmdb2MonitoringServiceSuppression.Shared.Configuration;
using Cmdb2MonitoringServiceSuppression.Shared.Logging;
using Cmdb2MonitoringServiceSuppression.Shared.Messaging;
using Cmdb2MonitoringServiceSuppression.Shared.Secrets;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);
await builder.Configuration.ResolveSecretReferencesAsync("cmdbwebhooks2kafka");
builder.AddServiceDefaults();

builder.Services.AddOptions<KafkaTopicsOptions>()
    .Bind(builder.Configuration.GetSection(KafkaTopicsOptions.SectionName))
    .Validate(options => !string.IsNullOrWhiteSpace(options.CmdbWebhookEvents), "CMDB webhook topic is required.")
    .ValidateOnStart();

builder.Services.AddOptions<CmdbWebhookOptions>()
    .Bind(builder.Configuration.GetSection(CmdbWebhookOptions.SectionName))
    .Validate(options => !string.IsNullOrWhiteSpace(options.Route), "CMDB webhook route is required.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.Source), "CMDB webhook source is required.")
    .ValidateOnStart();
builder.Services.AddOptions<CmdbWebhookNormalizationOptions>()
    .Bind(builder.Configuration.GetSection(CmdbWebhookNormalizationOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddSingleton<CmdbRawEventFactory>();
builder.Services.AddSingleton<KafkaJsonProducer>();

var app = builder.Build();
app.MapServiceHealth();

var webhookOptions = app.Services.GetRequiredService<IOptions<CmdbWebhookOptions>>().Value;
var topicOptions = app.Services.GetRequiredService<IOptions<KafkaTopicsOptions>>().Value;

app.MapPost(webhookOptions.Route, async (HttpContext context, ILogger<Program> logger) =>
{
    using var payload = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: context.RequestAborted);
    var eventId = Guid.NewGuid().ToString("N");
    var rawEventFactory = context.RequestServices.GetRequiredService<CmdbRawEventFactory>();
    var producer = context.RequestServices.GetRequiredService<KafkaJsonProducer>();
    var debugOptions = context.RequestServices.GetRequiredService<IOptions<DebugOptions>>();
    var rawEvent = rawEventFactory.FromWebhook(payload.RootElement, webhookOptions.Source, eventId);

    await producer.PublishAsync(topicOptions.CmdbWebhookEvents, rawEvent.CardId, rawEvent, context.RequestAborted);

    logger.LogInformation(
        "Accepted CMDBuild webhook {EventId} from {Source}; class={ClassCode}; card={CardId}; eventType={EventType}; target topic {Topic}; published={Published}",
        eventId,
        webhookOptions.Source,
        rawEvent.ClassCode,
        rawEvent.CardId,
        rawEvent.EventType,
        topicOptions.CmdbWebhookEvents,
        producer.Enabled);
    logger.LogDebugBasic(
        debugOptions,
        "raw CMDB event normalized: eventId={EventId}, class={ClassCode}, card={CardId}, attributes={AttributeCount}",
        eventId,
        rawEvent.ClassCode,
        rawEvent.CardId,
        rawEvent.Attributes.Count);

    return Results.Accepted(value: new
    {
        event_id = eventId,
        source = webhookOptions.Source,
        target_topic = topicOptions.CmdbWebhookEvents,
        published = producer.Enabled,
        event_type = rawEvent.EventType,
        class_code = rawEvent.ClassCode,
        card_id = rawEvent.CardId,
        attribute_count = rawEvent.Attributes.Count
    });
});

app.Run();

public sealed class CmdbWebhookOptions
{
    public const string SectionName = "CmdbWebhook";

    public string Route { get; init; } = "/webhooks/cmdbuild";

    public string Source { get; init; } = "CMDBuild";
}
