using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Infrastructure.Data;
using Infrastructure.Repositories;

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
            services.AddScoped<IUserRepository, UserRepository>();
            services.AddScoped<IRoleRepository, RoleRepository>();
            services.AddScoped<IProviderRepository, ProviderRepository>();
            services.AddScoped<IOAuthAccountRepository, OAuthAccountRepository>();
            services.AddScoped<ILocalAccountRepository, LocalAccountRepository>();

            return services;
        }
    }
}
