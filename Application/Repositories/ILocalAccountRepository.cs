using System.Threading.Tasks;
using Domain.Entities;

namespace Application.Repositories
{
    public interface ILocalAccountRepository
    {
        Task<LocalAccount?> GetByUserIdAsync(long userId);
        Task AddAsync(LocalAccount localAccount);
        Task UpdateAsync(LocalAccount localAccount);
    }
}
