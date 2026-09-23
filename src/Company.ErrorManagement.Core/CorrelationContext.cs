using System.Threading;
using Company.ErrorManagement.Contracts;

namespace Company.ErrorManagement.Core
{
    public sealed class AmbientCorrelationContext : ICorrelationContext
    {
        private static readonly AsyncLocal<Ambient> Current = new AsyncLocal<Ambient>();

        public string CorrelationId => Current.Value?.CorrelationId ?? string.Empty;
        public string? UserId => Current.Value?.UserId;

        public void Set(string correlationId, string? userId = null)
        {
            Current.Value = new Ambient { CorrelationId = correlationId, UserId = userId };
        }

        private sealed class Ambient
        {
            public string CorrelationId { get; set; } = string.Empty;
            public string? UserId { get; set; }
        }
    }
}
