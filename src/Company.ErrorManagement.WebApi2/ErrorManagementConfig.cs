using System;
using System.Web.Http;
using Company.ErrorManagement.Contracts;

namespace Company.ErrorManagement.WebApi2
{
    public sealed class ErrorManagementOptions
    {
        public string ApplicationName { get; set; } = "MainERP";
        public string EnvironmentName { get; set; } = "Development";
        public string? ApplicationVersion { get; set; }
        public string? ConnectionStringName { get; set; }
        public bool EnableClientErrorEndpoint { get; set; } = true;
    }

    public static class ErrorManagementConfig
    {
        public static void Register(
            HttpConfiguration config,
            IErrorReporter reporter,
            ICorrelationContext correlationContext,
            Action<ErrorManagementOptions> configure)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (reporter == null) throw new ArgumentNullException(nameof(reporter));
            if (correlationContext == null) throw new ArgumentNullException(nameof(correlationContext));
            if (configure == null) throw new ArgumentNullException(nameof(configure));

            var options = new ErrorManagementOptions();
            configure(options);

            config.MessageHandlers.Add(new ErrorCorrelationHandler(correlationContext));
            config.Filters.Add(new GlobalErrorManagementFilter(reporter, correlationContext, options));

            if (options.EnableClientErrorEndpoint)
            {
                config.Routes.MapHttpRoute(
                    name: "error-management-client",
                    routeTemplate: "api/error-management/client-errors",
                    defaults: new { controller = "ClientErrors" });
            }
        }
    }
}
