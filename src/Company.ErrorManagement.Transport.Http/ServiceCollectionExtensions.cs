using System;
using System.IO;
using System.Net.Http;
using Company.ErrorManagement.Contracts;
using Company.ErrorManagement.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Company.ErrorManagement.Transport.Http
{
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Registers a direct (blocking-retry) HTTP transport.
        /// Suitable when callers can tolerate a short network wait per error, or for the
        /// central service itself.  Use <see cref="AddErrorManagementHttpTransportWithSpool"/>
        /// for host adapters that must not block business requests.
        /// </summary>
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
            RegisterCoreServices(services);
            return services;
        }

        /// <summary>
        /// Registers a non-blocking HTTP transport backed by a bounded in-memory queue,
        /// a durable file spool, and a circuit breaker.
        /// <see cref="IErrorTransport.SendAsync"/> returns immediately with a provisional
        /// receipt; actual delivery happens on a background thread.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="configure">Configure the HTTP endpoint and per-attempt limits.</param>
        /// <param name="configureSpool">
        /// Configure the queue size, spool directory, and circuit breaker thresholds.
        /// When omitted, a 2 000-event in-memory queue is used with no durable spool.
        /// </param>
        public static IServiceCollection AddErrorManagementHttpTransportWithSpool(
            this IServiceCollection services,
            Action<HttpErrorTransportOptions> configure,
            Action<SpoolOptions>? configureSpool = null)
        {
            if (services == null) throw new ArgumentNullException(nameof(services));
            if (configure == null) throw new ArgumentNullException(nameof(configure));

            // Register the underlying typed HTTP client (used by BackgroundDeliveryService).
            var transportOptions = new HttpErrorTransportOptions();
            configure(transportOptions);
            transportOptions.Validate();
            services.AddSingleton(transportOptions);
            services.AddHttpClient<HttpErrorTransport>()
                .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
                {
                    AllowAutoRedirect = false,
                    UseCookies = false
                });

            // Spool options.
            var spoolOptions = new SpoolOptions();
            configureSpool?.Invoke(spoolOptions);
            services.AddSingleton(spoolOptions);

            // Optional durable spool store (only when a directory is configured).
            if (!string.IsNullOrWhiteSpace(spoolOptions.SpoolDirectory))
            {
                var spoolDir = spoolOptions.SpoolDirectory!;
                services.AddSingleton(sp =>
                    new LocalSpoolStore(spoolDir, spoolOptions.MaxSpoolSizeBytes));
            }

            // Circuit breaker.
            services.AddSingleton(sp =>
                new CircuitBreaker(
                    sp.GetRequiredService<SpoolOptions>().CircuitBreakerFailureThreshold,
                    sp.GetRequiredService<SpoolOptions>().CircuitBreakerOpenDuration));

            // Delivery queue (in-memory channel + spool overflow).
            services.AddSingleton<BackgroundDeliveryQueue>(sp =>
                new BackgroundDeliveryQueue(
                    sp.GetRequiredService<SpoolOptions>(),
                    sp.GetService<LocalSpoolStore>(),
                    sp.GetRequiredService<ILogger<BackgroundDeliveryQueue>>()));

            // Non-blocking transport facade.
            services.Replace(ServiceDescriptor.Singleton<IErrorTransport>(sp => new SpooledHttpErrorTransport(sp.GetRequiredService<BackgroundDeliveryQueue>(), sp.GetRequiredService<ILogger<SpooledHttpErrorTransport>>())));

            // Background delivery and spool replay service.
            services.AddSingleton<BackgroundDeliveryService>(sp =>
                new BackgroundDeliveryService(
                    sp.GetRequiredService<BackgroundDeliveryQueue>(),
                    sp.GetService<LocalSpoolStore>(),
                    sp.GetRequiredService<CircuitBreaker>(),
                    sp.GetRequiredService<System.Net.Http.IHttpClientFactory>(),
                    sp.GetRequiredService<HttpErrorTransportOptions>(),
                    sp.GetRequiredService<SpoolOptions>(),
                    sp.GetRequiredService<ILogger<BackgroundDeliveryService>>()));
            services.AddSingleton<IHostedService>(sp =>
                sp.GetRequiredService<BackgroundDeliveryService>());

            RegisterCoreServices(services);
            return services;
        }

        private static void RegisterCoreServices(IServiceCollection services)
        {
            services.TryAddSingleton<IErrorNormalizer, ErrorNormalizer>();
            services.TryAddSingleton<IPayloadRedactor>(_ => new PayloadRedactor());
            services.TryAddSingleton<IErrorFingerprintProvider, FingerprintProvider>();
            services.TryAddSingleton<IErrorReferenceGenerator, ErrorReferenceGenerator>();
            services.TryAddSingleton<ICorrelationContext, AmbientCorrelationContext>();
            services.TryAddTransient<IErrorReporter, ErrorReporter>();
        }
    }
}
