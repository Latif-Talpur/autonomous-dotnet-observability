using Company.ErrorManagement.Contracts;
using Company.ErrorManagement.Core;
using FluentAssertions;
using Xunit;

namespace Company.ErrorManagement.Core.Tests;

public class FingerprintProviderTests
{
    private readonly FingerprintProvider _sut = new();

    [Fact]
    public void Generate_IsDeterministic()
    {
        var envelope = new ErrorEnvelope
        {
            ApplicationCode = "ERP",
            EnvironmentCode = "PROD",
            Layer = ErrorLayer.AspNetCore,
            ExceptionType = "System.InvalidOperationException",
            Message = "Value 12345 not allowed",
            StackTrace = "at Something.Do() in File.cs:line 42"
        };

        var a = _sut.Generate(envelope);
        var b = _sut.Generate(envelope);

        a.Should().Be(b);
        a.Length.Should().Be(64);
    }

    [Fact]
    public void Generate_IgnoresVariableTokens()
    {
        var one = new ErrorEnvelope
        {
            ApplicationCode = "ERP",
            EnvironmentCode = "PROD",
            Layer = ErrorLayer.AspNetCore,
            ExceptionType = "System.InvalidOperationException",
            Message = "Value 12345 not allowed",
            StackTrace = "at File.cs:line 42"
        };
        var two = new ErrorEnvelope
        {
            ApplicationCode = "ERP",
            EnvironmentCode = "PROD",
            Layer = ErrorLayer.AspNetCore,
            ExceptionType = "System.InvalidOperationException",
            Message = "Value 999 not allowed",
            StackTrace = "at File.cs:line 88"
        };

        _sut.Generate(one).Should().Be(_sut.Generate(two));
    }

    [Fact]
    public void Generate_DiffersAcrossEnvironments()
    {
        var prod = new ErrorEnvelope { ApplicationCode = "ERP", EnvironmentCode = "PROD", Layer = ErrorLayer.AspNetCore, Message = "x" };
        var dev = new ErrorEnvelope { ApplicationCode = "ERP", EnvironmentCode = "DEV", Layer = ErrorLayer.AspNetCore, Message = "x" };
        _sut.Generate(prod).Should().NotBe(_sut.Generate(dev));
    }
}
