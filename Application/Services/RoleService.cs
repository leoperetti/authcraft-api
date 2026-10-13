using Application.Interfaces;
using Domain.Entities;
using Infrastructure.Repositories;

namespace Application.Services
{
    public class RoleService : IRoleService
    {
        private readonly IRoleRepository _roles;

        public RoleService(IRoleRepository roles)
        {
            _roles = roles;
        }

        public async Task<IList<string>> GetRolesAsync(long identityId)
        {
            return await _roles.GetRolesByIdentityIdAsync(identityId);
        }

        public async Task<IList<string>> GetAllRolesAsync()
        {
            return await _roles.GetAllRolesAsync();
        }

        public async Task AssignRoleAsync(long identityId, string roleName)
        {
            if (string.IsNullOrWhiteSpace(roleName))
                throw new ArgumentException("roleName is required", nameof(roleName));

            var role = await _roles.GetByNameAsync(roleName)
                ?? throw new InvalidOperationException($"El rol '{roleName}' no existe");

            var hasRole = await _roles.IdentityHasRoleAsync(identityId, role.Id);
            if (!hasRole)
            {
                var membershipRole = new MembershipRole
                {
                    MembershipId = identityId,
                    RoleId = role.Id,
                    TenantId = role.TenantId
                };
                await _roles.AddMembershipRoleAsync(membershipRole);
            }
        }

        public async Task AssignRoleAsync(Identity identity, string roleName)
        {
            if (identity == null)
                throw new ArgumentNullException(nameof(identity));

            if (string.IsNullOrWhiteSpace(roleName))
                throw new ArgumentException("roleName is required", nameof(roleName));

            var role = await _roles.GetByNameAsync(roleName)
                ?? throw new InvalidOperationException($"El rol '{roleName}' no existe");

            var membership = identity.Memberships.FirstOrDefault(currentMembership => currentMembership.TenantId == role.TenantId);
            if (membership == null)
            {
                membership = new Membership
                {
                    IdentityId = identity.Id,
                    Identity = identity,
                    TenantId = role.TenantId,
                    Status = "active",
                    RoleId = role.Id,
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow
                };
                identity.Memberships.Add(membership);
                return;
            }

            membership.RoleId = role.Id;
            membership.UpdatedAt = DateTimeOffset.UtcNow;
        }
    }
}
