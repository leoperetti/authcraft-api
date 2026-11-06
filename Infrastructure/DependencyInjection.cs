using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Infrastructure.Data;
using Infrastructure.Repositories;
using Infrastructure.Configuration;
using Infrastructure.Health;

namespace Infrastructure
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddInfrastructure(
            this IServiceCollection services,
            IConfiguration configuration,
            string? connectionStringOverride = null,
            string environmentName = "Development")
        {
            var resolvedConnectionString = DatabaseConfiguration.ResolveAndValidate(
                configuration,
                connectionStringOverride,
                environmentName);

            services.AddDbContext<AppDbContext>(options =>
                options.UseSqlServer(resolvedConnectionString));

            // Repositories
            services.AddScoped<IIdentityRepository, IdentityRepository>();
            services.AddScoped<IRoleRepository, RoleRepository>();
            services.AddScoped<IProviderRepository, ProviderRepository>();
            services.AddScoped<IOAuthAccountRepository, OAuthAccountRepository>();
            services.AddScoped<ILocalAccountRepository, LocalAccountRepository>();
            services.AddScoped<IInvitationRepository, InvitationRepository>();
            services.AddScoped<ITenantRepository, TenantRepository>();
            services.AddScoped<IUnitOfWork, UnitOfWork>();
            services.AddScoped<IPasswordResetTokenRepository, PasswordResetTokenRepository>();
            services.AddScoped<DatabaseReadinessCheck.IChecker, DatabaseReadinessCheck>();
            // Email service: registered in Application layer implementation (if desired)

            return services;
        }
    }
}
