using Domain.Entities;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories
{
    public interface ITenantRepository
    {
        Task<IReadOnlyList<Tenant>> ListAsync(CancellationToken cancellationToken = default);
        Task<Tenant?> GetDomainById(long id, CancellationToken cancellationToken = default);
        Task AddAsync(Tenant tenant, CancellationToken cancellationToken = default);
        Task UpdateAsync(Tenant tenant, CancellationToken cancellationToken = default);
        Task<bool> DeleteAsync(long id, CancellationToken cancellationToken = default);
        Task<string> GetDomainByClient(string clientId, CancellationToken cancellationToken = default);
        Task<Tenant?> GetByClientIdAsync(string clientId, CancellationToken cancellationToken = default);
    }

    public class TenantRepository : ITenantRepository
    {
        private readonly AppDbContext _dbContext;

        public TenantRepository(AppDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task<IReadOnlyList<Tenant>> ListAsync(CancellationToken cancellationToken = default)
        {
            return await _dbContext.Tenants
                .AsNoTracking()
                .OrderBy(tenant => tenant.Id)
                .ToListAsync(cancellationToken);
        }

        public async Task<Tenant?> GetDomainById(long id, CancellationToken cancellationToken = default)
        {
            return await _dbContext.Tenants
                .AsNoTracking()
                .FirstOrDefaultAsync(tenant => tenant.Id == id, cancellationToken);
        }

        public async Task AddAsync(Tenant tenant, CancellationToken cancellationToken = default)
        {
            await _dbContext.Tenants.AddAsync(tenant, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        public async Task UpdateAsync(Tenant tenant, CancellationToken cancellationToken = default)
        {
            _dbContext.Tenants.Update(tenant);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        public async Task<bool> DeleteAsync(long id, CancellationToken cancellationToken = default)
        {
            var tenant = await _dbContext.Tenants.FirstOrDefaultAsync(currentTenant => currentTenant.Id == id, cancellationToken);
            if (tenant is null)
            {
                return false;
            }

            _dbContext.Tenants.Remove(tenant);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }

        public async Task<Tenant?> GetByClientIdAsync(string clientId, CancellationToken cancellationToken = default)
        {
            return await _dbContext.Tenants
                .AsNoTracking()
                .FirstOrDefaultAsync(tenant => tenant.ClientId == clientId, cancellationToken);
        }

        public async Task<string> GetDomainByClient(string clientId, CancellationToken cancellationToken = default) => await _dbContext.Tenants
                .Where(tenant => tenant.ClientId == clientId)
                .Select(tenant => tenant.PrimaryDomain)
                .AsNoTracking()
                .FirstOrDefaultAsync(cancellationToken);
    }
}
