using System.Threading.Tasks;
using Domain.Entities;

namespace Infrastructure.Repositories
{
    public interface IIdentityRepository
    {
        Task<Identity?> GetByEmailAsync(string? email);
        Task<Identity?> GetByEmailWithMembershipsAsync(string? email);
        Task AddAsync(Identity identity);
        Task UpdateAsync(Identity identity);
        Task<Identity?> GetByIdAsync(long identityId);
        Task<IList<Identity>> GetAllAsync();
        Task<IList<Identity>> GetAllWithRolesAsync(string clientId);
        Task<Identity?> GetByIdWithRolesAsync(long id);
    }
}
