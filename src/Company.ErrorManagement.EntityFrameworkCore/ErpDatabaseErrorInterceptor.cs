using System;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Company.ErrorManagement.Contracts;
using Company.ErrorManagement.Core;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Company.ErrorManagement.EntityFrameworkCore
{
    public sealed class ErpDatabaseErrorInterceptor : DbCommandInterceptor
    {
        private readonly IExceptionReporter reporter;
        public ErpDatabaseErrorInterceptor(IExceptionReporter reporter) { this.reporter = reporter; }
        // Backward-compatible constructor; prefer DI registration to share deduplication across adapters.
        public ErpDatabaseErrorInterceptor(IErrorReporter reporter, ICorrelationContext correlation,
            ILogger<ErpDatabaseErrorInterceptor> logger, string applicationCode, string environmentCode)
            : this(new ExceptionReporter(reporter, correlation, applicationCode, environmentCode)) { }
        public override void CommandFailed(DbCommand command, CommandErrorEventData eventData)
            => Capture(command, eventData.Exception).GetAwaiter().GetResult();
        public override Task CommandFailedAsync(DbCommand command, CommandErrorEventData eventData, CancellationToken cancellationToken = default)
            => Capture(command, eventData.Exception);
        private async Task Capture(DbCommand command, Exception exception)
        {
            try { await reporter.ReportAsync(exception, new ErrorCaptureContext {
                Layer = ErrorLayer.Database, DbProvider = command.Connection?.GetType().FullName,
                DbProcedure = command.CommandType == CommandType.StoredProcedure ? command.CommandText : null
            }, CancellationToken.None).ConfigureAwait(false); }
            catch { /* Never replace the application's database failure with a reporting failure. */ }
        }
    }
}
