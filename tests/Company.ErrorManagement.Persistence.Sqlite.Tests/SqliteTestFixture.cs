using Company.ErrorManagement.Persistence.Sqlite;
using Microsoft.Extensions.Options;

namespace Company.ErrorManagement.Persistence.Sqlite.Tests;

public sealed class SqliteTestFixture : IDisposable
{
    public ISqliteConnectionFactory Factory { get; }
    private readonly string _dbPath;

    public SqliteTestFixture()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"erp_test_{Guid.NewGuid():N}.db");

        var options = Options.Create(new SqliteOptions { ConnectionString = $"Data Source={_dbPath}" });
        Factory = new SqliteConnectionFactory(options);

        ApplySchemas();
        ApplySeed();
    }

    private void ApplySchemas()
    {
        var root = FindRepoRoot();
        var runner = new SqliteMigrationRunner(Factory);
        runner.Apply(
            Path.Combine(root, "database", "migrations"),
            Path.Combine(root, "database", "seed"));
    }

    private void ApplySeed()
    {
        // Already applied by runner above with seed directory.
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
        throw new DirectoryNotFoundException("Could not locate repo root from " + AppContext.BaseDirectory);
    }

    public void Dispose()
    {
        try { File.Delete(_dbPath); } catch { }
    }
}
