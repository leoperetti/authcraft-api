using Domain.Entities;

namespace Infrastructure.Repositories
{
    public interface ILocalAccountRepository
    {
        Task<LocalAccount?> GetByUserIdAsync(long userId);
        Task AddAsync(LocalAccount localAccount);
        Task UpdateAsync(LocalAccount localAccount);
    }
}
