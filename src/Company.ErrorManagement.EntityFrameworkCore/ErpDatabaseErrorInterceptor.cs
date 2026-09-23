using System;
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
        private readonly IErrorReporter _reporter;
        private readonly ICorrelationContext _correlation;
        private readonly ILogger<ErpDatabaseErrorInterceptor> _logger;
        private readonly string _applicationCode;
        private readonly string _environmentCode;

        public ErpDatabaseErrorInterceptor(
            IErrorReporter reporter,
            ICorrelationContext correlation,
            ILogger<ErpDatabaseErrorInterceptor> logger,
            string applicationCode,
            string environmentCode)
        {
            _reporter = reporter;
            _correlation = correlation;
            _logger = logger;
            _applicationCode = applicationCode;
            _environmentCode = environmentCode;
        }

        public override void CommandFailed(DbCommand command, CommandErrorEventData eventData)
        {
            // Stamp before the async call so the ASP.NET Core exception middleware can see it
            // synchronously and skip its own capture, preventing a double occurrence record.
            eventData.Exception.Data["erp:capturing"] = true;
            _ = CaptureAsync(command, eventData.Exception, CancellationToken.None);
            base.CommandFailed(command, eventData);
        }

        public override Task CommandFailedAsync(DbCommand command, CommandErrorEventData eventData, CancellationToken cancellationToken = default)
        {
            eventData.Exception.Data["erp:capturing"] = true;
            _ = CaptureAsync(command, eventData.Exception, cancellationToken);
            return base.CommandFailedAsync(command, eventData, cancellationToken);
        }

        private async Task CaptureAsync(DbCommand command, Exception exception, CancellationToken cancellationToken)
        {
            try
            {
                var envelope = ErrorNormalizer.FromException(exception, ErrorLayer.Database);
                envelope.ApplicationCode = _applicationCode;
                envelope.EnvironmentCode = _environmentCode;
                envelope.CorrelationId = _correlation.CorrelationId ?? Guid.NewGuid().ToString("N");
                envelope.DbProvider = command.Connection?.GetType().Name;
                envelope.DbProcedure = command.CommandType == System.Data.CommandType.StoredProcedure
                    ? command.CommandText
                    : null;
                envelope.IsTimeout = IsTimeout(exception);
                envelope.CategoryCode = "DATABASE";

                await _reporter.CaptureAsync(envelope, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception captureEx)
            {
                _logger.LogWarning(captureEx, "Failed to capture EF Core command failure");
            }
        }

        private static bool IsTimeout(Exception exception)
        {
            var current = exception;
            while (current != null)
            {
                if (current is TimeoutException) return true;
                if (current.GetType().Name.Contains("Timeout", StringComparison.OrdinalIgnoreCase)) return true;
                current = current.InnerException;
            }
            return false;
        }
    }
}
