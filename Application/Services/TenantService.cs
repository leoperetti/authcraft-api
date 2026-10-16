using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Application.Interfaces;
using Domain.Dto;
using Domain.Entities;
using Domain.Request;
using Domain.Response;
using Infrastructure.Repositories;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace Application.Services
{
    public class TenantService : ITenantService
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

        private readonly IEmailService _emailService;
        private readonly IInvitationRepository _invitationRepository;
        private readonly ITenantRepository _tenantRepository;
        private readonly IConfiguration _configuration;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public TenantService(IEmailService emailService, IInvitationRepository invitationRepository, ITenantRepository tenantRepository, IConfiguration configuration, IHttpContextAccessor httpContextAccessor)
        {
            _emailService = emailService;
            _invitationRepository = invitationRepository;
            _tenantRepository = tenantRepository;
            _configuration = configuration;
            _httpContextAccessor = httpContextAccessor;
        }

        public async Task<IReadOnlyList<TenantDto>> ListAsync(CancellationToken cancellationToken = default)
        {
            var tenants = await _tenantRepository.ListAsync(cancellationToken);
            return tenants.Select(Map).ToList();
        }

        public async Task<TenantDto?> GetAsync(long id, CancellationToken cancellationToken = default)
        {
            var tenant = await _tenantRepository.GetByIdAsync(id, cancellationToken);
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
                PrimaryDomain = string.IsNullOrWhiteSpace(request.PrimaryDomain) ? null : request.PrimaryDomain.Trim().ToLowerInvariant(),
                Status = request.Status,
                Settings = request.Settings is null ? null : JsonSerializer.Serialize(request.Settings, JsonOptions),
                AllowedOrigins = JsonSerializer.Serialize(allowedOrigins, JsonOptions)
            };

            tenant.Roles.Add(new Role { Name = "owner", IsSystem = true });
            tenant.Roles.Add(new Role { Name = "admin", IsSystem = true });
            tenant.Roles.Add(new Role { Name = "user", IsSystem = true });

            tenant.TenantApiKeys.Add(new TenantApiKey
            {
                KeyHash = apiKeyHash,
                Scopes = JsonSerializer.Serialize(new[] { "tenants:read", "tenants:write" }, JsonOptions)
            });

            await _tenantRepository.AddAsync(tenant, cancellationToken);

            // Crear invitación para el owner
            var invitationCode = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
            var now = DateTimeOffset.UtcNow;
            var invitation = new Invitation
            {
                TenantId = tenant.Id,
                Email = request.OwnerEmail.Trim(),
                Code = invitationCode,
                CreatedAt = now,
                ExpiresAt = now.AddDays(7),
                Used = false
            };

            if (await _invitationRepository.HasPendingAsync(tenant.Id, request.OwnerEmail.Trim(), cancellationToken))
            {
                throw new PendingInvitationException();
            }

            await _invitationRepository.AddAsync(invitation, cancellationToken);

            // Construir URL de invitación
            var invitationBaseUrl = tenant.PrimaryDomain;
            string invitationUrl;
            if (!string.IsNullOrWhiteSpace(invitationBaseUrl))
            {
                invitationUrl = invitationBaseUrl.TrimEnd('/') + $"/signup?invitationCode={invitationCode}";
            }
            else
            {
                invitationUrl = $"/signup?invitationCode={invitationCode}";
            }

            // Enviar email informando de la apertura de cuenta (si hay configuración SMTP, el servicio lo enviará)
            var subject = "Invitación para completar la apertura de cuenta";
            var body = $"Se ha creado una cuenta para su organización. Use el siguiente enlace para completar el registro: <a href=\"{invitationUrl}\">{invitationUrl}</a>";
            _emailService.SendInvitation(request.OwnerEmail.Trim(), subject, body);

            return new CreateTenantResponse(Map(tenant), apiKeyPlain, invitationUrl);
        }

        public async Task<TenantDto?> UpdateAsync(long id, UpdateTenantRequest request, CancellationToken cancellationToken = default)
        {
            var tenant = await _tenantRepository.GetByIdAsync(id, cancellationToken);
            if (tenant is null)
            {
                return null;
            }

            if (request.Name is not null)
            {
                tenant.Name = request.Name.Trim();
            }

            if(request.PrimaryDomain is not null)
            {
                tenant.PrimaryDomain = string.IsNullOrWhiteSpace(request.PrimaryDomain) ? null : request.PrimaryDomain.Trim().ToLowerInvariant();
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

            await _tenantRepository.UpdateAsync(tenant, cancellationToken);
            return Map(tenant);
        }

        public async Task<bool> UpdateStatusAsync(long id, string status, CancellationToken cancellationToken = default)
        {
            var tenant = await _tenantRepository.GetByIdAsync(id, cancellationToken);
            if (tenant is null)
            {
                return false;
            }

            tenant.Status = status;
            await _tenantRepository.UpdateAsync(tenant, cancellationToken);
            return true;
        }

        public Task<bool> DeleteAsync(long id, CancellationToken cancellationToken = default)
            => _tenantRepository.DeleteAsync(id, cancellationToken);

        private static TenantDto Map(Tenant tenant)
        {
            return new TenantDto
            {
                Id = tenant.Id,
                ClientId = tenant.ClientId,
                Slug = tenant.Slug,
                Name = tenant.Name,
                PrimaryDomain = tenant.PrimaryDomain,
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
