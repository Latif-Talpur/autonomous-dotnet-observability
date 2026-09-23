using System;
using System.Data.Common;
using System.Globalization;
using Company.ErrorManagement.Contracts;

namespace Company.ErrorManagement.Core
{
    // Avoid provider package dependencies in the netstandard core. Read only allowlisted public metadata.
    public static class DatabaseExceptionMetadata
    {
        public static void Apply(Exception exception, ErrorEnvelope envelope)
        {
            for (Exception? current = exception; current != null; current = current.InnerException)
            {
                if (current is TimeoutException) { envelope.IsTimeout = true; envelope.IsTransient = true; }
                var type = current.GetType().FullName ?? "";
                if (!(current is DbException) && type != "Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException") continue;
                envelope.CategoryCode = "DATABASE";
                envelope.DbProvider = envelope.DbProvider ?? type;
                if (type == "Microsoft.Data.SqlClient.SqlException" || type == "System.Data.SqlClient.SqlException")
                {
                    envelope.DbErrorCode = Read(current, "Number");
                    envelope.DbProcedure = envelope.DbProcedure ?? Read(current, "Procedure");
                    if (int.TryParse(Read(current, "LineNumber"), out var line)) envelope.DbLineNumber = line;
                    envelope.IsTimeout |= envelope.DbErrorCode == "-2";
                    envelope.IsTransient |= envelope.IsTimeout || envelope.DbErrorCode == "1205" || envelope.DbErrorCode == "40501" || envelope.DbErrorCode == "40613";
                }
                else if (type == "Npgsql.PostgresException")
                {
                    envelope.DbErrorCode = Read(current, "SqlState");
                    // PostgreSQL routine/detail fields can reveal server internals; omit them.
                    envelope.IsTransient |= envelope.DbErrorCode == "40001" || envelope.DbErrorCode == "40P01" || envelope.DbErrorCode == "53300";
                }
                else if (type == "Microsoft.Data.Sqlite.SqliteException")
                {
                    envelope.DbErrorCode = Read(current, "SqliteErrorCode");
                    envelope.IsTransient |= envelope.DbErrorCode == "5" || envelope.DbErrorCode == "6";
                    var extended = Read(current, "SqliteExtendedErrorCode");
                    if (extended != null) envelope.Diagnostics["db.extendedCode"] = extended;
                }
                else if (type == "MySqlConnector.MySqlException" || type == "MySql.Data.MySqlClient.MySqlException")
                {
                    envelope.DbErrorCode = Read(current, "Number");
                    envelope.IsTransient |= envelope.DbErrorCode == "1205" || envelope.DbErrorCode == "1213";
                }
                else if (current is DbException db) envelope.DbErrorCode = db.ErrorCode.ToString(CultureInfo.InvariantCulture);
            }
        }
        private static string? Read(Exception exception, string name)
        {
            try { return Convert.ToString(exception.GetType().GetProperty(name)?.GetValue(exception, null), CultureInfo.InvariantCulture); }
            catch { return null; }
        }
    }
}
