using Microsoft.Extensions.DependencyInjection;
using Application.Interfaces;

namespace Application
{
    public static class DependencyServiceInjection
    {
        public static IServiceCollection AddApplicationDependencies(this IServiceCollection services)
        {
            services.AddScoped<IAuthenticationService, Services.AuthenticationService>();
            services.AddScoped<ILocalAuthService, Services.LocalAuthenticationService>();
            services.AddScoped<IRoleService, Services.RoleService>(); 
            services.AddScoped<IUserService, Services.UserService>();

            return services;
        }
    }
}
