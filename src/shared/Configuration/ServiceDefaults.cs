using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Text.Json.Serialization;

namespace Cmdb2MonitoringServiceSuppression.Shared.Configuration;

public static class ServiceDefaults
{
    public static void AddServiceDefaults(this WebApplicationBuilder builder)
    {
        builder.Services.Configure<JsonOptions>(options =>
        {
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
        });

        builder.Services.AddOptions<ServiceOptions>()
            .Bind(builder.Configuration.GetSection(ServiceOptions.SectionName))
            .Validate(options => !string.IsNullOrWhiteSpace(options.Name), "Service name is required.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.HealthRoute), "Health route is required.")
            .ValidateOnStart();

        builder.Services.AddOptions<DebugOptions>()
            .Bind(builder.Configuration.GetSection(DebugOptions.SectionName))
            .Validate(options => options.HasValidLevel(), "Debug level must be Basic or Verbose.")
            .ValidateOnStart();
    }

    public static void MapServiceHealth(this WebApplication app)
    {
        var serviceOptions = app.Services.GetRequiredService<IOptions<ServiceOptions>>().Value;

        app.MapGet(serviceOptions.HealthRoute, () => Results.Ok(new
        {
            service = serviceOptions.Name,
            status = "ok"
        }));
    }
}
