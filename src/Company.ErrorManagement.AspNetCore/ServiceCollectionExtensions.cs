using System;
using Microsoft.Extensions.DependencyInjection;

namespace Company.ErrorManagement.AspNetCore
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddErpErrorManagement(this IServiceCollection services, Action<ErrorManagementOptions> configure)
        {
            services.Configure(configure);
            return services;
        }
    }
}
