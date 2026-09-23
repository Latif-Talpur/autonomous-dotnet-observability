using System;
using System.Linq;
using Company.ErrorManagement.Contracts;

namespace Company.ErrorManagement.Core
{
    public static class CorrelationIds
    {
        public const string HeaderName = "X-Correlation-ID";
        public static bool IsValid(string? value) => !string.IsNullOrWhiteSpace(value) && value!.Length <= 128 &&
            value.All(c => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '-' || c == '_' || c == '.');
        public static string Normalize(string? value) => IsValid(value) ? value! : Guid.NewGuid().ToString("N");
        public static IDisposable BeginScope(ICorrelationContext context, string? correlationId = null, string? userId = null)
            => new Scope(context, Normalize(correlationId), userId);
        private sealed class Scope : IDisposable
        {
            private readonly ICorrelationContext context; private readonly string previous; private readonly string? user; private bool disposed;
            public Scope(ICorrelationContext context, string id, string? userId)
            { this.context = context; previous = context.CorrelationId; user = context.UserId; context.Set(id, userId); }
            public void Dispose() { if (!disposed) { context.Set(previous, user); disposed = true; } }
        }
    }
}
