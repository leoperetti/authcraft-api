using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Application.Interfaces;
using Domain.Dto;
using Domain.Entities;
using Domain.Request;
using Domain.Response;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Application.Services
{
    public class TenantService : ITenantService
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

        private readonly AppDbContext _dbContext;

        public TenantService(AppDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task<IReadOnlyList<TenantDto>> ListAsync(CancellationToken cancellationToken = default)
        {
            var tenants = await _dbContext.Tenants
                .AsNoTracking()
                .OrderBy(tenant => tenant.Id)
                .ToListAsync(cancellationToken);

            return tenants.Select(Map).ToList();
        }

        public async Task<TenantDto?> GetAsync(long id, CancellationToken cancellationToken = default)
        {
            var tenant = await _dbContext.Tenants
                .AsNoTracking()
                .FirstOrDefaultAsync(currentTenant => currentTenant.Id == id, cancellationToken);

            return tenant is null ? null : Map(tenant);
        }

        public async Task<CreateTenantResponse> CreateAsync(CreateTenantRequest request, CancellationToken cancellationToken = default)
        {
            var allowedOrigins = NormalizeOrigins(request.AllowedOrigins);
            var apiKeyPlain = $"tk_live_{Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant()}";
            var apiKeyHash = HashSha256(apiKeyPlain);

            var tenant = new Tenant
            {
                ClientId = request.ClientId.Trim(),
                Slug = request.Slug.Trim().ToLowerInvariant(),
                Name = request.Name.Trim(),
                Status = request.Status,
                Settings = request.Settings is null ? null : JsonSerializer.Serialize(request.Settings, JsonOptions),
                AllowedOrigins = JsonSerializer.Serialize(allowedOrigins, JsonOptions)
            };

            tenant.TenantApiKeys.Add(new TenantApiKey
            {
                KeyHash = apiKeyHash,
                Scopes = JsonSerializer.Serialize(new[] { "tenants:read", "tenants:write" }, JsonOptions)
            });

            _dbContext.Tenants.Add(tenant);
            await _dbContext.SaveChangesAsync(cancellationToken);

            return new CreateTenantResponse(Map(tenant), apiKeyPlain);
        }

        public async Task<TenantDto?> UpdateAsync(long id, UpdateTenantRequest request, CancellationToken cancellationToken = default)
        {
            var tenant = await _dbContext.Tenants.FirstOrDefaultAsync(currentTenant => currentTenant.Id == id, cancellationToken);
            if (tenant is null)
            {
                return null;
            }

            if (request.Name is not null)
            {
                tenant.Name = request.Name.Trim();
            }

            if (request.Status is not null)
            {
                tenant.Status = request.Status;
            }

            if (request.Settings is not null)
            {
                tenant.Settings = JsonSerializer.Serialize(request.Settings, JsonOptions);
            }

            if (request.AllowedOrigins is not null)
            {
                tenant.AllowedOrigins = JsonSerializer.Serialize(NormalizeOrigins(request.AllowedOrigins), JsonOptions);
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
            return Map(tenant);
        }

        public async Task<bool> UpdateStatusAsync(long id, string status, CancellationToken cancellationToken = default)
        {
            var tenant = await _dbContext.Tenants.FirstOrDefaultAsync(currentTenant => currentTenant.Id == id, cancellationToken);
            if (tenant is null)
            {
                return false;
            }

            tenant.Status = status;
            await _dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }

        public async Task<bool> DeleteAsync(long id, CancellationToken cancellationToken = default)
        {
            var tenant = await _dbContext.Tenants.FirstOrDefaultAsync(currentTenant => currentTenant.Id == id, cancellationToken);
            if (tenant is null)
            {
                return false;
            }

            _dbContext.Tenants.Remove(tenant);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }

        private static TenantDto Map(Tenant tenant)
        {
            return new TenantDto
            {
                Id = tenant.Id,
                ClientId = tenant.ClientId,
                Slug = tenant.Slug,
                Name = tenant.Name,
                Status = tenant.Status,
                Settings = DeserializeSettings(tenant.Settings),
                AllowedOrigins = DeserializeOrigins(tenant.AllowedOrigins)
            };
        }

        private static TenantSettingsDto? DeserializeSettings(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            try
            {
                return JsonSerializer.Deserialize<TenantSettingsDto>(json, JsonOptions);
            }
            catch
            {
                return null;
            }
        }

        private static IReadOnlyList<string> DeserializeOrigins(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return Array.Empty<string>();
            }

            try
            {
                return JsonSerializer.Deserialize<List<string>>(json, JsonOptions) ?? new List<string>();
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        private static List<string> NormalizeOrigins(IEnumerable<string> origins)
        {
            return origins
                .Where(origin => !string.IsNullOrWhiteSpace(origin))
                .Select(origin => origin.Trim().TrimEnd('/'))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string HashSha256(string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        }
    }
}
