using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Company.ErrorManagement.Persistence.Sqlite;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Company.ErrorManagement.IngestionService.Auth;

/// <summary>
/// Validates the <c>X-Api-Key</c> request header against registered application credentials.
/// On success, the principal carries <c>application_id</c>, <c>application_code</c>, and
/// role <c>application</c> claims.
/// </summary>
public sealed class ApiKeyAuthenticationHandler : AuthenticationHandler<ApiKeyAuthenticationOptions>
{
    private readonly IApplicationRepository _applications;

    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<ApiKeyAuthenticationOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IApplicationRepository applications)
        : base(options, logger, encoder)
    {
        _applications = applications;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(ApiKeyAuthenticationOptions.HeaderName, out var values))
            return AuthenticateResult.NoResult();

        var rawKey = values.FirstOrDefault()?.Trim();
        if (string.IsNullOrEmpty(rawKey))
            return AuthenticateResult.Fail("Empty API key.");

        var keyHash = HashKey(rawKey);
        var credential = await _applications.FindByKeyHashAsync(keyHash, CancellationToken.None);

        if (credential == null || !credential.IsActive)
            return AuthenticateResult.Fail("Invalid or inactive API key.");

        if (credential.ExpiresAtUtc.HasValue && credential.ExpiresAtUtc.Value < DateTime.UtcNow)
            return AuthenticateResult.Fail("API key has expired.");

        var claims = new[]
        {
            new Claim("application_id",   credential.ApplicationId),
            new Claim("application_code", credential.ApplicationCode),
            new Claim(ClaimTypes.Name,    credential.ApplicationCode),
            new Claim(ClaimTypes.Role,    "application")
        };

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var ticket   = new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name);
        return AuthenticateResult.Success(ticket);
    }

    /// <summary>SHA-256 hex of the raw key — same algorithm used when credentials are created.</summary>
    internal static string HashKey(string rawKey)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawKey));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
