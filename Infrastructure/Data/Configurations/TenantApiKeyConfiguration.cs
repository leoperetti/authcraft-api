using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Data.Configurations
{
    public class TenantApiKeyConfiguration : IEntityTypeConfiguration<TenantApiKey>
    {
        public void Configure(EntityTypeBuilder<TenantApiKey> builder)
        {
            builder.ToTable("tenant_api_keys");

            builder.HasKey(tenantApiKey => tenantApiKey.Id);

            builder.Property(tenantApiKey => tenantApiKey.Id)
                .HasColumnName("id")
                .ValueGeneratedNever();

            builder.Property(tenantApiKey => tenantApiKey.TenantId)
                .HasColumnName("tenant_id")
                .IsRequired();

            builder.Property(tenantApiKey => tenantApiKey.KeyHash)
                .HasColumnName("key_hash")
                .HasColumnType("NVARCHAR(255)")
                .IsRequired(false);

            builder.Property(tenantApiKey => tenantApiKey.Scopes)
                .HasColumnName("scopes")
                .HasColumnType("NVARCHAR(MAX)")
                .IsRequired(false);

            builder.HasOne(tenantApiKey => tenantApiKey.Tenant)
                .WithMany(tenant => tenant.TenantApiKeys)
                .HasForeignKey(tenantApiKey => tenantApiKey.TenantId)
                .HasConstraintName("FK_tenant_api_keys_tenants")
                .OnDelete(DeleteBehavior.NoAction);
        }
    }
}
