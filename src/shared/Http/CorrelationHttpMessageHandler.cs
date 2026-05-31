using Cmdb2MonitoringServiceSuppression.Shared.Configuration;
using Cmdb2MonitoringServiceSuppression.Shared.Observability;
using Microsoft.Extensions.Options;

namespace Cmdb2MonitoringServiceSuppression.Shared.Http;

public sealed class CorrelationHttpMessageHandler(IOptionsMonitor<CorrelationOptions> options) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var current = options.CurrentValue;
        if (current.Enabled)
        {
            var headerName = CorrelationContext.HeaderName(current.HeaderName);
            if (!request.Headers.Contains(headerName))
            {
                request.Headers.TryAddWithoutValidation(headerName, CorrelationContext.CurrentId ?? CorrelationContext.NewId());
            }
        }

        return base.SendAsync(request, cancellationToken);
    }
}
