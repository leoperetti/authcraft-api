using Domain.Entities;

namespace Application.Interfaces
{
    public interface IRoleService
    {
        Task<IList<string>> GetRolesAsync(long identityId);
        Task<IList<string>> GetAllRolesAsync(string clientId);
        Task AssignRoleAsync(long identityId, string clientId, string roleName);
        Task AssignRoleAsync(Identity identity, string clientId, string roleName);
    }
}

