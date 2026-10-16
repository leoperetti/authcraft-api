using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Data.Configurations
{
    public class MembershipConfiguration : IEntityTypeConfiguration<Membership>
    {
        public void Configure(EntityTypeBuilder<Membership> builder)
        {
            builder.ToTable("memberships", tableBuilder =>
            {
                tableBuilder.HasCheckConstraint("CK_memberships_status", "[status] IN ('invited','active','suspended','removed')");
            });

            builder.HasKey(membership => membership.IdentityId);

            builder.Property(membership => membership.IdentityId)
                .HasColumnName("identity_id")
                .ValueGeneratedNever();

            builder.Property(membership => membership.TenantId)
                .HasColumnName("tenant_id")
                .IsRequired();

            builder.Property(membership => membership.Status)
                .HasColumnName("status")
                .HasMaxLength(50)
                .IsRequired()
                .HasDefaultValue("active");

            builder.Property(membership => membership.InvitedBy)
                .HasColumnName("invited_by")
                .IsRequired(false);

            builder.Property(membership => membership.InvitedAt)
                .HasColumnName("invited_at")
                .IsRequired(false);

            builder.Property(membership => membership.JoinedAt)
                .HasColumnName("joined_at")
                .IsRequired(false);

            builder.Property(membership => membership.CreatedAt)
                .HasColumnName("created_at")
                .HasDefaultValueSql("SYSUTCDATETIME()");

            builder.Property(membership => membership.UpdatedAt)
                .HasColumnName("updated_at")
                .HasDefaultValueSql("SYSUTCDATETIME()");

            builder.HasIndex(membership => new { membership.IdentityId, membership.TenantId })
                .IsUnique()
                .HasDatabaseName("UQ_memberships_identity_tenant");

            builder.HasIndex(membership => new { membership.TenantId, membership.Status })
                .HasDatabaseName("IX_memberships_tenant_status");

            builder.HasIndex(membership => new { membership.IdentityId, membership.Status })
                .HasDatabaseName("IX_memberships_identity_status");

            builder.HasOne(membership => membership.Identity)
                .WithMany(identity => identity.Memberships)
                .HasForeignKey(membership => membership.IdentityId)
                .HasConstraintName("FK_memberships_identity")
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne(membership => membership.Tenant)
                .WithMany(tenant => tenant.Memberships)
                .HasForeignKey(membership => membership.TenantId)
                .HasConstraintName("FK_memberships_tenant")
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne(membership => membership.InvitedByIdentity)
                .WithMany()
                .HasForeignKey(membership => membership.InvitedBy)
                .HasConstraintName("FK_memberships_invited_by")
                .OnDelete(DeleteBehavior.NoAction);
        }
    }
}
