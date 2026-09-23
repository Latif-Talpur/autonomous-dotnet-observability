using Company.ErrorManagement.Contracts;
using Company.ErrorManagement.Persistence.Sqlite;
using Microsoft.AspNetCore.Mvc;

namespace Company.ErrorManagement.IngestionService.Endpoints;

public static class TicketEndpoints
{
    public static IEndpointRouteBuilder MapTicketEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/tickets").WithTags("Tickets");

        group.MapPost("/", async (
            [FromBody] TicketCreateRequest request,
            ITicketRepository repository,
            CancellationToken ct) =>
        {
            var ticket = await repository.CreateAsync(request, ct);
            return Results.Created($"/api/tickets/{ticket.TicketId}", ticket);
        });

        group.MapGet("/{ticketId}", async (
            string ticketId,
            ITicketRepository repository,
            CancellationToken ct) =>
        {
            var ticket = await repository.GetAsync(ticketId, ct);
            return ticket == null ? Results.NotFound() : Results.Ok(ticket);
        });

        group.MapGet("/", async (
            [AsParameters] TicketSearchQuery query,
            ITicketRepository repository,
            CancellationToken ct) =>
        {
            var results = await repository.SearchAsync(query, ct);
            return Results.Ok(results);
        });

        group.MapPost("/{ticketId}/assign", async (
            string ticketId,
            [FromBody] TicketAssignmentRequest request,
            ITicketRepository repository,
            CancellationToken ct) =>
        {
            await repository.AssignAsync(ticketId, request, ct);
            return Results.NoContent();
        });

        group.MapPost("/{ticketId}/status", async (
            string ticketId,
            [FromBody] TicketStatusChangeRequest request,
            ITicketRepository repository,
            CancellationToken ct) =>
        {
            await repository.ChangeStatusAsync(ticketId, request, ct);
            return Results.NoContent();
        });

        group.MapPost("/{ticketId}/comments", async (
            string ticketId,
            [FromBody] TicketCommentRequest request,
            ITicketRepository repository,
            CancellationToken ct) =>
        {
            await repository.AddCommentAsync(ticketId, request, ct);
            return Results.NoContent();
        });

        group.MapPost("/{ticketId}/resolve", async (
            string ticketId,
            [FromBody] TicketResolutionRequest request,
            ITicketRepository repository,
            CancellationToken ct) =>
        {
            await repository.ResolveAsync(ticketId, request, ct);
            return Results.NoContent();
        });

        group.MapGet("/{ticketId}/history", async (
            string ticketId,
            ITicketRepository repository,
            CancellationToken ct) =>
        {
            var history = await repository.GetStatusHistoryAsync(ticketId, ct);
            return Results.Ok(history);
        });

        return routes;
    }
}
