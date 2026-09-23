namespace Company.ErrorManagement.Persistence.Sqlite
{
    public sealed class SqliteOptions
    {
        public string ConnectionString { get; set; } = "Data Source=erp_error_management.db";
        public bool EnableForeignKeys { get; set; } = true;
        public string JournalMode { get; set; } = "WAL";
        public int BusyTimeoutMs { get; set; } = 5000;
    }
}
