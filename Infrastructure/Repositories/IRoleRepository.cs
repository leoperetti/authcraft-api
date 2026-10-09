using System.Collections.Generic;
using System.Threading.Tasks;
using Domain.Entities;

namespace Infrastructure.Repositories
{
    public interface IRoleRepository
    {
        Task<Role?> GetByNameAsync(string name);
        Task<Role?> GetByIdAsync(int id);
        Task AddAsync(Role role);
        Task AddUserRoleAsync(UserRole userRole);
        Task RemoveUserRoleAsync(UserRole userRole);
        Task<bool> UserHasRoleAsync(long userId, int roleId);
        Task<IList<string>> GetRolesByUserIdAsync(long userId);
        Task<IList<string>> GetAllRolesAsync();
        Task<List<Role>> GetRolesAsync();
    }
}
