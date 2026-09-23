using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Dapper;

namespace Company.ErrorManagement.Persistence.Sqlite
{
    public sealed class SqliteMigrationRunner
    {
        private readonly ISqliteConnectionFactory _factory;

        public SqliteMigrationRunner(ISqliteConnectionFactory factory)
        {
            _factory = factory;
        }

        public IReadOnlyList<int> Apply(string migrationsDirectory, string? seedDirectory = null)
        {
            if (!Directory.Exists(migrationsDirectory))
                throw new DirectoryNotFoundException(migrationsDirectory);

            var applied = new List<int>();
            using var conn = _factory.Open();
            var existing = ReadAppliedVersions(conn);

            var files = Directory.EnumerateFiles(migrationsDirectory, "V*.sql")
                                 .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                                 .ToList();

            foreach (var file in files)
            {
                if (!TryExtractVersion(Path.GetFileName(file), out var version)) continue;
                if (existing.Contains(version)) continue;
                var sql = File.ReadAllText(file);
                conn.Execute(sql);
                applied.Add(version);
            }

            if (!string.IsNullOrWhiteSpace(seedDirectory) && Directory.Exists(seedDirectory))
            {
                foreach (var seed in Directory.EnumerateFiles(seedDirectory!, "*.sql").OrderBy(f => f))
                {
                    conn.Execute(File.ReadAllText(seed));
                }
            }

            return applied;
        }

        private static HashSet<int> ReadAppliedVersions(System.Data.IDbConnection conn)
        {
            try
            {
                var rows = conn.Query<int>("SELECT version FROM schema_version");
                return new HashSet<int>(rows);
            }
            catch
            {
                return new HashSet<int>();
            }
        }

        private static bool TryExtractVersion(string fileName, out int version)
        {
            version = 0;
            if (!fileName.StartsWith("V", StringComparison.OrdinalIgnoreCase)) return false;
            var underscore = fileName.IndexOf("__", StringComparison.Ordinal);
            if (underscore <= 1) return false;
            var raw = fileName.Substring(1, underscore - 1);
            return int.TryParse(raw, out version);
        }
    }
}
