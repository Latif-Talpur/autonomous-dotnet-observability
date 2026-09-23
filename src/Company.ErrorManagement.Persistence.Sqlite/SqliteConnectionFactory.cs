using System.Data;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace Company.ErrorManagement.Persistence.Sqlite
{
    public interface ISqliteConnectionFactory
    {
        IDbConnection Open();
    }

    public sealed class SqliteConnectionFactory : ISqliteConnectionFactory
    {
        private readonly SqliteOptions _options;

        public SqliteConnectionFactory(IOptions<SqliteOptions> options)
        {
            _options = options.Value;
        }

        public IDbConnection Open()
        {
            var conn = new SqliteConnection(_options.ConnectionString);
            conn.Open();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText =
                    $"PRAGMA foreign_keys = {(_options.EnableForeignKeys ? "ON" : "OFF")};" +
                    $"PRAGMA journal_mode = {_options.JournalMode};" +
                    $"PRAGMA busy_timeout = {_options.BusyTimeoutMs};";
                cmd.ExecuteNonQuery();
            }
            return conn;
        }
    }
}
