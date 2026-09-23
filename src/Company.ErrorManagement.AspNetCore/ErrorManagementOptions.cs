namespace Company.ErrorManagement.AspNetCore
{
    public sealed class ErrorManagementOptions
    {
        public string ApplicationName { get; set; } = "ModernERP";
        public string EnvironmentName { get; set; } = "Development";
        public string? ApplicationVersion { get; set; }
        public bool IncludeStackTraceInDevelopment { get; set; } = false;
        public string CorrelationHeaderName { get; set; } = "X-Correlation-ID";
    }
}
