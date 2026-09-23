using System;
using Company.ErrorManagement.Contracts;
using Company.ErrorManagement.Core;
using Company.ErrorManagement.Transport.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Company.ErrorManagement.AspNetCore
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddErpErrorManagement(this IServiceCollection services, Action<ErrorManagementOptions> configure)
        {
            services.AddOptions<ErrorManagementOptions>().Configure(configure)
                .Validate(o => !string.IsNullOrWhiteSpace(o.ApplicationName) && !string.IsNullOrWhiteSpace(o.EnvironmentName), "Application and environment codes are required.")
                .Validate(o => CorrelationIds.IsValid(o.CorrelationHeaderName), "A valid correlation header name is required.").ValidateOnStart();
            services.TryAddSingleton<ICorrelationContext, AmbientCorrelationContext>();
            services.TryAddSingleton<IExceptionReporter>(s => {
                var o = s.GetRequiredService<IOptions<ErrorManagementOptions>>().Value;
                return new ExceptionReporter(s.GetRequiredService<IErrorReporter>(), s.GetRequiredService<ICorrelationContext>(), o.ApplicationName, o.EnvironmentName, o.ApplicationVersion);
            });
            return services;
        }
        public static IServiceCollection AddErpErrorManagementHttp(this IServiceCollection services,
            Action<ErrorManagementOptions> configure, Action<HttpErrorTransportOptions> transport)
        {
            services.AddErrorManagementHttpTransport(transport);
            return services.AddErpErrorManagement(configure);
        }
        public static IHttpClientBuilder AddErpCorrelationPropagation(this IHttpClientBuilder builder, string headerName = CorrelationIds.HeaderName)
        {
            if (!CorrelationIds.IsValid(headerName)) throw new ArgumentException("Invalid correlation header.", nameof(headerName));
            return builder.AddHttpMessageHandler(s => new CorrelationPropagationHandler(s.GetRequiredService<ICorrelationContext>(), headerName));
        }
    }
}
