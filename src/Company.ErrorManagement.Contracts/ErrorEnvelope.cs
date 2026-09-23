using System;
using System.Collections.Generic;

namespace Company.ErrorManagement.Contracts
{
    public sealed class ErrorEnvelope
    {
        public string EventId { get; set; } = Guid.NewGuid().ToString("N");
        public string? ErrorReference { get; set; }
        public string CorrelationId { get; set; } = string.Empty;
        public string? Fingerprint { get; set; }
        public int FingerprintVersion { get; set; } = 1;

        public string ApplicationCode { get; set; } = string.Empty;
        public string EnvironmentCode { get; set; } = string.Empty;
        public string? ApplicationVersion { get; set; }
        public string? Tenant { get; set; }

        public ErrorLayer Layer { get; set; } = ErrorLayer.Unknown;
        public string? Module { get; set; }
        public string? Screen { get; set; }
        public string? Component { get; set; }
        public string? Endpoint { get; set; }
        public string? Controller { get; set; }
        public string? Action { get; set; }
        public int? HttpStatus { get; set; }

        public string? ExceptionType { get; set; }
        public string? Message { get; set; }
        public string? StackTrace { get; set; }
        public string? InnerExceptionSummary { get; set; }

        public string? DbProvider { get; set; }
        public string? DbErrorCode { get; set; }
        public string? DbProcedure { get; set; }
        public int? DbLineNumber { get; set; }
        public bool IsTimeout { get; set; }

        public string? UserId { get; set; }
        public string? Browser { get; set; }
        public string? ClientVersion { get; set; }
        public string? Device { get; set; }
        public string? ClientIpHash { get; set; }

        public string? CategoryCode { get; set; }
        public string? SeverityCode { get; set; }
        public bool IsExpected { get; set; }
        public bool IsTransient { get; set; }

        public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;

        public IDictionary<string, string> Diagnostics { get; set; } = new Dictionary<string, string>();
    }

    public enum ErrorLayer
    {
        Unknown = 0,
        Angular = 1,
        WebApi2 = 2,
        AspNetCore = 3,
        Business = 4,
        Database = 5,
        BackgroundJob = 6
    }
}
