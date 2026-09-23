using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Web.Http;
using System.Web.Http.Dependencies;
using System.Web.Http.ExceptionHandling;
using Company.ErrorManagement.Contracts;
using Company.ErrorManagement.Core;
using Company.ErrorManagement.Transport.Http;

namespace Company.ErrorManagement.WebApi2
{
    public sealed class ErrorManagementOptions
    {
        public string ApplicationName { get; set; } = "ERP";
        public string EnvironmentName { get; set; } = "DEV";
        public string? ApplicationVersion { get; set; }
        public string? ConnectionStringName { get; set; }
        public bool EnableClientErrorEndpoint { get; set; } = true;
    }
    public static class ErrorManagementConfig
    {
        private const string Registered = "Company.ErrorManagement.Registered";
        public static IExceptionReporter RegisterHttp(HttpConfiguration config, Action<ErrorManagementOptions> configure,
            Action<HttpErrorTransportOptions> configureTransport)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (configureTransport == null) throw new ArgumentNullException(nameof(configureTransport));
            var options = Options(config, configure);
            var transportOptions = new HttpErrorTransportOptions(); configureTransport(transportOptions);
            var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false });
            try
            {
                var reporter = new ErrorReporter(new ErrorNormalizer(), new PayloadRedactor(), new FingerprintProvider(), new ErrorReferenceGenerator(), new HttpErrorTransport(client, transportOptions));
                return RegisterCore(config, reporter, new AmbientCorrelationContext(), options, client);
            }
            catch { client.Dispose(); throw; }
        }
        public static void Register(HttpConfiguration config, IErrorReporter reporter, ICorrelationContext correlationContext, Action<ErrorManagementOptions> configure)
        {
            if (reporter == null) throw new ArgumentNullException(nameof(reporter));
            if (correlationContext == null) throw new ArgumentNullException(nameof(correlationContext));
            RegisterCore(config, reporter, correlationContext, Options(config, configure), null);
        }
        private static ErrorManagementOptions Options(HttpConfiguration config, Action<ErrorManagementOptions> configure)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (configure == null) throw new ArgumentNullException(nameof(configure));
            if (config.Properties.ContainsKey(Registered)) throw new InvalidOperationException("Error management is already registered.");
            var options = new ErrorManagementOptions(); configure(options);
            if (string.IsNullOrWhiteSpace(options.ApplicationName) || string.IsNullOrWhiteSpace(options.EnvironmentName)) throw new ArgumentException("Application and environment codes are required.");
            return options;
        }
        private static IExceptionReporter RegisterCore(HttpConfiguration config, IErrorReporter reporter, ICorrelationContext correlation,
            ErrorManagementOptions options, IDisposable? ownedClient)
        {
            var exceptions = new ExceptionReporter(reporter, correlation, options.ApplicationName, options.EnvironmentName, options.ApplicationVersion);
            config.DependencyResolver = new FrameworkResolver(config.DependencyResolver, reporter, exceptions, correlation, options, ownedClient);
            config.MessageHandlers.Insert(0, new ErrorCorrelationHandler(correlation));
            config.Services.Add(typeof(IExceptionLogger), new ErpExceptionLogger(exceptions));
            config.Services.Replace(typeof(IExceptionHandler), new ErpExceptionHandler(exceptions));
            if (options.EnableClientErrorEndpoint)
                config.Routes.MapHttpRoute("error-management-client", "api/error-management/client-errors", new { controller = "ClientErrors" });
            config.Properties[Registered] = true;
            return exceptions;
        }
        // Decorate, rather than replace, the host container. Configure the host resolver before registration.
        private sealed class FrameworkResolver : IDependencyResolver
        {
            private readonly IDependencyResolver inner; private readonly IErrorReporter reporter; private readonly IExceptionReporter exceptions;
            private readonly ICorrelationContext correlation; private readonly ErrorManagementOptions options; private readonly IDisposable? owner;
            public FrameworkResolver(IDependencyResolver inner, IErrorReporter reporter, IExceptionReporter exceptions, ICorrelationContext correlation, ErrorManagementOptions options, IDisposable? owner)
            { this.inner=inner;this.reporter=reporter;this.exceptions=exceptions;this.correlation=correlation;this.options=options;this.owner=owner; }
            private object? Resolve(Type type)
            {
                if(type==typeof(IErrorReporter))return reporter;
                if(type==typeof(IExceptionReporter))return exceptions;
                if(type==typeof(ICorrelationContext))return correlation;
                if(type==typeof(ErrorManagementOptions))return options;
                if(type==typeof(ClientErrorsController))return new ClientErrorsController(reporter,correlation,options);
                return null;
            }
            public object? GetService(Type type)=>Resolve(type)??inner.GetService(type);
            public IEnumerable<object> GetServices(Type type) { var value=Resolve(type);return value==null?inner.GetServices(type):new[]{value}; }
            public IDependencyScope BeginScope()=>new Scope(inner.BeginScope(),Resolve);
            public void Dispose(){owner?.Dispose();inner.Dispose();}
            private sealed class Scope : IDependencyScope
            {
                private readonly IDependencyScope inner;private readonly Func<Type,object?> resolve;
                public Scope(IDependencyScope inner,Func<Type,object?> resolve){this.inner=inner;this.resolve=resolve;}
                public object? GetService(Type type)=>resolve(type)??inner.GetService(type);
                public IEnumerable<object> GetServices(Type type){var value=resolve(type);return value==null?inner.GetServices(type):new[]{value};}
                public void Dispose()=>inner.Dispose();
            }
        }
    }
}
