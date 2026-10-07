using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Infrastructure.Data;

namespace Infrastructure
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration, string? connectionStringOverride = null)
        {
            if (string.IsNullOrWhiteSpace(connectionStringOverride))
            {
                throw new InvalidOperationException("Database connection string is not configured. Set ConnectionStrings:DefaultConnection or ConnectionStrings:AZURE_SQL_CONNECTIONSTRING or provide an override.");
            }

            services.AddDbContext<AppDbContext>(options =>
                options.UseSqlServer(connectionStringOverride));

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
