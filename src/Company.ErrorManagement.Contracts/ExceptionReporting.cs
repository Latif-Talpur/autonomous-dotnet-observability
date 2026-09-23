using System;
using System.Threading;
using System.Threading.Tasks;

namespace Company.ErrorManagement.Contracts
{
    public interface IExceptionReporter
    {
        Task<ErrorReceipt> ReportAsync(Exception exception, ErrorCaptureContext? context = null, CancellationToken cancellationToken = default);
    }
    public sealed class ErrorCaptureContext
    {
        public ErrorLayer Layer { get; set; } = ErrorLayer.Business;
        public string? CorrelationId { get; set; }
        public string? UserId { get; set; }
        public string? Module { get; set; }
        public string? Endpoint { get; set; }
        public string? Controller { get; set; }
        public string? Action { get; set; }
        public string? DbProvider { get; set; }
        public string? DbProcedure { get; set; }
        public int? HttpStatus { get; set; }
    }
}
