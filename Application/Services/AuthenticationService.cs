using Application.Repositories;
using Domain.Entities;
using Domain.Request;
using Domain.Response;
using Domain.Dto;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using System.Security.Claims;

namespace Application.Services
{
    public class AuthenticationService : IAuthenticationService
    {
        private readonly IProviderRepository _providers;
        private readonly IUserRepository _users;
        private readonly IOAuthAccountRepository _oauths;
        private readonly ILocalAccountRepository _localAccounts;
        private readonly ILocalAuthService _localAuthService;

        public AuthenticationService(IProviderRepository providers, IUserRepository users, IOAuthAccountRepository oauths, ILocalAccountRepository localAccounts, ILocalAuthService localAuthService)
        {
            _providers = providers;
            _users = users;
            _oauths = oauths;
            _localAccounts = localAccounts;
            _localAuthService = localAuthService;
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

        public async Task<AuthResponse> LoginWithPasswordAsync(LoginRequest request, string jwtKey, string? jwtIssuer, string? jwtAudience)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            var user = await _users.GetByEmailAsync(request.Identifier);
            if (user == null) throw new InvalidOperationException("Credenciales inválidas");

            var local = await _localAccounts.GetByUserIdAsync(user.Id);
            if(user != null && local == null) throw new InvalidOperationException("Ya existe una cuenta con ese email, inicia sesión con Google");

            var valid = await _localAuthService.VerifyPasswordAsync(request.Password, local);
            if (!valid) throw new InvalidOperationException("Credenciales inválidas");

            var tokenHandler = new JwtSecurityTokenHandler();
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
            var signingCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var tokenClaims = new List<Claim>
            {
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim(ClaimTypes.Email, user.Email ?? string.Empty),
                new Claim(ClaimTypes.Name, user.Name ?? string.Empty),
            };

            var expires = DateTime.UtcNow.AddHours(10);
            var token = new JwtSecurityToken(
                issuer: string.IsNullOrEmpty(jwtIssuer) ? null : jwtIssuer,
                audience: string.IsNullOrEmpty(jwtAudience) ? null : jwtAudience,
                claims: tokenClaims,
                expires: expires,
                signingCredentials: signingCredentials
            );

            var tokenString = tokenHandler.WriteToken(token);

            var names = (user.Name ?? string.Empty).Split(' ', 2, StringSplitOptions.TrimEntries);
            var firstName = names.Length > 0 ? names[0] : string.Empty;
            var lastName = names.Length > 1 ? names[1] : string.Empty;

            var userDto = new AuthUserDto
            {
                Id = user.Id.ToString(),
                Email = user.Email ?? string.Empty,
                FirstName = firstName,
                LastName = lastName
            };

            var response = new AuthResponse
            {
                AccessToken = tokenString,
                TokenType = "Bearer",
                ExpiresIn = (int)(expires - DateTime.UtcNow).TotalSeconds,
                User = userDto
            };

            return response;
        }
    }
}
