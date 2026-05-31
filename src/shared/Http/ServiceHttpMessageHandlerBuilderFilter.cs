using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;

namespace Cmdb2MonitoringServiceSuppression.Shared.Http;

public sealed class ServiceHttpMessageHandlerBuilderFilter : IHttpMessageHandlerBuilderFilter
{
    public Action<HttpMessageHandlerBuilder> Configure(Action<HttpMessageHandlerBuilder> next)
    {
        return builder =>
        {
            next(builder);
            builder.AdditionalHandlers.Add(builder.Services.GetRequiredService<CorrelationHttpMessageHandler>());
            builder.AdditionalHandlers.Add(builder.Services.GetRequiredService<ResilienceHttpMessageHandler>());
        };
    }
}
