using System;
using System.Threading;
using System.Threading.Tasks;
using Company.ErrorManagement.Contracts;
using Dapper;

namespace Company.ErrorManagement.Persistence.Sqlite
{
    public interface IErrorRepository
    {
        Task<ErrorReceipt> UpsertAsync(ErrorEnvelope envelope, CancellationToken cancellationToken);
        Task<ErrorEnvelope?> FindByReferenceAsync(string errorReference, CancellationToken cancellationToken);
    }

    public sealed class ErrorRepository : IErrorRepository
    {
        private readonly ISqliteConnectionFactory _factory;

        public ErrorRepository(ISqliteConnectionFactory factory)
        {
            _factory = factory;
        }

        public Task<ErrorReceipt> UpsertAsync(ErrorEnvelope envelope, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var conn = _factory.Open();
            using var tx = conn.BeginTransaction();

            var appId = conn.ExecuteScalar<string>(
                "SELECT application_id FROM application WHERE code = @Code",
                new { Code = envelope.ApplicationCode }, tx);
            if (appId == null) throw new InvalidOperationException(
                $"Application '{envelope.ApplicationCode}' is not registered.");

            var envId = conn.ExecuteScalar<string>(
                "SELECT environment_id FROM environment WHERE code = @Code",
                new { Code = envelope.EnvironmentCode }, tx);
            if (envId == null) throw new InvalidOperationException(
                $"Environment '{envelope.EnvironmentCode}' is not registered.");

            // BeginTransaction acquires SQLite's write lock before checking the event.
            // A retry must return the original receipt without changing aggregates.
            var existing = conn.QuerySingleOrDefault<dynamic>(@"
                SELECT o.error_reference AS ErrorReference, o.correlation_id AS CorrelationId,
                       o.received_at_utc AS ReceivedAtUtc, o.tenant_id AS Tenant,
                       d.application_id AS ApplicationId, d.environment_id AS EnvironmentId,
                       d.fingerprint AS Fingerprint
                FROM error_occurrence o
                JOIN error_definition d ON d.error_definition_id = o.error_definition_id
                WHERE o.event_id = @EventId", new { envelope.EventId }, tx);
            if (existing != null)
            {
                if ((string)existing.ApplicationId != appId ||
                    (string)existing.EnvironmentId != envId ||
                    (string?)existing.Tenant != envelope.Tenant)
                    throw new InvalidOperationException("Event ID is already used in a different scope.");

                var original = new ErrorReceipt
                {
                    EventId = envelope.EventId,
                    ErrorReference = (string)existing.ErrorReference,
                    CorrelationId = (string)existing.CorrelationId,
                    Fingerprint = (string)existing.Fingerprint,
                    AcceptedAtUtc = DateTime.Parse((string)existing.ReceivedAtUtc,
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.RoundtripKind),
                    Persisted = true,
                    CanReportIssue = true
                };
                tx.Commit();
                return Task.FromResult(original);
            }

            var categoryId = envelope.CategoryCode == null
                ? null
                : conn.ExecuteScalar<string?>(
                    "SELECT category_id FROM error_category WHERE code = @Code",
                    new { Code = envelope.CategoryCode }, tx);

            var severityId = envelope.SeverityCode == null
                ? null
                : conn.ExecuteScalar<string?>(
                    "SELECT severity_id FROM severity WHERE code = @Code",
                    new { Code = envelope.SeverityCode }, tx);

            var existingDefId = conn.ExecuteScalar<string?>(@"
                SELECT error_definition_id FROM error_definition
                WHERE application_id = @AppId AND environment_id = @EnvId
                  AND fingerprint_version = @FpVer AND fingerprint = @Fp",
                new { AppId = appId, EnvId = envId, FpVer = envelope.FingerprintVersion, Fp = envelope.Fingerprint }, tx);

            string definitionId;
            if (existingDefId != null)
            {
                definitionId = existingDefId;
                conn.Execute(@"
                    UPDATE error_definition
                    SET last_occurred_at_utc = @Now,
                        occurrence_count = occurrence_count + 1
                    WHERE error_definition_id = @Id",
                    new { Now = FormatUtc(DateTime.UtcNow), Id = definitionId }, tx);
            }
            else
            {
                definitionId = Guid.NewGuid().ToString("N");
                conn.Execute(@"
                    INSERT INTO error_definition (
                        error_definition_id, application_id, environment_id, fingerprint,
                        fingerprint_version, layer, category_id, severity_id, exception_type,
                        normalized_message, first_occurred_at_utc, last_occurred_at_utc, occurrence_count)
                    VALUES (
                        @Id, @AppId, @EnvId, @Fp,
                        @FpVer, @Layer, @CatId, @SevId, @ExType,
                        @Msg, @Now, @Now, 1)",
                    new
                    {
                        Id = definitionId,
                        AppId = appId,
                        EnvId = envId,
                        Fp = envelope.Fingerprint,
                        FpVer = envelope.FingerprintVersion,
                        Layer = envelope.Layer.ToString(),
                        CatId = categoryId,
                        SevId = severityId,
                        ExType = envelope.ExceptionType,
                        Msg = envelope.Message ?? string.Empty,
                        Now = FormatUtc(DateTime.UtcNow)
                    }, tx);
            }

            var occurrenceId = Guid.NewGuid().ToString("N");
            var receipt = new ErrorReceipt
            {
                EventId = envelope.EventId,
                ErrorReference = envelope.ErrorReference ?? string.Empty,
                CorrelationId = envelope.CorrelationId,
                Fingerprint = envelope.Fingerprint,
                CanReportIssue = true,
                Persisted = true,
                SafeMessage = "Unable to process the request"
            };

            conn.Execute(@"
                    INSERT INTO error_occurrence (
                        error_occurrence_id, event_id, error_definition_id, error_reference,
                        correlation_id, occurred_at_utc, received_at_utc, user_id, tenant_id,
                        application_version, module, screen, component, endpoint, http_status,
                        stack_trace, diagnostic_payload, db_provider, db_error_code, db_procedure,
                        db_line_number, is_timeout, is_expected, is_transient, client_version, client_ip_hash)
                    VALUES (
                        @OccId, @EventId, @DefId, @Ref,
                        @Corr, @Occurred, @Received, @UserId, @Tenant,
                        @AppVer, @Module, @Screen, @Component, @Endpoint, @HttpStatus,
                        @Stack, @Diag, @DbProv, @DbErr, @DbProc,
                        @DbLine, @Timeout, @Expected, @Transient, @ClientVer, @IpHash)",
                    new
                    {
                        OccId = occurrenceId,
                        EventId = envelope.EventId,
                        DefId = definitionId,
                        Ref = envelope.ErrorReference,
                        Corr = envelope.CorrelationId,
                        Occurred = FormatUtc(envelope.OccurredAtUtc),
                        Received = FormatUtc(receipt.AcceptedAtUtc),
                        UserId = envelope.UserId,
                        Tenant = envelope.Tenant,
                        AppVer = envelope.ApplicationVersion,
                        Module = envelope.Module,
                        Screen = envelope.Screen,
                        Component = envelope.Component,
                        Endpoint = envelope.Endpoint,
                        HttpStatus = envelope.HttpStatus,
                        Stack = envelope.StackTrace,
                        Diag = SerializeDiagnostics(envelope),
                        DbProv = envelope.DbProvider,
                        DbErr = envelope.DbErrorCode,
                        DbProc = envelope.DbProcedure,
                        DbLine = envelope.DbLineNumber,
                        Timeout = envelope.IsTimeout ? 1 : 0,
                        Expected = envelope.IsExpected ? 1 : 0,
                        Transient = envelope.IsTransient ? 1 : 0,
                        ClientVer = envelope.ClientVersion,
                        IpHash = envelope.ClientIpHash
                    }, tx);
            // Any insert failure rolls back the definition and occurrence together.
            tx.Commit();
            return Task.FromResult(receipt);
        }

        public async Task<ErrorEnvelope?> FindByReferenceAsync(string errorReference, CancellationToken cancellationToken)
        {
            using var conn = _factory.Open();
            var row = await conn.QueryFirstOrDefaultAsync<dynamic>(@"
                SELECT o.error_reference AS ErrorReference,
                       o.event_id AS EventId,
                       o.correlation_id AS CorrelationId,
                       d.fingerprint AS Fingerprint,
                       d.fingerprint_version AS FingerprintVersion,
                       d.layer AS Layer,
                       d.exception_type AS ExceptionType,
                       d.normalized_message AS Message,
                       o.stack_trace AS StackTrace,
                       o.user_id AS UserId,
                       o.module AS Module,
                       o.screen AS Screen,
                       o.endpoint AS Endpoint,
                       o.http_status AS HttpStatus,
                       o.occurred_at_utc AS OccurredAtUtc
                FROM error_occurrence o
                INNER JOIN error_definition d ON d.error_definition_id = o.error_definition_id
                WHERE o.error_reference = @Ref
                LIMIT 1", new { Ref = errorReference });

            if (row == null) return null;

            var envelope = new ErrorEnvelope
            {
                ErrorReference = (string)row.ErrorReference,
                EventId = (string)row.EventId,
                CorrelationId = (string)row.CorrelationId,
                Fingerprint = (string)row.Fingerprint,
                FingerprintVersion = (int)(long)row.FingerprintVersion,
                ExceptionType = row.ExceptionType,
                Message = row.Message,
                StackTrace = row.StackTrace,
                UserId = row.UserId,
                Module = row.Module,
                Screen = row.Screen,
                Endpoint = row.Endpoint,
                HttpStatus = row.HttpStatus == null ? (int?)null : (int)(long)row.HttpStatus,
                OccurredAtUtc = DateTime.Parse((string)row.OccurredAtUtc, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind)
            };
            if (Enum.TryParse<ErrorLayer>((string)row.Layer, out var layer)) envelope.Layer = layer;
            return envelope;
        }

        private static string SerializeDiagnostics(ErrorEnvelope envelope)
        {
            if (envelope.Diagnostics == null || envelope.Diagnostics.Count == 0) return "{}";
            return System.Text.Json.JsonSerializer.Serialize(envelope.Diagnostics);
        }

        private static string FormatUtc(DateTime value) =>
            value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ",
                System.Globalization.CultureInfo.InvariantCulture);
    }
}
