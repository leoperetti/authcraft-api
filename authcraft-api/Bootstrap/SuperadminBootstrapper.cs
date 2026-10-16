using Domain.Entities;
using Infrastructure.Data;
using Konscious.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace authcraft_api.Bootstrap;

public sealed class SuperadminBootstrapper
{
    private readonly AppDbContext _appDbContext;
    private readonly SuperadminBootstrapOptions _options;
    private readonly ILogger<SuperadminBootstrapper> _logger;

    public SuperadminBootstrapper(
        AppDbContext appDbContext,
        IOptions<SuperadminBootstrapOptions> options,
        ILogger<SuperadminBootstrapper> logger)
    {
        _appDbContext = appDbContext;
        _options = options.Value;
        _logger = logger;
    }

    public async Task BootstrapAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_options.Email) || string.IsNullOrWhiteSpace(_options.Password))
        {
            _logger.LogWarning("Superadmin bootstrap habilitado pero faltan Email o Password. Se omite la inicialización.");
            return;
        }

        var tenant = await EnsureTenantAsync(cancellationToken);
        var superadminRole = await EnsureRolesAsync(tenant.Id, cancellationToken);
        var identity = await EnsureIdentityAsync(cancellationToken);
        await EnsureLocalAccountAsync(identity.Id, cancellationToken);
        await EnsureMembershipAsync(identity.Id, tenant.Id, superadminRole.Id, cancellationToken);

        await _appDbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Bootstrap superadmin verificado para {Email}", _options.Email);
    }

    private async Task<Tenant> EnsureTenantAsync(CancellationToken cancellationToken)
    {
        var tenant = await _appDbContext.Tenants
            .SingleOrDefaultAsync(currentTenant => currentTenant.Slug == _options.TenantSlug, cancellationToken);

        if (tenant != null)
        {
            return tenant;
        }

        tenant = new Tenant
        {
            ClientId = _options.TenantClientId,
            Slug = _options.TenantSlug,
            Name = _options.TenantName,
            Status = "active",
            AllowedOrigins = JsonSerializer.Serialize(new[] { "http://localhost:4200" })
        };

        await _appDbContext.Tenants.AddAsync(tenant, cancellationToken);
        await _appDbContext.SaveChangesAsync(cancellationToken);

        return tenant;
    }

    private async Task<Role> EnsureRolesAsync(long tenantId, CancellationToken cancellationToken)
    {
        var superadminRole = await EnsureRoleAsync(tenantId, "superadmin", cancellationToken);
        await EnsureRoleAsync(tenantId, "owner", cancellationToken);
        await EnsureRoleAsync(tenantId, "admin", cancellationToken);
        await EnsureRoleAsync(tenantId, "user", cancellationToken);

        return superadminRole;
    }

    private async Task<Role> EnsureRoleAsync(long tenantId, string roleName, CancellationToken cancellationToken)
    {
        var existingRole = await _appDbContext.Roles
            .SingleOrDefaultAsync(role => role.TenantId == tenantId && role.Name == roleName, cancellationToken);

        if (existingRole != null)
        {
            return existingRole;
        }

        var role = new Role
        {
            TenantId = tenantId,
            Name = roleName,
            IsSystem = true,
        };

        await _appDbContext.Roles.AddAsync(role, cancellationToken);
        await _appDbContext.SaveChangesAsync(cancellationToken);

        return role;
    }

    private async Task<Identity> EnsureIdentityAsync(CancellationToken cancellationToken)
    {
        var existingIdentity = await _appDbContext.Identities
            .SingleOrDefaultAsync(identity => identity.Email == _options.Email, cancellationToken);

        if (existingIdentity != null)
        {
            return existingIdentity;
        }

        var now = DateTimeOffset.UtcNow;
        var identity = new Identity
        {
            Email = _options.Email,
            Name = _options.FullName,
            CreatedAt = now,
            UpdatedAt = now
        };

        await _appDbContext.Identities.AddAsync(identity, cancellationToken);
        await _appDbContext.SaveChangesAsync(cancellationToken);

        return identity;
    }

    private async Task EnsureLocalAccountAsync(long identityId, CancellationToken cancellationToken)
    {
        var hasLocalAccount = await _appDbContext.LocalAccounts
            .AnyAsync(localAccount => localAccount.IdentityId == identityId, cancellationToken);

        if (hasLocalAccount)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var salt = new byte[16];
        RandomNumberGenerator.Fill(salt);

        var passwordBytes = Encoding.UTF8.GetBytes(_options.Password);
        var argon2 = new Argon2id(passwordBytes)
        {
            Salt = salt,
            DegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2),
            Iterations = 4,
            MemorySize = 64 * 1024
        };

        var passwordHash = await argon2.GetBytesAsync(32);

        var localAccount = new LocalAccount
        {
            IdentityId = identityId,
            PasswordHash = passwordHash,
            Salt = salt,
            Algorithm = "argon2id$m=65536,t=4,p=" + argon2.DegreeOfParallelism,
            CreatedAt = now,
            UpdatedAt = now
        };

        await _appDbContext.LocalAccounts.AddAsync(localAccount, cancellationToken);
    }

    private async Task EnsureMembershipAsync(long identityId, long tenantId, int superadminRoleId, CancellationToken cancellationToken)
    {
        var membership = await _appDbContext.Memberships
            .SingleOrDefaultAsync(existingMembership => existingMembership.IdentityId == identityId && existingMembership.TenantId == tenantId, cancellationToken);

        var now = DateTimeOffset.UtcNow;

        if (membership == null)
        {
            membership = new Membership
            {
                IdentityId = identityId,
                TenantId = tenantId,
                Status = "active",
                CreatedAt = now,
                UpdatedAt = now
            };

            await _appDbContext.Memberships.AddAsync(membership, cancellationToken);
        }
        else
        {
            membership.UpdatedAt = now;
        }

        var membershipHasSuperadminRole = await _appDbContext.MembershipRoles
            .AnyAsync(membershipRole => membershipRole.MembershipId == identityId && membershipRole.RoleId == superadminRoleId && membershipRole.TenantId == tenantId, cancellationToken);

        if (!membershipHasSuperadminRole)
        {
            await _appDbContext.MembershipRoles.AddAsync(new MembershipRole
            {
                MembershipId = identityId,
                RoleId = superadminRoleId,
                TenantId = tenantId
            }, cancellationToken);
        }
    }
}
