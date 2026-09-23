using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Company.ErrorManagement.Contracts;

namespace Company.ErrorManagement.Transport.Http
{
    public sealed class HttpErrorTransport : IErrorTransport
    {
        private readonly HttpClient _client;
        private readonly HttpErrorTransportOptions _options;
        private readonly JsonSerializerOptions _json = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };

        public HttpErrorTransport(HttpClient client, HttpErrorTransportOptions options)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            if (options == null) throw new ArgumentNullException(nameof(options));
            options.Validate();
            // Snapshot configuration so callers cannot mutate limits during a send.
            _options = new HttpErrorTransportOptions
            {
                Endpoint = options.Endpoint, AttemptTimeout = options.AttemptTimeout,
                MaxAttempts = options.MaxAttempts, InitialRetryDelay = options.InitialRetryDelay,
                MaxRetryDelay = options.MaxRetryDelay, MaxPayloadBytes = options.MaxPayloadBytes,
                MaxReceiptBytes = options.MaxReceiptBytes
            };
        }

        public async Task<ErrorReceipt> SendAsync(ErrorEnvelope envelope, CancellationToken cancellationToken)
        {
            if (envelope == null) throw new ArgumentNullException(nameof(envelope));
            if (string.IsNullOrWhiteSpace(envelope.EventId) ||
                string.IsNullOrWhiteSpace(envelope.ErrorReference))
                throw new ArgumentException("An event ID and error reference are required.", nameof(envelope));

            // Serialize once: every retry must carry exactly the same event and identity.
            var payload = JsonSerializer.Serialize(envelope, _json);
            if (Encoding.UTF8.GetByteCount(payload) > _options.MaxPayloadBytes)
                return NotPersisted(envelope);

            for (var attempt = 0; attempt < _options.MaxAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var delay = TimeSpan.FromMilliseconds(Math.Min(
                    _options.MaxRetryDelay.TotalMilliseconds,
                    _options.InitialRetryDelay.TotalMilliseconds * Math.Pow(2, attempt)));
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    timeout.CancelAfter(_options.AttemptTimeout);
                    try
                    {
                        using (var request = new HttpRequestMessage(HttpMethod.Post, _options.Endpoint))
                        {
                            request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
                            using (var response = await _client.SendAsync(request,
                                HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false))
                            {
                                if (response.IsSuccessStatusCode)
                                {
                                    var receipt = await ReadReceiptAsync(response, timeout.Token).ConfigureAwait(false);
                                    if (receipt != null && receipt.EventId == envelope.EventId &&
                                        !string.IsNullOrWhiteSpace(receipt.ErrorReference) && receipt.Persisted)
                                        return receipt;
                                    // A 2xx response with Persisted=false is not a persistence acknowledgement.
                                }
                                else
                                {
                                    var code = (int)response.StatusCode;
                                    if (code != 408 && code != 429 && code < 500)
                                        return NotPersisted(envelope);
                                    var retry = response.Headers.RetryAfter;
                                    var requested = retry?.Delta ??
                                        (retry?.Date.HasValue == true ? retry.Date.Value - DateTimeOffset.UtcNow : (TimeSpan?)null);
                                    if (requested.HasValue && requested.Value > delay)
                                        delay = requested.Value > _options.MaxRetryDelay
                                            ? _options.MaxRetryDelay : requested.Value;
                                }
                            }
                        }
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { }
                    catch (HttpRequestException) { }
                    catch (IOException) { }
                    catch (JsonException) { return NotPersisted(envelope); }
                }
                if (attempt + 1 < _options.MaxAttempts)
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
            return NotPersisted(envelope);
        }

        private async Task<ErrorReceipt?> ReadReceiptAsync(HttpResponseMessage response, CancellationToken token)
        {
            if (response.Content.Headers.ContentLength > _options.MaxReceiptBytes) return null;
            using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
            using (var buffer = new MemoryStream())
            {
                var bytes = new byte[4096];
                int count;
                while ((count = await stream.ReadAsync(bytes, 0, bytes.Length, token).ConfigureAwait(false)) != 0)
                {
                    if (buffer.Length + count > _options.MaxReceiptBytes) return null;
                    buffer.Write(bytes, 0, count);
                }
                return JsonSerializer.Deserialize<ErrorReceipt>(buffer.ToArray(), _json);
            }
        }

        private static ErrorReceipt NotPersisted(ErrorEnvelope envelope) => new ErrorReceipt
        {
            EventId = envelope.EventId,
            ErrorReference = envelope.ErrorReference ?? string.Empty,
            CorrelationId = envelope.CorrelationId,
            Fingerprint = envelope.Fingerprint,
            Persisted = false,
            CanReportIssue = false
        };
    }
}
