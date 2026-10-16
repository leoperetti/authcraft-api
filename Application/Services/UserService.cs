using Application.Audit;
using Application.Interfaces;
using Domain.Audit;
using Domain.Entities;
using Infrastructure.Repositories;
using System.ComponentModel.DataAnnotations;

namespace Application.Services
{
    public class UserService : IUserService
    {
        private readonly IIdentityRepository _identities;
        private readonly IRoleRepository _roles;
        private readonly IAuditService _audit;
        private readonly IUnitOfWork _unitOfWork;

        public UserService(IIdentityRepository identities, IRoleRepository roles, IAuditService audit, IUnitOfWork unitOfWork)
        {
            _identities = identities;
            _roles = roles;
            _audit = audit;
            _unitOfWork = unitOfWork;
        }

        public async Task AssignRolesAsync(long identityId, IEnumerable<string> roles, CancellationToken ct = default)
        {
            var identity = await _identities.GetByIdWithRolesAsync(identityId)
                   ?? throw new NotFoundException($"Identity {identityId} no encontrada.");

            var requested = roles
                .Where(role => !string.IsNullOrWhiteSpace(role))
                .Select(role => role.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var foundRoles = await _roles.GetRolesAsync();
            var missingRoles = requested.Except(foundRoles.Select(role => role.Name), StringComparer.OrdinalIgnoreCase).ToList();
            if (missingRoles.Count > 0)
                throw new ValidationException($"Roles inexistentes: {string.Join(", ", missingRoles)}");

            var currentRoles = identity.Memberships
                .SelectMany(membership => membership.MembershipRoles)
                .Select(membershipRole => membershipRole.Role.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var rolesToAdd = requested.Except(currentRoles, StringComparer.OrdinalIgnoreCase).ToList();
            var rolesToRemove = currentRoles.Except(requested, StringComparer.OrdinalIgnoreCase).ToList();

            if (rolesToAdd.Count == 0 && rolesToRemove.Count == 0)
                return;

            var now = DateTimeOffset.UtcNow;
            var rolesToAddByName = foundRoles
                .Where(foundRole => rolesToAdd.Contains(foundRole.Name, StringComparer.OrdinalIgnoreCase))
                .ToList();

            foreach (var role in rolesToAddByName)
            {
                var membership = identity.Memberships.FirstOrDefault(currentMembership => currentMembership.TenantId == role.TenantId);
                if (membership == null)
                {
                    membership = new Membership
                    {
                        IdentityId = identity.Id,
                        Identity = identity,
                        TenantId = role.TenantId,
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
                    continue;
                }

                membership.MembershipRoles.Add(new MembershipRole
                {
                    MembershipId = membership.IdentityId,
                    RoleId = role.Id,
                    TenantId = role.TenantId,
                    Role = role,
                    Membership = membership
                });
            }

            foreach (var membership in identity.Memberships.ToList())
            {
                var membershipRolesToRemove = membership.MembershipRoles
                    .Where(membershipRole => rolesToRemove.Contains(membershipRole.Role.Name, StringComparer.OrdinalIgnoreCase))
                    .ToList();

                foreach (var membershipRole in membershipRolesToRemove)
                {
                    membership.MembershipRoles.Remove(membershipRole);
                }

                if (membershipRolesToRemove.Count > 0)
                {
                    membership.UpdatedAt = now;
                }

                if (membership.MembershipRoles.Count == 0)
                {
                    identity.Memberships.Remove(membership);
                }
            }

            await _identities.UpdateAsync(identity);

            await _audit.LogAsync(new AuditEntry
            {
                Action = AuditActions.AssignRoles,
                EntityType = "Identity",
                EntityId = identity.Id.ToString(),
                OldValues = new { Roles = currentRoles.OrderBy(role => role).ToArray() },
                NewValues = new { Roles = requested.OrderBy(role => role).ToArray() },
                Success = true
            }, ct);

            await _unitOfWork.SaveChangesAsync(ct);
        }

        public async Task<IEnumerable<Identity>> GetAllWithRolesAsync(string clientId)
        {
            return await _identities.GetAllWithRolesAsync(clientId);
        }

        public async Task<Identity> GetByIdWithRolesAsync(long id)
        {
            return await _identities.GetByIdWithRolesAsync(id);
        }
    }
}
