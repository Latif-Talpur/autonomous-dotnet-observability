using Company.ErrorManagement.Contracts;
using Company.ErrorManagement.Core;
using FluentAssertions;
using Xunit;

namespace Company.ErrorManagement.Core.Tests;

public class ErrorReporterTests
{
    [Fact]
    public async Task CaptureAsync_Returns_Receipt_Even_When_Transport_Throws()
    {
        var reporter = BuildReporter(failTransport: true);
        var receipt = await reporter.CaptureAsync(new ErrorEnvelope
        {
            ApplicationCode = "APP",
            EnvironmentCode = "DEV",
            Message = "kaboom"
        }, CancellationToken.None);

        receipt.Should().NotBeNull();
        receipt.Persisted.Should().BeFalse();
    }

    [Fact]
    public async Task CaptureAsync_Assigns_Fingerprint_Before_Transport()
    {
        string? captured = null;
        var reporter = BuildReporter(onCapture: e => { captured = e.Fingerprint; });

        await reporter.CaptureAsync(new ErrorEnvelope
        {
            ApplicationCode = "APP",
            EnvironmentCode = "PROD",
            Layer = ErrorLayer.AspNetCore,
            Message = "some error"
        }, CancellationToken.None);

        captured.Should().NotBeNullOrWhiteSpace();
        captured!.Length.Should().Be(64);
    }

    [Fact]
    public async Task CaptureAsync_Generates_ErrorReference_When_Not_Set()
    {
        string? capturedRef = null;
        var reporter = BuildReporter(onCapture: e => { capturedRef = e.ErrorReference; });

        var envelope = new ErrorEnvelope
        {
            ApplicationCode = "APP",
            EnvironmentCode = "PROD",
            Message = "needs reference",
            ErrorReference = null
        };

        await reporter.CaptureAsync(envelope, CancellationToken.None);
        capturedRef.Should().StartWith("ERR-");
    }

    private static ErrorReporter BuildReporter(bool failTransport = false, Action<ErrorEnvelope>? onCapture = null)
    {
        var normalizer = new ErrorNormalizer();
        var redactor = new PayloadRedactor();
        var fingerprint = new FingerprintProvider();
        var refGen = new ErrorReferenceGenerator();
        IErrorTransport transport = failTransport
            ? new ThrowingTransport()
            : new CapturingTransport(onCapture);

        return new ErrorReporter(normalizer, redactor, fingerprint, refGen, transport);
    }

    private sealed class ThrowingTransport : IErrorTransport
    {
        public Task<ErrorReceipt> SendAsync(ErrorEnvelope error, CancellationToken cancellationToken)
            => throw new InvalidOperationException("transport unavailable");
    }

    private sealed class CapturingTransport : IErrorTransport
    {
        private readonly Action<ErrorEnvelope>? _callback;
        public CapturingTransport(Action<ErrorEnvelope>? callback) => _callback = callback;
        public Task<ErrorReceipt> SendAsync(ErrorEnvelope error, CancellationToken cancellationToken)
        {
            _callback?.Invoke(error);
            return Task.FromResult(new ErrorReceipt
            {
                ErrorReference = error.ErrorReference ?? "ERR-TEST",
                CorrelationId = error.CorrelationId,
                Persisted = true
            });
        }
    }
}
