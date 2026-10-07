using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Infrastructure.Data;

namespace Infrastructure
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
        {
            var connectionString = configuration.GetConnectionString("DefaultConnection");
            services.AddDbContext<AppDbContext>(options =>
                options.UseSqlServer(connectionString));

            // Repositories
            services.AddScoped<Application.Repositories.IUserRepository, Infrastructure.Repositories.UserRepository>();
            services.AddScoped<Application.Repositories.IProviderRepository, Infrastructure.Repositories.ProviderRepository>();
            services.AddScoped<Application.Repositories.IOAuthAccountRepository, Infrastructure.Repositories.OAuthAccountRepository>();
            services.AddScoped<Application.Repositories.ILocalAccountRepository, Infrastructure.Repositories.LocalAccountRepository>();

            // Application services implemented here
            services.AddScoped<Application.Services.IAuthenticationService, Application.Services.AuthenticationService>();
            services.AddScoped<Application.Services.ILocalAuthService, Application.Services.LocalAuthenticationService>();

            return services;
        }
    }
}
