using System;
using Company.ErrorManagement.Contracts;
using Company.ErrorManagement.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Company.ErrorManagement.Persistence.Sqlite
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddErrorManagementSqlite(this IServiceCollection services, Action<SqliteOptions>? configure = null)
        {
            services.AddOptions<SqliteOptions>();
            if (configure != null) services.Configure(configure);

            services.AddSingleton<ISqliteConnectionFactory, SqliteConnectionFactory>();
            services.AddSingleton<SqliteMigrationRunner>();

            services.AddSingleton<IErrorRepository, ErrorRepository>();
            services.AddSingleton<ITicketRepository, TicketRepository>();
            services.AddSingleton<IReportingRepository, ReportingRepository>();
            services.AddSingleton<IApplicationRepository, ApplicationRepository>();

            services.AddSingleton<IErrorTransport, DirectSqlErrorTransport>();
            services.AddSingleton<IErrorFingerprintProvider, FingerprintProvider>();
            services.AddSingleton<IPayloadRedactor>(_ => new PayloadRedactor());
            services.AddSingleton<IErrorNormalizer, ErrorNormalizer>();
            services.AddSingleton<IErrorReferenceGenerator, ErrorReferenceGenerator>();
            services.AddSingleton<ICorrelationContext, AmbientCorrelationContext>();
            services.AddSingleton<IErrorReporter, ErrorReporter>();

            return services;
        }
    }
}
