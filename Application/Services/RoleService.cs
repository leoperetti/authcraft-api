using Application.Interfaces;
using Domain.Entities;
using Infrastructure.Data;
using Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Application.Services
{
    public class RoleService : IRoleService
    {
        private readonly AppDbContext _dbContext;
        private readonly IIdentityRepository _identities;
        private readonly IRoleRepository _roles;

        public RoleService(AppDbContext dbContext, IIdentityRepository identities, IRoleRepository roles)
        {
            _dbContext = dbContext;
            _identities = identities;
            _roles = roles;
        }

        public async Task<IList<string>> GetRolesAsync(long identityId)
        {
            return await _roles.GetRolesByIdentityIdAsync(identityId);
        }

        public async Task<IList<string>> GetAllRolesAsync(string clientId)
        {
            if (string.IsNullOrWhiteSpace(clientId))
                throw new InvalidOperationException("Se requiere clientId para listar roles del tenant");

            return await _roles.GetAllRolesAsync(clientId.Trim());
        }

        public async Task AssignRoleAsync(long identityId, string clientId, string roleName)
        {
            var identity = await _identities.GetByIdWithRolesAsync(identityId)
                ?? throw new InvalidOperationException($"La identidad '{identityId}' no existe");

            await AssignRoleAsync(identity, clientId, roleName);
        }

        public async Task AssignRoleAsync(Identity identity, string clientId, string roleName)
        {
            if (identity == null)
                throw new ArgumentNullException(nameof(identity));

            if (string.IsNullOrWhiteSpace(clientId))
                throw new ArgumentException("clientId is required", nameof(clientId));

            if (string.IsNullOrWhiteSpace(roleName))
                throw new ArgumentException("roleName is required", nameof(roleName));

            var normalizedClientId = clientId.Trim();
            var tenantId = await _dbContext.Tenants
                .Where(tenant => tenant.ClientId == normalizedClientId)
                .Select(tenant => (long?)tenant.Id)
                .SingleOrDefaultAsync()
                ?? throw new InvalidOperationException($"No existe un tenant para el clientId '{normalizedClientId}'");

            var normalizedRoleName = roleName.Trim();
            var role = await _roles.GetByNameAsync(tenantId, normalizedRoleName)
                ?? throw new InvalidOperationException($"El rol '{normalizedRoleName}' no existe para el clientId '{normalizedClientId}'");

            var now = DateTimeOffset.UtcNow;
            var membership = identity.Memberships.FirstOrDefault(currentMembership => currentMembership.TenantId == tenantId);
            if (membership == null)
            {
                membership = new Membership
                {
                    IdentityId = identity.Id,
                    Identity = identity,
                    TenantId = tenantId,
                    Status = "active",
                    CreatedAt = now,
                    UpdatedAt = now
                };
                identity.Memberships.Add(membership);
            }
            else
            {
                membership.UpdatedAt = now;
            }

            var membershipAlreadyHasRole = membership.MembershipRoles.Any(membershipRole => membershipRole.RoleId == role.Id);
            if (membershipAlreadyHasRole)
            {
                return;
            }

            membership.MembershipRoles.Add(new MembershipRole
            {
                MembershipId = membership.IdentityId,
                RoleId = role.Id,
                TenantId = tenantId,
                Membership = membership,
                Role = role
            });
        }
    }
}
