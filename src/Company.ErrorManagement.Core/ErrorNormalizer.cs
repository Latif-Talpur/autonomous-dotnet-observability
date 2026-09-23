using System;
using System.Text;
using Company.ErrorManagement.Contracts;

namespace Company.ErrorManagement.Core
{
    public sealed class ErrorNormalizer : IErrorNormalizer
    {
        public ErrorEnvelope Normalize(ErrorEnvelope envelope)
        {
            if (envelope == null) throw new ArgumentNullException(nameof(envelope));

            if (envelope.OccurredAtUtc == default)
                envelope.OccurredAtUtc = DateTime.UtcNow;

            if (string.IsNullOrWhiteSpace(envelope.EventId))
                envelope.EventId = Guid.NewGuid().ToString("N");

            if (envelope.Message != null)
                envelope.Message = envelope.Message.Trim();

            envelope.ApplicationCode = (envelope.ApplicationCode ?? string.Empty).Trim();
            envelope.EnvironmentCode = (envelope.EnvironmentCode ?? string.Empty).Trim();
            envelope.CorrelationId = (envelope.CorrelationId ?? string.Empty).Trim();

            if (envelope.HttpStatus.HasValue && (envelope.HttpStatus < 100 || envelope.HttpStatus > 599))
                envelope.HttpStatus = null;

            return envelope;
        }

        public static ErrorEnvelope FromException(Exception exception, ErrorLayer layer)
        {
            if (exception == null) throw new ArgumentNullException(nameof(exception));

            var envelope = new ErrorEnvelope
            {
                Layer = layer,
                ExceptionType = exception.GetType().FullName,
                Message = exception.Message,
                StackTrace = exception.StackTrace,
                OccurredAtUtc = DateTime.UtcNow
            };

            if (exception.InnerException != null)
            {
                var sb = new StringBuilder();
                var current = exception.InnerException;
                var depth = 0;
                while (current != null && depth < 5)
                {
                    sb.Append(current.GetType().FullName).Append(": ").Append(current.Message).Append(" | ");
                    current = current.InnerException;
                    depth++;
                }
                envelope.InnerExceptionSummary = sb.ToString().TrimEnd(' ', '|');
            }

            return envelope;
        }
    }
}
