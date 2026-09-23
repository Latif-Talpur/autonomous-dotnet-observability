using Company.ErrorManagement.Contracts;
using Company.ErrorManagement.Core;
using FluentAssertions;
using Xunit;

namespace Company.ErrorManagement.Persistence.Sqlite.Tests;

public class TicketRepositoryTests : IClassFixture<SqliteTestFixture>
{
    private readonly TicketRepository _tickets;
    private readonly ErrorRepository _errors;
    private readonly FingerprintProvider _fp = new();
    private readonly ErrorNormalizer _normalizer = new();

    public TicketRepositoryTests(SqliteTestFixture fixture)
    {
        _errors = new ErrorRepository(fixture.Factory);
        _tickets = new TicketRepository(fixture.Factory);
    }

    private async Task<string> SeedErrorAsync()
    {
        var env = new ErrorEnvelope
        {
            ApplicationCode = "ERP",
            EnvironmentCode = "DEV",
            Layer = ErrorLayer.Business,
            ExceptionType = "System.Exception",
            Message = "ticket test " + Guid.NewGuid(),
            CorrelationId = Guid.NewGuid().ToString("N"),
            ErrorReference = $"ERR-{Guid.NewGuid():N}",
        };
        _normalizer.Normalize(env);
        env.Fingerprint = _fp.Generate(env);
        env.FingerprintVersion = _fp.Version;
        await _errors.UpsertAsync(env, CancellationToken.None);
        return env.ErrorReference!;
    }

    [Fact]
    public async Task Create_Returns_Ticket_With_Number()
    {
        var errorRef = await SeedErrorAsync();
        var ticket = await _tickets.CreateAsync(new TicketCreateRequest
        {
            ErrorReference = errorRef,
            ReportedBy = "user1",
            Description = "Something broke",
            Priority = 2
        }, CancellationToken.None);

        ticket.Should().NotBeNull();
        ticket.TicketNumber.Should().StartWith("TKT-");
        ticket.StatusCode.Should().Be("NEW");
        ticket.ErrorReference.Should().Be(errorRef);
    }

    [Fact]
    public async Task Assign_Updates_Assignee()
    {
        var errorRef = await SeedErrorAsync();
        var ticket = await _tickets.CreateAsync(new TicketCreateRequest { ErrorReference = errorRef, ReportedBy = "u1" }, CancellationToken.None);

        await _tickets.AssignAsync(ticket.TicketId, new TicketAssignmentRequest
        {
            AssignedTo = "engineer1",
            ChangedBy = "manager1"
        }, CancellationToken.None);

        var updated = await _tickets.GetAsync(ticket.TicketId, CancellationToken.None);
        updated!.AssignedTo.Should().Be("engineer1");
    }

    [Fact]
    public async Task ChangeStatus_Persists_History()
    {
        var errorRef = await SeedErrorAsync();
        var ticket = await _tickets.CreateAsync(new TicketCreateRequest { ErrorReference = errorRef, ReportedBy = "u1" }, CancellationToken.None);

        await _tickets.ChangeStatusAsync(ticket.TicketId, new TicketStatusChangeRequest
        {
            NewStatusCode = "INVESTIGATING",
            ChangedBy = "eng1",
            Reason = "Looking into it"
        }, CancellationToken.None);

        var history = await _tickets.GetStatusHistoryAsync(ticket.TicketId, CancellationToken.None);
        history.Should().HaveCount(2); // NEW + INVESTIGATING
        history.Last().NewStatusCode.Should().Be("INVESTIGATING");
    }

    [Fact]
    public async Task Resolve_Sets_ResolvedAtUtc()
    {
        var errorRef = await SeedErrorAsync();
        var ticket = await _tickets.CreateAsync(new TicketCreateRequest { ErrorReference = errorRef, ReportedBy = "u1" }, CancellationToken.None);

        await _tickets.ResolveAsync(ticket.TicketId, new TicketResolutionRequest
        {
            ResolutionCode = "FIXED",
            ResolutionNotes = "Deployed a patch",
            ChangedBy = "eng1"
        }, CancellationToken.None);

        var updated = await _tickets.GetAsync(ticket.TicketId, CancellationToken.None);
        updated!.StatusCode.Should().Be("RESOLVED");
        updated.ResolutionCode.Should().Be("FIXED");
        updated.ResolvedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task AddComment_Succeeds_Silently()
    {
        var errorRef = await SeedErrorAsync();
        var ticket = await _tickets.CreateAsync(new TicketCreateRequest { ErrorReference = errorRef, ReportedBy = "u1" }, CancellationToken.None);

        var act = async () => await _tickets.AddCommentAsync(ticket.TicketId, new TicketCommentRequest
        {
            CommentText = "Checked the logs",
            CreatedBy = "eng1",
            Visibility = "INTERNAL"
        }, CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Search_Returns_Matching_Tickets()
    {
        var errorRef = await SeedErrorAsync();
        await _tickets.CreateAsync(new TicketCreateRequest { ErrorReference = errorRef, ReportedBy = "searcher" }, CancellationToken.None);

        var results = await _tickets.SearchAsync(new TicketSearchQuery { PageSize = 50 }, CancellationToken.None);
        results.Items.Should().NotBeEmpty();
    }
}
