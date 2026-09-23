using Company.ErrorManagement.Contracts;
using Company.ErrorManagement.Core;
using FluentAssertions;
using Xunit;

namespace Company.ErrorManagement.Core.Tests;

public class ErrorNormalizerTests
{
    private readonly ErrorNormalizer _sut = new();

    [Fact]
    public void Normalize_Sets_OccurredAtUtc_When_Default()
    {
        var envelope = new ErrorEnvelope { OccurredAtUtc = default };
        var result = _sut.Normalize(envelope);
        result.OccurredAtUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void Normalize_Generates_EventId_When_Missing()
    {
        var envelope = new ErrorEnvelope { EventId = "" };
        var result = _sut.Normalize(envelope);
        result.EventId.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Normalize_Trims_Message_Whitespace()
    {
        var envelope = new ErrorEnvelope { Message = "  error  " };
        var result = _sut.Normalize(envelope);
        result.Message.Should().Be("error");
    }

    [Fact]
    public void Normalize_Clamps_InvalidHttpStatus()
    {
        var envelope = new ErrorEnvelope { HttpStatus = 999 };
        var result = _sut.Normalize(envelope);
        result.HttpStatus.Should().BeNull();
    }

    [Fact]
    public void FromException_Captures_ExceptionType_And_Message()
    {
        var ex = new InvalidOperationException("test failure");
        var result = ErrorNormalizer.FromException(ex, ErrorLayer.Business);
        result.ExceptionType.Should().Be("System.InvalidOperationException");
        result.Message.Should().Be("test failure");
        result.Layer.Should().Be(ErrorLayer.Business);
    }

    [Fact]
    public void FromException_Captures_Inner_Exception_Summary()
    {
        var inner = new ArgumentNullException("param1");
        var outer = new InvalidOperationException("outer", inner);
        var result = ErrorNormalizer.FromException(outer, ErrorLayer.AspNetCore);
        result.InnerExceptionSummary.Should().Contain("ArgumentNullException");
    }
}
