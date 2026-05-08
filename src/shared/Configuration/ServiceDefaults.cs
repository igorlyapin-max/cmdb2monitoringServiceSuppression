using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Cmdb2MonitoringServiceSuppression.Shared.Logging;
using Cmdb2MonitoringServiceSuppression.Shared.Secrets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
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

        builder.Services.AddOptions<ConfigurationReloadOptions>()
            .Bind(builder.Configuration.GetSection(ConfigurationReloadOptions.SectionName))
            .Validate(options => options.HasValidRoute(), "ConfigurationReload:Route must start with '/'.")
            .Validate(
                options => !options.Enabled || !string.IsNullOrWhiteSpace(options.BearerToken),
                "ConfigurationReload:BearerToken or ConfigurationReload:BearerTokenSecret is required when configuration reload is enabled.")
            .ValidateOnStart();
        builder.Services.AddSingleton<ConfigurationReloadState>();

        builder.Services.AddOptions<DebugOptions>()
            .Bind(builder.Configuration.GetSection(DebugOptions.SectionName))
            .Validate(options => options.HasValidLevel(), "Debug level must be Basic or Verbose.")
            .ValidateOnStart();

        builder.Services.AddOptions<KafkaOptions>()
            .Bind(builder.Configuration.GetSection(KafkaOptions.SectionName))
            .Validate(options => options.HasValidBootstrapServers(), "Kafka:BootstrapServers is required when Kafka is enabled.")
            .Validate(options => options.HasValidSecurityProtocol(), "Kafka:SecurityProtocol is invalid.")
            .Validate(options => options.HasValidAutoOffsetReset(), "Kafka:AutoOffsetReset must be Earliest or Latest.")
            .ValidateOnStart();

        builder.Services.AddOptions<KafkaLoggingOptions>()
            .Bind(builder.Configuration.GetSection(KafkaLoggingOptions.SectionName))
            .Validate(options => options.HasValidMinimumLevel(), "KafkaLogging:MinimumLevel is invalid.")
            .Validate(options => options.HasValidTopic(), "KafkaLogging:Topic is required when Kafka logging is enabled.")
            .ValidateOnStart();

        builder.Services.AddOptions<ElkLoggingOptions>()
            .Bind(builder.Configuration.GetSection(ElkLoggingOptions.SectionName))
            .Validate(options => options.HasValidMinimumLevel(), "ElkLogging:MinimumLevel is invalid.")
            .Validate(options => options.HasValidEndpoint(), "ElkLogging:Endpoint must be an absolute URL when ELK logging is enabled.")
            .ValidateOnStart();

        builder.Logging.Services.TryAddEnumerable(ServiceDescriptor.Singleton<ILoggerProvider, KafkaLoggerProvider>());
        builder.Logging.Services.TryAddEnumerable(ServiceDescriptor.Singleton<ILoggerProvider, ElkLoggerProvider>());
    }

    public static void MapServiceHealth(this WebApplication app)
    {
        var serviceOptions = app.Services.GetRequiredService<IOptions<ServiceOptions>>().Value;

        app.MapGet(serviceOptions.HealthRoute, () => Results.Ok(new
        {
            service = serviceOptions.Name,
            status = "ok",
            version = ServiceVersion(),
            configurationVersion = app.Services.GetRequiredService<ConfigurationReloadState>().Snapshot().Version,
            configurationStartedAt = app.Services.GetRequiredService<ConfigurationReloadState>().Snapshot().StartedAtUtc,
            configurationReloadedAt = app.Services.GetRequiredService<ConfigurationReloadState>().Snapshot().LastReloadedAtUtc
        }));
    }

    public static void MapConfigurationReload(this WebApplication app, ConfigurationManager configuration)
    {
        var serviceOptions = app.Services.GetRequiredService<IOptions<ServiceOptions>>().Value;
        var initialReloadOptions = app.Services.GetRequiredService<IOptions<ConfigurationReloadOptions>>().Value;

        app.MapPost(initialReloadOptions.Route, async (
            HttpRequest request,
            IOptionsMonitor<ConfigurationReloadOptions> reloadOptions,
            ConfigurationReloadState reloadState,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            var options = reloadOptions.CurrentValue;
            if (!options.Enabled)
            {
                return Results.Problem(
                    title: "Configuration reload is disabled.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            if (!HasValidBearerToken(request, options.BearerToken))
            {
                return Results.Unauthorized();
            }

            var logger = loggerFactory.CreateLogger("ConfigurationReload");
            try
            {
                ((IConfigurationRoot)configuration).Reload();
                await configuration.ResolveSecretReferencesAsync(serviceOptions.Name, cancellationToken);
                ResetOptionsCaches(request.HttpContext.RequestServices);

                var snapshot = reloadState.MarkReloaded();
                logger.LogInformation(
                    "Configuration reloaded for {Service}; configuration version {ConfigurationVersion}.",
                    serviceOptions.Name,
                    snapshot.Version);

                return Results.Ok(new
                {
                    service = serviceOptions.Name,
                    status = "reloaded",
                    version = ServiceVersion(),
                    configurationVersion = snapshot.Version,
                    configurationStartedAt = snapshot.StartedAtUtc,
                    configurationReloadedAt = snapshot.LastReloadedAtUtc
                });
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException)
            {
                logger.LogError(ex, "Configuration reload failed for {Service}.", serviceOptions.Name);
                return Results.Problem(
                    title: "Configuration reload failed.",
                    detail: ex.Message,
                    statusCode: StatusCodes.Status500InternalServerError);
            }
        });
    }

    private static void ResetOptionsCaches(IServiceProvider services)
    {
        Reset<ServiceOptions>(services);
        Reset<ConfigurationReloadOptions>(services);
        Reset<DebugOptions>(services);
        Reset<KafkaOptions>(services);
        Reset<KafkaTopicsOptions>(services);
        Reset<KafkaLoggingOptions>(services);
        Reset<ElkLoggingOptions>(services);
        Reset<ApplyOptions>(services);
        Reset<CmdbuildOptions>(services);
        Reset<ZabbixOptions>(services);
        Reset<ConversionRulesOptions>(services);
    }

    private static void Reset<TOptions>(IServiceProvider services)
        where TOptions : class
    {
        services.GetService<IOptionsMonitorCache<TOptions>>()?.Clear();
    }

    private static bool HasValidBearerToken(HttpRequest request, string expectedToken)
    {
        if (string.IsNullOrWhiteSpace(expectedToken))
        {
            return false;
        }

        var authorization = request.Headers.Authorization.ToString();
        const string prefix = "Bearer ";
        if (!authorization.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var providedToken = authorization[prefix.Length..].Trim();
        if (string.IsNullOrWhiteSpace(providedToken))
        {
            return false;
        }

        var providedBytes = Encoding.UTF8.GetBytes(providedToken);
        var expectedBytes = Encoding.UTF8.GetBytes(expectedToken.Trim());
        return providedBytes.Length == expectedBytes.Length
            && CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes);
    }

    private static string ServiceVersion()
    {
        var assembly = Assembly.GetEntryAssembly();
        var informationalVersion = assembly?
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        return string.IsNullOrWhiteSpace(informationalVersion)
            ? assembly?.GetName().Version?.ToString() ?? "unknown"
            : informationalVersion;
    }
}
