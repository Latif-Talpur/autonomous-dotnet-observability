using System;
using System.Threading;
using System.Threading.Tasks;
using Company.ErrorManagement.Contracts;

namespace Company.ErrorManagement.Core
{
    public sealed class ErrorReporter : IErrorReporter
    {
        private readonly IErrorNormalizer _normalizer;
        private readonly IPayloadRedactor _redactor;
        private readonly IErrorFingerprintProvider _fingerprint;
        private readonly IErrorReferenceGenerator _referenceGenerator;
        private readonly IErrorTransport _transport;

        public ErrorReporter(
            IErrorNormalizer normalizer,
            IPayloadRedactor redactor,
            IErrorFingerprintProvider fingerprint,
            IErrorReferenceGenerator referenceGenerator,
            IErrorTransport transport)
        {
            _normalizer = normalizer ?? throw new ArgumentNullException(nameof(normalizer));
            _redactor = redactor ?? throw new ArgumentNullException(nameof(redactor));
            _fingerprint = fingerprint ?? throw new ArgumentNullException(nameof(fingerprint));
            _referenceGenerator = referenceGenerator ?? throw new ArgumentNullException(nameof(referenceGenerator));
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        }

        public async Task<ErrorReceipt> CaptureAsync(ErrorEnvelope error, CancellationToken cancellationToken)
        {
            try
            {
                var normalized = _normalizer.Normalize(error);
                var redacted = _redactor.Redact(normalized);
                redacted.Fingerprint = _fingerprint.Generate(redacted);
                redacted.FingerprintVersion = _fingerprint.Version;

                if (string.IsNullOrWhiteSpace(redacted.ErrorReference))
                    redacted.ErrorReference = _referenceGenerator.Generate();

                return await _transport.SendAsync(redacted, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                return new ErrorReceipt
                {
                    ErrorReference = error?.ErrorReference ?? _referenceGenerator.Generate(),
                    CorrelationId = error?.CorrelationId ?? string.Empty,
                    EventId = error?.EventId ?? string.Empty,
                    Persisted = false,
                    CanReportIssue = false
                };
            }
        }
    }
}
