using System;

namespace Company.ErrorManagement.Contracts
{
    public class ClientErrorReport
    {
        public string? EventId { get; set; }
        public string? Message { get; set; }
        public string? ExceptionType { get; set; }
        public string? StackTrace { get; set; }
        public string? Module { get; set; }
        public string? Screen { get; set; }
        public string? Component { get; set; }
        public string? Url { get; set; }
        public string? Endpoint { get; set; }
        public string? Browser { get; set; }
        public string? Device { get; set; }
        public string? ClientVersion { get; set; }
        public string? ApplicationVersion { get; set; }
        public string? UserId { get; set; } // Compatibility only; server derives identity from its principal.
        public int? HttpStatus { get; set; }
        public DateTime? OccurredAtUtc { get; set; }
    }
}
