using Company.ErrorManagement.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace Company.ErrorManagement.IngestionService.Endpoints;

public static class ErrorIngestionEndpoints
{
    public static IEndpointRouteBuilder MapErrorIngestionEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/error-management").WithTags("Errors");

        group.MapPost("/events", async (
            [FromBody] ErrorEnvelope envelope,
            IErrorReporter reporter,
            CancellationToken ct) =>
        {
            var receipt = await reporter.CaptureAsync(envelope, ct);
            return Results.Accepted($"/api/error-management/events/{receipt.ErrorReference}", receipt);
        }).WithName("IngestError");

        group.MapPost("/client-errors", async (
            [FromBody] ErrorEnvelope envelope,
            IErrorReporter reporter,
            CancellationToken ct) =>
        {
            envelope.Layer = ErrorLayer.Angular;
            var receipt = await reporter.CaptureAsync(envelope, ct);
            return Results.Accepted(null, receipt);
        }).WithName("IngestClientError");

        return routes;
    }
}
