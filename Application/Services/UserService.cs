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
                .Select(membership => membership.Role.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var rolesToAdd = requested.Except(currentRoles, StringComparer.OrdinalIgnoreCase).ToList();
            var rolesToRemove = currentRoles.Except(requested, StringComparer.OrdinalIgnoreCase).ToList();

            if (rolesToAdd.Count == 0 && rolesToRemove.Count == 0)
                return;

            foreach (var role in foundRoles.Where(foundRole => rolesToAdd.Contains(foundRole.Name, StringComparer.OrdinalIgnoreCase)))
            {
                var existingMembership = identity.Memberships.FirstOrDefault(membership => membership.TenantId == role.TenantId);
                if (existingMembership == null)
                {
                    identity.Memberships.Add(new Membership
                    {
                        IdentityId = identity.Id,
                        TenantId = role.TenantId,
                        Status = "active",
                        RoleId = role.Id,
                        CreatedAt = DateTimeOffset.UtcNow,
                        UpdatedAt = DateTimeOffset.UtcNow
                    });
                    continue;
                }

                existingMembership.RoleId = role.Id;
                existingMembership.UpdatedAt = DateTimeOffset.UtcNow;
            }

            var membershipsToRemove = identity.Memberships
                .Where(membership => rolesToRemove.Contains(membership.Role.Name, StringComparer.OrdinalIgnoreCase))
                .ToList();

            foreach (var membership in membershipsToRemove)
                identity.Memberships.Remove(membership);

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

        public async Task<IEnumerable<Identity>> GetAllWithRolesAsync()
        {
            return await _identities.GetAllWithRolesAsync();
        }

        public async Task<Identity> GetByIdWithRolesAsync(long id)
        {
            return await _identities.GetByIdWithRolesAsync(id);
        }
    }
}
