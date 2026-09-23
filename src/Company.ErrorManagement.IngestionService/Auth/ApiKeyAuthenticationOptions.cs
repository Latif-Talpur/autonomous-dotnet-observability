using Microsoft.AspNetCore.Authentication;

namespace Company.ErrorManagement.IngestionService.Auth;

public sealed class ApiKeyAuthenticationOptions : AuthenticationSchemeOptions
{
    public const string SchemeName = "ApiKey";
    public const string HeaderName = "X-Api-Key";
}
