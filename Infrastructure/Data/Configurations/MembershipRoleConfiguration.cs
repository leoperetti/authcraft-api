using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Data.Configurations
{
    public class MembershipRoleConfiguration : IEntityTypeConfiguration<MembershipRole>
    {
        public void Configure(EntityTypeBuilder<MembershipRole> builder)
        {
            builder.ToTable("membership_roles");

            builder.HasKey(membershipRole => new { membershipRole.MembershipId, membershipRole.RoleId })
                .HasName("PK_membership_roles");

            builder.Property(membershipRole => membershipRole.MembershipId).HasColumnName("membership_id");
            builder.Property(membershipRole => membershipRole.RoleId).HasColumnName("role_id");
            builder.Property(membershipRole => membershipRole.TenantId)
                .HasColumnName("tenant_id")
                .IsRequired();

            builder.HasIndex(membershipRole => membershipRole.RoleId)
                .HasDatabaseName("IX_membership_roles_role");

            builder.HasOne(membershipRole => membershipRole.Membership)
                   .WithMany(membership => membership.MembershipRoles)
                   .HasForeignKey(membershipRole => membershipRole.MembershipId)
                   .HasPrincipalKey(membership => membership.IdentityId)
                   .HasConstraintName("FK_membership_roles_memberships")
                   .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne<Membership>()
                   .WithMany()
                   .HasForeignKey(membershipRole => new { membershipRole.MembershipId, membershipRole.TenantId })
                   .HasPrincipalKey(membership => new { membership.IdentityId, membership.TenantId })
                   .HasConstraintName("FK_membership_roles_membership_tenant")
                   .OnDelete(DeleteBehavior.NoAction);

            builder.HasOne(membershipRole => membershipRole.Role)
                   .WithMany(role => role.MembershipRoles)
                   .HasForeignKey(membershipRole => membershipRole.RoleId)
                   .HasConstraintName("FK_membership_roles_roles")
                   .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne<Role>()
                   .WithMany()
                   .HasForeignKey(membershipRole => new { membershipRole.RoleId, membershipRole.TenantId })
                   .HasPrincipalKey(role => new { role.Id, role.TenantId })
                   .HasConstraintName("FK_membership_roles_role_tenant")
                   .OnDelete(DeleteBehavior.NoAction);
        }
    }
}
