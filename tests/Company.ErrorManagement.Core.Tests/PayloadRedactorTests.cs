using Company.ErrorManagement.Contracts;
using Company.ErrorManagement.Core;
using FluentAssertions;
using Xunit;

namespace Company.ErrorManagement.Core.Tests;

public class PayloadRedactorTests
{
    [Fact]
    public void Redacts_bearer_tokens()
    {
        var redactor = new PayloadRedactor();
        var result = redactor.Redact("Authorization: Bearer eyJhbGciOiJIUzI1NiJ9.abc.def");
        result.Should().NotContain("eyJhbGciOiJIUzI1NiJ9.abc.def");
        result.Should().Contain("<redacted>");
    }

    [Fact]
    public void Redacts_connection_string_password()
    {
        var redactor = new PayloadRedactor();
        var result = redactor.Redact("Server=x;Database=y;User Id=admin;Password=Secret1;");
        result.Should().NotContain("Secret1");
        result.Should().Contain("Password=<redacted>");
    }

    [Fact]
    public void Redacts_sensitive_diagnostic_keys()
    {
        var redactor = new PayloadRedactor();
        var envelope = new ErrorEnvelope
        {
            Diagnostics = { ["password"] = "hunter2", ["custom"] = "safe" }
        };
        var result = redactor.Redact(envelope);
        result.Diagnostics["password"].Should().Be("<redacted>");
        result.Diagnostics["custom"].Should().Be("safe");
    }

    [Fact]
    public void Truncates_oversized_stack_traces()
    {
        var redactor = new PayloadRedactor(new RedactorOptions { MaxStackTraceChars = 20 });
        var envelope = new ErrorEnvelope { StackTrace = new string('x', 100) };
        var result = redactor.Redact(envelope);
        result.StackTrace!.Length.Should().BeLessThan(50);
        result.StackTrace!.Should().Contain("truncated");
    }
}
