using Domain.Entities;

namespace Infrastructure.Repositories
{
    public interface ITenantRepository
    {
        Task<IReadOnlyList<Tenant>> ListAsync(CancellationToken cancellationToken = default);
        Task<Tenant?> GetByIdAsync(long id, CancellationToken cancellationToken = default);
        Task AddAsync(Tenant tenant, CancellationToken cancellationToken = default);
        Task UpdateAsync(Tenant tenant, CancellationToken cancellationToken = default);
        Task<bool> DeleteAsync(long id, CancellationToken cancellationToken = default);
    }
}
