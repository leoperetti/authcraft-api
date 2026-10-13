using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Data.Configurations
{
    public class TenantConfiguration : IEntityTypeConfiguration<Tenant>
    {
        public void Configure(EntityTypeBuilder<Tenant> builder)
        {
            builder.ToTable("tenants", tableBuilder =>
            {
                tableBuilder.HasCheckConstraint("CK_tenants_status", "[status] IN ('active','suspended','archived')");
            });

            builder.HasKey(tenant => tenant.Id).HasName("PK_tenants");

            builder.Property(tenant => tenant.Id)
                .HasColumnName("id")
                .ValueGeneratedOnAdd();

            builder.Property(tenant => tenant.ClientId)
                .HasColumnName("client_id")
                .HasMaxLength(200)
                .IsRequired();

            builder.Property(tenant => tenant.Slug)
                .HasColumnName("slug")
                .HasMaxLength(100)
                .IsRequired();

            builder.Property(tenant => tenant.Name)
                .HasColumnName("name")
                .HasMaxLength(200)
                .IsRequired();

            builder.Property(tenant => tenant.Status)
                .HasColumnName("status")
                .HasMaxLength(20)
                .IsRequired()
                .HasDefaultValue("active");

            builder.Property(tenant => tenant.Settings)
                .HasColumnName("settings")
                .HasColumnType("nvarchar(max)")
                .IsRequired(false);

            builder.Property(tenant => tenant.AllowedOrigins)
                .HasColumnName("allowed_origins")
                .HasColumnType("varchar(2083)")
                .IsRequired(false);

            builder.HasIndex(tenant => tenant.Slug)
                .IsUnique()
                .HasDatabaseName("UQ_tenants_slug");
        }
    }
}
