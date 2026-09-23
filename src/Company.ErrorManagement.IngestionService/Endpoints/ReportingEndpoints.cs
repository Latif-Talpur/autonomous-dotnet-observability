using Company.ErrorManagement.Persistence.Sqlite;

namespace Company.ErrorManagement.IngestionService.Endpoints;

public static class ReportingEndpoints
{
    public static IEndpointRouteBuilder MapReportingEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/reports").WithTags("Reports");

        group.MapGet("/top-errors", async (
            IReportingRepository reporting,
            int? days,
            int? take,
            CancellationToken ct) =>
        {
            var from = DateTime.UtcNow.AddDays(-(days ?? 7));
            var rows = await reporting.GetTopErrorsAsync(from, take ?? 25, ct);
            return Results.Ok(rows);
        });

        group.MapGet("/queue-stats", async (
            IReportingRepository reporting,
            CancellationToken ct) =>
        {
            var rows = await reporting.GetQueueStatsAsync(ct);
            return Results.Ok(rows);
        });

        group.MapPost("/retention/run", async (
            IReportingRepository reporting,
            int retentionDays,
            CancellationToken ct) =>
        {
            var cutoff = DateTime.UtcNow.AddDays(-retentionDays);
            var archived = await reporting.ArchiveOccurrencesOlderThanAsync(cutoff, ct);
            return Results.Ok(new { archived });
        });

        return routes;
    }
}
