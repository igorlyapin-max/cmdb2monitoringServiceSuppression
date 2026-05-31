using Cmdb2MonitoringServiceSuppression.Shared.Configuration;
using Cmdb2MonitoringServiceSuppression.Shared.Observability;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cmdb2MonitoringServiceSuppression.Shared.Http;

public sealed class ResilienceHttpMessageHandler(
    IOptionsMonitor<ResilienceOptions> options,
    AppMetrics metrics,
    ILogger<ResilienceHttpMessageHandler> logger) : DelegatingHandler
{
    private readonly object gate = new();
    private int consecutiveFailures;
    private DateTimeOffset openUntilUtc;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var current = options.CurrentValue;
        if (!current.Enabled)
        {
            return await base.SendAsync(request, cancellationToken);
        }

        ThrowIfCircuitOpen(request, current);

        var template = await RequestTemplate.CreateAsync(request, cancellationToken);
        request.Dispose();

        Exception? lastException = null;
        HttpResponseMessage? lastResponse = null;
        var maxAttempts = Math.Max(1, current.MaxAttempts);

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var started = DateTimeOffset.UtcNow;
            using var attemptRequest = template.Create();
            try
            {
                var response = await base.SendAsync(attemptRequest, cancellationToken);
                metrics.ObserveSeconds(
                    "http_client_request_duration_seconds",
                    DateTimeOffset.UtcNow - started,
                    ("method", template.Method.Method),
                    ("host", template.Host),
                    ("status", ((int)response.StatusCode).ToString()));

                if (!ShouldRetry(response) || attempt == maxAttempts)
                {
                    RecordCircuitResult(request, response.IsSuccessStatusCode || !ShouldRetry(response), current);
                    return response;
                }

                lastResponse = response;
                metrics.Increment(
                    "http_client_retries_total",
                    ("method", template.Method.Method),
                    ("host", template.Host),
                    ("reason", ((int)response.StatusCode).ToString()));
                response.Dispose();
            }
            catch (Exception ex) when (IsTransientException(ex) && attempt < maxAttempts)
            {
                lastException = ex;
                metrics.Increment(
                    "http_client_retries_total",
                    ("method", template.Method.Method),
                    ("host", template.Host),
                    ("reason", ex.GetType().Name));
                logger.LogWarning(
                    ex,
                    "Transient HTTP request failure for {Method} {Host}; retry attempt {Attempt}/{MaxAttempts}.",
                    template.Method,
                    template.Host,
                    attempt + 1,
                    maxAttempts);
            }
            catch (Exception ex) when (IsTransientException(ex))
            {
                RecordCircuitResult(request, success: false, current);
                metrics.Increment(
                    "http_client_failures_total",
                    ("method", template.Method.Method),
                    ("host", template.Host),
                    ("reason", ex.GetType().Name));
                throw;
            }

            await Task.Delay(Delay(current, attempt), cancellationToken);
        }

        RecordCircuitResult(request, success: false, current);
        lastResponse?.Dispose();
        throw new HttpRequestException(
            $"HTTP request failed after {maxAttempts} attempts.",
            lastException);
    }

    private void ThrowIfCircuitOpen(HttpRequestMessage request, ResilienceOptions current)
    {
        lock (gate)
        {
            if (openUntilUtc <= DateTimeOffset.UtcNow)
            {
                return;
            }

            metrics.Increment(
                "http_client_circuit_open_total",
                ("host", request.RequestUri?.Host ?? "unknown"));
            throw new HttpRequestException(
                $"Circuit breaker is open for {request.RequestUri?.Host ?? "unknown"} until {openUntilUtc:O}.");
        }
    }

    private void RecordCircuitResult(HttpRequestMessage request, bool success, ResilienceOptions current)
    {
        lock (gate)
        {
            if (success)
            {
                consecutiveFailures = 0;
                return;
            }

            consecutiveFailures++;
            if (consecutiveFailures < current.CircuitBreakerFailures)
            {
                return;
            }

            openUntilUtc = DateTimeOffset.UtcNow.AddSeconds(current.CircuitBreakerBreakSeconds);
            consecutiveFailures = 0;
            metrics.Increment(
                "http_client_circuit_breaks_total",
                ("host", request.RequestUri?.Host ?? "unknown"));
            logger.LogWarning(
                "Circuit breaker opened for {Host} until {OpenUntilUtc}.",
                request.RequestUri?.Host ?? "unknown",
                openUntilUtc);
        }
    }

    private static bool ShouldRetry(HttpResponseMessage response)
    {
        var statusCode = (int)response.StatusCode;
        return statusCode == StatusCodes.Status408RequestTimeout
            || statusCode == StatusCodes.Status429TooManyRequests
            || statusCode >= StatusCodes.Status500InternalServerError;
    }

    private static bool IsTransientException(Exception ex)
    {
        return ex is HttpRequestException or TaskCanceledException or TimeoutException;
    }

    private static TimeSpan Delay(ResilienceOptions options, int attempt)
    {
        var baseDelay = Math.Max(0, options.BaseDelayMs);
        var maxDelay = Math.Max(baseDelay, options.MaxDelayMs);
        var exponential = baseDelay * Math.Pow(2, Math.Max(0, attempt - 1));
        var jitter = Random.Shared.Next(0, Math.Max(1, baseDelay + 1));
        var delay = Math.Min(maxDelay, (int)exponential + jitter);
        return TimeSpan.FromMilliseconds(delay);
    }

    private sealed class RequestTemplate
    {
        private readonly Uri? requestUri;
        private readonly Version version;
        private readonly HttpVersionPolicy versionPolicy;
        private readonly IReadOnlyList<KeyValuePair<string, IEnumerable<string>>> requestHeaders;
        private readonly byte[]? content;
        private readonly IReadOnlyList<KeyValuePair<string, IEnumerable<string>>> contentHeaders;

        private RequestTemplate(
            HttpMethod method,
            Uri? requestUri,
            Version version,
            HttpVersionPolicy versionPolicy,
            IReadOnlyList<KeyValuePair<string, IEnumerable<string>>> requestHeaders,
            byte[]? content,
            IReadOnlyList<KeyValuePair<string, IEnumerable<string>>> contentHeaders)
        {
            Method = method;
            this.requestUri = requestUri;
            this.version = version;
            this.versionPolicy = versionPolicy;
            this.requestHeaders = requestHeaders;
            this.content = content;
            this.contentHeaders = contentHeaders;
            Host = requestUri?.Host ?? "unknown";
        }

        public HttpMethod Method { get; }

        public string Host { get; }

        public static async Task<RequestTemplate> CreateAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            byte[]? content = null;
            var contentHeaders = new List<KeyValuePair<string, IEnumerable<string>>>();
            if (request.Content is not null)
            {
                content = await request.Content.ReadAsByteArrayAsync(cancellationToken);
                contentHeaders.AddRange(request.Content.Headers.Select(header =>
                    new KeyValuePair<string, IEnumerable<string>>(header.Key, header.Value.ToArray())));
            }

            return new RequestTemplate(
                request.Method,
                request.RequestUri,
                request.Version,
                request.VersionPolicy,
                request.Headers.Select(header =>
                    new KeyValuePair<string, IEnumerable<string>>(header.Key, header.Value.ToArray())).ToArray(),
                content,
                contentHeaders);
        }

        public HttpRequestMessage Create()
        {
            var request = new HttpRequestMessage(Method, requestUri)
            {
                Version = version,
                VersionPolicy = versionPolicy
            };

            foreach (var header in requestHeaders)
            {
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            if (content is not null)
            {
                request.Content = new ByteArrayContent(content);
                foreach (var header in contentHeaders)
                {
                    request.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }

            return request;
        }
    }
}
