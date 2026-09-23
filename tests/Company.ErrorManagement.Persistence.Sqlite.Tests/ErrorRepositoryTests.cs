using System;
using System.Threading;
using System.Threading.Tasks;
using Company.ErrorManagement.Contracts;
using Dapper;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Company.ErrorManagement.Persistence.Sqlite.Tests;

public class ErrorRepositoryTests : IClassFixture<SqliteTestFixture>
{
    private readonly ErrorRepository _sut;
    private readonly ISqliteConnectionFactory _factory;

    public ErrorRepositoryTests(SqliteTestFixture fixture)
    {
        _factory = fixture.Factory;
        _sut = new ErrorRepository(_factory);
    }

    private static ErrorEnvelope BuildEnvelope(string? fingerprint = null) => new()
    {
        ApplicationCode = "ERP",
        EnvironmentCode = "DEV",
        Layer = ErrorLayer.AspNetCore,
        ExceptionType = "System.InvalidOperationException",
        Message = "Test error",
        CorrelationId = Guid.NewGuid().ToString("N"),
        ErrorReference = $"ERR-{Guid.NewGuid():N}",
        Fingerprint = fingerprint ?? Guid.NewGuid().ToString("N")
    };

    private void AssertCounts(string fingerprint, int definitions, int occurrences)
    {
        using var conn = _factory.Open();
        conn.ExecuteScalar<int>("SELECT COUNT(*) FROM error_definition WHERE fingerprint = @fingerprint",
            new { fingerprint }).Should().Be(definitions);
        conn.ExecuteScalar<int>(@"SELECT COUNT(*) FROM error_occurrence o
            JOIN error_definition d ON d.error_definition_id = o.error_definition_id
            WHERE d.fingerprint = @fingerprint", new { fingerprint }).Should().Be(occurrences);
        conn.ExecuteScalar<int>("SELECT COALESCE(SUM(occurrence_count), 0) FROM error_definition WHERE fingerprint = @fingerprint",
            new { fingerprint }).Should().Be(occurrences);
    }

    [Fact]
    public async Task New_event_is_persisted_and_can_be_found()
    {
        var envelope = BuildEnvelope();
        var receipt = await _sut.UpsertAsync(envelope, CancellationToken.None);
        receipt.Persisted.Should().BeTrue();
        receipt.ErrorReference.Should().Be(envelope.ErrorReference);
        var found = await _sut.FindByReferenceAsync(receipt.ErrorReference, CancellationToken.None);
        found.Should().NotBeNull();
        found!.EventId.Should().Be(envelope.EventId);
        AssertCounts(envelope.Fingerprint!, 1, 1);
    }

    [Fact]
    public async Task Distinct_events_with_same_fingerprint_are_counted()
    {
        var first = BuildEnvelope();
        for (var i = 0; i < 3; i++)
            await _sut.UpsertAsync(BuildEnvelope(first.Fingerprint), CancellationToken.None);
        AssertCounts(first.Fingerprint!, 1, 3);
    }

    [Fact]
    public async Task Replay_returns_original_receipt_without_changing_counts()
    {
        var first = BuildEnvelope();
        var original = await _sut.UpsertAsync(first, CancellationToken.None);
        var retry = BuildEnvelope();
        retry.EventId = first.EventId;
        var receipt = await _sut.UpsertAsync(retry, CancellationToken.None);

        receipt.Persisted.Should().BeTrue();
        receipt.CanReportIssue.Should().BeTrue();
        receipt.EventId.Should().Be(original.EventId);
        receipt.ErrorReference.Should().Be(original.ErrorReference);
        receipt.CorrelationId.Should().Be(original.CorrelationId);
        receipt.Fingerprint.Should().Be(original.Fingerprint);
        receipt.AcceptedAtUtc.Should().BeCloseTo(original.AcceptedAtUtc, TimeSpan.FromMilliseconds(1));
        AssertCounts(first.Fingerprint!, 1, 1);
        AssertCounts(retry.Fingerprint!, 0, 0);
    }

    [Theory]
    [InlineData("TEST", null)]
    [InlineData("DEV", "different-tenant")]
    public async Task Replay_in_another_scope_is_rejected(string environment, string? tenant)
    {
        var first = BuildEnvelope();
        await _sut.UpsertAsync(first, CancellationToken.None);
        var retry = BuildEnvelope(first.Fingerprint);
        retry.EventId = first.EventId;
        retry.EnvironmentCode = environment;
        retry.Tenant = tenant;

        Func<Task> act = () => _sut.UpsertAsync(retry, CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>();
        AssertCounts(first.Fingerprint!, 1, 1);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Reference_conflict_rolls_back_definition_changes(bool sameFingerprint)
    {
        var first = BuildEnvelope();
        await _sut.UpsertAsync(first, CancellationToken.None);
        var conflict = BuildEnvelope(sameFingerprint ? first.Fingerprint : null);
        conflict.ErrorReference = first.ErrorReference;

        Func<Task> act = () => _sut.UpsertAsync(conflict, CancellationToken.None);
        await act.Should().ThrowAsync<SqliteException>();
        AssertCounts(first.Fingerprint!, 1, 1);
        if (!sameFingerprint) AssertCounts(conflict.Fingerprint!, 0, 0);
    }

    [Fact]
    public async Task Concurrent_retries_only_count_once()
    {
        var first = BuildEnvelope();
        var tasks = new Task<ErrorReceipt>[8];
        for (var i = 0; i < tasks.Length; i++)
        {
            var retry = BuildEnvelope(first.Fingerprint);
            retry.EventId = first.EventId;
            tasks[i] = Task.Run(() => _sut.UpsertAsync(retry, CancellationToken.None));
        }
        var receipts = await Task.WhenAll(tasks);
        foreach (var receipt in receipts)
        {
            receipt.Persisted.Should().BeTrue();
            receipt.ErrorReference.Should().Be(receipts[0].ErrorReference);
        }
        AssertCounts(first.Fingerprint!, 1, 1);
    }

    [Fact]
    public async Task Unknown_reference_returns_null()
    {
        var found = await _sut.FindByReferenceAsync("ERR-DOES-NOT-EXIST", CancellationToken.None);
        found.Should().BeNull();
    }
}
