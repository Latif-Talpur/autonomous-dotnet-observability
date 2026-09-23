using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Company.ErrorManagement.Contracts;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Company.ErrorManagement.Transport.Http
{
    /// <summary>
    /// Hosted service that drains the in-memory delivery queue and periodically replays
    /// the durable spool.  Delivery uses a single HTTP attempt per event; the circuit
    /// breaker and replay interval together provide retries across the background loop
    /// without blocking the host application.
    /// </summary>
    internal sealed class BackgroundDeliveryService : IHostedService, IDisposable
    {
        private readonly BackgroundDeliveryQueue _queue;
        private readonly LocalSpoolStore? _spool;
        private readonly CircuitBreaker _circuit;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly HttpErrorTransportOptions _singleAttemptOptions;
        private readonly SpoolOptions _spoolOptions;
        private readonly ILogger<BackgroundDeliveryService> _logger;

        private CancellationTokenSource? _cts;
        private Task? _deliveryTask;
        private Task? _replayTask;

        public BackgroundDeliveryService(
            BackgroundDeliveryQueue queue,
            LocalSpoolStore? spool,
            CircuitBreaker circuit,
            IHttpClientFactory httpClientFactory,
            HttpErrorTransportOptions transportOptions,
            SpoolOptions spoolOptions,
            ILogger<BackgroundDeliveryService> logger)
        {
            _queue = queue;
            _spool = spool;
            _circuit = circuit;
            _httpClientFactory = httpClientFactory;
            _spoolOptions = spoolOptions;
            _logger = logger;

            // Background delivery makes a single attempt per event.
            // The circuit breaker and spool replay handle long-term retries.
            _singleAttemptOptions = new HttpErrorTransportOptions
            {
                ApiKey = transportOptions.ApiKey,
                Endpoint = transportOptions.Endpoint,
                AttemptTimeout = transportOptions.AttemptTimeout,
                MaxAttempts = 1,
                InitialRetryDelay = transportOptions.InitialRetryDelay,
                MaxRetryDelay = transportOptions.MaxRetryDelay,
                MaxPayloadBytes = transportOptions.MaxPayloadBytes,
                MaxReceiptBytes = transportOptions.MaxReceiptBytes
            };
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _cts = new CancellationTokenSource();
            _deliveryTask = DeliverFromQueueAsync(_cts.Token);
            if (_spool != null)
                _replayTask = ReplaySpoolLoopAsync(_cts.Token);
            return Task.CompletedTask;
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            if (_cts == null) return;
            _cts.Cancel();

            // Give the loops a moment to observe cancellation before returning.
            try
            {
                var combined = Task.WhenAll(
                    _deliveryTask ?? Task.CompletedTask,
                    _replayTask ?? Task.CompletedTask);
                await Task.WhenAny(combined, Task.Delay(Timeout.Infinite, cancellationToken))
                          .ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Background delivery stopped with exception");
            }
        }

        public void Dispose() => _cts?.Dispose();

        // ── Delivery from in-memory queue ──────────────────────────────────────

        private async Task DeliverFromQueueAsync(CancellationToken ct)
        {
            try
            {
                while (await _queue.Reader.WaitToReadAsync(ct).ConfigureAwait(false))
                {
                    while (_queue.Reader.TryRead(out var envelope))
                    {
                        await DeliverAsync(envelope, spoolPath: null, ct).ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException) { }
        }

        // ── Spool replay loop ───────────────────────────────────────────────────

        private async Task ReplaySpoolLoopAsync(CancellationToken ct)
        {
            try
            {
                // Replay any leftover spool immediately on startup (handles previous crashes).
                await ReplaySinglePassAsync(ct).ConfigureAwait(false);

                while (!ct.IsCancellationRequested)
                {
                    await Task.Delay(_spoolOptions.ReplayInterval, ct).ConfigureAwait(false);
                    await ReplaySinglePassAsync(ct).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) { }
        }

        private async Task ReplaySinglePassAsync(CancellationToken ct)
        {
            if (_spool == null || _circuit.IsOpen) return;

            foreach (var (path, entry) in _spool.ReadAll())
            {
                if (ct.IsCancellationRequested) return;
                await DeliverAsync(entry.Envelope, spoolPath: path, ct).ConfigureAwait(false);
            }
        }

        // ── Single-event delivery ───────────────────────────────────────────────

        private async Task DeliverAsync(ErrorEnvelope envelope, string? spoolPath, CancellationToken ct)
        {
            if (!_circuit.AllowAttempt())
            {
                // Circuit open: spool the event and wait for the next replay window.
                if (spoolPath == null)
                    _spool?.TryWrite(envelope);
                return;
            }

            var transport = CreateTransport();
            try
            {
                var receipt = await transport.SendAsync(envelope, ct).ConfigureAwait(false);

                if (receipt.Persisted)
                {
                    _circuit.RecordSuccess();
                    if (spoolPath != null)
                        _spool?.Delete(spoolPath);
                }
                else
                {
                    // Non-persisted receipt means the transport exhausted its single attempt.
                    _circuit.RecordFailure();
                    if (spoolPath == null)
                        _spool?.TryWrite(envelope);

                    _logger.LogDebug(
                        "Event {EventId} not persisted; circuit failures = {Failures}",
                        envelope.EventId, GetFailureCount());
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                _circuit.RecordFailure();
                if (spoolPath == null)
                    _spool?.TryWrite(envelope);

                _logger.LogWarning(ex, "Unexpected error delivering event {EventId}", envelope.EventId);
            }
        }

        private HttpErrorTransport CreateTransport() =>
            new HttpErrorTransport(
                _httpClientFactory.CreateClient(nameof(HttpErrorTransport)),
                _singleAttemptOptions);

        private int GetFailureCount()
        {
            // Expose the internal failure count via the circuit's public state only;
            // this is informational for logging and doesn't need to be precise.
            return _circuit.IsOpen ? _spoolOptions.CircuitBreakerFailureThreshold : 0;
        }
    }
}
