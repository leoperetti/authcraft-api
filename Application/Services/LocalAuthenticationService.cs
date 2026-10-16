using System.Security.Cryptography;
using System.Text;
using Application.Interfaces;
using Domain.Entities;
using Domain.Request;
using Infrastructure.Data;
using Infrastructure.Repositories;
using Konscious.Security.Cryptography;
using Microsoft.EntityFrameworkCore;

namespace Application.Services
{
    // Implements signup using Argon2id for password hashing (modern memory-hard algorithm)
    public class LocalAuthenticationService : ILocalAuthService
    {
        private readonly IIdentityRepository _identities;
        private readonly ILocalAccountRepository _localAccounts;
        private readonly AppDbContext _dbContext;
        private readonly IRoleService _roleService;
        private readonly IUnitOfWork _unitOfWork;

        public LocalAuthenticationService(IIdentityRepository identities, ILocalAccountRepository localAccounts, AppDbContext dbContext, Application.Interfaces.IRoleService roleService, IUnitOfWork unitOfWork)
        {
            _identities = identities;
            _localAccounts = localAccounts;
            _dbContext = dbContext;
            _roleService = roleService;
            _unitOfWork = unitOfWork;
        }

        public async Task<Identity> SignupAsync(SignupRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            var existingIdentity = await _identities.GetByEmailAsync(request.Email);
            if (existingIdentity != null)
                throw new InvalidOperationException("El email con el que intenta registrarse ya existe");

            var clientId = await ResolveClientIdAsync(request);
            var now = DateTimeOffset.UtcNow;
            string? normalizedPhone = null;
            if (!string.IsNullOrWhiteSpace(request.PhoneNumber))
            {
                normalizedPhone = NormalizeE164(request.PhoneNumber);
                if (normalizedPhone == null)
                    throw new InvalidOperationException("El número de teléfono debe estar en formato E.164 (por ejemplo +5511999998888)");
            }

            var identity = new Identity
            {
                Email = request.Email ?? string.Empty,
                PhoneNumber = normalizedPhone,
                Name = $"{request.FirstName} {request.LastName}",
                CreatedAt = now,
                UpdatedAt = now
            };

            await _identities.AddAsync(identity);

            // Generate a random salt
            var salt = new byte[16];
            RandomNumberGenerator.Fill(salt);

            // Hash the password using Argon2id
            var passwordBytes = Encoding.UTF8.GetBytes(request.Password ?? string.Empty);
            var argon2 = new Argon2id(passwordBytes)
            {
                Salt = salt,
                DegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2), // parallelism
                Iterations = 4, // time cost
                MemorySize = 64 * 1024 // 64 MB
            };

            var hash = await argon2.GetBytesAsync(32); // 256-bit

            var localAccount = new LocalAccount
            {
                Identity = identity,
                PasswordHash = hash,
                Salt = salt,
                Algorithm = "argon2id$m=65536,t=4,p=" + argon2.DegreeOfParallelism,
                CreatedAt = now,
                UpdatedAt = now
            };

            await _localAccounts.AddAsync(localAccount);

            var roleToAssign = await ResolveSignupRoleAsync(request, clientId);
            await _roleService.AssignRoleAsync(identity, clientId, roleToAssign);

            if (!string.IsNullOrWhiteSpace(request.InvitationCode))
            {
                var normalizedInvitationCode = request.InvitationCode.Trim();
                var nowForInvitation = DateTimeOffset.UtcNow;
                var invitation = await _dbContext.Invitations
                    .FirstOrDefaultAsync(currentInvitation =>
                        currentInvitation.Code == normalizedInvitationCode
                        && currentInvitation.Email == request.Email
                        && !currentInvitation.Used
                        && (!currentInvitation.ExpiresAt.HasValue || currentInvitation.ExpiresAt > nowForInvitation));

                if (invitation == null)
                    throw new InvalidOperationException("La invitación no es válida para el email indicado");

                invitation.Used = true;

                var joinedMembership = identity.Memberships
                    .FirstOrDefault(currentMembership => currentMembership.TenantId == invitation.TenantId);

                if (joinedMembership != null)
                {
                    joinedMembership.JoinedAt ??= nowForInvitation;
                    joinedMembership.UpdatedAt = nowForInvitation;
                }
            }

            await _unitOfWork.SaveChangesAsync();

            return identity;
        }

        private async Task<string> ResolveSignupRoleAsync(SignupRequest request, string clientId)
        {
            if (string.IsNullOrWhiteSpace(request.InvitationCode))
            {
                return "user";
            }

            var tenantId = await _dbContext.Tenants
                .Where(tenant => tenant.ClientId == clientId)
                .Select(tenant => (long?)tenant.Id)
                .SingleOrDefaultAsync();

            if (!tenantId.HasValue)
            {
                return "user";
            }

            var ownerRoleId = await _dbContext.Roles
                .Where(role => role.TenantId == tenantId.Value && role.Name == "owner")
                .Select(role => (int?)role.Id)
                .SingleOrDefaultAsync();

            if (!ownerRoleId.HasValue)
            {
                return "user";
            }

            var tenantAlreadyHasOwner = await _dbContext.MembershipRoles
                .AnyAsync(membershipRole => membershipRole.TenantId == tenantId.Value && membershipRole.RoleId == ownerRoleId.Value);

            return tenantAlreadyHasOwner ? "user" : "owner";
        }

        private async Task<string> ResolveClientIdAsync(SignupRequest request)
        {
            var normalizedRequestClientId = request.ClientId?.Trim();

            if (!string.IsNullOrWhiteSpace(request.InvitationCode))
            {
                var normalizedInvitationCode = request.InvitationCode.Trim();
                var now = DateTimeOffset.UtcNow;
                var invitationClientId = await _dbContext.Invitations
                    .Where(currentInvitation =>
                        currentInvitation.Code == normalizedInvitationCode
                        && currentInvitation.Email == request.Email
                        && !currentInvitation.Used
                        && (!currentInvitation.ExpiresAt.HasValue || currentInvitation.ExpiresAt > now))
                    .Select(currentInvitation => currentInvitation.Tenant.ClientId)
                    .SingleOrDefaultAsync();

                if (string.IsNullOrWhiteSpace(invitationClientId))
                    throw new InvalidOperationException("La invitación no es válida para el email indicado");

                if (!string.IsNullOrWhiteSpace(normalizedRequestClientId)
                    && !string.Equals(invitationClientId, normalizedRequestClientId, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("El clientId no coincide con el tenant de la invitación");
                }

                return invitationClientId;
            }

            if (string.IsNullOrWhiteSpace(normalizedRequestClientId))
                throw new InvalidOperationException("Se requiere clientId o invitationCode para determinar el tenant del registro");

            var tenantExists = await _dbContext.Tenants
                .AnyAsync(tenant => tenant.ClientId == normalizedRequestClientId);

            if (!tenantExists)
                throw new InvalidOperationException($"No existe un tenant para el clientId '{normalizedRequestClientId}'");

            return normalizedRequestClientId;
        }

        private static string? NormalizeE164(string phone)
        {
            if (string.IsNullOrWhiteSpace(phone)) return null;
            // Remove common separators and whitespace
            var cleaned = phone.Trim();
            cleaned = cleaned.Replace(" ", string.Empty).Replace("-", string.Empty).Replace("(", string.Empty).Replace(")", string.Empty);
            // Ensure starts with + and then 2-15 digits according to E.164
            var match = System.Text.RegularExpressions.Regex.Match(cleaned, "^\\+?[0-9]{2,15}$");
            if (!match.Success)
                return null;

            // If it doesn't start with '+', add it? We require + prefix for E.164
            if (!cleaned.StartsWith('+')) cleaned = "+" + cleaned;

            // Final validation: + followed by 1-15 digits, first digit cannot be 0
            if (System.Text.RegularExpressions.Regex.IsMatch(cleaned, "^\\+[1-9][0-9]{1,14}$"))
                return cleaned;

            return null;
        }

        public async Task<bool> VerifyPasswordAsync(string password, Domain.Entities.LocalAccount localAccount)
        {
            if (localAccount == null) return false;

            // Parse algorithm metadata if present. Expected format: "argon2id$m=65536,t=4,p=2"
            var memorySize = 64 * 1024;
            var iterations = 4;
            var parallelism = Math.Max(1, Environment.ProcessorCount / 2);

            try
            {
                if (!string.IsNullOrEmpty(localAccount.Algorithm) && localAccount.Algorithm.Contains("argon2id"))
                {
                    var parts = localAccount.Algorithm.Split('$');
                    if (parts.Length > 1)
                    {
                        var parms = parts[1].Split(',');
                        foreach (var p in parms)
                        {
                            var kv = p.Split('=');
                            if (kv.Length != 2) continue;
                            var key = kv[0].Trim();
                            var val = kv[1].Trim();
                            if (key == "m" && int.TryParse(val, out var m)) memorySize = m;
                            if (key == "t" && int.TryParse(val, out var t)) iterations = t;
                            if (key == "p" && int.TryParse(val, out var pval)) parallelism = pval;
                        }
                    }
                }
            }
            catch
            {
                // ignore parse errors and use defaults
            }

            var passwordBytes = Encoding.UTF8.GetBytes(password ?? string.Empty);
            var argon2 = new Argon2id(passwordBytes)
            {
                Salt = localAccount.Salt,
                DegreeOfParallelism = parallelism,
                Iterations = iterations,
                MemorySize = memorySize
            };

            var computed = await argon2.GetBytesAsync(localAccount.PasswordHash.Length);

            return CryptographicOperations.FixedTimeEquals(computed, localAccount.PasswordHash);
        }
    }
}
