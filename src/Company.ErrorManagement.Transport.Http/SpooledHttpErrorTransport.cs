using System;
using System.Threading;
using System.Threading.Tasks;
using Company.ErrorManagement.Contracts;
using Microsoft.Extensions.Logging;

namespace Company.ErrorManagement.Transport.Http
{
    /// <summary>
    /// Non-blocking <see cref="IErrorTransport"/> that enqueues each event for background
    /// delivery and returns a provisional receipt immediately.  The host application is
    /// never blocked waiting for network I/O; delivery failures are handled transparently
    /// via the durable spool and circuit breaker.
    /// </summary>
    public sealed class SpooledHttpErrorTransport : IErrorTransport
    {
        private readonly BackgroundDeliveryQueue _queue;
        private readonly ILogger<SpooledHttpErrorTransport> _logger;

        public SpooledHttpErrorTransport(
            BackgroundDeliveryQueue queue,
            ILogger<SpooledHttpErrorTransport> logger)
        {
            _queue = queue ?? throw new ArgumentNullException(nameof(queue));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Enqueues the event for background delivery and returns immediately with a
        /// provisional receipt.  <see cref="ErrorReceipt.Persisted"/> is always false
        /// because persistence is confirmed asynchronously.  The <see cref="ErrorReceipt.ErrorReference"/>
        /// is the pre-assigned reference so callers can show it to the user right away.
        /// </summary>
        public Task<ErrorReceipt> SendAsync(ErrorEnvelope envelope, CancellationToken cancellationToken)
        {
            if (envelope == null) throw new ArgumentNullException(nameof(envelope));

            if (!_queue.TryEnqueue(envelope))
            {
                _logger.LogWarning(
                    "Event {EventId} dropped — delivery queue and spool are both full",
                    envelope.EventId);
            }

            // Return a provisional receipt. The reference is already set by ErrorReporter
            // before SendAsync is called, so callers can display it to the user immediately.
            return Task.FromResult(new ErrorReceipt
            {
                EventId = envelope.EventId,
                ErrorReference = envelope.ErrorReference ?? string.Empty,
                CorrelationId = envelope.CorrelationId,
                Fingerprint = envelope.Fingerprint,
                Persisted = false,
                CanReportIssue = false
            });
        }
    }
}
