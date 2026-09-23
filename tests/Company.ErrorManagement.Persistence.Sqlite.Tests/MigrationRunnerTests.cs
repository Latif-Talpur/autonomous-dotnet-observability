using Company.ErrorManagement.Persistence.Sqlite;
using Dapper;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Company.ErrorManagement.Persistence.Sqlite.Tests;

public class MigrationRunnerTests
{
    [Fact]
    public void Apply_Creates_Schema_Version_Rows()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"erp_migration_test_{Guid.NewGuid():N}.db");
        try
        {
            var factory = new SqliteConnectionFactory(Options.Create(new SqliteOptions { ConnectionString = $"Data Source={dbPath}" }));
            var runner = new SqliteMigrationRunner(factory);

            var root = FindRepoRoot();
            var applied = runner.Apply(Path.Combine(root, "database", "migrations"));

            applied.Should().Contain(1);
            applied.Should().Contain(2);

            using var conn = factory.Open();
            var versions = conn.Query<int>("SELECT version FROM schema_version").ToList();
            versions.Should().Contain(1);
            versions.Should().Contain(2);
        }
        finally
        {
            try { File.Delete(dbPath); } catch { }
        }
    }

    [Fact]
    public void Apply_Is_Idempotent()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"erp_idempotent_{Guid.NewGuid():N}.db");
        try
        {
            var factory = new SqliteConnectionFactory(Options.Create(new SqliteOptions { ConnectionString = $"Data Source={dbPath}" }));
            var runner = new SqliteMigrationRunner(factory);
            var root = FindRepoRoot();
            var dir = Path.Combine(root, "database", "migrations");

            var first = runner.Apply(dir);
            var second = runner.Apply(dir);

            second.Should().BeEmpty("already applied migrations should be skipped");
        }
        finally
        {
            try { File.Delete(dbPath); } catch { }
        }
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "AutonomousDotNetObservability.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate repo root");
    }
}
