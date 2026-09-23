using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Dapper;

namespace Company.ErrorManagement.Persistence.Sqlite
{
    public interface IReportingRepository
    {
        Task<IReadOnlyList<TopErrorRow>> GetTopErrorsAsync(DateTime fromUtc, int take, CancellationToken cancellationToken);
        Task<IReadOnlyList<QueueStatsRow>> GetQueueStatsAsync(CancellationToken cancellationToken);
        Task<int> ArchiveOccurrencesOlderThanAsync(DateTime cutoffUtc, CancellationToken cancellationToken);
    }

    public sealed class ReportingRepository : IReportingRepository
    {
        private readonly ISqliteConnectionFactory _factory;

        public ReportingRepository(ISqliteConnectionFactory factory)
        {
            _factory = factory;
        }

        public async Task<IReadOnlyList<TopErrorRow>> GetTopErrorsAsync(DateTime fromUtc, int take, CancellationToken cancellationToken)
        {
            using var conn = _factory.Open();
            var rows = await conn.QueryAsync<TopErrorRow>(@"
                SELECT ed.error_definition_id AS ErrorDefinitionId,
                       ed.exception_type AS ExceptionType,
                       ed.normalized_message AS NormalizedMessage,
                       ed.occurrence_count AS OccurrenceCount,
                       ed.last_occurred_at_utc AS LastOccurredAtUtc,
                       ed.first_occurred_at_utc AS FirstOccurredAtUtc,
                       a.code AS ApplicationCode,
                       e.code AS EnvironmentCode
                FROM error_definition ed
                INNER JOIN application a ON a.application_id = ed.application_id
                INNER JOIN environment e ON e.environment_id = ed.environment_id
                WHERE ed.last_occurred_at_utc >= @From
                ORDER BY ed.occurrence_count DESC
                LIMIT @Take", new { From = FormatUtc(fromUtc), Take = take });
            return rows.AsList();
        }

        public async Task<IReadOnlyList<QueueStatsRow>> GetQueueStatsAsync(CancellationToken cancellationToken)
        {
            using var conn = _factory.Open();
            var rows = await conn.QueryAsync<QueueStatsRow>(@"
                SELECT sq.code AS QueueCode, ts.code AS StatusCode, COUNT(*) AS Count
                FROM ticket t
                LEFT JOIN support_queue sq ON sq.queue_id = t.queue_id
                INNER JOIN ticket_status ts ON ts.status_id = t.status_id
                GROUP BY sq.code, ts.code
                ORDER BY sq.code, ts.code");
            return rows.AsList();
        }

        public Task<int> ArchiveOccurrencesOlderThanAsync(DateTime cutoffUtc, CancellationToken cancellationToken)
        {
            using var conn = _factory.Open();
            using var tx = conn.BeginTransaction();

            var batchId = Guid.NewGuid().ToString("N");
            var startedAt = FormatUtc(DateTime.UtcNow);
            conn.Execute(@"
                INSERT INTO archive_batch (archive_batch_id, cutoff_at_utc, started_at_utc, archived_row_count, status)
                VALUES (@Id, @Cutoff, @Now, 0, 'STARTED')",
                new { Id = batchId, Cutoff = FormatUtc(cutoffUtc), Now = startedAt }, tx);

            var rows = conn.Execute(@"
                DELETE FROM error_occurrence
                WHERE occurred_at_utc < @Cutoff
                  AND error_occurrence_id NOT IN (SELECT error_occurrence_id FROM ticket WHERE error_occurrence_id IS NOT NULL)",
                new { Cutoff = FormatUtc(cutoffUtc) }, tx);

            conn.Execute(@"
                UPDATE archive_batch
                SET completed_at_utc = @Now, archived_row_count = @Rows, status = 'COMPLETED'
                WHERE archive_batch_id = @Id",
                new { Id = batchId, Rows = rows, Now = FormatUtc(DateTime.UtcNow) }, tx);

            tx.Commit();
            return Task.FromResult(rows);
        }

        private static string FormatUtc(DateTime value) =>
            value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ",
                System.Globalization.CultureInfo.InvariantCulture);
    }

    public sealed class TopErrorRow
    {
        public string ErrorDefinitionId { get; set; } = string.Empty;
        public string? ExceptionType { get; set; }
        public string NormalizedMessage { get; set; } = string.Empty;
        public int OccurrenceCount { get; set; }
        public string ApplicationCode { get; set; } = string.Empty;
        public string EnvironmentCode { get; set; } = string.Empty;
        public string LastOccurredAtUtc { get; set; } = string.Empty;
        public string FirstOccurredAtUtc { get; set; } = string.Empty;
    }

    public sealed class QueueStatsRow
    {
        public string? QueueCode { get; set; }
        public string StatusCode { get; set; } = string.Empty;
        public int Count { get; set; }
    }
}
