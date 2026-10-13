using Microsoft.Extensions.DependencyInjection;
using Application.Interfaces;
using Application.Audit;
using Application.Services;

namespace Application
{
    public static class DependencyServiceInjection
    {
        public static IServiceCollection AddApplicationDependencies(this IServiceCollection services)
        {
            services.AddScoped<IAuthenticationService, AuthenticationService>();
            services.AddScoped<ILocalAuthService, LocalAuthenticationService>();
            services.AddScoped<IRoleService, RoleService>();
            services.AddScoped<ITenantService, TenantService>();
            services.AddScoped<IUserService, UserService>();
            services.AddScoped<IAuditService, AuditService>();

            return services;
        }
    }
}
