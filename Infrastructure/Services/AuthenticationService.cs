using System;
using System.Threading.Tasks;
using Application.Repositories;
using Application.Services;
using Domain.Entities;

namespace Infrastructure.Services
{
    public class AuthenticationService : IAuthenticationService
    {
        private readonly IProviderRepository _providers;
        private readonly IUserRepository _users;
        private readonly IOAuthAccountRepository _oauths;

        public AuthenticationService(IProviderRepository providers, IUserRepository users, IOAuthAccountRepository oauths)
        {
            _providers = providers;
            _users = users;
            _oauths = oauths;
        }

        public async Task<(User user, OAuthAccount oauth)> UpsertFromGoogleAsync(GoogleAuthInfo info)
        {
            var providerName = "google";
            var provider = await _providers.GetByNameAsync(providerName);
            if (provider == null)
            {
                provider = new Provider { Name = providerName };
                await _providers.AddAsync(provider);
            }

            var providerUserId = info.Subject ?? string.Empty;

            var oauth = await _oauths.GetByProviderAndProviderUserIdAsync(provider.Id, providerUserId);

            if (oauth != null)
            {
                oauth.Email = info.Email ?? oauth.Email;
                oauth.EmailVerified = info.EmailVerified;
                oauth.Hd = info.HostedDomain;
                oauth.LastLoginAt = DateTimeOffset.UtcNow;
                oauth.UpdatedAt = DateTimeOffset.UtcNow;

                await _oauths.UpdateAsync(oauth);
                return (oauth.User ?? throw new InvalidOperationException("OAuthAccount has no related User"), oauth);
            }

            var user = await _users.GetByEmailAsync(info.Email);
            if (user == null)
            {
                user = new User
                {
                    Email = info.Email ?? string.Empty,
                    Name = info.Name,
                    Picture = info.Picture,
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow
                };
                await _users.AddAsync(user);
            }

            var newOauth = new OAuthAccount
            {
                UserId = user.Id,
                ProviderId = provider.Id,
                ProviderUserId = providerUserId,
                Email = info.Email ?? string.Empty,
                EmailVerified = info.EmailVerified,
                Hd = info.HostedDomain,
                LastLoginAt = DateTimeOffset.UtcNow,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            await _oauths.AddAsync(newOauth);

            return (user, newOauth);
        }
    }
}
