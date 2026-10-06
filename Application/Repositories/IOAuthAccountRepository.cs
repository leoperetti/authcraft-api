using System.Threading.Tasks;
using Domain.Entities;

namespace Application.Repositories
{
    public interface IOAuthAccountRepository
    {
        Task<OAuthAccount?> GetByProviderAndProviderUserIdAsync(int providerId, string providerUserId);
        Task AddAsync(OAuthAccount oauth);
        Task UpdateAsync(OAuthAccount oauth);
    }
}
