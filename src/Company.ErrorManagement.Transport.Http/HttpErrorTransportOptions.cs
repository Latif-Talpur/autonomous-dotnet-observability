using System;

namespace Company.ErrorManagement.Transport.Http
{
    public sealed class HttpErrorTransportOptions
    {
        // Full endpoint, e.g. https://observability.example/api/error-management/events.
        // Server-side application key; never put this in browser configuration.
        public string? ApiKey { get; set; }
        public Uri? Endpoint { get; set; }
        public TimeSpan AttemptTimeout { get; set; } = TimeSpan.FromSeconds(5);
        public int MaxAttempts { get; set; } = 3;
        public TimeSpan InitialRetryDelay { get; set; } = TimeSpan.FromMilliseconds(250);
        public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromSeconds(5);
        public int MaxPayloadBytes { get; set; } = 256 * 1024;
        public int MaxReceiptBytes { get; set; } = 16 * 1024;

        internal void Validate()
        {
            if (Endpoint == null || !Endpoint.IsAbsoluteUri ||
                (Endpoint.Scheme != Uri.UriSchemeHttps &&
                 !(Endpoint.Scheme == Uri.UriSchemeHttp && Endpoint.IsLoopback)) ||
                !string.IsNullOrEmpty(Endpoint.UserInfo) || !string.IsNullOrEmpty(Endpoint.Fragment))
                throw new ArgumentException("Provide an HTTPS ingestion endpoint (HTTP is allowed for loopback).");
            if (ApiKey != null && (ApiKey.Length > 512 || ApiKey.IndexOfAny(new[] { '\r', '\n' }) >= 0))
                throw new ArgumentException("Invalid API key.");
            if (AttemptTimeout <= TimeSpan.Zero || AttemptTimeout > TimeSpan.FromMinutes(1) ||
                MaxAttempts < 1 || MaxAttempts > 5 ||
                InitialRetryDelay < TimeSpan.Zero || MaxRetryDelay < InitialRetryDelay ||
                MaxRetryDelay > TimeSpan.FromMinutes(1) || MaxPayloadBytes < 1 || MaxReceiptBytes < 1)
                throw new ArgumentOutOfRangeException(nameof(HttpErrorTransportOptions));
        }
    }
}
