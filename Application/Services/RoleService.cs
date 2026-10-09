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

        public async Task<IList<string>> GetRolesAsync(long userId)
        {
            return await _roles.GetRolesByUserIdAsync(userId);
        }

        public async Task<IList<string>> GetAllRolesAsync()
        {
            return await _roles.GetAllRolesAsync();
        }

        public async Task AssignRoleAsync(long userId, string roleName)
        {
            if (string.IsNullOrWhiteSpace(roleName))
                throw new ArgumentException("roleName is required", nameof(roleName));

            var role = await _roles.GetByNameAsync(roleName);

            var has = await _roles.UserHasRoleAsync(userId, role.Id);
            if (!has)
            {
                var userRole = new UserRole { UserId = userId, RoleId = role.Id };
                await _roles.AddUserRoleAsync(userRole);
            }
        }
    }
}
