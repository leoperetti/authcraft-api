using Application.Interfaces;
using Domain.Entities;
using Infrastructure.Repositories;
using System.ComponentModel.DataAnnotations;

namespace Application.Services
{
    public class UserService : IUserService
    {
        private readonly IUserRepository _users;
        private readonly IRoleRepository _roles;
        public UserService(IUserRepository users, IRoleRepository roles)
        {
            _users = users;
            _roles = roles;
        }
        public async Task AssignRolesAsync(long userId, IEnumerable<string> roles, CancellationToken ct = default)
        {
            var user = await _users.GetByIdWithRolesAsync(userId)
                   ?? throw new NotFoundException($"Usuario {userId} no encontrado.");

            // Normalizar: sin vacíos, sin duplicados, sin distinguir mayúsculas
            var requested = roles
                .Where(r => !string.IsNullOrWhiteSpace(r))
                .Select(r => r.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            // Validar que todos los roles pedidos existan
            var found = await _roles.GetRolesAsync();
            var missing = requested.Except(found.Select(r => r.Name), StringComparer.OrdinalIgnoreCase).ToList();
            if (missing.Count > 0)
                throw new ValidationException($"Roles inexistentes: {string.Join(", ", missing)}");

            var current = user.UserRoles
                .Select(ur => ur.Role.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var toAdd = requested.Except(current, StringComparer.OrdinalIgnoreCase).ToList();
            var toRemove = current.Except(requested, StringComparer.OrdinalIgnoreCase).ToList();

            if (toAdd.Count == 0 && toRemove.Count == 0)
                return; // nada que cambiar

            foreach (var role in found.Where(r => toAdd.Contains(r.Name, StringComparer.OrdinalIgnoreCase)))
                user.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id });

            var rolesToRemove = new List<UserRole>();
            if (toRemove.Count > 0)
            {
                rolesToRemove = user.UserRoles
                .Where(ur => toRemove.Contains(ur.Role.Name, StringComparer.OrdinalIgnoreCase))
                .ToList();
            }

            foreach (var ur in rolesToRemove)
                user.UserRoles.Remove(ur);

            await _users.UpdateAsync(user);
        }
    }
}
