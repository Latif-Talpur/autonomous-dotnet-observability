using System.Security.Claims;
using Company.ErrorManagement.Contracts;
using Company.ErrorManagement.IngestionService.Auth;
using Microsoft.AspNetCore.Mvc;

namespace Company.ErrorManagement.IngestionService.Endpoints;

public static class ErrorIngestionEndpoints
{
    public static IEndpointRouteBuilder MapErrorIngestionEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/error-management").WithTags("Errors");

        // Server-side errors require an application API key.
        // The authenticated application code must match the envelope to prevent cross-app pollution.
        group.MapPost("/events", async (
            [FromBody] ErrorEnvelope envelope,
            ClaimsPrincipal user,
            IErrorReporter reporter,
            CancellationToken ct) =>
        {
            var appCode = user.FindFirstValue("application_code");
            if (appCode != null && !string.Equals(envelope.ApplicationCode, appCode, StringComparison.OrdinalIgnoreCase))
                return Results.Forbid();

            var receipt = await reporter.CaptureAsync(envelope, ct);
            return Results.Accepted($"/api/error-management/events/{receipt.ErrorReference}", receipt);
        })
        .WithName("IngestError")
        .RequireAuthorization(AuthorizationPolicies.ApplicationIngestion);

        // Browser client errors: no API key required (key would be visible to all users).
        // Security relies on CORS restricting allowed origins.
        group.MapPost("/client-errors", async (
            [FromBody] ErrorEnvelope envelope,
            IErrorReporter reporter,
            CancellationToken ct) =>
        {
            envelope.Layer = ErrorLayer.Angular;
            var receipt = await reporter.CaptureAsync(envelope, ct);
            return Results.Accepted(null, receipt);
        })
        .WithName("IngestClientError")
        .RequireCors("ClientErrors");

        return routes;
    }
}
