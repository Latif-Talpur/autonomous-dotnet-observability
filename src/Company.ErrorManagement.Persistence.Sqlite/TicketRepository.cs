using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Company.ErrorManagement.Contracts;
using Dapper;

namespace Company.ErrorManagement.Persistence.Sqlite
{
    public interface ITicketRepository
    {
        Task<TicketView> CreateAsync(TicketCreateRequest request, CancellationToken cancellationToken);
        Task<TicketView?> GetAsync(string ticketId, CancellationToken cancellationToken);
        Task<PagedResult<TicketView>> SearchAsync(TicketSearchQuery query, CancellationToken cancellationToken);
        Task AssignAsync(string ticketId, TicketAssignmentRequest request, CancellationToken cancellationToken);
        Task ChangeStatusAsync(string ticketId, TicketStatusChangeRequest request, CancellationToken cancellationToken);
        Task AddCommentAsync(string ticketId, TicketCommentRequest request, CancellationToken cancellationToken);
        Task ResolveAsync(string ticketId, TicketResolutionRequest request, CancellationToken cancellationToken);
        Task<IReadOnlyList<TicketStatusHistoryEntry>> GetStatusHistoryAsync(string ticketId, CancellationToken cancellationToken);
    }

    public sealed class TicketRepository : ITicketRepository
    {
        private readonly ISqliteConnectionFactory _factory;
        private static int _sequence;

        public TicketRepository(ISqliteConnectionFactory factory)
        {
            _factory = factory;
        }

        public Task<TicketView> CreateAsync(TicketCreateRequest request, CancellationToken cancellationToken)
        {
            using var conn = _factory.Open();
            using var tx = conn.BeginTransaction();

            var occurrence = conn.QueryFirstOrDefault<(string OccurrenceId, string DefinitionId)>(@"
                SELECT error_occurrence_id AS OccurrenceId, error_definition_id AS DefinitionId
                FROM error_occurrence
                WHERE error_reference = @Ref LIMIT 1", new { Ref = request.ErrorReference }, tx);

            if (occurrence.OccurrenceId == null)
                throw new InvalidOperationException($"Error reference '{request.ErrorReference}' was not found.");

            var queueId = request.QueueCode == null
                ? conn.ExecuteScalar<string?>("SELECT queue_id FROM support_queue WHERE code = 'L1'", null, tx)
                : conn.ExecuteScalar<string?>("SELECT queue_id FROM support_queue WHERE code = @Code",
                    new { Code = request.QueueCode }, tx);

            var severityId = request.SeverityCode == null
                ? conn.ExecuteScalar<string?>(@"
                    SELECT ed.severity_id FROM error_definition ed
                    INNER JOIN error_occurrence eo ON eo.error_definition_id = ed.error_definition_id
                    WHERE eo.error_reference = @Ref", new { Ref = request.ErrorReference }, tx)
                : conn.ExecuteScalar<string?>("SELECT severity_id FROM severity WHERE code = @Code",
                    new { Code = request.SeverityCode }, tx);

            var newStatus = conn.ExecuteScalar<string>(
                "SELECT status_id FROM ticket_status WHERE code = 'NEW'", null, tx);

            var slaId = severityId == null || queueId == null ? null : conn.ExecuteScalar<string?>(@"
                SELECT sla_policy_id FROM sla_policy
                WHERE queue_id = @Q AND severity_id = @S AND is_active = 1",
                new { Q = queueId, S = severityId }, tx);

            var ticketId = Guid.NewGuid().ToString("N");
            var ticketNumber = GenerateTicketNumber();
            var now = FormatUtc(DateTime.UtcNow);

            conn.Execute(@"
                INSERT INTO ticket (
                    ticket_id, ticket_number, error_definition_id, error_occurrence_id,
                    queue_id, status_id, severity_id, sla_policy_id, priority, source,
                    reported_by, description, opened_at_utc, updated_at_utc, row_version)
                VALUES (
                    @Id, @Number, @DefId, @OccId,
                    @Queue, @Status, @Severity, @Sla, @Priority, @Source,
                    @By, @Desc, @Now, @Now, 0)",
                new
                {
                    Id = ticketId,
                    Number = ticketNumber,
                    DefId = occurrence.DefinitionId,
                    OccId = occurrence.OccurrenceId,
                    Queue = queueId,
                    Status = newStatus,
                    Severity = severityId,
                    Sla = slaId,
                    Priority = Math.Max(1, Math.Min(5, request.Priority)),
                    Source = string.IsNullOrWhiteSpace(request.Source) ? "USER" : request.Source,
                    By = request.ReportedBy,
                    Desc = request.Description,
                    Now = now
                }, tx);

            conn.Execute(@"
                INSERT INTO ticket_status_history (
                    history_id, ticket_id, previous_status_id, new_status_id,
                    changed_by, changed_at_utc, reason)
                VALUES (@Hid, @Tid, NULL, @NewStatus, @By, @Now, 'Ticket created')",
                new { Hid = Guid.NewGuid().ToString("N"), Tid = ticketId, NewStatus = newStatus,
                      By = request.ReportedBy, Now = now }, tx);

            tx.Commit();
            var view = InternalGet(conn, ticketId) ?? throw new InvalidOperationException("Ticket vanished");
            return Task.FromResult(view);
        }

        public Task<TicketView?> GetAsync(string ticketId, CancellationToken cancellationToken)
        {
            using var conn = _factory.Open();
            return Task.FromResult(InternalGet(conn, ticketId));
        }

        public Task<PagedResult<TicketView>> SearchAsync(TicketSearchQuery query, CancellationToken cancellationToken)
        {
            using var conn = _factory.Open();
            var sql = @"
                SELECT t.ticket_id AS TicketId, t.ticket_number AS TicketNumber,
                       ts.code AS StatusCode, ts.name AS StatusName,
                       sq.code AS QueueCode, sv.code AS SeverityCode,
                       t.priority AS Priority, t.assigned_to AS AssignedTo,
                       t.description AS Description, t.reported_by AS ReportedBy,
                       t.opened_at_utc AS OpenedAtUtc, t.resolved_at_utc AS ResolvedAtUtc,
                       t.closed_at_utc AS ClosedAtUtc,
                       t.resolution_code AS ResolutionCode, t.resolution_notes AS ResolutionNotes,
                       eo.error_reference AS ErrorReference,
                       t.error_definition_id AS ErrorDefinitionId
                FROM ticket t
                INNER JOIN ticket_status ts ON ts.status_id = t.status_id
                LEFT JOIN support_queue sq ON sq.queue_id = t.queue_id
                LEFT JOIN severity sv ON sv.severity_id = t.severity_id
                LEFT JOIN error_occurrence eo ON eo.error_occurrence_id = t.error_occurrence_id
                WHERE (@Queue IS NULL OR sq.code = @Queue)
                  AND (@Status IS NULL OR ts.code = @Status)
                  AND (@Assigned IS NULL OR t.assigned_to = @Assigned)
                  AND (@Severity IS NULL OR sv.code = @Severity)
                  AND (@Text IS NULL OR t.description LIKE @TextLike OR t.ticket_number LIKE @TextLike)
                ORDER BY t.opened_at_utc DESC
                LIMIT @Take OFFSET @Skip;
                SELECT COUNT(1)
                FROM ticket t
                INNER JOIN ticket_status ts ON ts.status_id = t.status_id
                LEFT JOIN support_queue sq ON sq.queue_id = t.queue_id
                LEFT JOIN severity sv ON sv.severity_id = t.severity_id
                WHERE (@Queue IS NULL OR sq.code = @Queue)
                  AND (@Status IS NULL OR ts.code = @Status)
                  AND (@Assigned IS NULL OR t.assigned_to = @Assigned)
                  AND (@Severity IS NULL OR sv.code = @Severity)
                  AND (@Text IS NULL OR t.description LIKE @TextLike OR t.ticket_number LIKE @TextLike);";

            var textLike = query.Text == null ? null : "%" + query.Text + "%";
            using var multi = conn.QueryMultiple(sql, new
            {
                Queue = query.QueueCode,
                Status = query.StatusCode,
                Assigned = query.AssignedTo,
                Severity = query.SeverityCode,
                Text = query.Text,
                TextLike = textLike,
                Take = query.PageSize,
                Skip = query.PageIndex * query.PageSize
            });
            var items = multi.Read<TicketView>().AsList();
            var total = multi.ReadFirst<int>();
            return Task.FromResult(new PagedResult<TicketView>
            {
                Items = items,
                TotalCount = total,
                PageIndex = query.PageIndex,
                PageSize = query.PageSize
            });
        }

        public Task AssignAsync(string ticketId, TicketAssignmentRequest request, CancellationToken cancellationToken)
        {
            using var conn = _factory.Open();
            using var tx = conn.BeginTransaction();

            var previous = conn.ExecuteScalar<string?>(
                "SELECT assigned_to FROM ticket WHERE ticket_id = @Id", new { Id = ticketId }, tx);
            var now = FormatUtc(DateTime.UtcNow);

            conn.Execute(@"
                UPDATE ticket
                SET assigned_to = @To, assigned_at_utc = @Now, updated_at_utc = @Now,
                    row_version = row_version + 1
                WHERE ticket_id = @Id",
                new { Id = ticketId, To = request.AssignedTo, Now = now }, tx);

            conn.Execute(@"
                INSERT INTO ticket_assignment_history (
                    history_id, ticket_id, previous_assignee, new_assignee,
                    changed_by, changed_at_utc, reason)
                VALUES (@Hid, @Tid, @Prev, @New, @By, @Now, @Reason)",
                new
                {
                    Hid = Guid.NewGuid().ToString("N"),
                    Tid = ticketId,
                    Prev = previous,
                    New = request.AssignedTo,
                    By = request.ChangedBy,
                    Now = now,
                    Reason = request.Reason
                }, tx);

            tx.Commit();
            return Task.CompletedTask;
        }

        public Task ChangeStatusAsync(string ticketId, TicketStatusChangeRequest request, CancellationToken cancellationToken)
        {
            using var conn = _factory.Open();
            using var tx = conn.BeginTransaction();

            var prevStatusId = conn.ExecuteScalar<string?>(
                "SELECT status_id FROM ticket WHERE ticket_id = @Id",
                new { Id = ticketId }, tx);

            var newStatusId = conn.ExecuteScalar<string?>(
                "SELECT status_id FROM ticket_status WHERE code = @Code",
                new { Code = request.NewStatusCode }, tx);

            if (newStatusId == null)
                throw new InvalidOperationException($"Unknown status '{request.NewStatusCode}'.");

            var isTerminal = conn.ExecuteScalar<long>(
                "SELECT is_terminal FROM ticket_status WHERE status_id = @Id",
                new { Id = newStatusId }, tx) == 1;

            var now = FormatUtc(DateTime.UtcNow);
            conn.Execute(@"
                UPDATE ticket
                SET status_id = @New, updated_at_utc = @Now,
                    row_version = row_version + 1,
                    closed_at_utc = CASE WHEN @Terminal = 1 THEN @Now ELSE closed_at_utc END
                WHERE ticket_id = @Id",
                new { Id = ticketId, New = newStatusId, Now = now, Terminal = isTerminal ? 1 : 0 }, tx);

            conn.Execute(@"
                INSERT INTO ticket_status_history (
                    history_id, ticket_id, previous_status_id, new_status_id,
                    changed_by, changed_at_utc, reason)
                VALUES (@Hid, @Tid, @Prev, @New, @By, @Now, @Reason)",
                new
                {
                    Hid = Guid.NewGuid().ToString("N"),
                    Tid = ticketId,
                    Prev = prevStatusId,
                    New = newStatusId,
                    By = request.ChangedBy,
                    Now = now,
                    Reason = request.Reason
                }, tx);

            tx.Commit();
            return Task.CompletedTask;
        }

        public Task AddCommentAsync(string ticketId, TicketCommentRequest request, CancellationToken cancellationToken)
        {
            using var conn = _factory.Open();
            conn.Execute(@"
                INSERT INTO ticket_comment (comment_id, ticket_id, visibility, comment_text, created_by, created_at_utc)
                VALUES (@Cid, @Tid, @Vis, @Text, @By, @Now)",
                new
                {
                    Cid = Guid.NewGuid().ToString("N"),
                    Tid = ticketId,
                    Vis = request.Visibility,
                    Text = request.CommentText,
                    By = request.CreatedBy,
                    Now = FormatUtc(DateTime.UtcNow)
                });
            return Task.CompletedTask;
        }

        public Task ResolveAsync(string ticketId, TicketResolutionRequest request, CancellationToken cancellationToken)
        {
            using var conn = _factory.Open();
            using var tx = conn.BeginTransaction();

            var prevStatusId = conn.ExecuteScalar<string?>(
                "SELECT status_id FROM ticket WHERE ticket_id = @Id",
                new { Id = ticketId }, tx);

            var resolvedStatus = conn.ExecuteScalar<string>(
                "SELECT status_id FROM ticket_status WHERE code = 'RESOLVED'", null, tx);

            var now = FormatUtc(DateTime.UtcNow);
            conn.Execute(@"
                UPDATE ticket
                SET status_id = @New, resolved_at_utc = @Now, updated_at_utc = @Now,
                    resolution_code = @Code, resolution_notes = @Notes,
                    row_version = row_version + 1
                WHERE ticket_id = @Id",
                new { Id = ticketId, New = resolvedStatus, Now = now,
                      Code = request.ResolutionCode, Notes = request.ResolutionNotes }, tx);

            conn.Execute(@"
                INSERT INTO ticket_status_history (
                    history_id, ticket_id, previous_status_id, new_status_id,
                    changed_by, changed_at_utc, reason)
                VALUES (@Hid, @Tid, @Prev, @New, @By, @Now, @Reason)",
                new
                {
                    Hid = Guid.NewGuid().ToString("N"),
                    Tid = ticketId,
                    Prev = prevStatusId,
                    New = resolvedStatus,
                    By = request.ChangedBy,
                    Now = now,
                    Reason = "Resolved: " + request.ResolutionCode
                }, tx);

            tx.Commit();
            return Task.CompletedTask;
        }

        public async Task<IReadOnlyList<TicketStatusHistoryEntry>> GetStatusHistoryAsync(string ticketId, CancellationToken cancellationToken)
        {
            using var conn = _factory.Open();
            var rows = await conn.QueryAsync<dynamic>(@"
                SELECT h.history_id AS HistoryId,
                       (SELECT code FROM ticket_status WHERE status_id = h.previous_status_id) AS PreviousStatusCode,
                       (SELECT code FROM ticket_status WHERE status_id = h.new_status_id) AS NewStatusCode,
                       h.changed_by AS ChangedBy,
                       h.changed_at_utc AS ChangedAtUtc,
                       h.reason AS Reason
                FROM ticket_status_history h
                WHERE ticket_id = @Id
                ORDER BY h.changed_at_utc ASC", new { Id = ticketId });
            return rows.Select(r => new TicketStatusHistoryEntry
            {
                HistoryId = (string)r.HistoryId,
                PreviousStatusCode = r.PreviousStatusCode,
                NewStatusCode = (string)r.NewStatusCode,
                ChangedBy = (string)r.ChangedBy,
                ChangedAtUtc = DateTime.Parse((string)r.ChangedAtUtc,
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.RoundtripKind),
                Reason = r.Reason
            }).ToList();
        }

        private static TicketView? InternalGet(System.Data.IDbConnection conn, string ticketId)
        {
            return conn.QueryFirstOrDefault<TicketView>(@"
                SELECT t.ticket_id AS TicketId, t.ticket_number AS TicketNumber,
                       ts.code AS StatusCode, ts.name AS StatusName,
                       sq.code AS QueueCode, sv.code AS SeverityCode,
                       t.priority AS Priority, t.assigned_to AS AssignedTo,
                       t.description AS Description, t.reported_by AS ReportedBy,
                       t.opened_at_utc AS OpenedAtUtc, t.resolved_at_utc AS ResolvedAtUtc,
                       t.closed_at_utc AS ClosedAtUtc,
                       t.resolution_code AS ResolutionCode, t.resolution_notes AS ResolutionNotes,
                       eo.error_reference AS ErrorReference,
                       t.error_definition_id AS ErrorDefinitionId
                FROM ticket t
                INNER JOIN ticket_status ts ON ts.status_id = t.status_id
                LEFT JOIN support_queue sq ON sq.queue_id = t.queue_id
                LEFT JOIN severity sv ON sv.severity_id = t.severity_id
                LEFT JOIN error_occurrence eo ON eo.error_occurrence_id = t.error_occurrence_id
                WHERE t.ticket_id = @Id", new { Id = ticketId });
        }

        private static string GenerateTicketNumber()
        {
            var seq = Interlocked.Increment(ref _sequence) & 0xFFFFFF;
            return $"TKT-{DateTime.UtcNow:yyyyMMdd}-{seq:D6}";
        }

        private static string FormatUtc(DateTime value) =>
            value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ",
                System.Globalization.CultureInfo.InvariantCulture);
    }
}
