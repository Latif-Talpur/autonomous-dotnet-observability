using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;

namespace Company.ErrorManagement.IngestionService.Auth;

/// <summary>
/// Development-only token issuer.  Only mapped when the environment is not Production.
/// Issue a JWT for testing with: POST /api/auth/token { "userId": "...", "role": "admin" }
/// </summary>
public static class DevTokenEndpoints
{
    public static IEndpointRouteBuilder MapDevTokenEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/api/auth/token", (
            [FromBody] DevTokenRequest request,
            IConfiguration config) =>
        {
            var jwtSection = config.GetSection("Jwt");
            var signingKey  = jwtSection["SigningKey"] ?? throw new InvalidOperationException("Jwt:SigningKey not configured.");
            var issuer      = jwtSection["Issuer"]     ?? "erp-error-management";
            var audience    = jwtSection["Audience"]   ?? "erp-error-management";

            var role = request.Role?.ToLowerInvariant() switch
            {
                "admin"   => "admin",
                "support" => "support",
                _         => "user"
            };

            var claims = new List<Claim>
            {
                new Claim(JwtRegisteredClaimNames.Sub,  request.UserId ?? Guid.NewGuid().ToString("N")),
                new Claim(JwtRegisteredClaimNames.Jti,  Guid.NewGuid().ToString("N")),
                new Claim(ClaimTypes.Role, role),
                new Claim(ClaimTypes.Name, request.UserId ?? "dev-user")
            };

            var key   = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
            var token = new JwtSecurityToken(
                issuer:             issuer,
                audience:           audience,
                claims:             claims,
                expires:            DateTime.UtcNow.AddHours(8),
                signingCredentials: creds);

            return Results.Ok(new { token = new JwtSecurityTokenHandler().WriteToken(token), role, expiresIn = 28800 });
        })
        .WithTags("Dev")
        .AllowAnonymous()
        .WithSummary("Development-only: issue a signed JWT for API testing.");

        return routes;
    }

    private sealed class DevTokenRequest
    {
        public string? UserId { get; set; }
        public string? Role   { get; set; } // admin | support | user
    }
}
