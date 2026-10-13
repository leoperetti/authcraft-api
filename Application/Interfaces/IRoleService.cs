using Domain.Entities;

namespace Application.Interfaces
{
    public interface IRoleService
    {
        Task<IList<string>> GetRolesAsync(long identityId);
        Task<IList<string>> GetAllRolesAsync();
        Task AssignRoleAsync(long identityId, string roleName);
        Task AssignRoleAsync(Identity identity, string roleName);
    }
}

