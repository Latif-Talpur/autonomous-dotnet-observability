using Microsoft.AspNetCore.Builder;

namespace Company.ErrorManagement.AspNetCore
{
    public static class ApplicationBuilderExtensions
    {
        public static IApplicationBuilder UseErpCorrelation(this IApplicationBuilder app)
        {
            return app.UseMiddleware<CorrelationMiddleware>();
        }

        public static IApplicationBuilder UseErpExceptionHandling(this IApplicationBuilder app)
        {
            return app.UseMiddleware<ErpExceptionHandlingMiddleware>();
        }
    }
}
