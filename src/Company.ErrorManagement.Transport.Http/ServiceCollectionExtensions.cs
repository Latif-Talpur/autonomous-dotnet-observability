using System;
using System.Net.Http;
using Company.ErrorManagement.Contracts;
using Company.ErrorManagement.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Company.ErrorManagement.Transport.Http
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddErrorManagementHttpTransport(
            this IServiceCollection services, Action<HttpErrorTransportOptions> configure)
        {
            if (services == null) throw new ArgumentNullException(nameof(services));
            if (configure == null) throw new ArgumentNullException(nameof(configure));
            var options = new HttpErrorTransportOptions();
            configure(options);
            options.Validate();
            services.AddSingleton(options);
            services.AddHttpClient<HttpErrorTransport>()
                .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
                {
                    AllowAutoRedirect = false,
                    UseCookies = false
                });
            services.Replace(ServiceDescriptor.Transient<IErrorTransport>(provider =>
                provider.GetRequiredService<HttpErrorTransport>()));
            services.TryAddSingleton<IErrorNormalizer, ErrorNormalizer>();
            services.TryAddSingleton<IPayloadRedactor>(_ => new PayloadRedactor());
            services.TryAddSingleton<IErrorFingerprintProvider, FingerprintProvider>();
            services.TryAddSingleton<IErrorReferenceGenerator, ErrorReferenceGenerator>();
            services.TryAddSingleton<ICorrelationContext, AmbientCorrelationContext>();
            services.TryAddTransient<IErrorReporter, ErrorReporter>();
            return services;
        }
    }
}
