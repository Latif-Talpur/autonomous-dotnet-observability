using Company.ErrorManagement.Core;
using FluentAssertions;
using Xunit;

namespace Company.ErrorManagement.Core.Tests;

public class ErrorReferenceGeneratorTests
{
    [Fact]
    public void Generate_Returns_NonEmpty_Reference()
    {
        var gen = new ErrorReferenceGenerator();
        var result = gen.Generate();
        result.Should().StartWith("ERR-");
        result.Length.Should().BeGreaterThan(8);
    }

    [Fact]
    public void Generate_Returns_Unique_References()
    {
        var gen = new ErrorReferenceGenerator();
        var a = gen.Generate();
        var b = gen.Generate();
        a.Should().NotBe(b);
    }
}
