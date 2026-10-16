using Application.Interfaces;
using Infrastructure.Repositories;
using Domain.Dto;
using Domain.Entities;
using Domain.Request;
using Domain.Response;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace Application.Services
{
    public class AuthenticationService : IAuthenticationService
    {
        private readonly IProviderRepository _providers;
        private readonly IIdentityRepository _identities;
        private readonly IOAuthAccountRepository _oauths;
        private readonly ILocalAccountRepository _localAccounts;
        private readonly ILocalAuthService _localAuthService;
        private readonly IRoleService _roleService;
        private readonly IUnitOfWork _unitOfWork;

        public AuthenticationService(IProviderRepository providers, IIdentityRepository identities, IOAuthAccountRepository oauths, ILocalAccountRepository localAccounts, ILocalAuthService localAuthService, IRoleService roleService, IUnitOfWork unitOfWork)
        {
            _providers = providers;
            _identities = identities;
            _oauths = oauths;
            _localAccounts = localAccounts;
            _localAuthService = localAuthService;
            _roleService = roleService;
            _unitOfWork = unitOfWork;
        }

        public async Task<(Identity identity, OAuthAccount oauth)> UpsertFromGoogleAsync(GoogleAuthInfo info, string clientId)
        {
            if (string.IsNullOrWhiteSpace(clientId))
                throw new InvalidOperationException("Se requiere clientId para determinar el tenant");

            var normalizedClientId = clientId.Trim();

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
                await _unitOfWork.SaveChangesAsync();
                return (oauth.Identity ?? throw new InvalidOperationException("OAuthAccount has no related Identity"), oauth);
            }

            var identity = await _identities.GetByEmailAsync(info.Email);
            bool isNewIdentity = identity == null;
            if (isNewIdentity)
            {
                identity = new Identity
                {
                    Email = info.Email ?? string.Empty,
                    Name = info.Name,
                    Picture = info.Picture,
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow
                };
                await _identities.AddAsync(identity);
            }

            var newOauth = new OAuthAccount
            {
                Identity = identity,
                Provider = provider,
                ProviderUserId = providerUserId,
                Email = info.Email ?? string.Empty,
                EmailVerified = info.EmailVerified,
                Hd = info.HostedDomain,
                LastLoginAt = DateTimeOffset.UtcNow,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            await _oauths.AddAsync(newOauth);

            if (isNewIdentity)
            {
                await _roleService.AssignRoleAsync(identity, normalizedClientId, "user");
            }

            await _unitOfWork.SaveChangesAsync();

            return (identity, newOauth);
        }

        public async Task<AuthResponse> LoginWithPasswordAsync(LoginRequest request, string jwtKey, string? jwtIssuer, string? jwtAudience)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (string.IsNullOrWhiteSpace(request.ClientId)) throw new InvalidOperationException("Se requiere clientId");

            var normalizedClientId = request.ClientId.Trim();
            var identity = await _identities.GetByEmailWithMembershipsAsync(request.Identifier);
            if (identity == null) throw new InvalidOperationException("Credenciales inválidas");

            var localAccount = await _localAccounts.GetByIdentityIdAsync(identity.Id);
            if (localAccount == null) throw new InvalidOperationException("Ya existe una cuenta con ese email, inicia sesión con Google");

            var valid = await _localAuthService.VerifyPasswordAsync(request.Password, localAccount);
            if (!valid) throw new InvalidOperationException("Credenciales inválidas");

            var membership = identity.Memberships
                .FirstOrDefault(currentMembership =>
                    currentMembership.Tenant != null
                    && string.Equals(currentMembership.Tenant.ClientId, normalizedClientId, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(currentMembership.Status, "active", StringComparison.OrdinalIgnoreCase));

            if (membership == null)
                throw new InvalidOperationException("Para poder acceder debe crear una nueva cuenta");

            var tokenHandler = new JwtSecurityTokenHandler();
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
            var signingCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var tokenClaims = new List<Claim>
            {
                new Claim(JwtRegisteredClaimNames.Sub, identity.Id.ToString()),
                new Claim(ClaimTypes.Email, identity.Email ?? string.Empty),
                new Claim(ClaimTypes.Name, identity.Name ?? string.Empty),
                new Claim("tenant", normalizedClientId)
            };

            var roleNames = membership.MembershipRoles
                .Select(membershipRole => membershipRole.Role.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var roleName in roleNames)
            {
                tokenClaims.Add(new Claim(ClaimTypes.Role, roleName));
            }

            var expires = DateTime.UtcNow.AddHours(24);
            var token = new JwtSecurityToken(
                issuer: string.IsNullOrEmpty(jwtIssuer) ? null : jwtIssuer,
                audience: string.IsNullOrEmpty(jwtAudience) ? null : jwtAudience,
                claims: tokenClaims,
                expires: expires,
                signingCredentials: signingCredentials
            );

            var tokenString = tokenHandler.WriteToken(token);

            var names = (identity.Name ?? string.Empty).Split(' ', 2, StringSplitOptions.TrimEntries);
            var firstName = names.Length > 0 ? names[0] : string.Empty;
            var lastName = names.Length > 1 ? names[1] : string.Empty;

            var userDto = new AuthUserDto
            {
                Id = identity.Id.ToString(),
                Email = identity.Email ?? string.Empty,
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
