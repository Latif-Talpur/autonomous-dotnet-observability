using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Company.ErrorManagement.Contracts;
using Company.ErrorManagement.Core;

namespace Company.ErrorManagement.Transport.Http
{
    // Attach only to explicitly configured internal API clients, never arbitrary third-party requests.
    public sealed class CorrelationPropagationHandler : DelegatingHandler
    {
        private readonly ICorrelationContext correlation;
        private readonly string header;
        public CorrelationPropagationHandler(ICorrelationContext correlation, string header = CorrelationIds.HeaderName)
        { this.correlation = correlation; this.header = header; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            request.Headers.Remove(header);
            request.Headers.TryAddWithoutValidation(header, CorrelationIds.Normalize(correlation.CorrelationId));
            return base.SendAsync(request, ct);
        }
    }
}
