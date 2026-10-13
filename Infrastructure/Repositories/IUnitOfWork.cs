using System.Threading;
using System.Threading.Tasks;

namespace Infrastructure.Repositories
{
    public interface IUnitOfWork
    {
        Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    }
}
