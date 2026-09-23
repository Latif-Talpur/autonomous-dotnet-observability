using System.Threading.Channels;
using Company.ErrorManagement.Contracts;
using Microsoft.Extensions.Logging;

namespace Company.ErrorManagement.Transport.Http
{
    /// <summary>
    /// Bounded in-memory channel with a durable spool as the overflow destination.
    /// Enqueue never blocks: events that exceed the channel capacity fall through to the
    /// spool, and events that exceed the spool limit are dropped with a warning.
    /// </summary>
    internal sealed class BackgroundDeliveryQueue
    {
        private readonly Channel<ErrorEnvelope> _channel;
        private readonly LocalSpoolStore? _spool;
        private readonly ILogger _logger;

        public ChannelReader<ErrorEnvelope> Reader => _channel.Reader;

        public BackgroundDeliveryQueue(SpoolOptions options, LocalSpoolStore? spool, ILogger<BackgroundDeliveryQueue> logger)
        {
            _spool = spool;
            _logger = logger;
            _channel = Channel.CreateBounded<ErrorEnvelope>(new BoundedChannelOptions(options.MaxQueuedEvents)
            {
                FullMode = BoundedChannelFullMode.DropWrite,
                SingleReader = true,
                SingleWriter = false
            });
        }

        /// <summary>
        /// Enqueues an envelope for background delivery.
        /// Falls through to the durable spool when the in-memory queue is full.
        /// Logs a warning and returns false if both the queue and spool are full.
        /// </summary>
        public bool TryEnqueue(ErrorEnvelope envelope)
        {
            if (_channel.Writer.TryWrite(envelope)) return true;

            if (_spool != null)
            {
                if (_spool.TryWrite(envelope)) return true;
                _logger.LogWarning(
                    "Spool storage limit reached; dropping event {EventId}. " +
                    "Increase MaxSpoolSizeBytes or resolve the outage.",
                    envelope.EventId);
            }
            else
            {
                _logger.LogWarning(
                    "Delivery queue is full and no spool directory is configured; " +
                    "dropping event {EventId}. Configure SpoolDirectory or increase MaxQueuedEvents.",
                    envelope.EventId);
            }

            return false;
        }
    }
}
