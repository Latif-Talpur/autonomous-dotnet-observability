using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Company.ErrorManagement.Contracts;

namespace Company.ErrorManagement.Core
{
    public sealed class ExceptionReporter : IExceptionReporter
    {
        private readonly IErrorReporter reporter;
        private readonly ICorrelationContext correlation;
        private readonly string application, environment;
        private readonly string? version;
        private readonly ConditionalWeakTable<Exception, Captures> captures = new ConditionalWeakTable<Exception, Captures>();
        private sealed class Captures { public readonly Dictionary<string, Task<ErrorReceipt>> Items = new Dictionary<string, Task<ErrorReceipt>>(); }
        public ExceptionReporter(IErrorReporter reporter, ICorrelationContext correlation, string application, string environment, string? version = null)
        {
            this.reporter = reporter; this.correlation = correlation;
            this.application = application; this.environment = environment; this.version = version;
        }
        public Task<ErrorReceipt> ReportAsync(Exception exception, ErrorCaptureContext? context = null, CancellationToken cancellationToken = default)
        {
            if (exception == null) throw new ArgumentNullException(nameof(exception));
            context = context ?? new ErrorCaptureContext();
            var id = CorrelationIds.Normalize(context.CorrelationId ?? correlation.CorrelationId);
            var root = exception;
            while (root.InnerException != null) root = root.InnerException;
            var state = captures.GetValue(root, _ => new Captures());
            lock (state)
            {
                if (state.Items.TryGetValue(id, out var captured)) return captured;
                var envelope = ErrorNormalizer.FromException(exception, context.Layer);
                envelope.ApplicationCode = application; envelope.EnvironmentCode = environment; envelope.ApplicationVersion = version;
                envelope.CorrelationId = id; envelope.UserId = context.UserId ?? correlation.UserId;
                envelope.Module = context.Module; envelope.Endpoint = context.Endpoint; envelope.Controller = context.Controller; envelope.Action = context.Action;
                envelope.HttpStatus = context.HttpStatus; envelope.DbProvider = context.DbProvider; envelope.DbProcedure = context.DbProcedure;
                envelope.ErrorReference = new ErrorReferenceGenerator().Generate();
                DatabaseExceptionMetadata.Apply(exception, envelope);
                // First capture wins: command interception and later wrappers share one receipt per request.
                var task = CaptureSafely(envelope, cancellationToken); state.Items[id] = task; return task;
            }
        }
        private async Task<ErrorReceipt> CaptureSafely(ErrorEnvelope envelope, CancellationToken ct)
        {
            try { return await reporter.CaptureAsync(envelope, ct).ConfigureAwait(false); }
            catch { return new ErrorReceipt { EventId = envelope.EventId, ErrorReference = envelope.ErrorReference!, CorrelationId = envelope.CorrelationId, Persisted = false, CanReportIssue = false }; }
        }
    }
}
