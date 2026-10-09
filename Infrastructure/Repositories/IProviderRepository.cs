using System.Threading.Tasks;
using Domain.Entities;

namespace Infrastructure.Repositories
{
    public interface IProviderRepository
    {
        Task<Provider?> GetByNameAsync(string name);
        Task AddAsync(Provider provider);
    }
}
