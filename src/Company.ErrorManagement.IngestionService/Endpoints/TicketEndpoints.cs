using System.Security.Claims;
using Company.ErrorManagement.Contracts;
using Company.ErrorManagement.IngestionService.Auth;
using Company.ErrorManagement.Persistence.Sqlite;
using Microsoft.AspNetCore.Mvc;

namespace Company.ErrorManagement.IngestionService.Endpoints;

public static class TicketEndpoints
{
    public static IEndpointRouteBuilder MapTicketEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/tickets").WithTags("Tickets");

        // Any authenticated user can create a ticket for an error they encountered.
        // ReportedBy is derived from the JWT sub claim, never trusted from the request body.
        group.MapPost("/", async (
            [FromBody] TicketCreateRequest request,
            ClaimsPrincipal user,
            ITicketRepository repository,
            CancellationToken ct) =>
        {
            request.ReportedBy = user.FindFirstValue(ClaimTypes.NameIdentifier)
                              ?? user.FindFirstValue(ClaimTypes.Name)
                              ?? request.ReportedBy;

            var ticket = await repository.CreateAsync(request, ct);
            return Results.Created($"/api/tickets/{ticket.TicketId}", ticket);
        })
        .RequireAuthorization(AuthorizationPolicies.AnyUser);

        // Users can view their own tickets; support and admin can view any ticket.
        group.MapGet("/{ticketId}", async (
            string ticketId,
            ClaimsPrincipal user,
            ITicketRepository repository,
            CancellationToken ct) =>
        {
            var ticket = await repository.GetAsync(ticketId, ct);
            if (ticket == null) return Results.NotFound();

            var sub     = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue(ClaimTypes.Name);
            var isStaff = IsStaff(user);
            if (!isStaff && ticket.ReportedBy != sub)
                return Results.Forbid();

            return Results.Ok(ticket);
        })
        .RequireAuthorization(AuthorizationPolicies.AnyUser);

        // Searching the full queue is restricted to support staff.
        group.MapGet("/", async (
            [AsParameters] TicketSearchQuery query,
            ITicketRepository repository,
            CancellationToken ct) =>
        {
            var results = await repository.SearchAsync(query, ct);
            return Results.Ok(results);
        })
        .RequireAuthorization(AuthorizationPolicies.Support);

        group.MapPost("/{ticketId}/assign", async (
            string ticketId,
            [FromBody] TicketAssignmentRequest request,
            ClaimsPrincipal user,
            ITicketRepository repository,
            CancellationToken ct) =>
        {
            request.ChangedBy = ActorId(user);
            await repository.AssignAsync(ticketId, request, ct);
            return Results.NoContent();
        })
        .RequireAuthorization(AuthorizationPolicies.Support);

        group.MapPost("/{ticketId}/status", async (
            string ticketId,
            [FromBody] TicketStatusChangeRequest request,
            ClaimsPrincipal user,
            ITicketRepository repository,
            CancellationToken ct) =>
        {
            request.ChangedBy = ActorId(user);
            await repository.ChangeStatusAsync(ticketId, request, ct);
            return Results.NoContent();
        })
        .RequireAuthorization(AuthorizationPolicies.Support);

        // Users can comment on their own tickets; support can comment on any ticket.
        group.MapPost("/{ticketId}/comments", async (
            string ticketId,
            [FromBody] TicketCommentRequest request,
            ClaimsPrincipal user,
            ITicketRepository repository,
            CancellationToken ct) =>
        {
            request.CreatedBy = ActorId(user);
            if (!IsStaff(user))
            {
                var ticket = await repository.GetAsync(ticketId, ct);
                if (ticket == null) return Results.NotFound();
                if (ticket.ReportedBy != ActorId(user)) return Results.Forbid();
                // End-users can only leave user-visible comments.
                request.Visibility = "REPORTER";
            }
            await repository.AddCommentAsync(ticketId, request, ct);
            return Results.NoContent();
        })
        .RequireAuthorization(AuthorizationPolicies.AnyUser);

        group.MapPost("/{ticketId}/resolve", async (
            string ticketId,
            [FromBody] TicketResolutionRequest request,
            ClaimsPrincipal user,
            ITicketRepository repository,
            CancellationToken ct) =>
        {
            request.ChangedBy = ActorId(user);
            await repository.ResolveAsync(ticketId, request, ct);
            return Results.NoContent();
        })
        .RequireAuthorization(AuthorizationPolicies.Support);

        // Full audit history is restricted to staff — it may contain technical details.
        group.MapGet("/{ticketId}/history", async (
            string ticketId,
            ITicketRepository repository,
            CancellationToken ct) =>
        {
            var history = await repository.GetStatusHistoryAsync(ticketId, ct);
            return Results.Ok(history);
        })
        .RequireAuthorization(AuthorizationPolicies.Support);

        return routes;
    }

    private static bool IsStaff(ClaimsPrincipal user) =>
        user.IsInRole("support") || user.IsInRole("admin");

    private static string ActorId(ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? user.FindFirstValue(ClaimTypes.Name)
        ?? "unknown";
}
