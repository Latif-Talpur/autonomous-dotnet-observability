using Company.ErrorManagement.IngestionService.Auth;
using Company.ErrorManagement.Persistence.Sqlite;
using Microsoft.AspNetCore.Mvc;

namespace Company.ErrorManagement.IngestionService.Endpoints;

public static class ApplicationEndpoints
{
    public static IEndpointRouteBuilder MapApplicationEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/applications")
                          .WithTags("Applications")
                          .RequireAuthorization(AuthorizationPolicies.Admin);

        // List all registered applications.
        group.MapGet("/", async (
            IApplicationRepository repo,
            CancellationToken ct) =>
        {
            var apps = await repo.GetApplicationsAsync(ct);
            return Results.Ok(apps);
        });

        // Register a new application (or update the name of an existing one).
        group.MapPost("/", async (
            [FromBody] RegisterApplicationRequest request,
            IApplicationRepository repo,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.Name))
                return Results.BadRequest("Code and Name are required.");

            var id = await repo.RegisterApplicationAsync(request.Code.Trim(), request.Name.Trim(), ct);
            return Results.Ok(new { applicationId = id, code = request.Code, name = request.Name });
        });

        // List credentials for an application.
        group.MapGet("/{applicationId}/credentials", async (
            string applicationId,
            IApplicationRepository repo,
            CancellationToken ct) =>
        {
            var credentials = await repo.GetCredentialsAsync(applicationId, ct);
            return Results.Ok(credentials);
        });

        // Issue a new API key for an application.
        // The raw key is returned ONCE here and never stored — it cannot be retrieved again.
        group.MapPost("/{applicationId}/credentials", async (
            string applicationId,
            [FromBody] CreateCredentialRequest request,
            IApplicationRepository repo,
            CancellationToken ct) =>
        {
            TimeSpan? validFor = request.ValidForDays.HasValue
                ? TimeSpan.FromDays(request.ValidForDays.Value)
                : null;

            var (credentialId, rawKey) = await repo.CreateCredentialAsync(
                applicationId, request.Description, validFor, ct);

            return Results.Ok(new
            {
                credentialId,
                rawKey,      // shown only once — caller must store this securely
                prefix = rawKey[..12]
            });
        });

        // Revoke a credential (soft delete — sets is_active = 0).
        group.MapDelete("/{applicationId}/credentials/{credentialId}", async (
            string applicationId,
            string credentialId,
            IApplicationRepository repo,
            CancellationToken ct) =>
        {
            await repo.RevokeCredentialAsync(credentialId, ct);
            return Results.NoContent();
        });

        return routes;
    }

    private sealed class RegisterApplicationRequest
    {
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
    }

    private sealed class CreateCredentialRequest
    {
        public string? Description  { get; set; }
        public int?    ValidForDays { get; set; }
    }
}
