using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Data.Configurations
{
    public class RoleConfiguration : IEntityTypeConfiguration<Role>
    {
        public void Configure(EntityTypeBuilder<Role> builder)
        {
            builder.ToTable("roles");

            builder.HasKey(role => role.Id);
            builder.Property(role => role.Id).HasColumnName("id").ValueGeneratedOnAdd();

            builder.Property(role => role.TenantId)
                .HasColumnName("tenant_id")
                .IsRequired();

            builder.Property(role => role.Name)
                .HasColumnName("name")
                .HasMaxLength(50)
                .IsRequired();

            builder.Property(role => role.IsSystem)
                .HasColumnName("is_system")
                .IsRequired();

            builder.HasIndex(role => new { role.TenantId, role.Name })
                .IsUnique()
                .HasDatabaseName("UQ_roles_name");

            builder.HasAlternateKey(role => new { role.Id, role.TenantId })
                .HasName("UQ_roles_id_tenant");

            builder.HasOne(role => role.Tenant)
                .WithMany(tenant => tenant.Roles)
                .HasForeignKey(role => role.TenantId)
                .HasConstraintName("FK_roles_tenants")
                .OnDelete(DeleteBehavior.NoAction);
        }
    }
}
