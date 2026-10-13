using Domain.Entities;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories
{
    public class ProviderRepository : IProviderRepository
    {
        private readonly AppDbContext _db;
        public ProviderRepository(AppDbContext db) => _db = db;

        public async Task<Provider?> GetByNameAsync(string name)
        {
            return await _db.Providers.SingleOrDefaultAsync(p => p.Name == name);
        }

        public Task AddAsync(Provider provider)
        {
            _db.Providers.Add(provider);
            return Task.CompletedTask;
        }
    }
}
