using authcraft_api.Bootstrap;
using Domain.Entities;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace authcraft_api.Tests;

public class SuperadminBootstrapperTests
{
    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    private static SuperadminBootstrapper CreateBootstrapper(AppDbContext dbContext) =>
        new(
            dbContext,
            Options.Create(new SuperadminBootstrapOptions
            {
                Enabled = true,
                TenantSlug = "tenant-admin-portal",
                TenantClientId = "tenant_default",
                TenantName = "Authcraft-Tenants",
                Email = "admin@example.com",
                Password = "Test-Password-123"
            }),
            NullLogger<SuperadminBootstrapper>.Instance);

    private static async Task<Tenant> AddTenantAsync(AppDbContext dbContext, string slug)
    {
        var tenant = new Tenant { ClientId = slug, Slug = slug, Name = slug, Status = "active" };
        dbContext.Tenants.Add(tenant);
        await dbContext.SaveChangesAsync();
        return tenant;
    }

    [Fact]
    public async Task Bootstrap_CreatesTeacherSystemRole_ForCekideAppTenant()
    {
        await using var dbContext = CreateDbContext();
        var cekide = await AddTenantAsync(dbContext, "cekide-app");

        await CreateBootstrapper(dbContext).BootstrapAsync(CancellationToken.None);

        var teacherRoles = await dbContext.Roles
            .Where(role => role.TenantId == cekide.Id && role.Name == "teacher")
            .ToListAsync();
        var teacher = Assert.Single(teacherRoles);
        Assert.True(teacher.IsSystem);
    }

    [Fact]
    public async Task Bootstrap_IsIdempotent_ForTeacherRole()
    {
        await using var dbContext = CreateDbContext();
        var cekide = await AddTenantAsync(dbContext, "cekide-app");
        var bootstrapper = CreateBootstrapper(dbContext);

        await bootstrapper.BootstrapAsync(CancellationToken.None);
        await bootstrapper.BootstrapAsync(CancellationToken.None);

        Assert.Equal(1, await dbContext.Roles.CountAsync(role => role.TenantId == cekide.Id && role.Name == "teacher"));
    }

    [Fact]
    public async Task Bootstrap_DoesNotCreateTeacherRole_ForOtherTenants()
    {
        await using var dbContext = CreateDbContext();
        await AddTenantAsync(dbContext, "cekide-app");

        await CreateBootstrapper(dbContext).BootstrapAsync(CancellationToken.None);

        var adminPortal = await dbContext.Tenants.SingleAsync(tenant => tenant.Slug == "tenant-admin-portal");
        Assert.False(await dbContext.Roles.AnyAsync(role => role.TenantId == adminPortal.Id && role.Name == "teacher"));
    }

    [Fact]
    public async Task Bootstrap_KeepsExistingSystemRoles()
    {
        await using var dbContext = CreateDbContext();
        await AddTenantAsync(dbContext, "cekide-app");

        await CreateBootstrapper(dbContext).BootstrapAsync(CancellationToken.None);

        var adminPortal = await dbContext.Tenants.SingleAsync(tenant => tenant.Slug == "tenant-admin-portal");
        var roleNames = await dbContext.Roles
            .Where(role => role.TenantId == adminPortal.Id)
            .Select(role => role.Name)
            .ToListAsync();
        Assert.Equivalent(new[] { "superadmin", "owner", "admin", "user" }, roleNames);
    }

    [Fact]
    public async Task Bootstrap_DoesNotFail_WhenCekideAppTenantDoesNotExist()
    {
        await using var dbContext = CreateDbContext();

        await CreateBootstrapper(dbContext).BootstrapAsync(CancellationToken.None);

        Assert.False(await dbContext.Roles.AnyAsync(role => role.Name == "teacher"));
    }
}
