using Infrastructure.Data;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text.Json;

namespace authcraft_api.Cors
{
    public class TenantCorsPolicyProvider : ICorsPolicyProvider
    {
        private static readonly CorsPolicy BlockedPolicy = new CorsPolicyBuilder().Build();

        private readonly IServiceScopeFactory _serviceScopeFactory;

        public TenantCorsPolicyProvider(IServiceScopeFactory serviceScopeFactory)
        {
            _serviceScopeFactory = serviceScopeFactory;
        }

        public async Task<CorsPolicy?> GetPolicyAsync(HttpContext context, string? policyName)
        {
            var origin = context.Request.Headers.Origin.ToString();
            if (string.IsNullOrWhiteSpace(origin))
            {
                return BlockedPolicy;
            }

            var tenantIdentifier = ResolveTenantIdentifier(context);
            if (string.IsNullOrWhiteSpace(tenantIdentifier))
            {
                return BlockedPolicy;
            }

            var normalizedOrigin = NormalizeOrigin(origin);
            if (string.IsNullOrWhiteSpace(normalizedOrigin))
            {
                return BlockedPolicy;
            }

            using var scope = _serviceScopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var tenantOriginsJson = await GetTenantAllowedOriginsJsonAsync(dbContext, tenantIdentifier);
            if (string.IsNullOrWhiteSpace(tenantOriginsJson))
            {
                return BlockedPolicy;
            }

            var allowedOrigins = DeserializeOrigins(tenantOriginsJson);
            var isAllowed = allowedOrigins.Any(allowedOrigin =>
                string.Equals(NormalizeOrigin(allowedOrigin), normalizedOrigin, StringComparison.OrdinalIgnoreCase));

            if (!isAllowed)
            {
                return BlockedPolicy;
            }

            return new CorsPolicyBuilder()
                .WithOrigins(origin)
                .AllowAnyHeader()
                .AllowAnyMethod()
                .Build();
        }

        private static string? ResolveTenantIdentifier(HttpContext context)
        {
            var tenantFromQuery = context.Request.Query["tenant"].ToString();
            if (!string.IsNullOrWhiteSpace(tenantFromQuery))
            {
                return tenantFromQuery.Trim();
            }

            var tenantClaimValue = context.User.FindFirstValue("tenant")
                                  ?? context.User.FindFirstValue("tenant_id")
                                  ?? context.User.FindFirstValue("tid");

            return string.IsNullOrWhiteSpace(tenantClaimValue) ? null : tenantClaimValue.Trim();
        }

        private static async Task<string?> GetTenantAllowedOriginsJsonAsync(AppDbContext dbContext, string tenantIdentifier)
        {
            if (long.TryParse(tenantIdentifier, out var tenantId))
            {
                return await dbContext.Tenants
                    .AsNoTracking()
                    .Where(tenant => tenant.Id == tenantId)
                    .Select(tenant => tenant.AllowedOrigins)
                    .FirstOrDefaultAsync();
            }

            var normalizedTenantIdentifier = tenantIdentifier.Trim().ToLowerInvariant();
            return await dbContext.Tenants
                .AsNoTracking()
                .Where(tenant => tenant.Slug.ToLower() == normalizedTenantIdentifier || tenant.ClientId.ToLower() == normalizedTenantIdentifier)
                .Select(tenant => tenant.AllowedOrigins)
                .FirstOrDefaultAsync();
        }

        private static IReadOnlyList<string> DeserializeOrigins(string? originsJson)
        {
            if (string.IsNullOrWhiteSpace(originsJson))
            {
                return Array.Empty<string>();
            }

            try
            {
                return JsonSerializer.Deserialize<List<string>>(originsJson) ?? new List<string>();
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        private static string NormalizeOrigin(string origin)
        {
            return origin.Trim().TrimEnd('/');
        }
    }
}
