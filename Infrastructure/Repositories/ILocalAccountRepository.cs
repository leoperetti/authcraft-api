using Domain.Entities;

namespace Infrastructure.Repositories
{
    public interface ILocalAccountRepository
    {
        Task<LocalAccount?> GetByIdentityIdAsync(long identityId);
        Task AddAsync(LocalAccount localAccount);
        Task UpdateAsync(LocalAccount localAccount);
    }
}
