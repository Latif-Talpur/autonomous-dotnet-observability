using System.Threading.Tasks;
using Company.ErrorManagement.Contracts;
using Company.ErrorManagement.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Company.ErrorManagement.AspNetCore
{
    public sealed class CorrelationMiddleware
    {
        private readonly RequestDelegate next;
        private readonly ErrorManagementOptions options;
        public CorrelationMiddleware(RequestDelegate next, IOptions<ErrorManagementOptions> options) { this.next = next; this.options = options.Value; }
        public async Task InvokeAsync(HttpContext http, ICorrelationContext correlation)
        {
            var header = options.CorrelationHeaderName;
            var id = CorrelationIds.Normalize(http.Request.Headers[header].Count == 1 ? http.Request.Headers[header][0] : null);
            var user = http.User.Identity?.IsAuthenticated == true ? http.User.Identity.Name : null;
            using (CorrelationIds.BeginScope(correlation, id, user))
            {
                http.Response.OnStarting(() => { http.Response.Headers[header] = id; return Task.CompletedTask; });
                await next(http);
            }
        }
    }
}
