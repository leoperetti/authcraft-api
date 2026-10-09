using Domain.Entities;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories
{
    public class OAuthAccountRepository : IOAuthAccountRepository
    {
        private readonly AppDbContext _db;
        public OAuthAccountRepository(AppDbContext db) => _db = db;

        public async Task<OAuthAccount?> GetByProviderAndProviderUserIdAsync(int providerId, string providerUserId)
        {
            return await _db.OAuthAccounts.Include(o => o.User).
                SingleOrDefaultAsync(o => o.ProviderId == providerId && o.ProviderUserId == providerUserId);
        }

        public async Task AddAsync(OAuthAccount oauth)
        {
            _db.OAuthAccounts.Add(oauth);
            await _db.SaveChangesAsync();
        }

        public async Task UpdateAsync(OAuthAccount oauth)
        {
            _db.OAuthAccounts.Update(oauth);
            await _db.SaveChangesAsync();
        }
    }
}
