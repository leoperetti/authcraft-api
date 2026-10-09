using Domain.Entities;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories
{
    public class RoleRepository : IRoleRepository
    {
        private readonly AppDbContext _db;
        public RoleRepository(AppDbContext db) => _db = db;

        public async Task<Role?> GetByNameAsync(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            return await _db.Roles.SingleOrDefaultAsync(r => r.Name == name);
        }

        public async Task<Role?> GetByIdAsync(int id) => await _db.Roles.FindAsync(id);

        public async Task AddAsync(Role role)
        {
            _db.Roles.Add(role);
            await _db.SaveChangesAsync();
        }

        public async Task AddUserRoleAsync(UserRole userRole)
        {
            _db.UsersRoles.Add(userRole);
            await _db.SaveChangesAsync();
        }

        public async Task RemoveUserRoleAsync(UserRole userRole)
        {
            _db.UsersRoles.Remove(userRole);
            await _db.SaveChangesAsync();
        }

        public async Task<bool> UserHasRoleAsync(long userId, int roleId)
        {
            return await _db.UsersRoles.AnyAsync(ur => ur.UserId == userId && ur.RoleId == roleId);
        }

        public async Task<IList<string>> GetRolesByUserIdAsync(long userId)
        {
            return await _db.UsersRoles
                .Where(ur => ur.UserId == userId)
                .Select(ur => ur.Role.Name)
                .ToListAsync();
        }

        public async Task<IList<string>> GetAllRolesAsync()
        {
            return await _db.Roles
                .Select(r => r.Name)
                .ToListAsync();
        }

        public async Task<List<Role>> GetRolesAsync()
        {
            return await _db.Roles.ToListAsync();
        }
    }
}
