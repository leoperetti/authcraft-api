using Domain.Entities;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories
{
    public interface IRoleRepository
    {
        Task<Role?> GetByNameAsync(long tenantId, string name);
        Task<Role?> GetByIdAsync(int id);
        Task AddAsync(Role role);
        Task AddMembershipRoleAsync(MembershipRole membershipRole);
        Task RemoveMembershipRoleAsync(MembershipRole membershipRole);
        Task<bool> IdentityHasRoleAsync(long identityId, int roleId);
        Task<IList<string>> GetRolesByIdentityIdAsync(long identityId);
        Task<IList<string>> GetAllRolesAsync(string clientId);
        Task<List<Role>> GetRolesAsync();
    }

    public class RoleRepository : IRoleRepository
    {
        private readonly AppDbContext _db;
        public RoleRepository(AppDbContext db) => _db = db;

        public async Task<Role?> GetByNameAsync(long tenantId, string name)
        {
            if (tenantId <= 0 || string.IsNullOrWhiteSpace(name)) return null;

            var normalizedRoleName = name.Trim();
            return await _db.Roles.SingleOrDefaultAsync(role => role.TenantId == tenantId && role.Name == normalizedRoleName);
        }

        public async Task<Role?> GetByIdAsync(int id) => await _db.Roles.FindAsync(id);

        public Task AddAsync(Role role)
        {
            _db.Roles.Add(role);
            return Task.CompletedTask;
        }

        public Task AddMembershipRoleAsync(MembershipRole membershipRole)
        {
            _db.MembershipRoles.Add(membershipRole);
            return Task.CompletedTask;
        }

        public Task RemoveMembershipRoleAsync(MembershipRole membershipRole)
        {
            _db.MembershipRoles.Remove(membershipRole);
            return Task.CompletedTask;
        }

        public async Task<bool> IdentityHasRoleAsync(long identityId, int roleId)
        {
            return await _db.MembershipRoles.AnyAsync(membershipRole => membershipRole.MembershipId == identityId && membershipRole.RoleId == roleId);
        }

        public async Task<IList<string>> GetRolesByIdentityIdAsync(long identityId)
        {
            return await _db.MembershipRoles
                .Where(membershipRole => membershipRole.MembershipId == identityId)
                .Select(membershipRole => membershipRole.Role.Name)
                .ToListAsync();
        }

        public async Task<IList<string>> GetAllRolesAsync(string clientId)
        {
            if (string.IsNullOrWhiteSpace(clientId))
                return new List<string>();

            var normalizedClientId = clientId.Trim();

            return await _db.Roles
                .Where(role => role.Tenant.ClientId == normalizedClientId)
                .Select(role => role.Name)
                .ToListAsync();
        }

        public async Task<List<Role>> GetRolesAsync()
        {
            return await _db.Roles.ToListAsync();
        }
    }
}
