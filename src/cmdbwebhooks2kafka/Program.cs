using System.Text.Json;
using Cmdb2MonitoringServiceSuppression.Shared.Configuration;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);
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

var app = builder.Build();
app.MapServiceHealth();

var webhookOptions = app.Services.GetRequiredService<IOptions<CmdbWebhookOptions>>().Value;
var topicOptions = app.Services.GetRequiredService<IOptions<KafkaTopicsOptions>>().Value;

app.MapPost(webhookOptions.Route, async (HttpContext context, ILogger<Program> logger) =>
{
    using var payload = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: context.RequestAborted);
    var eventId = Guid.NewGuid().ToString("N");

    logger.LogInformation(
        "Accepted CMDBuild webhook {EventId} from {Source}; target topic {Topic}",
        eventId,
        webhookOptions.Source,
        topicOptions.CmdbWebhookEvents);

    return Results.Accepted(value: new
    {
        event_id = eventId,
        source = webhookOptions.Source,
        target_topic = topicOptions.CmdbWebhookEvents,
        payload_kind = payload.RootElement.ValueKind.ToString()
    });
});

app.Run();

public sealed class CmdbWebhookOptions
{
    public const string SectionName = "CmdbWebhook";

    public string Route { get; init; } = "/webhooks/cmdbuild";

    public string Source { get; init; } = "CMDBuild";
}
