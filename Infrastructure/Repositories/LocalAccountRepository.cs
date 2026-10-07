using Application.Repositories;
using Domain.Entities;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories
{
    public class LocalAccountRepository : ILocalAccountRepository
    {
        private readonly AppDbContext _db;
        public LocalAccountRepository(AppDbContext db) => _db = db;

        public async Task<LocalAccount?> GetByUserIdAsync(long userId)
        {
            return await _db.Set<LocalAccount>().Include(l => l.User).SingleOrDefaultAsync(l => l.UserId == userId);
        }

        public async Task AddAsync(LocalAccount localAccount)
        {
            _db.Set<LocalAccount>().Add(localAccount);
            await _db.SaveChangesAsync();
        }

        public async Task UpdateAsync(LocalAccount localAccount)
        {
            _db.Set<LocalAccount>().Update(localAccount);
            await _db.SaveChangesAsync();
        }
    }
}
