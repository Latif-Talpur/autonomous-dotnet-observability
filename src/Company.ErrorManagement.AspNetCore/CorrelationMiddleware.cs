using System;
using System.Threading.Tasks;
using Company.ErrorManagement.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Company.ErrorManagement.AspNetCore
{
    public sealed class CorrelationMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ErrorManagementOptions _options;

        public CorrelationMiddleware(RequestDelegate next, IOptions<ErrorManagementOptions> options)
        {
            _next = next;
            _options = options.Value;
        }

        public async Task InvokeAsync(HttpContext context, ICorrelationContext correlationContext)
        {
            var header = _options.CorrelationHeaderName;
            string correlationId;
            if (context.Request.Headers.TryGetValue(header, out var values) && !string.IsNullOrWhiteSpace(values))
            {
                correlationId = values!;
            }
            else
            {
                correlationId = Guid.NewGuid().ToString("N");
            }

            var userId = context.User?.Identity?.IsAuthenticated == true ? context.User.Identity.Name : null;
            correlationContext.Set(correlationId, userId);

            context.Response.OnStarting(() =>
            {
                if (!context.Response.Headers.ContainsKey(header))
                    context.Response.Headers[header] = correlationId;
                return Task.CompletedTask;
            });

            await _next(context);
        }
    }
}
