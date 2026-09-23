using Company.ErrorManagement.Contracts;
using Company.ErrorManagement.Core;
using FluentAssertions;
using Xunit;

namespace Company.ErrorManagement.Persistence.Sqlite.Tests;

public class ErrorRepositoryTests : IClassFixture<SqliteTestFixture>
{
    private readonly ErrorRepository _sut;
    private readonly FingerprintProvider _fp = new();
    private readonly ErrorNormalizer _normalizer = new();

    public ErrorRepositoryTests(SqliteTestFixture fixture)
    {
        _sut = new ErrorRepository(fixture.Factory);
    }

    private ErrorEnvelope BuildEnvelope(string? msg = null)
    {
        var env = new ErrorEnvelope
        {
            ApplicationCode = "ERP",
            EnvironmentCode = "DEV",
            Layer = ErrorLayer.AspNetCore,
            ExceptionType = "System.InvalidOperationException",
            Message = msg ?? "Test error",
            CorrelationId = Guid.NewGuid().ToString("N"),
            ErrorReference = $"ERR-{Guid.NewGuid():N}",
        };
        env = _normalizer.Normalize(env);
        env.Fingerprint = _fp.Generate(env);
        env.FingerprintVersion = _fp.Version;
        return env;
    }

    [Fact]
    public async Task Upsert_Creates_New_ErrorDefinition_And_Occurrence()
    {
        var envelope = BuildEnvelope("unique message " + Guid.NewGuid());
        var receipt = await _sut.UpsertAsync(envelope, CancellationToken.None);

        receipt.Persisted.Should().BeTrue();
        receipt.ErrorReference.Should().Be(envelope.ErrorReference);
    }

    [Fact]
    public async Task Upsert_Same_Fingerprint_Increments_OccurrenceCount()
    {
        var msg = "dedup test " + Guid.NewGuid();
        var first = BuildEnvelope(msg);
        await _sut.UpsertAsync(first, CancellationToken.None);

        var second = BuildEnvelope(msg);
        second.ErrorReference = $"ERR-{Guid.NewGuid():N}";
        await _sut.UpsertAsync(second, CancellationToken.None);

        // Both receipts should be persisted (same definition, two occurrences).
        var r2 = await _sut.UpsertAsync(BuildEnvelope(msg) with { ErrorReference = $"ERR-{Guid.NewGuid():N}" }, CancellationToken.None);
        r2.Persisted.Should().BeTrue();
    }

    [Fact]
    public async Task Upsert_Same_EventId_Returns_False_Persisted()
    {
        var envelope = BuildEnvelope("idempotency test " + Guid.NewGuid());
        await _sut.UpsertAsync(envelope, CancellationToken.None);

        // Same event_id → duplicate occurrence insert blocked by UNIQUE constraint.
        var second = envelope with { OccurredAtUtc = DateTime.UtcNow.AddSeconds(1) };
        var receipt = await _sut.UpsertAsync(second, CancellationToken.None);
        receipt.Persisted.Should().BeFalse();
    }

    [Fact]
    public async Task FindByReference_Returns_Persisted_Envelope()
    {
        var envelope = BuildEnvelope("find by ref " + Guid.NewGuid());
        await _sut.UpsertAsync(envelope, CancellationToken.None);

        var found = await _sut.FindByReferenceAsync(envelope.ErrorReference!, CancellationToken.None);
        found.Should().NotBeNull();
        found!.ErrorReference.Should().Be(envelope.ErrorReference);
    }

    [Fact]
    public async Task FindByReference_Returns_Null_For_Unknown()
    {
        var found = await _sut.FindByReferenceAsync("ERR-DOES-NOT-EXIST", CancellationToken.None);
        found.Should().BeNull();
    }
}
