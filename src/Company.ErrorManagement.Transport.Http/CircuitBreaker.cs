using System;
using System.Threading;

namespace Company.ErrorManagement.Transport.Http
{
    /// <summary>
    /// Thread-safe circuit breaker with Closed → Open → HalfOpen → Closed transitions.
    /// </summary>
    internal sealed class CircuitBreaker
    {
        private readonly int _threshold;
        private readonly TimeSpan _openDuration;

        private int _consecutiveFailures;
        // MinValue == closed; anything else == open-until ticks.
        private long _openUntilTicks = long.MinValue;
        // 1 while a half-open probe is in flight.
        private int _halfOpenInFlight;

        public CircuitBreaker(int threshold, TimeSpan openDuration)
        {
            if (threshold < 1) throw new ArgumentOutOfRangeException(nameof(threshold));
            _threshold = threshold;
            _openDuration = openDuration;
        }

        public bool IsOpen
        {
            get
            {
                var until = Interlocked.Read(ref _openUntilTicks);
                return until != long.MinValue && DateTime.UtcNow.Ticks < until;
            }
        }

        /// <summary>
        /// Returns true if a delivery attempt should be made.
        /// For a half-open circuit only one caller gets true at a time.
        /// </summary>
        public bool AllowAttempt()
        {
            var until = Interlocked.Read(ref _openUntilTicks);
            if (until == long.MinValue) return true;              // closed
            if (DateTime.UtcNow.Ticks < until) return false;     // open

            // Past the open-until time: allow exactly one probe (half-open).
            return Interlocked.CompareExchange(ref _halfOpenInFlight, 1, 0) == 0;
        }

        public void RecordSuccess()
        {
            Interlocked.Exchange(ref _consecutiveFailures, 0);
            Interlocked.Exchange(ref _openUntilTicks, long.MinValue);
            Interlocked.Exchange(ref _halfOpenInFlight, 0);
        }

        public void RecordFailure()
        {
            Interlocked.Exchange(ref _halfOpenInFlight, 0);
            var failures = Interlocked.Increment(ref _consecutiveFailures);
            if (failures >= _threshold)
                Interlocked.Exchange(ref _openUntilTicks,
                    DateTime.UtcNow.Add(_openDuration).Ticks);
        }
    }
}
