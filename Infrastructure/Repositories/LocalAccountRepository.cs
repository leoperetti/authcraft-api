using Domain.Entities;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories
{
    public class LocalAccountRepository : ILocalAccountRepository
    {
        private readonly AppDbContext _db;
        public LocalAccountRepository(AppDbContext db) => _db = db;

        public async Task<LocalAccount?> GetByIdentityIdAsync(long identityId)
        {
            return await _db.Set<LocalAccount>().Include(localAccount => localAccount.Identity).SingleOrDefaultAsync(localAccount => localAccount.IdentityId == identityId);
        }

        public Task AddAsync(LocalAccount localAccount)
        {
            _db.Set<LocalAccount>().Add(localAccount);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(LocalAccount localAccount)
        {
            _db.Set<LocalAccount>().Update(localAccount);
            return Task.CompletedTask;
        }
    }
}
