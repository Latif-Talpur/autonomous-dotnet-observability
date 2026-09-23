using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Company.ErrorManagement.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Company.ErrorManagement.EntityFrameworkCore
{
    public sealed class ErpConnectionErrorInterceptor : DbConnectionInterceptor
    {
        private readonly IExceptionReporter reporter;
        public ErpConnectionErrorInterceptor(IExceptionReporter reporter) { this.reporter = reporter; }
        public override void ConnectionFailed(DbConnection connection, ConnectionErrorEventData eventData) => Capture(connection, eventData.Exception).GetAwaiter().GetResult();
        public override Task ConnectionFailedAsync(DbConnection connection, ConnectionErrorEventData eventData, CancellationToken cancellationToken = default) => Capture(connection, eventData.Exception);
        private async Task Capture(DbConnection connection, Exception exception)
        {
            try { await reporter.ReportAsync(exception, new ErrorCaptureContext { Layer = ErrorLayer.Database, DbProvider = connection.GetType().FullName }, CancellationToken.None).ConfigureAwait(false); }
            catch { }
        }
    }
    public sealed class ErpSaveChangesErrorInterceptor : SaveChangesInterceptor
    {
        private readonly IExceptionReporter reporter;
        public ErpSaveChangesErrorInterceptor(IExceptionReporter reporter) { this.reporter = reporter; }
        public override void SaveChangesFailed(DbContextErrorEventData eventData) => Capture(eventData).GetAwaiter().GetResult();
        public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default) => Capture(eventData);
        private async Task Capture(DbContextErrorEventData data)
        {
            try { await reporter.ReportAsync(data.Exception, new ErrorCaptureContext { Layer = ErrorLayer.Database, DbProvider = data.Context?.Database.ProviderName }, CancellationToken.None).ConfigureAwait(false); }
            catch { }
        }
    }
    public static class ErpDatabaseRegistration
    {
        public static IServiceCollection AddErpDatabaseInterception(this IServiceCollection services)
        {
            services.TryAddSingleton<ErpDatabaseErrorInterceptor>(s => new ErpDatabaseErrorInterceptor(s.GetRequiredService<IExceptionReporter>()));
            services.TryAddSingleton<ErpConnectionErrorInterceptor>();
            services.TryAddSingleton<ErpSaveChangesErrorInterceptor>();
            return services;
        }
        public static DbContextOptionsBuilder AddErpErrorInterceptors(this DbContextOptionsBuilder options, IServiceProvider services) =>
            options.AddInterceptors(services.GetRequiredService<ErpDatabaseErrorInterceptor>(), services.GetRequiredService<ErpConnectionErrorInterceptor>(), services.GetRequiredService<ErpSaveChangesErrorInterceptor>());
    }
}
