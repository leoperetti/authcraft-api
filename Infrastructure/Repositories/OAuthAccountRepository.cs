using Domain.Entities;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Repositories
{
    public interface IOAuthAccountRepository
    {
        Task<OAuthAccount?> GetByProviderAndProviderUserIdAsync(int providerId, string providerUserId);
        Task AddAsync(OAuthAccount oauth);
        Task UpdateAsync(OAuthAccount oauth);
    }

    public class OAuthAccountRepository : IOAuthAccountRepository
    {
        private readonly AppDbContext _db;
        public OAuthAccountRepository(AppDbContext db) => _db = db;

        public async Task<OAuthAccount?> GetByProviderAndProviderUserIdAsync(int providerId, string providerUserId)
        {
            return await _db.OAuthAccounts.Include(oauthAccount => oauthAccount.Identity)
                .SingleOrDefaultAsync(oauthAccount => oauthAccount.ProviderId == providerId && oauthAccount.ProviderUserId == providerUserId);
        }

        public Task AddAsync(OAuthAccount oauth)
        {
            _db.OAuthAccounts.Add(oauth);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(OAuthAccount oauth)
        {
            _db.OAuthAccounts.Update(oauth);
            return Task.CompletedTask;
        }
    }
}
