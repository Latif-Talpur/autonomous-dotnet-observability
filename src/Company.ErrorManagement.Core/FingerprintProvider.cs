using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Company.ErrorManagement.Contracts;

namespace Company.ErrorManagement.Core
{
    public sealed class FingerprintProvider : IErrorFingerprintProvider
    {
        private static readonly Regex GuidPattern = new Regex(
            @"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}",
            RegexOptions.Compiled);

        private static readonly Regex NumberPattern = new Regex(@"\b\d+\b", RegexOptions.Compiled);

        private static readonly Regex WhitespacePattern = new Regex(@"\s+", RegexOptions.Compiled);

        private static readonly Regex StackLineNumberPattern = new Regex(
            @":line\s+\d+", RegexOptions.Compiled);

        private static readonly Regex StackHexAddress = new Regex(
            @"0x[0-9a-fA-F]+", RegexOptions.Compiled);

        public int Version => 1;

        public string Generate(ErrorEnvelope error)
        {
            if (error == null) throw new ArgumentNullException(nameof(error));

            var normalized = new StringBuilder();
            normalized.Append(error.ApplicationCode).Append('|');
            normalized.Append(error.EnvironmentCode).Append('|');
            normalized.Append(error.Layer).Append('|');
            normalized.Append(error.ExceptionType ?? string.Empty).Append('|');
            normalized.Append(NormalizeText(error.Message)).Append('|');
            normalized.Append(NormalizeStack(error.StackTrace));

            using (var sha = SHA256.Create())
            {
                var bytes = Encoding.UTF8.GetBytes(normalized.ToString());
                var hash = sha.ComputeHash(bytes);
                var sb = new StringBuilder(hash.Length * 2);
                foreach (var b in hash) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        private static string NormalizeText(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            var stripped = GuidPattern.Replace(value, "<guid>");
            stripped = NumberPattern.Replace(stripped, "<n>");
            stripped = WhitespacePattern.Replace(stripped, " ");
            return stripped.Trim();
        }

        private static string NormalizeStack(string? stackTrace)
        {
            if (string.IsNullOrWhiteSpace(stackTrace)) return string.Empty;
            var normalized = StackLineNumberPattern.Replace(stackTrace, string.Empty);
            normalized = StackHexAddress.Replace(normalized, "<hex>");
            normalized = GuidPattern.Replace(normalized, "<guid>");
            normalized = WhitespacePattern.Replace(normalized, " ");
            return normalized.Trim();
        }
    }
}
