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
        Task AddMembershipRoleAsync(MembershipRole membershipRole);
        Task RemoveMembershipRoleAsync(MembershipRole membershipRole);
        Task<bool> IdentityHasRoleAsync(long identityId, int roleId);
        Task<IList<string>> GetRolesByIdentityIdAsync(long identityId);
        Task<IList<string>> GetAllRolesAsync();
        Task<List<Role>> GetRolesAsync();
    }
}
