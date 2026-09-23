namespace Company.ErrorManagement.IngestionService.Auth;

/// <summary>Authorization policy name constants used across endpoint registrations.</summary>
public static class AuthorizationPolicies
{
    /// <summary>Server application authenticated with an API key.</summary>
    public const string ApplicationIngestion = "ApplicationIngestion";

    /// <summary>JWT user with role <c>support</c> or <c>admin</c>.</summary>
    public const string Support = "Support";

    /// <summary>JWT user with role <c>admin</c>.</summary>
    public const string Admin = "Admin";

    /// <summary>Any authenticated JWT user.</summary>
    public const string AnyUser = "AnyUser";
}
