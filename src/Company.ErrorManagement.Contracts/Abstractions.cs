using System.Threading;
using System.Threading.Tasks;

namespace Company.ErrorManagement.Contracts
{
    public interface IErrorReporter
    {
        Task<ErrorReceipt> CaptureAsync(ErrorEnvelope error, CancellationToken cancellationToken);
    }

    public interface IErrorTransport
    {
        Task<ErrorReceipt> SendAsync(ErrorEnvelope error, CancellationToken cancellationToken);
    }

    public interface IErrorFingerprintProvider
    {
        string Generate(ErrorEnvelope error);
        int Version { get; }
    }

    public interface ICorrelationContext
    {
        string CorrelationId { get; }
        string? UserId { get; }
        void Set(string correlationId, string? userId = null);
    }

    public interface IErrorReferenceGenerator
    {
        string Generate();
    }

    public interface IPayloadRedactor
    {
        string? Redact(string? value);
        ErrorEnvelope Redact(ErrorEnvelope envelope);
    }

    public interface IErrorNormalizer
    {
        ErrorEnvelope Normalize(ErrorEnvelope envelope);
    }
}
