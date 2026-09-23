using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Company.ErrorManagement.Contracts;

namespace Company.ErrorManagement.WebApi2
{
    public sealed class ErrorCorrelationHandler : DelegatingHandler
    {
        public const string HeaderName = "X-Correlation-ID";
        private readonly ICorrelationContext _correlation;

        public ErrorCorrelationHandler(ICorrelationContext correlation)
        {
            _correlation = correlation ?? throw new ArgumentNullException(nameof(correlation));
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string correlationId;
            if (request.Headers.TryGetValues(HeaderName, out var values))
            {
                correlationId = string.Empty;
                foreach (var v in values) { correlationId = v; break; }
                if (string.IsNullOrWhiteSpace(correlationId))
                    correlationId = Guid.NewGuid().ToString("N");
            }
            else
            {
                correlationId = Guid.NewGuid().ToString("N");
            }

            _correlation.Set(correlationId);

            var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response != null && !response.Headers.Contains(HeaderName))
                response.Headers.Add(HeaderName, correlationId);
            return response!;
        }
    }
}
