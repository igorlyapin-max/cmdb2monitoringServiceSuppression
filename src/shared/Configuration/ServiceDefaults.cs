using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Cmdb2MonitoringServiceSuppression.Shared.Http;
using Cmdb2MonitoringServiceSuppression.Shared.Logging;
using Cmdb2MonitoringServiceSuppression.Shared.Observability;
using Cmdb2MonitoringServiceSuppression.Shared.Secrets;
using System.Collections.Concurrent;
using System.Diagnostics;
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

        builder.Services.AddOptions<RateLimitingOptions>()
            .Bind(builder.Configuration.GetSection(RateLimitingOptions.SectionName))
            .Validate(options => options.HasValidWindow(), "RateLimiting:WindowSeconds must be greater than zero.")
            .Validate(options => options.HasValidPermitLimit(), "RateLimiting:PermitLimit must be greater than zero.")
            .ValidateOnStart();

        builder.Services.AddOptions<SecurityHeadersOptions>()
            .Bind(builder.Configuration.GetSection(SecurityHeadersOptions.SectionName))
            .Validate(options => options.HasValidHstsMaxAge(), "SecurityHeaders:HstsMaxAgeSeconds must be greater than zero.")
            .ValidateOnStart();

        builder.Services.AddOptions<MetricsOptions>()
            .Bind(builder.Configuration.GetSection(MetricsOptions.SectionName))
            .Validate(options => options.HasValidRoute(), "Metrics:Route must start with '/'.")
            .ValidateOnStart();

        builder.Services.AddOptions<CorrelationOptions>()
            .Bind(builder.Configuration.GetSection(CorrelationOptions.SectionName))
            .Validate(options => options.HasValidHeaderName(), "Correlation:HeaderName is invalid.")
            .ValidateOnStart();

        builder.Services.AddOptions<ResilienceOptions>()
            .Bind(builder.Configuration.GetSection(ResilienceOptions.SectionName))
            .Validate(options => options.HasValidRetryPolicy(), "Resilience retry policy settings are invalid.")
            .Validate(options => options.HasValidCircuitBreaker(), "Resilience circuit breaker settings are invalid.")
            .ValidateOnStart();

        builder.Services.AddOptions<KafkaOptions>()
            .Bind(builder.Configuration.GetSection(KafkaOptions.SectionName))
            .Validate(options => options.HasValidBootstrapServers(), "Kafka:BootstrapServers is required when Kafka is enabled.")
            .Validate(options => options.HasValidSecurityProtocol(), "Kafka:SecurityProtocol is invalid.")
            .Validate(options => options.HasValidAutoOffsetReset(), "Kafka:AutoOffsetReset must be Earliest or Latest.")
            .Validate(options => options.HasValidProcessingPolicy(), "Kafka processing retry settings are invalid.")
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

        builder.Services.AddSingleton<AppMetrics>();
        builder.Services.AddTransient<CorrelationHttpMessageHandler>();
        builder.Services.AddTransient<ResilienceHttpMessageHandler>();
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IHttpMessageHandlerBuilderFilter, ServiceHttpMessageHandlerBuilderFilter>());
        builder.Logging.Services.TryAddEnumerable(ServiceDescriptor.Singleton<ILoggerProvider, KafkaLoggerProvider>());
        builder.Logging.Services.TryAddEnumerable(ServiceDescriptor.Singleton<ILoggerProvider, ElkLoggerProvider>());
    }

    public static void UseServiceDefaults(this WebApplication app)
    {
        app.UseCorrelation();
        app.UseSecurityHeaders();
        app.UseFixedWindowRateLimiting();
        app.UseRequestMetrics();
        app.MapServiceMetrics();
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

    private static void MapServiceMetrics(this WebApplication app)
    {
        var metricsOptions = app.Services.GetRequiredService<IOptionsMonitor<MetricsOptions>>().CurrentValue;
        var metrics = app.Services.GetRequiredService<AppMetrics>();
        app.MapGet(metricsOptions.Route, () =>
        {
            if (!metrics.Enabled)
            {
                return Results.NotFound();
            }

            return Results.Text(metrics.RenderPrometheus(), "text/plain; version=0.0.4; charset=utf-8");
        });
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
        Reset<RateLimitingOptions>(services);
        Reset<SecurityHeadersOptions>(services);
        Reset<MetricsOptions>(services);
        Reset<CorrelationOptions>(services);
        Reset<ResilienceOptions>(services);
        Reset<KafkaOptions>(services);
        Reset<KafkaTopicsOptions>(services);
        Reset<KafkaLoggingOptions>(services);
        Reset<ElkLoggingOptions>(services);
        Reset<ApplyOptions>(services);
        Reset<CmdbuildOptions>(services);
        Reset<ZabbixOptions>(services);
        Reset<ConversionRulesOptions>(services);
        Reset<SemanticDeduplicationOptions>(services);
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

    private static void UseCorrelation(this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            var options = context.RequestServices.GetRequiredService<IOptionsMonitor<CorrelationOptions>>().CurrentValue;
            if (!options.Enabled)
            {
                await next(context);
                return;
            }

            var headerName = CorrelationContext.HeaderName(options.HeaderName);
            var correlationId = context.Request.Headers.TryGetValue(headerName, out var provided)
                && !string.IsNullOrWhiteSpace(provided.ToString())
                    ? provided.ToString().Trim()
                    : CorrelationContext.NewId();

            context.Response.Headers[headerName] = correlationId;
            using var scope = CorrelationContext.Begin(correlationId);
            using var loggerScope = app.Logger.BeginScope(new Dictionary<string, object>
            {
                ["CorrelationId"] = correlationId
            });
            await next(context);
        });
    }

    private static void UseSecurityHeaders(this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            var options = context.RequestServices.GetRequiredService<IOptionsMonitor<SecurityHeadersOptions>>().CurrentValue;
            if (options.Enabled)
            {
                SetHeader(context, "X-Content-Type-Options", "nosniff");
                SetHeader(context, "X-Frame-Options", options.FrameOptions);
                SetHeader(context, "Referrer-Policy", options.ReferrerPolicy);
                SetHeader(context, "Permissions-Policy", options.PermissionsPolicy);
                if (!string.IsNullOrWhiteSpace(options.ContentSecurityPolicy))
                {
                    SetHeader(context, "Content-Security-Policy", options.ContentSecurityPolicy);
                }

                if (options.HstsEnabled)
                {
                    SetHeader(context, "Strict-Transport-Security", $"max-age={options.HstsMaxAgeSeconds}; includeSubDomains");
                }
            }

            await next(context);
        });
    }

    private static void UseFixedWindowRateLimiting(this WebApplication app)
    {
        var counters = new ConcurrentDictionary<string, RateLimitCounter>(StringComparer.Ordinal);
        app.Use(async (context, next) =>
        {
            var options = context.RequestServices.GetRequiredService<IOptionsMonitor<RateLimitingOptions>>().CurrentValue;
            if (!options.Enabled || IsRateLimitExcluded(context.Request.Path, options.ExcludedPathPrefixes))
            {
                await next(context);
                return;
            }

            var now = DateTimeOffset.UtcNow;
            var key = RateLimitKey(context);
            var counter = counters.AddOrUpdate(
                key,
                _ => new RateLimitCounter(now, 1),
                (_, current) => current.InWindow(now, options.WindowSeconds)
                    ? current.Increment()
                    : new RateLimitCounter(now, 1));

            if (counter.Count > options.PermitLimit)
            {
                var metrics = context.RequestServices.GetRequiredService<AppMetrics>();
                metrics.Increment(
                    "http_rate_limited_requests_total",
                    ("method", context.Request.Method),
                    ("path", context.Request.Path.Value ?? "/"));
                context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                context.Response.ContentType = "application/json; charset=utf-8";
                await context.Response.WriteAsync(
                    "{\"error\":\"rate limit exceeded\"}",
                    context.RequestAborted);
                return;
            }

            await next(context);
        });
    }

    private static void UseRequestMetrics(this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            var metrics = context.RequestServices.GetRequiredService<AppMetrics>();
            if (!metrics.Enabled)
            {
                await next(context);
                return;
            }

            var started = Stopwatch.GetTimestamp();
            try
            {
                await next(context);
            }
            finally
            {
                var elapsed = Stopwatch.GetElapsedTime(started);
                metrics.Increment(
                    "http_requests_total",
                    ("method", context.Request.Method),
                    ("path", context.Request.Path.Value ?? "/"),
                    ("status", context.Response.StatusCode.ToString()));
                metrics.ObserveSeconds(
                    "http_request_duration_seconds",
                    elapsed,
                    ("method", context.Request.Method),
                    ("path", context.Request.Path.Value ?? "/"),
                    ("status", context.Response.StatusCode.ToString()));
            }
        });
    }

    private static void SetHeader(HttpContext context, string name, string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            context.Response.Headers[name] = value;
        }
    }

    private static bool IsRateLimitExcluded(PathString path, IReadOnlyCollection<string> excludedPrefixes)
    {
        return excludedPrefixes.Any(prefix =>
            !string.IsNullOrWhiteSpace(prefix)
            && path.StartsWithSegments(prefix.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private static string RateLimitKey(HttpContext context)
    {
        var forwardedFor = context.Request.Headers["X-Forwarded-For"].ToString();
        var remote = string.IsNullOrWhiteSpace(forwardedFor)
            ? context.Connection.RemoteIpAddress?.ToString() ?? "unknown"
            : forwardedFor.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "unknown";

        return $"{remote}:{context.Request.Method}:{context.Request.Path.Value ?? "/"}";
    }

    private sealed record RateLimitCounter(DateTimeOffset WindowStartedAt, int Count)
    {
        public bool InWindow(DateTimeOffset now, int windowSeconds)
        {
            return now - WindowStartedAt < TimeSpan.FromSeconds(windowSeconds);
        }

        public RateLimitCounter Increment()
        {
            return this with { Count = Count + 1 };
        }
    }
}
