using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Company.ErrorManagement.Contracts;
using Company.ErrorManagement.Core;
using System.Web.Http;

namespace Company.ErrorManagement.WebApi2
{
    public sealed class ErrorCorrelationHandler : DelegatingHandler
    {
        public const string HeaderName = CorrelationIds.HeaderName;
        internal const string PropertyKey = "Company.ErrorManagement.Correlation";
        private readonly ICorrelationContext correlation;
        public ErrorCorrelationHandler(ICorrelationContext correlation) { this.correlation = correlation ?? throw new ArgumentNullException(nameof(correlation)); }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var values = request.Headers.TryGetValues(HeaderName, out var incoming) ? incoming.ToArray() : Array.Empty<string>();
            var id = CorrelationIds.Normalize(values.Length == 1 ? values[0] : null);
            request.Properties[PropertyKey] = id;
            var principal = request.GetRequestContext()?.Principal;
            using (CorrelationIds.BeginScope(correlation, id, principal?.Identity?.IsAuthenticated == true ? principal.Identity.Name : null))
            {
                var response = await base.SendAsync(request, ct).ConfigureAwait(false);
                response.Headers.Remove(HeaderName); response.Headers.TryAddWithoutValidation(HeaderName, id); return response;
            }
        }
    }
}
