using System;

namespace Company.ErrorManagement.Contracts
{
    public sealed class ErrorReceipt
    {
        public string ErrorReference { get; set; } = string.Empty;
        public string CorrelationId { get; set; } = string.Empty;
        public string EventId { get; set; } = string.Empty;
        public string? Fingerprint { get; set; }
        public bool Persisted { get; set; }
        public bool CanReportIssue { get; set; }
        public string SafeMessage { get; set; } = "Unable to process the request";
        public DateTime AcceptedAtUtc { get; set; } = DateTime.UtcNow;
    }
}
