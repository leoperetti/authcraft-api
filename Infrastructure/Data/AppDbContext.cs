using Domain.Audit;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }

        public DbSet<Identity> Identities { get; set; } = null!;
        public DbSet<Role> Roles { get; set; } = null!;
        public DbSet<MembershipRole> MembershipRoles { get; set; } = null!;
        public DbSet<Membership> Memberships { get; set; } = null!;
        public DbSet<Tenant> Tenants { get; set; } = null!;
        public DbSet<TenantApiKey> TenantApiKeys { get; set; } = null!;
        public DbSet<OAuthAccount> OAuthAccounts { get; set; } = null!;
        public DbSet<Provider> Providers { get; set; } = null!;
        public DbSet<LocalAccount> LocalAccounts { get; set; } = null!;
        public DbSet<AuditLog> AuditLogs { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Aplicar todas las configuraciones definidas en este assembly (Infrastructure)
            modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

            base.OnModelCreating(modelBuilder);
        }
    }
}
