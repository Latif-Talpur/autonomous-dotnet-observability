using System.Threading;
using Company.ErrorManagement.Contracts;
using Company.ErrorManagement.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

namespace Company.ErrorManagement.AspNetCore
{
    public static class ClientErrorEndpoints
    {
        // Hosts attach their own authorization / rate-limit policy to the returned route.
        public static RouteHandlerBuilder MapErpClientErrors(this IEndpointRouteBuilder routes, string pattern = "/api/error-management/client-errors") =>
            routes.MapPost(pattern, async (ClientErrorReport payload, IErrorReporter reporter, ICorrelationContext correlation,
                IOptions<ErrorManagementOptions> options, HttpContext http, CancellationToken ct) =>
            {
                if (!ClientErrorMapping.IsValid(payload)) return Results.BadRequest();
                var o = options.Value;
                var user = http.User.Identity?.IsAuthenticated == true ? http.User.Identity.Name : null;
                var envelope = ClientErrorMapping.Map(payload, o.ApplicationName, o.EnvironmentName, o.ApplicationVersion, correlation.CorrelationId, user);
                var receipt = await reporter.CaptureAsync(envelope, ct);
                return Results.Json(new SafeErrorResponse {
                    ErrorReference = receipt.ErrorReference, CorrelationId = receipt.CorrelationId,
                    CanReportIssue = receipt.CanReportIssue, Status = receipt.Persisted ? 202 : 503,
                    Title = receipt.Persisted ? "Client error accepted" : "Error reporting is temporarily unavailable"
                }, statusCode: receipt.Persisted ? 202 : 503);
            });
    }
}
