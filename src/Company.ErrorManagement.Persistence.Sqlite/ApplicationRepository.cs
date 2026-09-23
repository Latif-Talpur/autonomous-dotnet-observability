using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Dapper;

namespace Company.ErrorManagement.Persistence.Sqlite
{
    // ── DTOs ────────────────────────────────────────────────────────────────────

    public sealed class ApplicationCredentialRecord
    {
        public string CredentialId  { get; set; } = string.Empty;
        public string ApplicationId { get; set; } = string.Empty;
        public string ApplicationCode { get; set; } = string.Empty;
        public string ApplicationName { get; set; } = string.Empty;
        public string KeyPrefix     { get; set; } = string.Empty;
        public bool   IsActive      { get; set; }
        public DateTime? ExpiresAtUtc { get; set; }
    }

    public sealed class ApplicationInfo
    {
        public string ApplicationId   { get; set; } = string.Empty;
        public string Code            { get; set; } = string.Empty;
        public string Name            { get; set; } = string.Empty;
        public bool   IsActive        { get; set; }
    }

    public sealed class CredentialInfo
    {
        public string   CredentialId  { get; set; } = string.Empty;
        public string   KeyPrefix     { get; set; } = string.Empty;
        public string?  Description   { get; set; }
        public DateTime CreatedAtUtc  { get; set; }
        public DateTime? ExpiresAtUtc { get; set; }
        public bool     IsActive      { get; set; }
    }

    // ── Interface ───────────────────────────────────────────────────────────────

    public interface IApplicationRepository
    {
        Task<ApplicationCredentialRecord?> FindByKeyHashAsync(string keyHash, CancellationToken ct);
        Task<IReadOnlyList<ApplicationInfo>> GetApplicationsAsync(CancellationToken ct);
        Task<string> RegisterApplicationAsync(string code, string name, CancellationToken ct);
        Task<(string CredentialId, string RawKey)> CreateCredentialAsync(
            string applicationId, string? description, TimeSpan? validFor, CancellationToken ct);
        Task RevokeCredentialAsync(string credentialId, CancellationToken ct);
        Task<IReadOnlyList<CredentialInfo>> GetCredentialsAsync(string applicationId, CancellationToken ct);
    }

    // ── Implementation ──────────────────────────────────────────────────────────

    public sealed class ApplicationRepository : IApplicationRepository
    {
        private readonly ISqliteConnectionFactory _factory;

        public ApplicationRepository(ISqliteConnectionFactory factory)
        {
            _factory = factory;
        }

        public async Task<ApplicationCredentialRecord?> FindByKeyHashAsync(string keyHash, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            using var conn = _factory.Open();
            return await conn.QuerySingleOrDefaultAsync<ApplicationCredentialRecord>(@"
                SELECT
                    c.credential_id   AS CredentialId,
                    c.application_id  AS ApplicationId,
                    a.code            AS ApplicationCode,
                    a.name            AS ApplicationName,
                    c.key_prefix      AS KeyPrefix,
                    c.is_active       AS IsActive,
                    c.expires_at_utc  AS ExpiresAtUtc
                FROM application_credential c
                JOIN application a ON a.application_id = c.application_id
                WHERE c.key_hash = @KeyHash",
                new { KeyHash = keyHash }).ConfigureAwait(false);
        }

        public async Task<IReadOnlyList<ApplicationInfo>> GetApplicationsAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            using var conn = _factory.Open();
            var rows = await conn.QueryAsync<ApplicationInfo>(@"
                SELECT application_id AS ApplicationId,
                       code           AS Code,
                       name           AS Name,
                       is_active      AS IsActive
                FROM application
                ORDER BY name").ConfigureAwait(false);
            return rows.AsList();
        }

        public async Task<string> RegisterApplicationAsync(string code, string name, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            using var conn = _factory.Open();
            var id = Guid.NewGuid().ToString("N");
            await conn.ExecuteAsync(@"
                INSERT INTO application (application_id, code, name)
                VALUES (@Id, @Code, @Name)
                ON CONFLICT(code) DO UPDATE SET name = excluded.name",
                new { Id = id, Code = code, Name = name }).ConfigureAwait(false);

            // Return the actual ID (may differ if the row already existed).
            return await conn.ExecuteScalarAsync<string>(
                "SELECT application_id FROM application WHERE code = @Code",
                new { Code = code }).ConfigureAwait(false) ?? id;
        }

        public async Task<(string CredentialId, string RawKey)> CreateCredentialAsync(
            string applicationId, string? description, TimeSpan? validFor, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var rawKey      = GenerateKey();
            var keyHash     = HashKey(rawKey);
            var keyPrefix   = rawKey[..12];
            var credId      = Guid.NewGuid().ToString("N");
            var expiresAt   = validFor.HasValue
                ? (DateTime?)DateTime.UtcNow.Add(validFor.Value)
                : null;

            using var conn = _factory.Open();
            await conn.ExecuteAsync(@"
                INSERT INTO application_credential
                    (credential_id, application_id, key_hash, key_prefix, description, expires_at_utc)
                VALUES
                    (@Id, @AppId, @Hash, @Prefix, @Desc, @Expires)",
                new
                {
                    Id      = credId,
                    AppId   = applicationId,
                    Hash    = keyHash,
                    Prefix  = keyPrefix,
                    Desc    = description,
                    Expires = expiresAt.HasValue
                        ? expiresAt.Value.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
                        : (string?)null
                }).ConfigureAwait(false);

            return (credId, rawKey);
        }

        public async Task RevokeCredentialAsync(string credentialId, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            using var conn = _factory.Open();
            await conn.ExecuteAsync(
                "UPDATE application_credential SET is_active = 0 WHERE credential_id = @Id",
                new { Id = credentialId }).ConfigureAwait(false);
        }

        public async Task<IReadOnlyList<CredentialInfo>> GetCredentialsAsync(string applicationId, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            using var conn = _factory.Open();
            var rows = await conn.QueryAsync<CredentialInfo>(@"
                SELECT credential_id AS CredentialId,
                       key_prefix    AS KeyPrefix,
                       description   AS Description,
                       created_at_utc AS CreatedAtUtc,
                       expires_at_utc AS ExpiresAtUtc,
                       is_active     AS IsActive
                FROM application_credential
                WHERE application_id = @AppId
                ORDER BY created_at_utc DESC",
                new { AppId = applicationId }).ConfigureAwait(false);
            return rows.AsList();
        }

        // ── Key generation ───────────────────────────────────────────────────────

        private static string GenerateKey()
        {
            // Format: erp_<32 random hex chars>  — 36 chars total, clearly identifiable.
            Span<byte> bytes = stackalloc byte[16];
            RandomNumberGenerator.Fill(bytes);
            return "erp_" + Convert.ToHexString(bytes).ToLowerInvariant();
        }

        internal static string HashKey(string rawKey)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawKey));
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }
    }
}
