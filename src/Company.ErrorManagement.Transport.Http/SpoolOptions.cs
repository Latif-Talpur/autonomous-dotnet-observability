using System;

namespace Company.ErrorManagement.Transport.Http
{
    public sealed class SpoolOptions
    {
        /// <summary>
        /// Maximum number of events to hold in the in-memory queue before spilling to disk.
        /// Events that exceed this limit are written to the spool directory (if configured) or dropped.
        /// </summary>
        public int MaxQueuedEvents { get; set; } = 2000;

        /// <summary>
        /// Directory for durable spool files. Null disables durable spooling; events that cannot
        /// fit in the in-memory queue will be dropped with a warning log entry.
        /// </summary>
        public string? SpoolDirectory { get; set; }

        /// <summary>
        /// Maximum combined size of all spool files. Events that would exceed this limit are dropped.
        /// </summary>
        public long MaxSpoolSizeBytes { get; set; } = 52_428_800; // 50 MB

        /// <summary>
        /// How often the background service scans the spool directory for events to replay.
        /// </summary>
        public TimeSpan ReplayInterval { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Number of consecutive delivery failures before the circuit breaker opens.
        /// </summary>
        public int CircuitBreakerFailureThreshold { get; set; } = 5;

        /// <summary>
        /// How long the circuit stays open before a single probe attempt is allowed.
        /// </summary>
        public TimeSpan CircuitBreakerOpenDuration { get; set; } = TimeSpan.FromSeconds(60);
    }
}
