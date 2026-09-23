using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Company.ErrorManagement.Contracts;

namespace Company.ErrorManagement.Core
{
    public sealed class PayloadRedactor : IPayloadRedactor
    {
        private static readonly string[] DefaultSensitiveKeys =
        {
            "password", "passwd", "pwd", "secret", "token", "authorization",
            "cookie", "set-cookie", "api-key", "apikey", "client_secret",
            "refresh_token", "access_token", "session", "session_id",
            "connectionstring", "connection-string", "conn-string"
        };

        private static readonly Regex ConnectionStringPattern = new Regex(
            @"(Password|Pwd|User\s*Id|Data\s*Source|Server|Initial\s*Catalog)\s*=\s*[^;]+",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex BearerTokenPattern = new Regex(
            @"Bearer\s+[A-Za-z0-9\-\._~\+\/]+=*",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex EmailPattern = new Regex(
            @"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}",
            RegexOptions.Compiled);

        private readonly HashSet<string> _sensitiveKeys;
        private readonly int _maxStackTraceChars;
        private readonly int _maxMessageChars;
        private readonly int _maxDiagnosticEntryChars;
        private readonly bool _redactEmails;

        public PayloadRedactor(RedactorOptions? options = null)
        {
            options ??= new RedactorOptions();
            _sensitiveKeys = new HashSet<string>(
                DefaultSensitiveKeys.Concat(options.AdditionalSensitiveKeys ?? Array.Empty<string>()),
                StringComparer.OrdinalIgnoreCase);
            _maxStackTraceChars = options.MaxStackTraceChars;
            _maxMessageChars = options.MaxMessageChars;
            _maxDiagnosticEntryChars = options.MaxDiagnosticEntryChars;
            _redactEmails = options.RedactEmails;
        }

        public string? Redact(string? value)
        {
            if (string.IsNullOrEmpty(value)) return value;
            var result = ConnectionStringPattern.Replace(value, m => $"{m.Groups[1].Value}=<redacted>");
            result = BearerTokenPattern.Replace(result, "Bearer <redacted>");
            if (_redactEmails) result = EmailPattern.Replace(result, "<redacted-email>");
            return result;
        }

        public ErrorEnvelope Redact(ErrorEnvelope envelope)
        {
            if (envelope == null) throw new ArgumentNullException(nameof(envelope));

            envelope.Message = Truncate(Redact(envelope.Message), _maxMessageChars);
            envelope.StackTrace = Truncate(Redact(envelope.StackTrace), _maxStackTraceChars);
            envelope.InnerExceptionSummary = Redact(envelope.InnerExceptionSummary);

            if (envelope.Diagnostics != null && envelope.Diagnostics.Count > 0)
            {
                var cleaned = new Dictionary<string, string>(envelope.Diagnostics.Count);
                foreach (var kv in envelope.Diagnostics)
                {
                    var value = _sensitiveKeys.Contains(kv.Key)
                        ? "<redacted>"
                        : Truncate(Redact(kv.Value), _maxDiagnosticEntryChars);
                    cleaned[kv.Key] = value ?? string.Empty;
                }
                envelope.Diagnostics = cleaned;
            }

            return envelope;
        }

        private static string? Truncate(string? value, int max)
        {
            if (string.IsNullOrEmpty(value)) return value;
            if (max <= 0 || value!.Length <= max) return value;
            return value.Substring(0, max) + "…<truncated>";
        }
    }

    public sealed class RedactorOptions
    {
        public IEnumerable<string>? AdditionalSensitiveKeys { get; set; }
        public int MaxMessageChars { get; set; } = 2000;
        public int MaxStackTraceChars { get; set; } = 12000;
        public int MaxDiagnosticEntryChars { get; set; } = 2000;
        public bool RedactEmails { get; set; } = false;
    }
}
